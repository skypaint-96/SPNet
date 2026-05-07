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
                var expressionTypes = new ComparisonExpressionTypes(microsoftActivitiesAssembly);

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
                    if (child.Name.LocalName == "Calc") stage.Actions.Add(new CalcActionYaml { To = ReadCalcTarget(child), Operator = "Add", LValue = new ExpressionYaml { Literal = "<exported>" }, RValue = new ExpressionYaml { Literal = "<exported>" } });
                    else if (child.Name.LocalName == "WriteToHistory") stage.Actions.Add(new WriteHistoryActionYaml { Message = new ExpressionYaml { Literal = "<exported expression>" } });
                    else if (child.Name.LocalName == "SetWorkflowStatus") stage.Actions.Add(new SetStatusActionYaml { Status = (string?)child.Attribute("Status") ?? "<exported>" });
                }
                if (stage.Actions.Count > 0) workflow.Stages.Add(stage);
            }
            workflow.ExportWarnings.Add("Partial structural export: supported actions are listed, but expressions may be placeholders when WF deserialization is not used.");
            if (workflow.Stages.Count == 0) workflow.Stages.Add(new StageYaml { Name = "Unsupported XAML", Actions = new System.Collections.Generic.List<WorkflowActionYaml>() });
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
                var commentType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.Comment");
                var delayForType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.DelayFor");
                var delayUntilType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.DelayUntil");
                var lookupWorkflowContextType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.LookupWorkflowContextProperty");
                var getCurrentListIdType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.GetCurrentListId");
                var getCurrentItemGuidType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.GetCurrentItemGuid");
                var setFieldType = GetRequiredType(sharePointAssembly, "Microsoft.SharePoint.WorkflowServices.Activities.SetField");
                var toStringType = GetRequiredType(microsoftActivitiesAssembly, "Microsoft.Activities.Expressions.ToString");
                var expressionTypes = new ComparisonExpressionTypes(microsoftActivitiesAssembly);
                var valueExpressionTypes = new ValueExpressionTypes(toStringType, lookupWorkflowContextType, getCurrentListIdType, getCurrentItemGuidType);

                var variableTypes = workflow.Variables?.ToDictionary(v => v.Name, v => MapVariableType(v.Type), StringComparer.OrdinalIgnoreCase) ?? new System.Collections.Generic.Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                foreach (var target in workflow.Stages.SelectMany(s => s.Actions ?? new System.Collections.Generic.List<WorkflowActionYaml>()).OfType<CalcActionYaml>().Select(a => a.To).Where(t => !string.IsNullOrWhiteSpace(t) && !variableTypes.ContainsKey(t))) variableTypes[target] = typeof(double);
                ValidateAssignments(workflow, variableTypes);

                var flowchart = new Flowchart();
                FlowStep previous = null;
                foreach (var stageModel in workflow.Stages)
                {
                    var sequence = new Sequence { DisplayName = string.IsNullOrWhiteSpace(stageModel.Name) ? "Stage" : stageModel.Name };
                    foreach (var action in stageModel.Actions ?? new System.Collections.Generic.List<WorkflowActionYaml>()) sequence.Activities.Add(BuildAction(action, calcType, writeToHistoryType, setStatusType, commentType, delayForType, delayUntilType, lookupWorkflowContextType, getCurrentListIdType, getCurrentItemGuidType, setFieldType, valueExpressionTypes, expressionTypes, variableTypes));
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
                foreach (var variable in variableTypes)
                {
                    builder.Properties.Add(new DynamicActivityProperty { Name = variable.Key, Type = typeof(InArgument<>).MakeGenericType(variable.Value) });
                }
                return builder;
            }
        }

        private static Activity BuildAction(WorkflowActionYaml action, Type calcType, Type writeToHistoryType, Type setStatusType, Type commentType, Type delayForType, Type delayUntilType, Type lookupWorkflowContextType, Type getCurrentListIdType, Type getCurrentItemGuidType, Type setFieldType, ValueExpressionTypes valueExpressionTypes, ComparisonExpressionTypes expressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (action is CalcActionYaml calcAction) return BuildCalc(calcAction, calcType, valueExpressionTypes);
            if (action is WriteHistoryActionYaml historyAction) return BuildWriteHistory(historyAction, writeToHistoryType, valueExpressionTypes);
            if (action is SetStatusActionYaml statusAction) return BuildSetStatus(statusAction, setStatusType);
            if (action is CommentActionYaml commentAction) return BuildComment(commentAction, commentType, valueExpressionTypes);
            if (action is DelayForActionYaml delayForAction) return BuildDelayFor(delayForAction, delayForType, valueExpressionTypes);
            if (action is DelayUntilActionYaml delayUntilAction) return BuildDelayUntil(delayUntilAction, delayUntilType, valueExpressionTypes);
            if (action is AssignActionYaml assignAction) return BuildAssign(assignAction, valueExpressionTypes, variableTypes);
            if (action is LookupWorkflowContextActionYaml contextAction) return BuildLookupWorkflowContext(contextAction, lookupWorkflowContextType);
            if (action is GetCurrentListIdActionYaml listIdAction) return BuildGetCurrentListId(listIdAction, getCurrentListIdType);
            if (action is GetCurrentItemGuidActionYaml itemGuidAction) return BuildGetCurrentItemGuid(itemGuidAction, getCurrentItemGuidType);
            if (action is SetFieldActionYaml setFieldAction) return BuildSetField(setFieldAction, setFieldType, valueExpressionTypes);
            if (action is WhileActionYaml whileAction) return BuildWhile(whileAction, calcType, writeToHistoryType, setStatusType, commentType, delayForType, delayUntilType, lookupWorkflowContextType, getCurrentListIdType, getCurrentItemGuidType, setFieldType, valueExpressionTypes, expressionTypes, variableTypes);
            if (action is IfActionYaml ifAction) return BuildIf(ifAction, calcType, writeToHistoryType, setStatusType, commentType, delayForType, delayUntilType, lookupWorkflowContextType, getCurrentListIdType, getCurrentItemGuidType, setFieldType, valueExpressionTypes, expressionTypes, variableTypes);
            throw new InvalidOperationException("Unsupported action type: " + action.Type);
        }

        private static Activity BuildWhile(WhileActionYaml action, Type calcType, Type writeToHistoryType, Type setStatusType, Type commentType, Type delayForType, Type delayUntilType, Type lookupWorkflowContextType, Type getCurrentListIdType, Type getCurrentItemGuidType, Type setFieldType, ValueExpressionTypes valueExpressionTypes, ComparisonExpressionTypes expressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            return new While
            {
                DisplayName = string.Equals(action.Type, "loop", StringComparison.OrdinalIgnoreCase) ? "loop" : "while",
                Condition = BuildBooleanExpression(action.Condition, valueExpressionTypes, expressionTypes),
                Body = BuildSequence(action.Actions, calcType, writeToHistoryType, setStatusType, commentType, delayForType, delayUntilType, lookupWorkflowContextType, getCurrentListIdType, getCurrentItemGuidType, setFieldType, valueExpressionTypes, expressionTypes, variableTypes)
            };
        }

        private static Activity BuildIf(IfActionYaml action, Type calcType, Type writeToHistoryType, Type setStatusType, Type commentType, Type delayForType, Type delayUntilType, Type lookupWorkflowContextType, Type getCurrentListIdType, Type getCurrentItemGuidType, Type setFieldType, ValueExpressionTypes valueExpressionTypes, ComparisonExpressionTypes expressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            return new If
            {
                DisplayName = "if",
                Condition = BuildBooleanExpression(action.Condition, valueExpressionTypes, expressionTypes),
                Then = BuildSequence(action.Then, calcType, writeToHistoryType, setStatusType, commentType, delayForType, delayUntilType, lookupWorkflowContextType, getCurrentListIdType, getCurrentItemGuidType, setFieldType, valueExpressionTypes, expressionTypes, variableTypes),
                Else = action.Else == null || action.Else.Count == 0 ? null : BuildSequence(action.Else, calcType, writeToHistoryType, setStatusType, commentType, delayForType, delayUntilType, lookupWorkflowContextType, getCurrentListIdType, getCurrentItemGuidType, setFieldType, valueExpressionTypes, expressionTypes, variableTypes)
            };
        }

        private static Sequence BuildSequence(System.Collections.Generic.IEnumerable<WorkflowActionYaml> actions, Type calcType, Type writeToHistoryType, Type setStatusType, Type commentType, Type delayForType, Type delayUntilType, Type lookupWorkflowContextType, Type getCurrentListIdType, Type getCurrentItemGuidType, Type setFieldType, ValueExpressionTypes valueExpressionTypes, ComparisonExpressionTypes expressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            var sequence = new Sequence();
            foreach (var child in actions ?? Enumerable.Empty<WorkflowActionYaml>()) sequence.Activities.Add(BuildAction(child, calcType, writeToHistoryType, setStatusType, commentType, delayForType, delayUntilType, lookupWorkflowContextType, getCurrentListIdType, getCurrentItemGuidType, setFieldType, valueExpressionTypes, expressionTypes, variableTypes));
            return sequence;
        }

        private static Activity BuildSetField(SetFieldActionYaml action, Type setFieldType, ValueExpressionTypes valueExpressionTypes)
        {
            var setField = Create(setFieldType);
            SetProperty(setField, "FieldName", new InArgument<string>(action.FieldName ?? string.Empty));
            SetProperty(setField, "FieldValue", ToInArgument<object>(action.Value, valueExpressionTypes));
            return (Activity)setField;
        }

        private static Activity BuildLookupWorkflowContext(LookupWorkflowContextActionYaml action, Type lookupWorkflowContextType)
        {
            var lookup = Create(lookupWorkflowContextType);
            SetProperty(lookup, "PropertyName", new InArgument<string>(action.PropertyName ?? string.Empty));
            SetProperty(lookup, "Result", new OutArgument<string>(new ArgumentReference<string>(action.To)));
            return (Activity)lookup;
        }

        private static Activity BuildGetCurrentListId(GetCurrentListIdActionYaml action, Type getCurrentListIdType)
        {
            var lookup = Create(getCurrentListIdType);
            SetProperty(lookup, "Result", new OutArgument<Guid>(new ArgumentReference<Guid>(action.To)));
            return (Activity)lookup;
        }

        private static Activity BuildGetCurrentItemGuid(GetCurrentItemGuidActionYaml action, Type getCurrentItemGuidType)
        {
            var lookup = Create(getCurrentItemGuidType);
            SetProperty(lookup, "Result", new OutArgument<Guid>(new ArgumentReference<Guid>(action.To)));
            return (Activity)lookup;
        }

        private static Activity BuildCalc(CalcActionYaml action, Type calcType, ValueExpressionTypes valueExpressionTypes)
        {
            var calc = Create(calcType);
            SetProperty(calc, "LValue", ToInArgument<double>(action.LValue, valueExpressionTypes));
            SetProperty(calc, "RValue", ToInArgument<double>(action.RValue, valueExpressionTypes));
            SetProperty(calc, "Operator", new InArgument<string>(action.Operator ?? "Add"));
            SetProperty(calc, "To", new OutArgument<double>(new ArgumentReference<double>(action.To)));
            return (Activity)calc;
        }

        private static Activity BuildWriteHistory(WriteHistoryActionYaml action, Type writeToHistoryType, ValueExpressionTypes valueExpressionTypes)
        {
            var write = Create(writeToHistoryType);
            SetProperty(write, "Message", ToInArgument<string>(action.Message, valueExpressionTypes));
            return (Activity)write;
        }

        private static Activity BuildSetStatus(SetStatusActionYaml action, Type setStatusType)
        {
            var status = Create(setStatusType);
            SetProperty(status, "Status", new InArgument<string>(action.Status ?? string.Empty));
            return (Activity)status;
        }

        private static Activity BuildComment(CommentActionYaml action, Type commentType, ValueExpressionTypes valueExpressionTypes)
        {
            var comment = Create(commentType);
            SetProperty(comment, "CommentText", ToInArgument<string>(action.Text, valueExpressionTypes));
            return (Activity)comment;
        }

        private static Activity BuildDelayFor(DelayForActionYaml action, Type delayForType, ValueExpressionTypes valueExpressionTypes)
        {
            var delay = Create(delayForType);
            SetProperty(delay, "Days", ToInArgument<double>(action.Days, valueExpressionTypes));
            SetProperty(delay, "Hours", ToInArgument<double>(action.Hours, valueExpressionTypes));
            SetProperty(delay, "Minutes", ToInArgument<double>(action.Minutes, valueExpressionTypes));
            return (Activity)delay;
        }

        private static Activity BuildDelayUntil(DelayUntilActionYaml action, Type delayUntilType, ValueExpressionTypes valueExpressionTypes)
        {
            var delay = Create(delayUntilType);
            SetProperty(delay, "Date", ToInArgument<DateTime>(action.Date, valueExpressionTypes));
            return (Activity)delay;
        }

        private static void ValidateAssignments(WorkflowYaml workflow, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            foreach (var action in workflow.Stages.SelectMany(s => s.Actions ?? new System.Collections.Generic.List<WorkflowActionYaml>()).OfType<AssignActionYaml>())
            {
                if (!variableTypes.ContainsKey(action.To ?? string.Empty)) throw new InvalidOperationException(action.Type + " action target variable is not declared: " + action.To);
            }
        }

        private static Activity BuildAssign(AssignActionYaml action, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (!variableTypes.TryGetValue(action.To ?? string.Empty, out var targetType)) throw new InvalidOperationException("assign action target variable is not declared: " + action.To);
            var value = action.Value ?? new ExpressionYaml();
            if (targetType == typeof(double)) return new Assign<double> { To = new OutArgument<double>(new ArgumentReference<double>(action.To)), Value = ToInArgument<double>(value, valueExpressionTypes) };
            if (targetType == typeof(bool)) return new Assign<bool> { To = new OutArgument<bool>(new ArgumentReference<bool>(action.To)), Value = ToInArgument<bool>(value, valueExpressionTypes) };
            if (targetType == typeof(DateTime)) return new Assign<DateTime> { To = new OutArgument<DateTime>(new ArgumentReference<DateTime>(action.To)), Value = ToInArgument<DateTime>(value, valueExpressionTypes) };
            if (targetType == typeof(Guid)) return new Assign<Guid> { To = new OutArgument<Guid>(new ArgumentReference<Guid>(action.To)), Value = ToInArgument<Guid>(value, valueExpressionTypes) };
            if (targetType == typeof(int)) return new Assign<int> { To = new OutArgument<int>(new ArgumentReference<int>(action.To)), Value = ToInArgument<int>(value, valueExpressionTypes) };
            return new Assign<string> { To = new OutArgument<string>(new ArgumentReference<string>(action.To)), Value = ToInArgument<string>(value, valueExpressionTypes) };
        }

        private static Activity<bool> BuildBooleanExpression(ComparisonExpressionYaml condition, ValueExpressionTypes valueExpressionTypes, ComparisonExpressionTypes expressionTypes)
        {
            condition = condition ?? new ComparisonExpressionYaml();
            var op = (string.IsNullOrWhiteSpace(condition.Operator) ? condition.Type : condition.Operator).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            if (op == "islessthan" || op == "lessthan") return CreateComparison(expressionTypes.IsLessThan, condition, valueExpressionTypes);
            if (op == "greaterthan" || op == "isgreaterthan") return CreateComparison(expressionTypes.IsGreaterThan, condition, valueExpressionTypes);
            if (op == "equals" || op == "equal" || op == "isequal") return CreateComparison(expressionTypes.IsEqualNumber, condition, valueExpressionTypes);
            if (op == "lessthanorequal" || op == "islessthanorequal") return CreateComparison(expressionTypes.IsLessThanOrEqual, condition, valueExpressionTypes);
            if (op == "greaterthanorequal" || op == "isgreaterthanorequal") return CreateComparison(expressionTypes.IsGreaterThanOrEqual, condition, valueExpressionTypes);
            throw new InvalidOperationException("Unsupported comparison condition: " + (condition.Operator ?? condition.Type));
        }

        private static Activity<bool> CreateComparison(Type comparisonType, ComparisonExpressionYaml condition, ValueExpressionTypes valueExpressionTypes)
        {
            var comparison = Create(comparisonType);
            SetProperty(comparison, "Left", ToInArgument<double>(condition.Left, valueExpressionTypes));
            SetProperty(comparison, "Right", ToInArgument<double>(condition.Right, valueExpressionTypes));
            return (Activity<bool>)comparison;
        }

        private sealed class ValueExpressionTypes
        {
            public ValueExpressionTypes(Type toString, Type lookupWorkflowContext, Type getCurrentListId, Type getCurrentItemGuid)
            {
                ToStringExpression = toString;
                LookupWorkflowContext = lookupWorkflowContext;
                GetCurrentListId = getCurrentListId;
                GetCurrentItemGuid = getCurrentItemGuid;
            }

            public Type ToStringExpression { get; }
            public Type LookupWorkflowContext { get; }
            public Type GetCurrentListId { get; }
            public Type GetCurrentItemGuid { get; }
        }

        private sealed class ComparisonExpressionTypes
        {
            public ComparisonExpressionTypes(Assembly assembly)
            {
                IsLessThan = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsLessThan`1");
                IsGreaterThan = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsGreaterThan`1");
                IsLessThanOrEqual = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsLessThanOrEqual`1");
                IsGreaterThanOrEqual = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsGreaterThanOrEqual`1");
                IsEqualNumber = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsEqualNumber`1");
            }

            public Type IsLessThan { get; }
            public Type IsGreaterThan { get; }
            public Type IsLessThanOrEqual { get; }
            public Type IsGreaterThanOrEqual { get; }
            public Type IsEqualNumber { get; }

            private static Type GetGenericComparisonType(Assembly assembly, string typeName) => GetRequiredType(assembly, typeName).MakeGenericType(typeof(double));
        }

        private static InArgument<T> ToInArgument<T>(ExpressionYaml expression, ValueExpressionTypes valueExpressionTypes)
        {
            expression = expression ?? new ExpressionYaml();
            if (string.Equals(expression.Type, "toString", StringComparison.OrdinalIgnoreCase) && expression.Value != null) expression = new ExpressionYaml { ToString = expression.Value };
            if (!string.IsNullOrWhiteSpace(expression.Variable)) return new InArgument<T>(new ArgumentValue<T>(expression.Variable));
            if (expression.ToString != null)
            {
                var toString = Create(valueExpressionTypes.ToStringExpression);
                SetProperty(toString, "Object", CreateToStringObjectArgument(expression.ToString));
                return (InArgument<T>)CreateInArgument(typeof(T), toString);
            }
            var expressionActivity = CreateLookupExpressionActivity(expression, typeof(T), valueExpressionTypes);
            if (expressionActivity != null) return (InArgument<T>)CreateInArgument(typeof(T), expressionActivity);
            return new InArgument<T>((T)Convert.ChangeType(expression.Literal ?? DefaultLiteral(typeof(T)), typeof(T)));
        }

        private static object? CreateLookupExpressionActivity(ExpressionYaml expression, Type resultType, ValueExpressionTypes valueExpressionTypes)
        {
            var type = (expression.Type ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (type == "lookupworkflowcontext" || type == "lookupcontextproperty")
            {
                if (resultType != typeof(string) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to String/Object arguments.");
                if (string.IsNullOrWhiteSpace(expression.PropertyName)) throw new InvalidOperationException(expression.Type + " expression requires 'propertyName'.");
                var lookup = Create(valueExpressionTypes.LookupWorkflowContext);
                SetProperty(lookup, "PropertyName", new InArgument<string>(expression.PropertyName));
                return lookup;
            }

            if (type == "getcurrentlistid")
            {
                if (resultType != typeof(Guid) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to Guid/Object arguments.");
                return Create(valueExpressionTypes.GetCurrentListId);
            }

            if (type == "getcurrentitemguid")
            {
                if (resultType != typeof(Guid) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to Guid/Object arguments.");
                return Create(valueExpressionTypes.GetCurrentItemGuid);
            }

            return null;
        }

        private static object CreateToStringObjectArgument(ExpressionYaml expression)
        {
            if (!string.IsNullOrWhiteSpace(expression.Variable)) return new InArgument<double>(new ArgumentValue<double>(expression.Variable));
            if (expression.Literal is string) return new InArgument<string>(Convert.ToString(expression.Literal));
            return new InArgument<double>(Convert.ToDouble(expression.Literal ?? 0d));
        }

        private static object DefaultLiteral(Type type) => type == typeof(string) ? string.Empty : type == typeof(bool) ? false : 0d;

        private static Type MapVariableType(string type)
        {
            if (string.Equals(type, "Double", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Number", StringComparison.OrdinalIgnoreCase)) return typeof(double);
            if (string.Equals(type, "Boolean", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Bool", StringComparison.OrdinalIgnoreCase)) return typeof(bool);
            if (string.Equals(type, "DateTime", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Date", StringComparison.OrdinalIgnoreCase)) return typeof(DateTime);
            if (string.Equals(type, "Guid", StringComparison.OrdinalIgnoreCase)) return typeof(Guid);
            if (string.Equals(type, "Int32", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Int", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Integer", StringComparison.OrdinalIgnoreCase)) return typeof(int);
            return typeof(string);
        }

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
            var property = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal) && p.CanWrite);
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
