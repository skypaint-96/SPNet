using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xaml;
using System.Xml.Linq;
using Microsoft.VisualBasic.Activities;

namespace SPNet.Workflow.WfSerializer
{
    /// <summary>
    /// Builds SharePoint Designer-compatible WF activity trees from SPNet YAML and serializes them to XAML.
    /// </summary>
    public static partial class WfActivityBuilderSerializer
    {
        private const string SharePointProxyAssemblyName = "Microsoft.SharePoint.WorkflowServices.Activities.Proxy.dll";
        private const string MicrosoftActivitiesProxyAssemblyName = "Microsoft.Activities.Proxy.dll";
        private const string SpdTechnicalClassSuffix = ".MTW";
        private const string SpdNextSentinel = "4294967294";
        private const string SpdStageContainerAttribute = "StageContainer-8EDBFE6D-DA0D-42F6-A806-F5807380DA4D";
        private const string SpdStageHeaderAttribute = "StageHeader-7FE15537-DFDB-4198-ABFA-8AF8B9D669AE";
        private const string SpdStageFooterAttribute = "StageFooter-3A59FA7C-C493-47A1-8F8B-1F481143EB08";
        private const string SpdInitBlockAttribute = "InitBlock-7751C281-B0D1-4336-87B4-83F2198EDE6D";
        private const string SpdExpressionIdAttribute = "9DC0D15A-C39A-4859-B3B3-107B531800AD";
        private const string SpdEmptyDynamicValueArgumentName = "EmptyDictionary";
        private const string SpdRequestHeadersArgumentName = "varRequestHeaders";
        private static readonly XNamespace ActivitiesNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly XNamespace GenericCollectionsNamespace = "clr-namespace:System.Collections.Generic;assembly=mscorlib";
        private static readonly XNamespace MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        private static readonly XNamespace Workflow2012ActivitiesNamespace = "http://schemas.microsoft.com/workflow/2012/07/xaml/activities";
        private static readonly XNamespace SharePointNamespace = "clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities";
        private static readonly XNamespace SharePointExpressionNamespace = "clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities.Expressions";
        private static readonly XNamespace SharePointProxyNamespace = "clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy";
        private static readonly XNamespace SharePointExpressionProxyNamespace = "clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities.Expressions;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy";
        private static readonly XNamespace AuthoringNamespace = "clr-namespace:Microsoft.Web.Authoring.Workflow;assembly=Microsoft.Web.Authoring";

        /// <summary>
        /// Serializes a minimal proof-of-concept workflow that exercises the legacy SharePoint Designer serializer path.
        /// </summary>
        /// <param name="options">Serializer options containing the workflow name, output path, and WebsiteCache location.</param>
        public static void SerializeSampleWorkflow(WfSerializerOptions options)
        {
            var cacheFolder = Path.GetFullPath(options.CacheFolder);
            var outputPath = Path.GetFullPath(options.OutputXamlPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);

            using (new CacheAssemblyResolver(cacheFolder))
            {
                LoadManagedDlls(cacheFolder);

                var sharePointAssembly = LoadRequiredAssembly(cacheFolder, SharePointProxyAssemblyName);
                var microsoftActivitiesAssembly = LoadRequiredAssembly(cacheFolder, MicrosoftActivitiesProxyAssemblyName);

                var proxyTypes = new ProxyActivityTypeCatalog(sharePointAssembly, microsoftActivitiesAssembly);

                var builder = new WorkflowActivityBuilder(BuildAction).BuildProofOfConcept(options.WorkflowName, GetDottedWorkflowClassName(options.WorkflowName), proxyTypes.Calc, proxyTypes.WriteToHistory, proxyTypes.ToStringExpression);
                File.WriteAllText(outputPath, AddSharePointDesignerMetadata(SerializeBuilder(builder), options.WorkflowName));
            }
        }

        /// <summary>
        /// Loads an SPNet workflow YAML file, builds the corresponding WF activity tree, and writes Designer-compatible XAML.
        /// </summary>
        /// <param name="workflowYamlPath">Path to the SPNet workflow YAML file.</param>
        /// <param name="outputXamlPath">Path where the generated XAML should be written.</param>
        /// <param name="cacheFolder">SharePoint Designer WebsiteCache folder containing required proxy assemblies.</param>
        /// <param name="config">Tool configuration loaded from defaults and local settings.</param>
        public static void SerializeYamlWorkflow(string workflowYamlPath, string outputXamlPath, string cacheFolder, SpNetToolConfig config)
        {
            var workflow = WorkflowYaml.Load(workflowYamlPath);
            SerializeWorkflow(BuildWorkflowFromYaml(workflow, cacheFolder), outputXamlPath, cacheFolder, workflow);
            WriteParameterFormFieldSidecar(workflow, outputXamlPath);
        }

        /// <summary>
        /// Performs a lightweight structural export from workflow XAML to SPNet YAML for inspection and diagnostics.
        /// </summary>
        /// <param name="inputXamlPath">Path to the workflow XAML file.</param>
        /// <param name="outputYamlPath">Path where the exported YAML should be written.</param>

        private static ActivityBuilder BuildWorkflowFromYaml(WorkflowYaml workflow, string cacheFolder)
        {
            var resolvedCacheFolder = Path.GetFullPath(cacheFolder);
            using (new CacheAssemblyResolver(resolvedCacheFolder))
            {
                LoadManagedDlls(resolvedCacheFolder);
                var sharePointAssembly = LoadRequiredAssembly(resolvedCacheFolder, SharePointProxyAssemblyName);
                var microsoftActivitiesAssembly = LoadRequiredAssembly(resolvedCacheFolder, MicrosoftActivitiesProxyAssemblyName);
                var proxyTypes = new ProxyActivityTypeCatalog(sharePointAssembly, microsoftActivitiesAssembly);
                var valueExpressionTypes = proxyTypes.CreateValueExpressionTypes();

                var variableTypes = WorkflowVariableTypeInferer.Infer(workflow, proxyTypes.DynamicValue, SpdEmptyDynamicValueArgumentName, SpdRequestHeadersArgumentName);
                var parameterTypes = workflow.EffectiveFormFields.ToDictionary(p => p.Name, WorkflowTypeMapper.MapParameterType, StringComparer.OrdinalIgnoreCase);
                ValidateAssignments(workflow, variableTypes);

                var buildContext = new WorkflowActivityBuildContext(proxyTypes.CreateBuildContextTypes(), valueExpressionTypes, proxyTypes.ComparisonExpressionTypes, proxyTypes.DynamicValue, variableTypes, parameterTypes);
                return new WorkflowActivityBuilder(BuildAction).Build(workflow, buildContext);
            }
        }

        private static Activity BuildAction(WorkflowActionYaml action, WorkflowActivityBuildContext context)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (ActionBuilders.TryGetValue(action.GetType(), out var builder)) return builder(action, context);
            throw new InvalidOperationException("Unsupported action type: " + action.Type);
        }

        private static void WriteParameterFormFieldSidecar(WorkflowYaml workflow, string outputXamlPath)
        {
            var formFields = workflow.EffectiveFormFields;
            var metadata = workflow.ToEffectiveMetadata();
            var metadataOutputPath = Path.GetFullPath(outputXamlPath + ".metadata.json");
            Directory.CreateDirectory(Path.GetDirectoryName(metadataOutputPath) ?? Environment.CurrentDirectory);
            File.WriteAllText(metadataOutputPath, metadata.ToJson());
            if (formFields.Count == 0) return;
            var outputPath = Path.GetFullPath(outputXamlPath + ".formfield.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
            File.WriteAllText(outputPath, WorkflowParameterFormFieldSerializer.Serialize(formFields));
        }

        private delegate Activity ActionBuilder(WorkflowActionYaml action, WorkflowActivityBuildContext context);

        private static readonly IReadOnlyDictionary<Type, ActionBuilder> ActionBuilders = CreateActionBuilders();

        private static IReadOnlyDictionary<Type, ActionBuilder> CreateActionBuilders()
        {
            var builders = new Dictionary<Type, ActionBuilder>();
            Register<CalcActionYaml>(builders, (action, context) => BuildCalc(action, context.GetProxyActivityType("Calc"), context.ValueExpressionTypes));
            Register<WriteHistoryActionYaml>(builders, (action, context) => BuildWriteHistory(action, context.GetProxyActivityType("WriteToHistory"), context.ValueExpressionTypes));
            Register<SetStatusActionYaml>(builders, (action, context) => BuildSetStatus(action, context.GetProxyActivityType("SetWorkflowStatus")));
            Register<CommentActionYaml>(builders, (action, context) => BuildComment(action, context.GetProxyActivityType("Comment"), context.ValueExpressionTypes));
            Register<DelayForActionYaml>(builders, (action, context) => BuildDelayFor(action, context.GetProxyActivityType("DelayFor"), context.ValueExpressionTypes));
            Register<DelayUntilActionYaml>(builders, (action, context) => BuildDelayUntil(action, context.GetProxyActivityType("DelayUntil"), context.ValueExpressionTypes));
            Register<AssignActionYaml>(builders, (action, context) => BuildAssign(action, context.ValueExpressionTypes, context.VariableTypes));
            Register<StringReplaceActionYaml>(builders, (action, context) => BuildStringReplace(action, context.ValueExpressionTypes, context.VariableTypes));
            Register<StringSubstringActionYaml>(builders, (action, context) => BuildStringSubstring(action, context.ValueExpressionTypes, context.VariableTypes));
            Register<StringTrimActionYaml>(builders, (action, context) => BuildStringTrim(action, context.ValueExpressionTypes, context.VariableTypes));
            Register<LookupWorkflowContextActionYaml>(builders, (action, context) => BuildLookupWorkflowContext(action, context.GetProxyActivityType("LookupWorkflowContextProperty")));
            Register<GetCurrentListIdActionYaml>(builders, (action, context) => BuildGetCurrentListId(action, context.GetProxyActivityType("GetCurrentListId")));
            Register<GetCurrentItemGuidActionYaml>(builders, (action, context) => BuildGetCurrentItemGuid(action, context.GetProxyActivityType("GetCurrentItemGuid")));
            Register<SetFieldActionYaml>(builders, (action, context) => BuildSetField(action, context.GetProxyActivityType("SetField"), context.ValueExpressionTypes));
            Register<CreateListItemActionYaml>(builders, (action, context) => BuildCreateListItem(action, context.GetProxyActivityType("CreateListItem"), context.ValueExpressionTypes));
            Register<UpdateListItemActionYaml>(builders, (action, context) => BuildUpdateListItem(action, context.GetProxyActivityType("UpdateListItem"), context.ValueExpressionTypes));
            Register<DeleteListItemActionYaml>(builders, (action, context) => BuildDeleteListItem(action, context.GetProxyActivityType("DeleteListItem"), context.ValueExpressionTypes));
            Register<LookupListItemStringPropertyActionYaml>(builders, (action, context) => BuildLookupListItemStringProperty(action, context.GetProxyActivityType("LookupSPListItemStringProperty"), context.ValueExpressionTypes, context.VariableTypes));
            Register<LookupListItemIntPropertyActionYaml>(builders, (action, context) => BuildLookupListItemIntProperty(action, context.GetOptionalProxyActivityType("LookupSPListItemIntProperty"), context.ValueExpressionTypes, context.VariableTypes));
            Register<CallHttpWebServiceActionYaml>(builders, (action, context) => BuildCallHttpWebService(action, context.GetProxyActivityType("CallHTTPWebService"), context.DynamicValueType, context.ValueExpressionTypes));
            Register<SendEmailActionYaml>(builders, (action, context) => BuildSendEmail(action, context.GetProxyActivityType("Email"), context.GetProxyActivityType("ExpandInitFormUsers"), context.ValueExpressionTypes));
            Register<SingleTaskActionYaml>(builders, (action, context) => BuildSingleTask(action, context.GetProxyActivityType("SingleTask"), context.ValueExpressionTypes));
            Register<LookupRestPropertyNameActionYaml>(builders, (action, context) => BuildLookupRestPropertyName(action, context.GetProxyActivityType("LookupSPListItemPropertyNameInREST"), context.ValueExpressionTypes));
            Register<GetDynamicValuePropertyActionYaml>(builders, (action, context) => BuildGetDynamicValueProperty(action, context.GetProxyActivityType("GetDynamicValueProperty"), context.DynamicValueType, context.ValueExpressionTypes, context.VariableTypes));
            Register<BuildDynamicValueActionYaml>(builders, (action, context) => BuildDynamicValue(action, context.ValueExpressionTypes, context.DynamicValueType, context.VariableTypes));
            Register<WhileActionYaml>(builders, BuildWhile);
            Register<IfActionYaml>(builders, BuildIf);
            return builders;
        }

        private static void Register<TAction>(IDictionary<Type, ActionBuilder> builders, Func<TAction, WorkflowActivityBuildContext, Activity> builder)
            where TAction : WorkflowActionYaml
        {
            builders.Add(typeof(TAction), (action, context) => builder((TAction)action, context));
        }

        public static bool CanBuildActionForTest(Type actionType)
        {
            if (actionType == null) throw new ArgumentNullException(nameof(actionType));
            return ActionBuilders.ContainsKey(actionType);
        }

        /// <summary>
        /// Inspects workflow XAML using WF deserialization when possible, with a structural fallback when deserialization fails.
        /// </summary>
        /// <param name="inputXamlPath">Path to the workflow XAML file.</param>
        /// <param name="cacheFolder">SharePoint Designer WebsiteCache folder containing required proxy assemblies.</param>
        /// <returns>A text report describing the workflow structure.</returns>
        public static string InspectWorkflowXaml(string inputXamlPath, string cacheFolder)
        {
            if (string.IsNullOrWhiteSpace(inputXamlPath)) throw new ArgumentException("Input XAML path is required.", nameof(inputXamlPath));
            if (!File.Exists(inputXamlPath)) throw new FileNotFoundException("Input XAML not found: " + inputXamlPath, inputXamlPath);
            if (string.IsNullOrWhiteSpace(cacheFolder)) throw new ArgumentException("SharePoint Designer cache folder is required.", nameof(cacheFolder));
            if (!Directory.Exists(cacheFolder)) throw new DirectoryNotFoundException("Cache folder not found: " + cacheFolder);

            using (new CacheAssemblyResolver(Path.GetFullPath(cacheFolder)))
            {
                LoadManagedDlls(Path.GetFullPath(cacheFolder));
                var xaml = PrepareXamlForDeserialization(File.ReadAllText(inputXamlPath));
                try
                {
                    using (var textReader = new StringReader(xaml))
                    using (var reader = new XamlXmlReader(textReader))
                    using (var builderReader = ActivityXamlServices.CreateBuilderReader(reader))
                    {
                        var loaded = XamlServices.Load(builderReader);
                        var builder = loaded as ActivityBuilder;
                        var report = new StringBuilder();
                        report.AppendLine("LoadedType: " + loaded.GetType().FullName);
                        if (builder != null)
                        {
                            report.AppendLine("ActivityBuilder.Name: " + builder.Name);
                            report.AppendLine("Properties: " + builder.Properties.Count);
                            foreach (var property in builder.Properties) report.AppendLine("  Property: " + property.Name + " Type=" + property.Type);
                            report.AppendLine("Implementation:");
                            AppendActivity(report, builder.Implementation, 1);
                        }
                        else if (loaded is Activity activity)
                        {
                            report.AppendLine("Activity.DisplayName: " + activity.DisplayName);
                            AppendActivity(report, activity, 1);
                        }
                        return report.ToString();
                    }
                }
                catch (Exception ex)
                {
                    return InspectWorkflowXamlStructurally(File.ReadAllText(inputXamlPath), ex);
                }
            }
        }

        private static string InspectWorkflowXamlStructurally(string xaml, Exception deserializationException)
        {
            var document = XDocument.Parse(xaml);
            var report = new StringBuilder();
            report.AppendLine("WF deserialization failed: " + deserializationException.Message);
            report.AppendLine("Structural XAML inspection fallback:");
            var root = document.Root;
            report.AppendLine("Root: " + root?.Name.LocalName + " Class=" + (string?)root?.Attribute(XamlNamespace + "Class"));
            var members = root?.Element(XamlNamespace + "Members")?.Elements(XamlNamespace + "Property").ToList() ?? new System.Collections.Generic.List<XElement>();
            report.AppendLine("Properties: " + members.Count);
            foreach (var property in members.Take(40)) report.AppendLine("  Property: " + (string?)property.Attribute("Name") + " Type=" + (string?)property.Attribute("Type"));
            var activityElements = document.Descendants().Where(e => e.Name.NamespaceName != XamlNamespace.NamespaceName && e.Name.NamespaceName != AuthoringNamespace.NamespaceName && e.Name.LocalName != "Dictionary" && e.Name.LocalName != "String").ToList();
            report.AppendLine("Activity-like elements: " + activityElements.Count);
            foreach (var element in activityElements.Take(120)) report.AppendLine("  " + element.Name + " DisplayName=" + (string?)element.Attribute("DisplayName"));
            return report.ToString();
        }

        /// <summary>
        /// Serializes an activity builder to XAML and applies SharePoint Designer metadata normalization.
        /// </summary>
        /// <param name="builder">WF activity builder to serialize.</param>
        /// <param name="outputXamlPath">Path where the generated XAML should be written.</param>
        /// <param name="cacheFolder">SharePoint Designer WebsiteCache folder containing required proxy assemblies.</param>
        public static void SerializeWorkflow(ActivityBuilder builder, string outputXamlPath, string cacheFolder)
        {
            SerializeWorkflow(builder, outputXamlPath, cacheFolder, null);
        }

        private static void SerializeWorkflow(ActivityBuilder builder, string outputXamlPath, string cacheFolder, WorkflowYaml? sourceWorkflow)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (string.IsNullOrWhiteSpace(outputXamlPath)) throw new ArgumentException("Output XAML path is required.", nameof(outputXamlPath));
            if (string.IsNullOrWhiteSpace(cacheFolder)) throw new ArgumentException("SharePoint Designer cache folder is required.", nameof(cacheFolder));
            if (!Directory.Exists(cacheFolder)) throw new DirectoryNotFoundException("Cache folder not found: " + cacheFolder);

            var resolvedCacheFolder = Path.GetFullPath(cacheFolder);
            var outputPath = Path.GetFullPath(outputXamlPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);

            using (new CacheAssemblyResolver(resolvedCacheFolder))
            {
                LoadManagedDlls(resolvedCacheFolder);
                LoadRequiredAssembly(resolvedCacheFolder, MicrosoftActivitiesProxyAssemblyName);
                File.WriteAllText(outputPath, AddSharePointDesignerMetadata(SerializeBuilder(builder), builder.Name, sourceWorkflow));
            }
        }

        /// <summary>
        /// Adds the SharePoint Designer metadata and namespace shape required for Designer to render generated workflow XAML.
        /// </summary>
        /// <param name="xaml">Raw WF XAML generated by the serializer.</param>
        /// <param name="workflowName">Friendly workflow name used for default stage metadata.</param>
        /// <returns>XAML with SharePoint Designer metadata applied.</returns>
        public static string AddSharePointDesignerMetadata(string xaml, string workflowName)
        {
            return AddSharePointDesignerMetadata(xaml, workflowName, null);
        }

        private static string AddSharePointDesignerMetadata(string xaml, string workflowName, WorkflowYaml? sourceWorkflow)
        {
            if (string.IsNullOrWhiteSpace(xaml)) throw new ArgumentException("Workflow XAML is required.", nameof(xaml));

            var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            var root = document.Root ?? throw new InvalidOperationException("Workflow XAML has no root element.");

            RejectRawLanguageExpressionActivities(document);

            EnsureNamespace(root, "mc", MarkupCompatibilityNamespace);
            EnsureNamespace(root, "mwaw", AuthoringNamespace);
            EnsureNamespace(root, "scg", "clr-namespace:System.Collections.Generic;assembly=mscorlib");
            EnsureNamespace(root, "local", SharePointNamespace);
            EnsureNamespace(root, "p", Workflow2012ActivitiesNamespace);
            EnsureIgnorablePrefix(root, "mwaw");
            NormalizeSharePointActivityNamespaces(document);
            RemoveEmptyRootCustomAttributes(root);

            RemoveTopLevelSequenceDisplayName(root);
            EnsureLifecycleListItemPropertiesElements(document);
            EnsureInitBlock(document, root);
            EnsureExpressionIds(document, sourceWorkflow == null ? null : CreateDesignerExpressionIdMap(sourceWorkflow));
            EnsureConditionExpressionResultPlaceholders(document);
            EnsureOperandExpressionResultPlaceholders(document);
            EnsureConditionOperandDesignerArgumentShape(document);

            var firstStageContainer = document.Descendants(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes")
                .Any(e => e.Descendants(XamlNamespace + "String").Any(s => ((string?)s.Attribute(XamlNamespace + "Key")) == "StageAttribute" && ((string?)s ?? string.Empty).StartsWith("StageContainer-", StringComparison.OrdinalIgnoreCase)));
            if (firstStageContainer) return document.ToString(SaveOptions.DisableFormatting);

            var flowStep = document.Descendants(ActivitiesNamespace + "FlowStep").FirstOrDefault(e => e.Elements(ActivitiesNamespace + "Sequence").Any());
            if (flowStep == null) return document.ToString(SaveOptions.DisableFormatting);

            var stage = flowStep.Elements(ActivitiesNamespace + "Sequence").First();
            var stageName = (string?)stage.Attribute("DisplayName") ?? workflowName ?? "Stage 1";
            stage.Attribute("DisplayName")?.Remove();

            var originalChildren = stage.Nodes().ToList();
            stage.RemoveNodes();
            EnsureFlowStepNextAttribute(flowStep);
            stage.Add(CreateCustomAttributes(SpdStageContainerAttribute));
            stage.Add(new XElement(SharePointNamespace + "SetWorkflowStatus",
                new XAttribute("Disabled", "False"),
                new XAttribute("Status", stageName),
                CreateCustomAttributes(SpdStageHeaderAttribute)));

            var body = new XElement(ActivitiesNamespace + "Sequence", new XAttribute("DisplayName", stageName));
            body.Add(originalChildren);
            stage.Add(body);
            stage.Add(new XElement(ActivitiesNamespace + "Sequence",
                new XAttribute("DisplayName", stageName + " Footer"),
                CreateCustomAttributes(SpdStageFooterAttribute)));

            return document.ToString(SaveOptions.DisableFormatting);
        }

        private static void RejectRawLanguageExpressionActivities(XDocument document)
        {
            // SharePoint Workflow Manager validation rejects raw VB/C# WF language expressions because they require compilation.
            // Generated SPNet workflows must use structured SharePoint/Microsoft.Activities proxy expression activities instead.
            // Exporter/parser code may still read these names for legacy/downloaded-XAML compatibility, but build output may not contain them.
            var forbidden = document.Descendants()
                .Select(e => e.Name.LocalName)
                .FirstOrDefault(n => n == "VisualBasicValue" || n == "VisualBasicReference" || n == "CSharpValue" || n == "CSharpReference");
            if (forbidden != null) throw new InvalidOperationException("Generated SharePoint workflow XAML must not contain raw WF language expression activity '" + forbidden + "'. Use structured SharePoint/Microsoft.Activities expression activities instead; raw VB/C# expressions require compilation and are rejected by Workflow Manager validation.");
        }

        private static void EnsureLifecycleListItemPropertiesElements(XDocument document)
        {
            foreach (var activity in document.Descendants().Where(e => e.Name.LocalName == "CreateListItem" || e.Name.LocalName == "UpdateListItem").ToList())
            {
                var attribute = activity.Attribute("ListItemProperties");
                if (attribute == null || attribute.Value.IndexOf("Dictionary(Of String, Object) From", StringComparison.Ordinal) < 0) continue;
                var propertyElementName = activity.Name.Namespace + (activity.Name.LocalName + ".ListItemProperties");
                if (activity.Element(propertyElementName) != null) { attribute.Remove(); continue; }

                var valuesElement = new XElement(Workflow2012ActivitiesNamespace + "BuildDictionary.Values");
                foreach (var entry in ParseSerializedDictionaryEntries(attribute.Value))
                {
                    valuesElement.Add(new XElement(ActivitiesNamespace + "InArgument",
                        new XAttribute(XamlNamespace + "TypeArguments", "x:Object"),
                        new XAttribute(XamlNamespace + "Key", entry.Key),
                        new XElement(ActivitiesNamespace + "Cast",
                            new XAttribute(XamlNamespace + "TypeArguments", "x:String, x:Object"),
                            new XAttribute("Operand", entry.Value),
                            new XElement(ActivitiesNamespace + "Cast.Result",
                                new XElement(ActivitiesNamespace + "OutArgument", new XAttribute(XamlNamespace + "TypeArguments", "x:Object"))))));
                }

                activity.Add(new XElement(propertyElementName,
                    new XElement(ActivitiesNamespace + "InArgument",
                        new XAttribute(XamlNamespace + "TypeArguments", "scg:IDictionary(x:String, x:Object)"),
                        new XElement(Workflow2012ActivitiesNamespace + "BuildDictionary",
                            new XAttribute(XamlNamespace + "TypeArguments", "x:String, x:Object"),
                            new XAttribute("Dictionary", "{x:Null}"),
                            valuesElement))));
                attribute.Remove();
            }
        }

        private static System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> ParseSerializedDictionaryEntries(string expression)
        {
            var bodyStart = expression.IndexOf("From {{", StringComparison.Ordinal);
            var bodyEnd = expression.LastIndexOf("}}]", StringComparison.Ordinal);
            if (bodyStart < 0 || bodyEnd <= bodyStart) yield break;
            var body = expression.Substring(bodyStart + "From {{".Length, bodyEnd - bodyStart - "From {{".Length);
            foreach (var rawEntry in body.Split(new[] { "}, {" }, StringSplitOptions.None))
            {
                var parts = rawEntry.Split(new[] { "\", \"" }, StringSplitOptions.None);
                if (parts.Length != 2) continue;
                yield return new System.Collections.Generic.KeyValuePair<string, string>(parts[0].Trim('"'), parts[1].Trim('"'));
            }
        }

        private static XElement CreateCustomAttributes(string stageAttributeValue) =>
            new XElement(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes",
                new XElement(GenericCollectionsNamespace + "Dictionary",
                    new XAttribute(XamlNamespace + "TypeArguments", "x:String, x:String"),
                    new XElement(XamlNamespace + "String",
                        new XAttribute(XamlNamespace + "Key", "StageAttribute"),
                        stageAttributeValue)));

        private static void RemoveEmptyRootCustomAttributes(XElement root)
        {
            foreach (var attributes in root.Elements(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes").ToList())
            {
                if (!attributes.Descendants(XamlNamespace + "String").Any()) attributes.Remove();
            }
        }

        private static void EnsureFlowStepNextAttribute(XElement flowStep)
        {
            var existing = flowStep.Elements(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes")
                .Any(e => e.Descendants(XamlNamespace + "String").Any(s => ((string?)s.Attribute(XamlNamespace + "Key")) == "Next"));
            if (existing) return;
            flowStep.AddFirst(new XElement(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes",
                new XElement(GenericCollectionsNamespace + "Dictionary",
                    new XAttribute(XamlNamespace + "TypeArguments", "x:String, x:String"),
                    new XElement(XamlNamespace + "String", new XAttribute(XamlNamespace + "Key", "Next"), SpdNextSentinel))));
        }

        private static void RemoveTopLevelSequenceDisplayName(XElement root)
        {
            var topSequence = root.Elements(ActivitiesNamespace + "Sequence").FirstOrDefault();
            topSequence?.Attribute("DisplayName")?.Remove();
        }

        private static void EnsureInitBlock(XDocument document, XElement root)
        {
            var hasInitBlock = document.Descendants(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes")
                .Any(e => e.Descendants(XamlNamespace + "String").Any(s => ((string?)s.Attribute(XamlNamespace + "Key")) == "InitBlock"));
            if (hasInitBlock) return;
            var topSequence = root.Elements(ActivitiesNamespace + "Sequence").FirstOrDefault();
            if (topSequence == null) return;
            var initBlock = new XElement(ActivitiesNamespace + "Sequence",
                CreateCustomDictionaryAttribute("InitBlock", SpdInitBlockAttribute));
            var lastPropertyElement = topSequence.Elements().LastOrDefault(e => e.Name.LocalName.Contains("."));
            if (lastPropertyElement == null) topSequence.AddFirst(initBlock);
            else lastPropertyElement.AddAfterSelf(initBlock);
        }

        public static string AddSharePointDesignerMetadataForTest(string xaml, string workflowName, WorkflowYaml sourceWorkflow)
        {
            return AddSharePointDesignerMetadata(xaml, workflowName, sourceWorkflow);
        }

        private static void EnsureExpressionIds(XDocument document, IReadOnlyDictionary<string, Queue<string>>? preservedIds)
        {
            foreach (var expression in document.Descendants().Where(IsExpressionActivityElement).ToList())
            {
                var existing = expression.Elements(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes")
                    .Any(e => e.Descendants(XamlNamespace + "String").Any(s => string.Equals((string?)s.Attribute(XamlNamespace + "Key"), "Id", StringComparison.OrdinalIgnoreCase)));
                if (existing) continue;
                var id = TryDequeueDesignerId(preservedIds, expression.Name.LocalName) ?? SpdExpressionIdAttribute;
                expression.AddFirst(CreateCustomDictionaryAttribute("Id", id));
            }
        }

        private static string? TryDequeueDesignerId(IReadOnlyDictionary<string, Queue<string>>? preservedIds, string activityName)
        {
            if (preservedIds == null || !preservedIds.TryGetValue(activityName, out var ids) || ids.Count == 0) return null;
            return ids.Dequeue();
        }

        private static IReadOnlyDictionary<string, Queue<string>> CreateDesignerExpressionIdMap(WorkflowYaml workflow)
        {
            var ids = new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var expression in EnumerateExpressions(workflow.Stages.SelectMany(s => s.Actions ?? new List<WorkflowActionYaml>())))
            {
                var activityName = GetDesignerExpressionActivityName(expression);
                if (activityName == null || string.IsNullOrWhiteSpace(expression.DesignerId)) continue;
                if (!ids.TryGetValue(activityName, out var queue)) ids[activityName] = queue = new Queue<string>();
                queue.Enqueue(expression.DesignerId);
            }

            return ids;
        }

        private static IEnumerable<ExpressionYaml> EnumerateExpressions(IEnumerable<WorkflowActionYaml> actions)
        {
            foreach (var action in actions)
            {
                foreach (var expression in EnumerateExpressions(action)) yield return expression;
                if (action is IfActionYaml ifAction)
                {
                    foreach (var expression in EnumerateExpressions(ifAction.Then ?? new List<WorkflowActionYaml>())) yield return expression;
                    foreach (var expression in EnumerateExpressions(ifAction.Else ?? new List<WorkflowActionYaml>())) yield return expression;
                }
                else if (action is WhileActionYaml whileAction)
                {
                    foreach (var expression in EnumerateExpressions(whileAction.Actions ?? new List<WorkflowActionYaml>())) yield return expression;
                }
            }
        }

        private static IEnumerable<ExpressionYaml> EnumerateExpressions(WorkflowActionYaml action)
        {
            if (action is IfActionYaml ifAction) foreach (var expression in EnumerateExpressions(ifAction.Condition)) yield return expression;
            if (action is WhileActionYaml whileAction) foreach (var expression in EnumerateExpressions(whileAction.Condition)) yield return expression;
        }

        private static IEnumerable<ExpressionYaml> EnumerateExpressions(ComparisonExpressionYaml condition)
        {
            foreach (var expression in EnumerateExpressions(condition.Left)) yield return expression;
            foreach (var expression in EnumerateExpressions(condition.Right)) yield return expression;
            if (condition.LeftCondition != null) foreach (var expression in EnumerateExpressions(condition.LeftCondition)) yield return expression;
            if (condition.RightCondition != null) foreach (var expression in EnumerateExpressions(condition.RightCondition)) yield return expression;
            if (condition.Operand != null) foreach (var expression in EnumerateExpressions(condition.Operand)) yield return expression;
        }

        private static IEnumerable<ExpressionYaml> EnumerateExpressions(ExpressionYaml? expression)
        {
            if (expression == null) yield break;
            yield return expression;
            foreach (var child in EnumerateExpressions(expression.ListId)) yield return child;
            foreach (var child in EnumerateExpressions(expression.ItemId)) yield return child;
            foreach (var child in EnumerateExpressions(expression.ItemGuid)) yield return child;
            foreach (var child in EnumerateExpressions(expression.Value)) yield return child;
            foreach (var value in expression.Values ?? new List<ExpressionYaml>()) foreach (var child in EnumerateExpressions(value)) yield return child;
            foreach (var child in EnumerateExpressions(expression.ToString)) yield return child;
        }

        private static string? GetDesignerExpressionActivityName(ExpressionYaml expression)
        {
            var type = (expression.Type ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (type == "parsedate") return "ParseDate";
            if (type == "parsedynamicvalue") return "ParseDynamicValue";
            if (expression.ToString != null) return "ToString";
            return null;
        }

        private static bool IsExpressionActivityElement(XElement element)
        {
            if (element.Name.NamespaceName == "http://schemas.microsoft.com/workflow/2012/07/xaml/activities")
            {
                switch (element.Name.LocalName)
                {
                    case "ToString":
                    case "ParseDate":
                    case "ParseDynamicValue":
                        return true;
                }
            }

            // SPD-authored date-equals-ignoring-time conditions do not attach SPDesignerXamlWriter.CustomAttributes
            // directly to the ConvertTimeZoneFromSPLocalToUtc wrapper. The designer-visible expression identity belongs
            // to the nested ParseDate operand only; adding an Id sibling on the timezone wrapper makes the subtree differ
            // from Designer output and can hide the RHS token in SharePoint Designer.
            return false;
        }

        private static void EnsureConditionExpressionResultPlaceholders(XDocument document)
        {
            foreach (var condition in document.Descendants().Where(e => e.Name.LocalName == "If.Condition" || e.Name.LocalName == "While.Condition"))
            {
                foreach (var expression in condition.Descendants().Where(IsBooleanConditionExpressionElement))
                {
                    if (expression.Attribute("Result") == null) expression.SetAttributeValue("Result", "{x:Null}");
                }
            }
        }

        private static void EnsureOperandExpressionResultPlaceholders(XDocument document)
        {
            foreach (var condition in document.Descendants().Where(e => e.Name.LocalName == "If.Condition" || e.Name.LocalName == "While.Condition"))
            {
                foreach (var expression in condition.Descendants().Where(IsOperandExpressionElement))
                {
                    if (expression.Attribute("Result") == null) expression.SetAttributeValue("Result", "{x:Null}");
                }
            }
        }

        private static bool IsBooleanConditionExpressionElement(XElement element)
        {
            if (element.Name.Namespace == Workflow2012ActivitiesNamespace)
            {
                switch (element.Name.LocalName)
                {
                    case "And":
                    case "Or":
                    case "Not":
                    case "IsEqualString":
                    case "IsEqualBoolean":
                    case "IsEqualNumber":
                    case "ContainsString":
                    case "StartsWithString":
                    case "EndsWithString":
                    case "IsLessThan":
                    case "IsGreaterThan":
                    case "IsLessThanOrEqual":
                    case "IsGreaterThanOrEqual":
                    case "ParseDynamicValue":
                        return true;
                }
            }

            if (element.Name.Namespace.NamespaceName.StartsWith("clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities.Expressions", StringComparison.Ordinal))
            {
                switch (element.Name.LocalName)
                {
                    case "IsEqualDate":
                    case "IsGreaterThanDateTime":
                    case "IsGreaterThanOrEqualDateTime":
                    case "IsLessThanDateTime":
                    case "IsLessThanOrEqualDateTime":
                    case "IsEqualDynamicValue":
                        return true;
                }
            }

            return false;
        }

        private static bool IsOperandExpressionElement(XElement element)
        {
            if (element.Name.Namespace == Workflow2012ActivitiesNamespace)
            {
                switch (element.Name.LocalName)
                {
                    case "ParseDate":
                    case "ParseDynamicValue":
                        return true;
                }
            }

            return element.Name.Namespace.NamespaceName.StartsWith("clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities", StringComparison.Ordinal) &&
                element.Name.LocalName == "ConvertTimeZoneFromSPLocalToUtc";
        }

        private static void EnsureConditionOperandDesignerArgumentShape(XDocument document)
        {
            foreach (var condition in document.Descendants().Where(e => e.Name.LocalName == "If.Condition" || e.Name.LocalName == "While.Condition"))
            {
                foreach (var argumentValue in condition.Descendants().Where(e => e.Name.LocalName == "ArgumentValue").ToList())
                {
                    EnsureArgumentValueResult(argumentValue);
                }

                foreach (var parseDate in condition.Descendants(Workflow2012ActivitiesNamespace + "ParseDate").ToList())
                {
                    EnsureParseDateDesignerCultureName(parseDate);
                }
            }
        }

        private static void EnsureArgumentValueResult(XElement argumentValue)
        {
            var typeArguments = ((string?)argumentValue.Attribute(XamlNamespace + "TypeArguments")) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeArguments)) return;
            var resultElementName = argumentValue.Name.Namespace + (argumentValue.Name.LocalName + ".Result");
            if (argumentValue.Elements().Any(e => e.Name.LocalName == "ArgumentValue.Result")) return;
            argumentValue.Add(new XElement(resultElementName,
                new XElement(ActivitiesNamespace + "OutArgument",
                    new XAttribute(XamlNamespace + "TypeArguments", typeArguments))));
        }

        private static void EnsureParseDateDesignerCultureName(XElement parseDate)
        {
            if (parseDate.Elements().Any(e => e.Name.LocalName == "ParseDate.CultureName")) return;
            var cultureAttribute = parseDate.Attribute("CultureName");
            if (cultureAttribute == null) return;
            cultureAttribute.Remove();
            var customAttributes = parseDate.Elements(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes").ToList();
            foreach (var customAttribute in customAttributes) customAttribute.Remove();
            parseDate.AddFirst(new XElement(Workflow2012ActivitiesNamespace + "ParseDate.CultureName",
                new XElement(ActivitiesNamespace + "InArgument",
                    new XAttribute(XamlNamespace + "TypeArguments", "x:String"),
                    new XElement(Workflow2012ActivitiesNamespace + "GetConfigurationValue",
                        new XAttribute("DefaultValue", "{x:Null}"),
                        new XAttribute("Name", "Microsoft.SharePoint.ActivationProperties.CultureName"),
                        new XAttribute("Result", "{x:Null}")))));
            foreach (var customAttribute in customAttributes)
            {
                parseDate.Elements().First(e => e.Name.LocalName == "ParseDate.CultureName").AddAfterSelf(customAttribute);
            }
        }

        private static XElement CreateCustomDictionaryAttribute(string key, string value) =>
            new XElement(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes",
                new XElement(GenericCollectionsNamespace + "Dictionary",
                    new XAttribute(XamlNamespace + "TypeArguments", "x:String, x:String"),
                    new XElement(XamlNamespace + "String", new XAttribute(XamlNamespace + "Key", key), value)));

        private static void EnsureNamespace(XElement root, string prefix, XNamespace xmlNamespace)
        {
            var attributeName = XNamespace.Xmlns + prefix;
            if (root.Attribute(attributeName) == null) root.SetAttributeValue(attributeName, xmlNamespace.NamespaceName);
        }

        private static void EnsureIgnorablePrefix(XElement root, string prefix)
        {
            var attributeName = MarkupCompatibilityNamespace + "Ignorable";
            var existing = ((string?)root.Attribute(attributeName) ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!existing.Contains(prefix)) existing.Add(prefix);
            root.SetAttributeValue(attributeName, string.Join(" ", existing));
        }

        private static void NormalizeSharePointActivityNamespaces(XDocument document)
        {
            foreach (var element in document.Descendants().Where(e => e.Name.Namespace == SharePointProxyNamespace).ToList())
            {
                element.Name = SharePointNamespace + element.Name.LocalName;
            }

            foreach (var element in document.Descendants().Where(e => e.Name.Namespace == SharePointExpressionProxyNamespace).ToList())
            {
                element.Name = SharePointExpressionNamespace + element.Name.LocalName;
            }

            var root = document.Root;
            if (root?.Attribute(XNamespace.Xmlns + "mswa") != null && !document.Descendants().Any(e => e.GetPrefixOfNamespace(e.Name.Namespace) == "mswa"))
            {
                root.Attribute(XNamespace.Xmlns + "mswa")?.Remove();
            }
        }

        private static string PrepareXamlForDeserialization(string xaml)
        {
            var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            var root = document.Root ?? throw new InvalidOperationException("Workflow XAML has no root element.");
            root.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy");
            root.SetAttributeValue(XNamespace.Xmlns + "p", "clr-namespace:Microsoft.Activities.Expressions;assembly=Microsoft.Activities.Proxy");
            return document.ToString(SaveOptions.DisableFormatting);
        }

        internal static string GetDottedWorkflowClassName(string workflowName)
        {
            if (!string.IsNullOrWhiteSpace(workflowName) && workflowName.EndsWith(SpdTechnicalClassSuffix, StringComparison.OrdinalIgnoreCase)) return workflowName;
            return (string.IsNullOrWhiteSpace(workflowName) ? "GeneratedWorkflow" : workflowName) + SpdTechnicalClassSuffix;
        }

        private static string SerializeBuilder(ActivityBuilder builder)
        {
            using (var stringWriter = new StringWriter())
            {
                var schemaContext = new XamlSchemaContext();
                using (var xamlWriter = new XamlXmlWriter(stringWriter, schemaContext))
                {
                    using (var builderWriter = ActivityXamlServices.CreateBuilderWriter(xamlWriter))
                    {
                        XamlServices.Save(builderWriter, builder);
                    }
                }

                return stringWriter.ToString();
            }
        }

        private static void AppendActivity(StringBuilder report, Activity activity, int depth)
        {
            if (activity == null) return;
            var indent = new string(' ', depth * 2);
            report.AppendLine(indent + activity.GetType().FullName + " DisplayName=" + activity.DisplayName);
            if (activity is Sequence sequence)
            {
                report.AppendLine(indent + "  Variables=" + sequence.Variables.Count + " Activities=" + sequence.Activities.Count);
                foreach (var child in sequence.Activities) AppendActivity(report, child, depth + 1);
            }
            else if (activity is Flowchart flowchart)
            {
                report.AppendLine(indent + "  Nodes=" + flowchart.Nodes.Count + " StartNode=" + (flowchart.StartNode == null ? "<null>" : flowchart.StartNode.GetType().FullName));
                foreach (var node in flowchart.Nodes.OfType<FlowStep>()) AppendActivity(report, node.Action, depth + 1);
            }
        }

        private static void LoadManagedDlls(string cacheFolder)
        {
            foreach (var dll in Directory.EnumerateFiles(cacheFolder, "*.dll"))
            {
                try
                {
                    Assembly.LoadFrom(dll);
                }
                catch (Exception ex) when (ex is BadImageFormatException || ex is FileLoadException)
                {
                    // SharePoint Designer cache folders can contain duplicate or native DLLs; ignore those just like the original proof-of-concept.
                }
            }
        }

        private static Assembly LoadRequiredAssembly(string cacheFolder, string fileName)
        {
            var path = Path.Combine(cacheFolder, fileName);
            if (!File.Exists(path)) throw new FileNotFoundException("Missing DLL: " + path, path);
            return Assembly.LoadFrom(path);
        }

        private static Type GetRequiredType(Assembly assembly, string typeName) =>
            assembly.GetType(typeName, throwOnError: true, ignoreCase: false);

        private static Type GetOptionalType(Assembly assembly, string typeName) =>
            assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
    }
}
