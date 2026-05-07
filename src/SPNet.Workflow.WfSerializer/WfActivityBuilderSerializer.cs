using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using System.Activities.XamlIntegration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xaml;
using System.Xml.Linq;

namespace SPNet.Workflow.WfSerializer
{
    public static class WfActivityBuilderSerializer
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
        private static readonly XNamespace ActivitiesNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly XNamespace GenericCollectionsNamespace = "clr-namespace:System.Collections.Generic;assembly=mscorlib";
        private static readonly XNamespace MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        private static readonly XNamespace SharePointNamespace = "clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities";
        private static readonly XNamespace SharePointProxyNamespace = "clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy";
        private static readonly XNamespace AuthoringNamespace = "clr-namespace:Microsoft.Web.Authoring.Workflow;assembly=Microsoft.Web.Authoring";

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

                var calcType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.Calc");
                var writeToHistoryType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.WriteToHistory");
                var toStringType = GetRequiredType(microsoftActivitiesAssembly, "Microsoft.Activities.Expressions.ToString");

                var builder = BuildProofOfConceptWorkflow(options.WorkflowName, GetDottedWorkflowClassName(options.WorkflowName), calcType, writeToHistoryType, toStringType);
                File.WriteAllText(outputPath, AddSharePointDesignerMetadata(SerializeBuilder(builder), options.WorkflowName));
            }
        }

        public static void SerializeYamlWorkflow(string workflowYamlPath, string outputXamlPath, string cacheFolder, SpNetToolConfig config)
        {
            var workflow = WorkflowYaml.Load(workflowYamlPath);
            SerializeWorkflow(BuildWorkflowFromYaml(workflow, cacheFolder), outputXamlPath, cacheFolder);
        }

        public static void ExportWorkflowYaml(string inputXamlPath, string outputYamlPath)
        {
            if (!File.Exists(inputXamlPath)) throw new FileNotFoundException("Input XAML not found: " + inputXamlPath, inputXamlPath);
            var document = XDocument.Parse(File.ReadAllText(inputXamlPath));
            var root = document.Root ?? throw new InvalidOperationException("Workflow XAML has no root element.");
            var className = (string?)root.Attribute(XamlNamespace + "Class") ?? "ExportedWorkflow.MTW";
            var name = className.EndsWith(SpdTechnicalClassSuffix, StringComparison.OrdinalIgnoreCase) ? className.Substring(0, className.Length - SpdTechnicalClassSuffix.Length) : className;
            var workflow = new WorkflowYaml { Name = name, TechnicalName = className };
            foreach (var property in root.Element(XamlNamespace + "Members")?.Elements(XamlNamespace + "Property") ?? Enumerable.Empty<XElement>())
            {
                workflow.Variables.Add(new VariableYaml { Name = (string?)property.Attribute("Name") ?? "variable", Type = ((string?)property.Attribute("Type") ?? string.Empty).Contains("Double") ? "Double" : "String" });
            }
            var stageElements = document.Descendants(ActivitiesNamespace + "Sequence").Where(e => !string.IsNullOrWhiteSpace((string?)e.Attribute("DisplayName"))).ToList();
            foreach (var stageElement in stageElements.Take(20))
            {
                var stage = new StageYaml { Name = (string?)stageElement.Attribute("DisplayName") ?? "Stage" };
                foreach (var child in stageElement.Elements().Where(e => e.Name.Namespace == SharePointNamespace))
                {
                    if (child.Name.LocalName == "Calc") stage.Actions.Add(new ActionYaml { Type = "calc", To = ReadCalcTarget(child), Operator = "Add", LValue = new ExpressionYaml { Literal = "<exported>" }, RValue = new ExpressionYaml { Literal = "<exported>" } });
                    else if (child.Name.LocalName == "WriteToHistory") stage.Actions.Add(new ActionYaml { Type = "writeHistory", Message = new ExpressionYaml { Literal = "<exported expression>" } });
                    else if (child.Name.LocalName == "SetWorkflowStatus") stage.Actions.Add(new ActionYaml { Type = "setStatus", Status = (string?)child.Attribute("Status") ?? "<exported>" });
                }
                if (stage.Actions.Count > 0) workflow.Stages.Add(stage);
            }
            workflow.ExportWarnings.Add("Partial structural export: supported actions are listed, but expressions may be placeholders when WF deserialization is not used.");
            if (workflow.Stages.Count == 0) workflow.Stages.Add(new StageYaml { Name = "Unsupported XAML", Actions = new System.Collections.Generic.List<ActionYaml>() });
            workflow.Save(outputYamlPath);
        }

        private static string ReadCalcTarget(XElement calc)
        {
            var text = calc.ToString(SaveOptions.DisableFormatting);
            var marker = "ArgumentReference";
            return text.Contains(marker) ? "calc" : "<exported>";
        }

        private static ActivityBuilder BuildWorkflowFromYaml(WorkflowYaml workflow, string cacheFolder)
        {
            var resolvedCacheFolder = Path.GetFullPath(cacheFolder);
            using (new CacheAssemblyResolver(resolvedCacheFolder))
            {
                LoadManagedDlls(resolvedCacheFolder);
                var sharePointAssembly = LoadRequiredAssembly(resolvedCacheFolder, SharePointProxyAssemblyName);
                var microsoftActivitiesAssembly = LoadRequiredAssembly(resolvedCacheFolder, MicrosoftActivitiesProxyAssemblyName);
                var calcType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.Calc");
                var writeToHistoryType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.WriteToHistory");
                var setStatusType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.SetWorkflowStatus");
                var toStringType = GetRequiredType(microsoftActivitiesAssembly, "Microsoft.Activities.Expressions.ToString");

                var flowchart = new Flowchart();
                FlowStep previous = null;
                foreach (var stageModel in workflow.Stages)
                {
                    var sequence = new Sequence { DisplayName = string.IsNullOrWhiteSpace(stageModel.Name) ? "Stage" : stageModel.Name };
                    foreach (var action in stageModel.Actions ?? new System.Collections.Generic.List<ActionYaml>()) sequence.Activities.Add(BuildAction(action, calcType, writeToHistoryType, setStatusType, toStringType));
                    var step = new FlowStep { Action = sequence };
                    flowchart.Nodes.Add(step);
                    if (flowchart.StartNode == null) flowchart.StartNode = step;
                    if (previous != null) previous.Next = step;
                    previous = step;
                }

                var outerSequence = new Sequence { DisplayName = workflow.Name };
                flowchart.DisplayName = workflow.Name;
                outerSequence.Activities.Add(flowchart);
                var builder = new ActivityBuilder { Name = string.IsNullOrWhiteSpace(workflow.TechnicalName) ? GetDottedWorkflowClassName(workflow.Name) : workflow.TechnicalName, Implementation = outerSequence };
                foreach (var variable in workflow.Variables ?? new System.Collections.Generic.List<VariableYaml>())
                {
                    builder.Properties.Add(new DynamicActivityProperty { Name = variable.Name, Type = typeof(InArgument<>).MakeGenericType(MapVariableType(variable.Type)) });
                }
                foreach (var target in workflow.Stages.SelectMany(s => s.Actions ?? new System.Collections.Generic.List<ActionYaml>()).Where(a => string.Equals(a.Type, "calc", StringComparison.OrdinalIgnoreCase)).Select(a => a.To).Where(t => !string.IsNullOrWhiteSpace(t) && !builder.Properties.Any(p => p.Name == t)))
                {
                    builder.Properties.Add(new DynamicActivityProperty { Name = target, Type = typeof(InArgument<double>) });
                }
                return builder;
            }
        }

        private static Activity BuildAction(ActionYaml action, Type calcType, Type writeToHistoryType, Type setStatusType, Type toStringType)
        {
            if (string.Equals(action.Type, "calc", StringComparison.OrdinalIgnoreCase))
            {
                var calc = Create(calcType);
                SetProperty(calc, "LValue", ToInArgument<double>(action.LValue, toStringType));
                SetProperty(calc, "RValue", ToInArgument<double>(action.RValue, toStringType));
                SetProperty(calc, "Operator", new InArgument<string>(action.Operator ?? "Add"));
                SetProperty(calc, "To", new OutArgument<double>(new ArgumentReference<double>(action.To)));
                return (Activity)calc;
            }
            if (string.Equals(action.Type, "writeHistory", StringComparison.OrdinalIgnoreCase))
            {
                var write = Create(writeToHistoryType);
                SetProperty(write, "Message", ToInArgument<string>(action.Message, toStringType));
                return (Activity)write;
            }
            if (string.Equals(action.Type, "setStatus", StringComparison.OrdinalIgnoreCase))
            {
                var status = Create(setStatusType);
                SetProperty(status, "Status", action.Status ?? string.Empty);
                return (Activity)status;
            }
            throw new InvalidOperationException("Unsupported action type: " + action.Type);
        }

        private static InArgument<T> ToInArgument<T>(ExpressionYaml expression, Type toStringType)
        {
            expression = expression ?? new ExpressionYaml();
            if (!string.IsNullOrWhiteSpace(expression.Variable)) return new InArgument<T>(new ArgumentValue<T>(expression.Variable));
            if (expression.ToString != null)
            {
                var toString = Create(toStringType);
                SetProperty(toString, "Object", CreateToStringObjectArgument(expression.ToString));
                return (InArgument<T>)CreateInArgument(typeof(T), toString);
            }
            return new InArgument<T>((T)Convert.ChangeType(expression.Literal ?? DefaultLiteral(typeof(T)), typeof(T)));
        }

        private static object CreateToStringObjectArgument(ExpressionYaml expression)
        {
            if (!string.IsNullOrWhiteSpace(expression.Variable)) return new InArgument<double>(new ArgumentValue<double>(expression.Variable));
            if (expression.Literal is string) return new InArgument<string>(Convert.ToString(expression.Literal));
            return new InArgument<double>(Convert.ToDouble(expression.Literal ?? 0d));
        }

        private static object DefaultLiteral(Type type) => type == typeof(string) ? string.Empty : 0d;

        private static Type MapVariableType(string type) => string.Equals(type, "Double", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Number", StringComparison.OrdinalIgnoreCase) ? typeof(double) : typeof(string);

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

        public static void SerializeWorkflow(ActivityBuilder builder, string outputXamlPath, string cacheFolder)
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
                File.WriteAllText(outputPath, AddSharePointDesignerMetadata(SerializeBuilder(builder), builder.Name));
            }
        }

        public static string AddSharePointDesignerMetadata(string xaml, string workflowName)
        {
            if (string.IsNullOrWhiteSpace(xaml)) throw new ArgumentException("Workflow XAML is required.", nameof(xaml));

            var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            var root = document.Root ?? throw new InvalidOperationException("Workflow XAML has no root element.");

            EnsureNamespace(root, "mc", MarkupCompatibilityNamespace);
            EnsureNamespace(root, "mwaw", AuthoringNamespace);
            EnsureNamespace(root, "scg", "clr-namespace:System.Collections.Generic;assembly=mscorlib");
            EnsureNamespace(root, "local", SharePointNamespace);
            EnsureIgnorablePrefix(root, "mwaw");
            NormalizeSharePointActivityNamespaces(document);
            RemoveEmptyRootCustomAttributes(root);

            RemoveTopLevelSequenceDisplayName(root);
            EnsureInitBlock(document, root);
            EnsureExpressionIds(document);

            var firstStageContainer = document.Descendants(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes")
                .Any(e => e.Descendants(XamlNamespace + "String").Any(s => ((string?)s.Attribute(XamlNamespace + "Key")) == "StageAttribute" && ((string?)s).StartsWith("StageContainer-", StringComparison.OrdinalIgnoreCase)));
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

        private static void EnsureExpressionIds(XDocument document)
        {
            foreach (var expression in document.Descendants().Where(IsExpressionActivityElement).ToList())
            {
                var existing = expression.Elements(AuthoringNamespace + "SPDesignerXamlWriter.CustomAttributes")
                    .Any(e => e.Descendants(XamlNamespace + "String").Any(s => string.Equals((string?)s.Attribute(XamlNamespace + "Key"), "Id", StringComparison.OrdinalIgnoreCase)));
                if (existing) continue;
                expression.AddFirst(CreateCustomDictionaryAttribute("Id", SpdExpressionIdAttribute));
            }
        }

        private static bool IsExpressionActivityElement(XElement element) =>
            element.Name.NamespaceName == "http://schemas.microsoft.com/workflow/2012/07/xaml/activities" &&
            element.Name.LocalName == "ToString";

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

        private static ActivityBuilder BuildProofOfConceptWorkflow(string workflowName, string workflowClassName, Type calcType, Type writeToHistoryType, Type toStringType)
        {
            var calc = Create(calcType);
            SetProperty(calc, "LValue", new InArgument<double>(1.0));
            SetProperty(calc, "RValue", new InArgument<double>(1.0));
            SetProperty(calc, "Operator", new InArgument<string>("Add"));
            SetProperty(calc, "To", new OutArgument<double>(new ArgumentReference<double>("calc")));

            var toString = Create(toStringType);
            SetProperty(toString, "Object", new InArgument<double>(new ArgumentValue<double>("calc")));

            var writeToHistory = Create(writeToHistoryType);
            SetProperty(writeToHistory, "Message", CreateInArgument(typeof(string), toString));

            var stageSequence = new Sequence { DisplayName = "Stage 1" };
            stageSequence.Activities.Add((Activity)calc);
            stageSequence.Activities.Add((Activity)writeToHistory);

            var flowStep = new FlowStep { Action = stageSequence };
            var flowchart = new Flowchart { StartNode = flowStep };
            flowchart.Nodes.Add(flowStep);

            var outerSequence = new Sequence { DisplayName = workflowName };
            outerSequence.Activities.Add(flowchart);

            var builder = new ActivityBuilder
            {
                Name = workflowClassName,
                Implementation = outerSequence
            };
            builder.Properties.Add(new DynamicActivityProperty
            {
                Name = "calc",
                Type = typeof(InArgument<double>)
            });
            return builder;
        }

        private static string GetDottedWorkflowClassName(string workflowName)
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

        private static object Create(Type type) => Activator.CreateInstance(type) ?? throw new InvalidOperationException("Could not instantiate " + type.FullName + ".");

        private static void SetProperty(object target, string propertyName, object value)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite) throw new InvalidOperationException("Type " + target.GetType().FullName + " does not expose writable property " + propertyName + ".");
            property.SetValue(target, value, null);
        }

        private static object CreateInArgument(Type resultType, object expressionActivity)
        {
            var argumentType = typeof(InArgument<>).MakeGenericType(resultType);
            var expressionType = typeof(Activity<>).MakeGenericType(resultType);
            return Activator.CreateInstance(argumentType, expressionType.IsInstanceOfType(expressionActivity) ? expressionActivity : throw new InvalidOperationException(expressionActivity.GetType().FullName + " is not an Activity<" + resultType.Name + ">."))!;
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
    }
}
