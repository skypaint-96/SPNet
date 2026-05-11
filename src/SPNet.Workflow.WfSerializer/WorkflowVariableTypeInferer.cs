using System;
using System.Collections.Generic;
using System.Linq;

namespace SPNet.Workflow.WfSerializer
{
    internal static class WorkflowVariableTypeInferer
    {
        public static Dictionary<string, Type> Infer(WorkflowYaml workflow, Type dynamicValueType, string emptyDynamicValueArgumentName, string requestHeadersArgumentName)
        {
            var variableTypes = workflow.Variables?.ToDictionary(v => v.Name, v => WorkflowTypeMapper.MapDeclaredVariableType(v.Type), StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in workflow.Variables ?? Enumerable.Empty<VariableYaml>()) if (string.Equals(variable.Type, "DynamicValue", StringComparison.OrdinalIgnoreCase)) variableTypes[variable.Name] = dynamicValueType;
            var actions = EnumerateActions(workflow).ToList();

            foreach (var target in actions.OfType<CalcActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(double));
            foreach (var target in actions.OfType<StringReplaceActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<StringSubstringActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<StringTrimActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<LookupListItemStringPropertyActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(string));
            foreach (var target in actions.OfType<LookupListItemIntPropertyActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(int));
            foreach (var target in actions.OfType<CallHttpWebServiceActionYaml>().SelectMany(a => new[] { a.ResponseContentTo, a.ResponseHeadersTo })) AddIfMissing(variableTypes, target, dynamicValueType);
            foreach (var action in actions.OfType<GetDynamicValuePropertyActionYaml>()) AddIfMissing(variableTypes, action.To, MapDynamicValuePropertyType(action.ValueType, dynamicValueType));
            foreach (var action in actions.OfType<SetDynamicValuePropertyActionYaml>()) AddIfMissing(variableTypes, string.IsNullOrWhiteSpace(action.To) ? action.Source : action.To, dynamicValueType);
            foreach (var target in actions.OfType<CountDynamicValueItemsActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, typeof(int));
            foreach (var target in actions.OfType<BuildDynamicValueActionYaml>().Select(a => a.To)) AddIfMissing(variableTypes, target, dynamicValueType);
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

        internal static Type MapDynamicValuePropertyType(string? valueType, Type dynamicValueType)
        {
            var normalized = (valueType ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (normalized == "dynamicvalue" || normalized == "dictionary") return dynamicValueType;
            if (normalized == "boolean" || normalized == "bool") return typeof(bool);
            if (normalized == "int32" || normalized == "int" || normalized == "integer") return typeof(int);
            if (normalized == "double" || normalized == "number") return typeof(double);
            if (normalized == "datetime" || normalized == "date") return typeof(DateTime);
            if (normalized == "guid") return typeof(Guid);
            return typeof(string);
        }
    }
}
