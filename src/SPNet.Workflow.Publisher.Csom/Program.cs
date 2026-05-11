using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
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
            var metadata = LoadDefinitionMetadata(options);
            var formFieldXml = BuildFormFieldXml(metadata);
            if (string.IsNullOrWhiteSpace(formFieldXml)) formFieldXml = LoadExplicitFallbackFormFieldXml(options);
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

                if (EffectiveTargetType(options, metadata) == TargetType.List)
                {
                    PublishListWorkflow(context, web, deploymentService, subscriptionService, options, metadata, xaml, formFieldXml);
                }
                else
                {
                    PublishSiteWorkflow(context, web, deploymentService, subscriptionService, options, metadata, xaml, formFieldXml);
                }
            }
        }

        private static void PublishListWorkflow(ClientContext context, Web web, WorkflowDeploymentService deploymentService, WorkflowSubscriptionService subscriptionService, PublishOptions options, PublisherWorkflowMetadata metadata, string xaml, string formFieldXml)
        {
            var targetList = GetTargetList(web, options, metadata);
            var workflowHistoryList = web.Lists.GetByTitle("Workflow History");
            var workflowTasksList = web.Lists.GetByTitle("Workflow Tasks");
            context.Load(targetList, l => l.Id, l => l.Title);
            context.Load(workflowHistoryList, l => l.Id, l => l.Title);
            context.Load(workflowTasksList, l => l.Id, l => l.Title);
            context.ExecuteQuery();

            var definition = new WorkflowDefinition(context)
            {
                DisplayName = EffectiveWorkflowName(options, metadata),
                Description = EffectiveDescription(metadata, "SPNet CSOM-published workflow."),
                Xaml = xaml,
                RestrictToType = "List",
                RestrictToScope = targetList.Id.ToString()
            };
            ApplyWorkflowDefinitionMetadata(definition, metadata, formFieldXml, null);

            var saveResult = deploymentService.SaveDefinition(definition);
            context.ExecuteQuery();
            var definitionId = saveResult.Value;
            EnsureSavedDefinitionMetadata(context, deploymentService, definitionId, metadata, formFieldXml);

            deploymentService.PublishDefinition(definitionId);
            context.ExecuteQuery();

            var subscription = new WorkflowSubscription(context)
            {
                DefinitionId = definitionId,
                Name = EffectiveWorkflowName(options, metadata),
                Enabled = true,
                EventSourceId = targetList.Id,
                EventTypes = BuildEventTypes(options, metadata)
            };
            SetWorkflowSubscriptionProperty(subscription, "TaskListId", workflowTasksList.Id);
            SetWorkflowSubscriptionProperty(subscription, "HistoryListId", workflowHistoryList.Id);

            var subscriptionResult = subscriptionService.PublishSubscriptionForList(subscription, targetList.Id);
            context.ExecuteQuery();

            WriteResult(new Dictionary<string, object>
            {
                ["Action"] = "Publish",
                ["Status"] = "Published",
                ["WorkflowName"] = EffectiveWorkflowName(options, metadata),
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

        private static void PublishSiteWorkflow(ClientContext context, Web web, WorkflowDeploymentService deploymentService, WorkflowSubscriptionService subscriptionService, PublishOptions options, PublisherWorkflowMetadata metadata, string xaml, string formFieldXml)
        {
            var definition = new WorkflowDefinition(context)
            {
                DisplayName = EffectiveWorkflowName(options, metadata),
                Description = EffectiveDescription(metadata, "SPNet CSOM-published site workflow."),
                Xaml = xaml,
                RestrictToType = "Site",
                RestrictToScope = web.Id.ToString()
            };
            ApplyWorkflowDefinitionMetadata(definition, metadata, formFieldXml, null);

            var saveResult = deploymentService.SaveDefinition(definition);
            context.ExecuteQuery();
            var definitionId = saveResult.Value;
            EnsureSavedDefinitionMetadata(context, deploymentService, definitionId, metadata, formFieldXml);

            deploymentService.PublishDefinition(definitionId);
            context.ExecuteQuery();

            var subscription = new WorkflowSubscription(context)
            {
                DefinitionId = definitionId,
                Name = EffectiveWorkflowName(options, metadata),
                Enabled = true,
                EventSourceId = web.Id,
                EventTypes = BuildEventTypes(options, metadata)
            };

            var subscriptionResult = subscriptionService.PublishSubscription(subscription);
            context.ExecuteQuery();

            WriteResult(new Dictionary<string, object>
            {
                ["Action"] = "Publish",
                ["Status"] = "Published",
                ["WorkflowName"] = EffectiveWorkflowName(options, metadata),
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

        internal static string DiscoverMetadataJsonSidecarPath(string xamlPath)
        {
            if (string.IsNullOrWhiteSpace(xamlPath)) return null;
            return xamlPath + ".metadata.json";
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

        private static string LoadExplicitFallbackFormFieldXml(PublishOptions options)
        {
            var path = options.FormFieldXmlPath;
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!System.IO.File.Exists(path)) throw new FileNotFoundException("FormField XML file not found.", path);
            options.EffectiveFormFieldXmlPath = path;
            return NormalizeFormFieldXml(System.IO.File.ReadAllText(path));
        }

        private static void EnsureSavedDefinitionMetadata(ClientContext context, WorkflowDeploymentService deploymentService, Guid definitionId, PublisherWorkflowMetadata metadata, string formFieldXml)
        {
            if (string.IsNullOrWhiteSpace(formFieldXml) && metadata == null) return;
            var savedDefinition = deploymentService.GetDefinition(definitionId);
            context.Load(savedDefinition);
            context.ExecuteQuery();
            ApplyWorkflowDefinitionMetadata(savedDefinition, metadata, formFieldXml, ComputeInitiationUrl(definitionId));
            deploymentService.SaveDefinition(savedDefinition);
            context.ExecuteQuery();
        }

        internal static void ApplyWorkflowDefinitionMetadata(object definition, string formFieldXml, string initiationUrl)
        {
            ApplyWorkflowDefinitionMetadata(definition, null, formFieldXml, initiationUrl);
        }

        internal static void ApplyWorkflowDefinitionMetadata(object definition, PublisherWorkflowMetadata metadata, string formFieldXml, string initiationUrl)
        {
            if (definition == null) return;
            if (metadata != null && !string.IsNullOrWhiteSpace(metadata.DisplayName)) TrySetPublicProperty(definition, "DisplayName", metadata.DisplayName);
            if (metadata != null && !string.IsNullOrWhiteSpace(metadata.Description)) TrySetPublicProperty(definition, "Description", metadata.Description);
            if (!string.IsNullOrWhiteSpace(formFieldXml))
            {
                SetWorkflowDefinitionMetadataValue(definition, "FormField", formFieldXml);
                SetWorkflowDefinitionMetadataValue(definition, "RequiresInitiationForm", true);
            }
            if (!string.IsNullOrWhiteSpace(metadata?.Initiation?.Url)) SetWorkflowDefinitionMetadataValue(definition, "InitiationUrl", metadata.Initiation.Url);
            else if (!string.IsNullOrWhiteSpace(initiationUrl)) SetWorkflowDefinitionMetadataValue(definition, "InitiationUrl", initiationUrl);
        }

        internal static PublisherWorkflowMetadata LoadDefinitionMetadata(PublishOptions options)
        {
            if (options == null) return null;
            var path = options.MetadataJsonPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var sidecarPath = DiscoverMetadataJsonSidecarPath(options.XamlPath);
                if (!string.IsNullOrWhiteSpace(sidecarPath) && System.IO.File.Exists(sidecarPath)) path = sidecarPath;
            }

            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!System.IO.File.Exists(path)) throw new FileNotFoundException("Workflow metadata JSON file not found.", path);
            options.EffectiveMetadataJsonPath = path;
            return PublisherWorkflowMetadata.FromJson(System.IO.File.ReadAllText(path));
        }

        internal static string BuildFormFieldXml(PublisherWorkflowMetadata metadata)
        {
            if (metadata?.Initiation?.FormFields == null || metadata.Initiation.FormFields.Count == 0) return null;
            var fields = new XElement("Fields");
            foreach (var field in metadata.Initiation.FormFields)
            {
                var element = new XElement("Field");
                AddXmlAttribute(element, "Name", field.Name);
                AddXmlAttribute(element, "FormType", string.IsNullOrWhiteSpace(field.FormType) ? "Initiation" : field.FormType);
                AddXmlAttribute(element, "Format", field.Format);
                AddXmlAttribute(element, "Type", field.Type);
                AddXmlAttribute(element, "BaseType", field.BaseType);
                AddXmlAttribute(element, "MaxLength", field.MaxLength);
                AddXmlAttribute(element, "NumLines", field.NumLines);
                AddXmlAttribute(element, "Sortable", field.Sortable);
                AddXmlAttribute(element, "RichTextMode", field.RichTextMode);
                AddXmlAttribute(element, "List", field.List);
                AddXmlAttribute(element, "ShowField", field.ShowField);
                AddXmlAttribute(element, "Mult", field.Mult);
                AddXmlAttribute(element, "UserSelectionMode", field.UserSelectionMode);
                AddXmlAttribute(element, "UserSelectionScope", field.UserSelectionScope);
                AddXmlAttribute(element, "DisplayName", string.IsNullOrWhiteSpace(field.DisplayName) ? field.Name : field.DisplayName);
                AddXmlAttribute(element, "Description", field.Description, true);
                AddXmlAttribute(element, "Direction", string.IsNullOrWhiteSpace(field.Direction) ? "None" : field.Direction);
                if (field.Default != null) element.Add(new XElement("Default", Convert.ToString(field.Default)));
                if (field.Choices != null && field.Choices.Count > 0)
                {
                    element.Add(new XElement("CHOICES", field.Choices.Select(choice => new XElement("CHOICE", string.IsNullOrWhiteSpace(choice.DisplayName) ? null : new XAttribute("DisplayName", choice.DisplayName), choice.Value ?? string.Empty))));
                }
                fields.Add(element);
            }
            return new XDocument(fields).ToString(SaveOptions.DisableFormatting);
        }

        private static void AddXmlAttribute(XElement element, string name, string value, bool allowEmpty = false)
        {
            if (allowEmpty || !string.IsNullOrWhiteSpace(value)) element.SetAttributeValue(name, value ?? string.Empty);
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

        private static List<string> BuildEventTypes(PublishOptions options) => BuildEventTypes(options, null);

        internal static List<string> BuildEventTypes(PublishOptions options, PublisherWorkflowMetadata metadata)
        {
            var eventTypes = new List<string>();
            if (metadata?.Start?.Manual ?? options.StartManual) eventTypes.Add("WorkflowStart");
            if (metadata?.Start?.OnCreated ?? options.StartOnCreated) eventTypes.Add("ItemAdded");
            if (metadata?.Start?.OnUpdated ?? options.StartOnUpdated) eventTypes.Add("ItemUpdated");
            if (eventTypes.Count == 0) eventTypes.Add("WorkflowStart");
            return eventTypes;
        }

        internal static string EffectiveWorkflowName(PublishOptions options, PublisherWorkflowMetadata metadata) => !string.IsNullOrWhiteSpace(options.WorkflowName) ? options.WorkflowName : metadata?.DisplayName;
        internal static TargetType EffectiveTargetType(PublishOptions options, PublisherWorkflowMetadata metadata) => metadata?.Target?.Type ?? options.TargetType;
        private static string EffectiveDescription(PublisherWorkflowMetadata metadata, string fallback) => !string.IsNullOrWhiteSpace(metadata?.Description) ? metadata.Description : fallback;

        private static List GetTargetList(Web web, PublishOptions options, PublisherWorkflowMetadata metadata)
        {
            if (options.TargetListId.HasValue) return web.Lists.GetById(options.TargetListId.Value);
            if (!string.IsNullOrWhiteSpace(options.TargetListTitle)) return web.Lists.GetByTitle(options.TargetListTitle);
            if (!string.IsNullOrWhiteSpace(metadata?.Target?.ListTitle)) return web.Lists.GetByTitle(metadata.Target.ListTitle);
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
        public string MetadataJsonPath { get; private set; }
        public string EffectiveFormFieldXmlPath { get; internal set; }
        public string EffectiveMetadataJsonPath { get; internal set; }

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
                FormFieldXmlPath = Optional(map, "form-field-xml"),
                MetadataJsonPath = Optional(map, "metadata-json")
            };
            if (map.ContainsKey("target-list-id")) options.TargetListId = Guid.Parse(map["target-list-id"]);
            if (map.ContainsKey("start-manual")) options.StartManual = bool.Parse(map["start-manual"]);
            if (map.ContainsKey("start-created")) options.StartOnCreated = bool.Parse(map["start-created"]);
            if (map.ContainsKey("start-updated")) options.StartOnUpdated = bool.Parse(map["start-updated"]);
            if (map.ContainsKey("if-exists")) options.IfExists = ParseEnum<IfExistsPolicy>(map["if-exists"], "if-exists");
            if (!System.IO.File.Exists(options.XamlPath)) throw new FileNotFoundException("XAML file not found.", options.XamlPath);
            if (!string.IsNullOrWhiteSpace(options.FormFieldXmlPath) && !System.IO.File.Exists(options.FormFieldXmlPath)) throw new FileNotFoundException("FormField XML file not found.", options.FormFieldXmlPath);
            if (!string.IsNullOrWhiteSpace(options.MetadataJsonPath) && !System.IO.File.Exists(options.MetadataJsonPath)) throw new FileNotFoundException("Workflow metadata JSON file not found.", options.MetadataJsonPath);
            return options;
        }

        private static string Required(Dictionary<string, string> map, string key) => map.ContainsKey(key) && !string.IsNullOrWhiteSpace(map[key]) ? map[key] : throw new ArgumentException("--" + key + " is required.");
        private static string Optional(Dictionary<string, string> map, string key) => map.ContainsKey(key) ? map[key] : null;
        private static T ParseEnum<T>(string value, string name) where T : struct => Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new ArgumentException("Invalid --" + name + ": " + value);
    }

    internal sealed class PublisherWorkflowMetadata
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public PublisherWorkflowTarget Target { get; set; } = new PublisherWorkflowTarget();
        public PublisherWorkflowStartOptions Start { get; set; } = new PublisherWorkflowStartOptions();
        public PublisherWorkflowInitiation Initiation { get; set; } = new PublisherWorkflowInitiation();

        public static PublisherWorkflowMetadata FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new PublisherWorkflowMetadata();
            var raw = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object> ?? new Dictionary<string, object>();
            var metadata = new PublisherWorkflowMetadata { DisplayName = ReadString(raw, "displayName"), Description = ReadString(raw, "description") };
            if (ReadMap(raw, "target") is Dictionary<string, object> target) metadata.Target = new PublisherWorkflowTarget { Type = ReadTargetType(target, "type"), ListTitle = ReadString(target, "listTitle") };
            if (ReadMap(raw, "start") is Dictionary<string, object> start) metadata.Start = new PublisherWorkflowStartOptions { Manual = ReadBool(start, "manual"), OnCreated = ReadBool(start, "onCreated"), OnUpdated = ReadBool(start, "onUpdated") };
            if (ReadMap(raw, "initiation") is Dictionary<string, object> initiation)
            {
                metadata.Initiation = new PublisherWorkflowInitiation { Url = ReadString(initiation, "url") };
                metadata.Initiation.FormFields = ReadFormFields(initiation);
            }
            return metadata;
        }

        private static Dictionary<string, object> ReadMap(Dictionary<string, object> map, string key) => map.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;
        private static string ReadString(Dictionary<string, object> map, string key) => map.TryGetValue(key, out var value) && value != null ? Convert.ToString(value) : null;
        private static bool? ReadBool(Dictionary<string, object> map, string key) => map.TryGetValue(key, out var value) && value != null ? Convert.ToBoolean(value) : (bool?)null;
        private static TargetType? ReadTargetType(Dictionary<string, object> map, string key) => Enum.TryParse<TargetType>(ReadString(map, key), true, out var parsed) ? parsed : (TargetType?)null;
        private static List<PublisherFormField> ReadFormFields(Dictionary<string, object> map)
        {
            var fields = new List<PublisherFormField>();
            if (!map.TryGetValue("formFields", out var value) || !(value is object[] array)) return fields;
            foreach (var item in array)
            {
                if (!(item is Dictionary<string, object> raw)) continue;
                raw = new Dictionary<string, object>(raw, StringComparer.OrdinalIgnoreCase);
                fields.Add(new PublisherFormField { Name = ReadString(raw, "name"), FormType = ReadString(raw, "formType"), Type = ReadString(raw, "type"), DisplayName = ReadString(raw, "displayName"), Description = ReadString(raw, "description"), Direction = ReadString(raw, "direction"), Default = raw.TryGetValue("default", out var defaultValue) ? defaultValue : null, Format = ReadString(raw, "format"), BaseType = ReadString(raw, "baseType"), MaxLength = ReadString(raw, "maxLength"), NumLines = ReadString(raw, "numLines"), Sortable = ReadString(raw, "sortable"), RichTextMode = ReadString(raw, "richTextMode"), List = ReadString(raw, "list"), ShowField = ReadString(raw, "showField"), Mult = ReadString(raw, "mult"), UserSelectionMode = ReadString(raw, "userSelectionMode"), UserSelectionScope = ReadString(raw, "userSelectionScope"), Choices = ReadChoices(raw) });
            }
            return fields;
        }

        private static List<PublisherChoice> ReadChoices(Dictionary<string, object> map)
        {
            var choices = new List<PublisherChoice>();
            if (!map.TryGetValue("choices", out var value) || !(value is object[] array)) return choices;
            foreach (var item in array)
            {
                if (item is string text)
                {
                    choices.Add(new PublisherChoice { Value = text, DisplayName = text });
                }
                else if (item is Dictionary<string, object> raw)
                {
                    raw = new Dictionary<string, object>(raw, StringComparer.OrdinalIgnoreCase);
                    choices.Add(new PublisherChoice { Value = ReadString(raw, "value"), DisplayName = ReadString(raw, "displayName") });
                }
            }
            return choices;
        }
    }

    internal sealed class PublisherWorkflowStartOptions { public bool? Manual { get; set; } public bool? OnCreated { get; set; } public bool? OnUpdated { get; set; } }
    internal sealed class PublisherWorkflowTarget { public TargetType? Type { get; set; } public string ListTitle { get; set; } }
    internal sealed class PublisherWorkflowInitiation { public string Url { get; set; } public List<PublisherFormField> FormFields { get; set; } = new List<PublisherFormField>(); }
    internal sealed class PublisherChoice { public string Value { get; set; } public string DisplayName { get; set; } }
    internal sealed class PublisherFormField { public string Name { get; set; } public string FormType { get; set; } public string Type { get; set; } public string DisplayName { get; set; } public string Description { get; set; } public string Direction { get; set; } public object Default { get; set; } public string Format { get; set; } public string BaseType { get; set; } public string MaxLength { get; set; } public string NumLines { get; set; } public string Sortable { get; set; } public string RichTextMode { get; set; } public string List { get; set; } public string ShowField { get; set; } public string Mult { get; set; } public string UserSelectionMode { get; set; } public string UserSelectionScope { get; set; } public List<PublisherChoice> Choices { get; set; } = new List<PublisherChoice>(); }
}
