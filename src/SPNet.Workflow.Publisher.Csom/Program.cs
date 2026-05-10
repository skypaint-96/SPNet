using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.WorkflowServices;

[assembly: InternalsVisibleTo("SPNet.Workflow.WfSerializer.Tests")]

namespace SPNet.Workflow.Publisher.Csom
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var options = PublishOptions.Parse(args);
                WriteAssemblyDiagnostics();
                Publish(options);
                return 0;
            }
            catch (Exception ex)
            {
                WriteResult(new Dictionary<string, object>
                {
                    ["Action"] = "Publish",
                    ["Status"] = "Failed",
                    ["ErrorCode"] = ex.GetType().Name,
                    ["ErrorMessage"] = ex.Message
                });
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void Publish(PublishOptions options)
        {
            var xaml = System.IO.File.ReadAllText(options.XamlPath);
            var formFieldXml = LoadFormFieldXml(options);
            using (var context = new ClientContext(options.SiteUrl))
            {
                ConfigureAuthentication(context, options);
                var web = context.Web;
                context.Load(web, w => w.Id, w => w.Title);
                context.ExecuteQuery();

                var manager = new WorkflowServicesManager(context, web);
                var deploymentService = manager.GetWorkflowDeploymentService();
                var subscriptionService = manager.GetWorkflowSubscriptionService();

                EnsureNoExistingDefinition(context, deploymentService, options.WorkflowName, options.IfExists);

                if (options.TargetType == TargetType.List)
                {
                    PublishListWorkflow(context, web, deploymentService, subscriptionService, options, xaml, formFieldXml);
                }
                else
                {
                    PublishSiteWorkflow(context, web, deploymentService, subscriptionService, options, xaml, formFieldXml);
                }
            }
        }

        private static void PublishListWorkflow(ClientContext context, Web web, WorkflowDeploymentService deploymentService, WorkflowSubscriptionService subscriptionService, PublishOptions options, string xaml, string formFieldXml)
        {
            var targetList = GetTargetList(web, options);
            var workflowHistoryList = web.Lists.GetByTitle("Workflow History");
            var workflowTasksList = web.Lists.GetByTitle("Workflow Tasks");
            context.Load(targetList, l => l.Id, l => l.Title);
            context.Load(workflowHistoryList, l => l.Id, l => l.Title);
            context.Load(workflowTasksList, l => l.Id, l => l.Title);
            context.ExecuteQuery();

            var definition = new WorkflowDefinition(context)
            {
                DisplayName = options.WorkflowName,
                Description = "SPNet CSOM-published workflow.",
                Xaml = xaml,
                RestrictToType = "List",
                RestrictToScope = targetList.Id.ToString()
            };
            ApplyWorkflowDefinitionMetadata(definition, formFieldXml, null);

            var saveResult = deploymentService.SaveDefinition(definition);
            context.ExecuteQuery();
            var definitionId = saveResult.Value;
            EnsureSavedDefinitionMetadata(context, deploymentService, definitionId, formFieldXml);

            deploymentService.PublishDefinition(definitionId);
            context.ExecuteQuery();

            var subscription = new WorkflowSubscription(context)
            {
                DefinitionId = definitionId,
                Name = options.WorkflowName,
                Enabled = true,
                EventSourceId = targetList.Id,
                EventTypes = BuildEventTypes(options)
            };
            SetWorkflowSubscriptionProperty(subscription, "TaskListId", workflowTasksList.Id);
            SetWorkflowSubscriptionProperty(subscription, "HistoryListId", workflowHistoryList.Id);

            var subscriptionResult = subscriptionService.PublishSubscriptionForList(subscription, targetList.Id);
            context.ExecuteQuery();

            WriteResult(new Dictionary<string, object>
            {
                ["Action"] = "Publish",
                ["Status"] = "Published",
                ["WorkflowName"] = options.WorkflowName,
                ["TargetType"] = "List",
                ["TargetListTitle"] = targetList.Title,
                ["TargetListId"] = targetList.Id.ToString(),
                ["DefinitionId"] = definitionId.ToString(),
                ["SubscriptionId"] = subscriptionResult.Value.ToString(),
                ["StartManual"] = options.StartManual,
                ["StartOnCreated"] = options.StartOnCreated,
                ["StartOnUpdated"] = options.StartOnUpdated,
                ["HasFormField"] = !string.IsNullOrWhiteSpace(formFieldXml),
                ["FormFieldXmlPath"] = options.EffectiveFormFieldXmlPath
            });
        }

        private static void PublishSiteWorkflow(ClientContext context, Web web, WorkflowDeploymentService deploymentService, WorkflowSubscriptionService subscriptionService, PublishOptions options, string xaml, string formFieldXml)
        {
            var definition = new WorkflowDefinition(context)
            {
                DisplayName = options.WorkflowName,
                Description = "SPNet CSOM-published site workflow.",
                Xaml = xaml,
                RestrictToType = "Site",
                RestrictToScope = web.Id.ToString()
            };
            ApplyWorkflowDefinitionMetadata(definition, formFieldXml, null);

            var saveResult = deploymentService.SaveDefinition(definition);
            context.ExecuteQuery();
            var definitionId = saveResult.Value;
            EnsureSavedDefinitionMetadata(context, deploymentService, definitionId, formFieldXml);

            deploymentService.PublishDefinition(definitionId);
            context.ExecuteQuery();

            var subscription = new WorkflowSubscription(context)
            {
                DefinitionId = definitionId,
                Name = options.WorkflowName,
                Enabled = true,
                EventSourceId = web.Id,
                EventTypes = BuildEventTypes(options)
            };

            var subscriptionResult = subscriptionService.PublishSubscription(subscription);
            context.ExecuteQuery();

            WriteResult(new Dictionary<string, object>
            {
                ["Action"] = "Publish",
                ["Status"] = "Published",
                ["WorkflowName"] = options.WorkflowName,
                ["TargetType"] = "Site",
                ["DefinitionId"] = definitionId.ToString(),
                ["SubscriptionId"] = subscriptionResult.Value.ToString(),
                ["HasFormField"] = !string.IsNullOrWhiteSpace(formFieldXml),
                ["FormFieldXmlPath"] = options.EffectiveFormFieldXmlPath
            });
        }

        internal static string DiscoverFormFieldSidecarPath(string xamlPath)
        {
            if (string.IsNullOrWhiteSpace(xamlPath)) return null;
            return xamlPath + ".formfield.xml";
        }

        internal static string ComputeInitiationUrl(Guid definitionId)
        {
            return "wfsvc/" + definitionId.ToString("N") + "/WFInitForm.aspx";
        }

        internal static string NormalizeFormFieldXml(string formFieldXml)
        {
            if (string.IsNullOrWhiteSpace(formFieldXml)) return null;
            var document = XDocument.Parse(formFieldXml, LoadOptions.PreserveWhitespace);
            if (!string.Equals(document.Root?.Name.LocalName, "Fields", StringComparison.Ordinal)) throw new ArgumentException("FormField XML root element must be <Fields>.");
            return document.ToString(SaveOptions.DisableFormatting);
        }

        private static string LoadFormFieldXml(PublishOptions options)
        {
            var path = options.FormFieldXmlPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var sidecarPath = DiscoverFormFieldSidecarPath(options.XamlPath);
                if (!string.IsNullOrWhiteSpace(sidecarPath) && System.IO.File.Exists(sidecarPath)) path = sidecarPath;
            }

            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!System.IO.File.Exists(path)) throw new FileNotFoundException("FormField XML file not found.", path);
            options.EffectiveFormFieldXmlPath = path;
            return NormalizeFormFieldXml(System.IO.File.ReadAllText(path));
        }

        private static void EnsureSavedDefinitionMetadata(ClientContext context, WorkflowDeploymentService deploymentService, Guid definitionId, string formFieldXml)
        {
            if (string.IsNullOrWhiteSpace(formFieldXml)) return;
            var savedDefinition = deploymentService.GetDefinition(definitionId);
            context.Load(savedDefinition);
            context.ExecuteQuery();
            ApplyWorkflowDefinitionMetadata(savedDefinition, formFieldXml, ComputeInitiationUrl(definitionId));
            deploymentService.SaveDefinition(savedDefinition);
            context.ExecuteQuery();
        }

        internal static void ApplyWorkflowDefinitionMetadata(object definition, string formFieldXml, string initiationUrl)
        {
            if (definition == null || string.IsNullOrWhiteSpace(formFieldXml)) return;
            SetWorkflowDefinitionMetadataValue(definition, "FormField", formFieldXml);
            SetWorkflowDefinitionMetadataValue(definition, "RequiresInitiationForm", true);
            if (!string.IsNullOrWhiteSpace(initiationUrl)) SetWorkflowDefinitionMetadataValue(definition, "InitiationUrl", initiationUrl);
        }

        private static void SetWorkflowDefinitionMetadataValue(object definition, string name, object value)
        {
            if (TrySetPublicProperty(definition, name, value)) return;
            if (TryInvokeSetProperty(definition, name, value)) return;
            if (TrySetPropertyDefinitionsValue(definition, name, value)) return;

            if (string.Equals(name, "InitiationUrl", StringComparison.OrdinalIgnoreCase)) return;
            throw new MissingMemberException(definition.GetType().FullName, name);
        }

        private static bool TrySetPublicProperty(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite) return false;
            property.SetValue(target, CoerceValue(value, property.PropertyType), null);
            return true;
        }

        private static bool TryInvokeSetProperty(object target, string name, object value)
        {
            var setProperty = target.GetType().GetMethod("SetProperty", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string), typeof(object) }, null);
            if (setProperty != null)
            {
                setProperty.Invoke(target, new[] { name, value });
                return true;
            }

            setProperty = target.GetType().GetMethod("SetProperty", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string), typeof(string) }, null);
            if (setProperty != null)
            {
                setProperty.Invoke(target, new[] { name, Convert.ToString(value) });
                return true;
            }

            return false;
        }

        private static bool TrySetPropertyDefinitionsValue(object target, string name, object value)
        {
            var propertyDefinitions = target.GetType().GetProperty("PropertyDefinitions", BindingFlags.Instance | BindingFlags.Public);
            var propertyDefinitionsValue = propertyDefinitions == null ? null : propertyDefinitions.GetValue(target, null);
            if (propertyDefinitionsValue == null) return false;

            var indexer = propertyDefinitionsValue.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public, null, typeof(string), new[] { typeof(string) }, null);
            if (indexer != null && indexer.CanWrite)
            {
                indexer.SetValue(propertyDefinitionsValue, Convert.ToString(value), new object[] { name });
                return true;
            }

            var add = propertyDefinitionsValue.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string), typeof(string) }, null);
            if (add != null)
            {
                add.Invoke(propertyDefinitionsValue, new[] { name, Convert.ToString(value) });
                return true;
            }

            return false;
        }

        private static object CoerceValue(object value, Type targetType)
        {
            if (value == null || targetType.IsInstanceOfType(value)) return value;
            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null) targetType = nullableType;
            if (targetType == typeof(string)) return Convert.ToString(value);
            if (targetType == typeof(bool)) return Convert.ToBoolean(value);
            if (targetType == typeof(Guid)) return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value));
            return Convert.ChangeType(value, targetType);
        }

        private static List<string> BuildEventTypes(PublishOptions options)
        {
            var eventTypes = new List<string>();
            if (options.StartManual) eventTypes.Add("WorkflowStart");
            if (options.StartOnCreated) eventTypes.Add("ItemAdded");
            if (options.StartOnUpdated) eventTypes.Add("ItemUpdated");
            if (eventTypes.Count == 0) eventTypes.Add("WorkflowStart");
            return eventTypes;
        }

        private static List GetTargetList(Web web, PublishOptions options)
        {
            if (options.TargetListId.HasValue) return web.Lists.GetById(options.TargetListId.Value);
            if (!string.IsNullOrWhiteSpace(options.TargetListTitle)) return web.Lists.GetByTitle(options.TargetListTitle);
            throw new ArgumentException("--target-list-title or --target-list-id is required when --target-type List.");
        }

        private static void SetWorkflowSubscriptionProperty(WorkflowSubscription subscription, string name, object value)
        {
            var property = subscription.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
            {
                property.SetValue(subscription, value, null);
                return;
            }

            var setProperty = subscription.GetType().GetMethod("SetProperty", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string), typeof(object) }, null);
            if (setProperty != null)
            {
                setProperty.Invoke(subscription, new[] { name, value });
                return;
            }

            setProperty = subscription.GetType().GetMethod("SetProperty", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string), typeof(string) }, null);
            if (setProperty != null)
            {
                setProperty.Invoke(subscription, new[] { name, Convert.ToString(value) });
                return;
            }

            var propertyDefinitions = subscription.GetType().GetProperty("PropertyDefinitions", BindingFlags.Instance | BindingFlags.Public);
            var propertyDefinitionsValue = propertyDefinitions == null ? null : propertyDefinitions.GetValue(subscription, null);
            if (propertyDefinitionsValue != null)
            {
                var indexer = propertyDefinitionsValue.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public, null, typeof(string), new[] { typeof(string) }, null);
                if (indexer != null && indexer.CanWrite)
                {
                    indexer.SetValue(propertyDefinitionsValue, Convert.ToString(value), new object[] { name });
                    return;
                }

                var add = propertyDefinitionsValue.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string), typeof(string) }, null);
                if (add != null)
                {
                    add.Invoke(propertyDefinitionsValue, new[] { name, Convert.ToString(value) });
                    return;
                }
            }

            throw new MissingMemberException("WorkflowSubscription", name);
        }

        private static void EnsureNoExistingDefinition(ClientContext context, WorkflowDeploymentService deploymentService, string workflowName, IfExistsPolicy ifExists)
        {
            if (ifExists != IfExistsPolicy.Fail) throw new NotSupportedException("Only --if-exists Fail is currently implemented.");
            var definitions = deploymentService.EnumerateDefinitions(true);
            context.Load(definitions);
            context.ExecuteQuery();
            var existing = definitions.Where(d => d != null && string.Equals(d.DisplayName, workflowName, StringComparison.Ordinal)).Select(d => d.Id.ToString()).ToArray();
            if (existing.Length > 0) throw new InvalidOperationException("Workflow '" + workflowName + "' already exists: " + string.Join(", ", existing));
        }

        private static void ConfigureAuthentication(ClientContext context, PublishOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.CookieHeader))
            {
                context.ExecutingWebRequest += (sender, e) => e.WebRequestExecutor.RequestHeaders["Cookie"] = options.CookieHeader;
                return;
            }

            if (!string.IsNullOrWhiteSpace(options.Username))
            {
                context.Credentials = new NetworkCredential(options.Username, options.Password ?? string.Empty, options.Domain ?? string.Empty);
                return;
            }

            context.Credentials = CredentialCache.DefaultNetworkCredentials;
        }

        private static void WriteAssemblyDiagnostics()
        {
            var names = new[]
            {
                "Microsoft.SharePoint.Client",
                "Microsoft.SharePoint.Client.Runtime",
                "Microsoft.SharePoint.Client.WorkflowServices"
            };
            foreach (var name in names)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) ?? Assembly.Load(name);
                Console.WriteLine("SPNET_ASSEMBLY " + Json(new Dictionary<string, object>
                {
                    ["Name"] = assembly.GetName().Name,
                    ["Version"] = assembly.GetName().Version.ToString(),
                    ["Location"] = assembly.Location
                }));
            }
        }

        private static void WriteResult(Dictionary<string, object> value) => Console.WriteLine("SPNET_RESULT " + Json(value));

        private static string Json(Dictionary<string, object> value) => "{" + string.Join(",", value.Select(kvp => Quote(kvp.Key) + ":" + JsonValue(kvp.Value))) + "}";

        private static string JsonValue(object value)
        {
            if (value == null) return "null";
            if (value is bool b) return b ? "true" : "false";
            return Quote(Convert.ToString(value));
        }

        private static string Quote(string value) => "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }

    internal enum TargetType { Site, List }
    internal enum IfExistsPolicy { Fail }

    internal sealed class PublishOptions
    {
        public Uri SiteUrl { get; private set; }
        public string WorkflowName { get; private set; }
        public string XamlPath { get; private set; }
        public TargetType TargetType { get; private set; }
        public string TargetListTitle { get; private set; }
        public Guid? TargetListId { get; private set; }
        public bool StartManual { get; private set; } = true;
        public bool StartOnCreated { get; private set; }
        public bool StartOnUpdated { get; private set; }
        public IfExistsPolicy IfExists { get; private set; } = IfExistsPolicy.Fail;
        public string CookieHeader { get; private set; }
        public string Username { get; private set; }
        public string Password { get; private set; }
        public string Domain { get; private set; }
        public string FormFieldXmlPath { get; private set; }
        public string EffectiveFormFieldXmlPath { get; internal set; }

        public static PublishOptions Parse(string[] args)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i];
                if (!key.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unexpected argument: " + key);
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value for " + key);
                map[key.Substring(2)] = args[++i];
            }

            var options = new PublishOptions
            {
                SiteUrl = new Uri(Required(map, "site-url")),
                WorkflowName = Required(map, "workflow-name"),
                XamlPath = Required(map, "xaml"),
                TargetType = ParseEnum<TargetType>(Required(map, "target-type"), "target-type"),
                TargetListTitle = Optional(map, "target-list-title"),
                CookieHeader = Optional(map, "cookie-header"),
                Username = Optional(map, "username"),
                Password = Optional(map, "password"),
                Domain = Optional(map, "domain"),
                FormFieldXmlPath = Optional(map, "form-field-xml")
            };
            if (map.ContainsKey("target-list-id")) options.TargetListId = Guid.Parse(map["target-list-id"]);
            if (map.ContainsKey("start-manual")) options.StartManual = bool.Parse(map["start-manual"]);
            if (map.ContainsKey("start-created")) options.StartOnCreated = bool.Parse(map["start-created"]);
            if (map.ContainsKey("start-updated")) options.StartOnUpdated = bool.Parse(map["start-updated"]);
            if (map.ContainsKey("if-exists")) options.IfExists = ParseEnum<IfExistsPolicy>(map["if-exists"], "if-exists");
            if (!System.IO.File.Exists(options.XamlPath)) throw new FileNotFoundException("XAML file not found.", options.XamlPath);
            if (!string.IsNullOrWhiteSpace(options.FormFieldXmlPath) && !System.IO.File.Exists(options.FormFieldXmlPath)) throw new FileNotFoundException("FormField XML file not found.", options.FormFieldXmlPath);
            return options;
        }

        private static string Required(Dictionary<string, string> map, string key) => map.ContainsKey(key) && !string.IsNullOrWhiteSpace(map[key]) ? map[key] : throw new ArgumentException("--" + key + " is required.");
        private static string Optional(Dictionary<string, string> map, string key) => map.ContainsKey(key) ? map[key] : null;
        private static T ParseEnum<T>(string value, string name) where T : struct => Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new ArgumentException("Invalid --" + name + ": " + value);
    }
}
