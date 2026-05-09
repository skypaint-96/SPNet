using System;
using System.Collections.Generic;
using System.Linq;

namespace SPNet.Workflow.WfSerializer
{
    internal static class WorkflowVariableTypeInferer
    {
        public static Dictionary<string, Type> Infer(WorkflowYaml workflow, Type dynamicValueType, string emptyDynamicValueArgumentName, string requestHeadersArgumentName)
        {
            var variableTypes = workflow.Variables?.ToDictionary(v => v.Name, v => MapDeclaredVariableType(v.Type), StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            var actions = EnumerateActions(workflow).ToList();

            foreach (var target in actions.OfType<CalcActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(double));
            foreach (var target in actions.OfType<StringReplaceActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<StringSubstringActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<StringTrimActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<LookupListItemStringPropertyActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<LookupListItemIntPropertyActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(int));
            foreach (var target in actions.OfType<CallHttpWebServiceActionYaml>().SelectMany(a => new[] { a.ResponseContentTo, a.ResponseHeadersTo })) AddIfMissing(variableTypes, target, dynamicValueType);
            foreach (var target in actions.OfType<GetDynamicValuePropertyActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<SingleTaskActionYaml>().Select(a => a.TaskIdTo)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<SingleTaskActionYaml>().Select(a => a.OutcomeTo)) AddIfMissing(variableTypes, target, typeof(int));

            if (actions.OfType<CallHttpWebServiceActionYaml>().Any())
            {
                AddIfMissing(variableTypes, emptyDynamicValueArgumentName, dynamicValueType);
                AddIfMissing(variableTypes, requestHeadersArgumentName, dynamicValueType);
            }

            return variableTypes;
        }

        private static IEnumerable<WorkflowActionYaml> EnumerateActions(WorkflowYaml workflow) =>
            workflow.Stages.SelectMany(s => EnumerateActions(s.Actions));

        private static IEnumerable<WorkflowActionYaml> EnumerateActions(IEnumerable<WorkflowActionYaml>? actions)
        {
            foreach (var action in actions ?? Enumerable.Empty<WorkflowActionYaml>())
            {
                yield return action;
                if (action is IfActionYaml ifAction)
                {
                    foreach (var child in EnumerateActions(ifAction.Then)) yield return child;
                    foreach (var child in EnumerateActions(ifAction.Else)) yield return child;
                }
                else if (action is WhileActionYaml whileAction)
                {
                    foreach (var child in EnumerateActions(whileAction.Actions)) yield return child;
                }
            }
        }

        private static void AddIfMissing(Dictionary<string, Type> variableTypes, string? variableName, Type variableType)
        {
            var name = variableName;
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!variableTypes.ContainsKey(name!)) variableTypes[name!] = variableType;
        }

        private static Type MapDeclaredVariableType(string type)
        {
            if (string.Equals(type, "Double", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Number", StringComparison.OrdinalIgnoreCase)) return typeof(double);
            if (string.Equals(type, "Boolean", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Bool", StringComparison.OrdinalIgnoreCase)) return typeof(bool);
            if (string.Equals(type, "DateTime", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Date", StringComparison.OrdinalIgnoreCase)) return typeof(DateTime);
            if (string.Equals(type, "Guid", StringComparison.OrdinalIgnoreCase)) return typeof(Guid);
            if (string.Equals(type, "DynamicValue", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("DynamicValue variables are only available as HTTP response targets and are emitted using the SharePoint Designer proxy type at build time.");
            if (string.Equals(type, "Int32", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Int", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Integer", StringComparison.OrdinalIgnoreCase)) return typeof(int);
            return typeof(string);
        }
    }
}
