using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualBasic.Activities;

namespace SPNet.Workflow.WfSerializer
{
    public static partial class WfActivityBuilderSerializer
    {
        private static Activity BuildLookupListItemStringProperty(LookupListItemStringPropertyActionYaml action, Type lookupType, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateLookupTarget(action, typeof(string), variableTypes);
            var lookup = BuildLookupListItemPropertyBase(action, lookupType, valueExpressionTypes);
            ActivityReflectionWriter.SetProperty(lookup, "Result", new OutArgument<string>(new ArgumentReference<string>(action.To)));
            return (Activity)lookup;
        }

        private static Activity BuildLookupListItemIntProperty(LookupListItemIntPropertyActionYaml action, Type? lookupType, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (lookupType == null) throw new InvalidOperationException(action.Type + " is not supported by the local SharePoint Designer proxy assembly; LookupSPListItemIntProperty was not found.");
            ValidateLookupTarget(action, typeof(int), variableTypes);
            var lookup = BuildLookupListItemPropertyBase(action, lookupType, valueExpressionTypes);
            ActivityReflectionWriter.SetProperty(lookup, "Result", new OutArgument<int>(new ArgumentReference<int>(action.To)));
            return (Activity)lookup;
        }

        private static object BuildLookupListItemPropertyBase(LookupListItemPropertyActionYaml action, Type lookupType, ValueExpressionTypes valueExpressionTypes)
        {
            var lookup = ActivityReflectionWriter.Create(lookupType);
            ActivityReflectionWriter.SetProperty(lookup, "ListId", ToInArgument<Guid>(action.ListId, valueExpressionTypes));
            SetListItemIdentity(lookup, action, valueExpressionTypes);
            ActivityReflectionWriter.SetProperty(lookup, "PropertyName", new InArgument<string>(string.IsNullOrWhiteSpace(action.PropertyName) ? action.FieldName : action.PropertyName));
            return lookup;
        }

        private static void ValidateLookupTarget(LookupListItemPropertyActionYaml action, Type expectedType, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (!variableTypes.TryGetValue(action.To ?? string.Empty, out var targetType)) throw new InvalidOperationException(action.Type + " action target variable is not declared: " + action.To);
            if (targetType != expectedType) throw new InvalidOperationException(action.Type + " target variable must be " + expectedType.Name + ": " + action.To);
        }

        private static Activity BuildCreateListItem(CreateListItemActionYaml action, Type createListItemType, ValueExpressionTypes valueExpressionTypes)
        {
            var create = ActivityReflectionWriter.Create(createListItemType);
            ActivityReflectionWriter.SetProperty(create, "ListId", ToInArgument<Guid>(action.ListId, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(create, "ListItemProperties", ToDictionaryArgument(action.Fields, valueExpressionTypes));
            if (!string.IsNullOrWhiteSpace(action.ItemGuidTo)) ActivityReflectionWriter.SetProperty(create, "ItemGuid", new InOutArgument<Guid>(new ArgumentReference<Guid>(action.ItemGuidTo)));
            if (!string.IsNullOrWhiteSpace(action.ItemIdTo)) ActivityReflectionWriter.SetProperty(create, "ItemId", new OutArgument<int>(new ArgumentReference<int>(action.ItemIdTo)));
            return (Activity)create;
        }

        private static Activity BuildUpdateListItem(UpdateListItemActionYaml action, Type updateListItemType, ValueExpressionTypes valueExpressionTypes)
        {
            var update = ActivityReflectionWriter.Create(updateListItemType);
            ActivityReflectionWriter.SetProperty(update, "ListId", ToInArgument<Guid>(action.ListId, valueExpressionTypes));
            SetListItemIdentity(update, action, valueExpressionTypes);
            ActivityReflectionWriter.SetProperty(update, "ListItemProperties", ToDictionaryArgument(action.Fields, valueExpressionTypes));
            return (Activity)update;
        }

        private static Activity BuildDeleteListItem(DeleteListItemActionYaml action, Type deleteListItemType, ValueExpressionTypes valueExpressionTypes)
        {
            var delete = ActivityReflectionWriter.Create(deleteListItemType);
            ActivityReflectionWriter.SetProperty(delete, "ListId", ToInArgument<Guid>(action.ListId, valueExpressionTypes));
            SetListItemIdentity(delete, action, valueExpressionTypes);
            return (Activity)delete;
        }

        private static void SetListItemIdentity(object activity, TargetedListItemActionYaml action, ValueExpressionTypes valueExpressionTypes)
        {
            if (HasExpression(action.ItemGuid)) ActivityReflectionWriter.SetProperty(activity, "ItemGuid", ToInArgument<Guid>(action.ItemGuid, valueExpressionTypes));
            if (HasExpression(action.ItemId)) ActivityReflectionWriter.SetProperty(activity, "ItemId", ToInArgument<int>(action.ItemId, valueExpressionTypes));
        }

        private static InArgument<IDictionary<string, object>> ToDictionaryArgument(Dictionary<string, ExpressionYaml> fields, ValueExpressionTypes valueExpressionTypes)
        {
            var buildDictionary = ActivityReflectionWriter.Create(valueExpressionTypes.BuildDictionary);
            ActivityReflectionWriter.SetProperty(buildDictionary, "Dictionary", new InArgument<IDictionary<string, object>>());
            var values = ActivityReflectionWriter.GetProperty(buildDictionary, "Values", "BuildDictionary does not expose Values.");
            var addMethod = values.GetType().GetMethods().First(m => m.Name == "Add" && m.GetParameters().Length == 2);
            foreach (var field in fields ?? new Dictionary<string, ExpressionYaml>())
            {
                addMethod.Invoke(values, new object[] { field.Key, ToObjectFieldArgument(field.Value, valueExpressionTypes) });
            }
            return new InArgument<IDictionary<string, object>>((Activity<IDictionary<string, object>>)buildDictionary);
        }

        private static InArgument<object> ToObjectFieldArgument(ExpressionYaml expression, ValueExpressionTypes valueExpressionTypes)
        {
            expression = expression ?? new ExpressionYaml();
            var valueType = InferObjectFieldValueType(expression);
            var cast = Activator.CreateInstance(typeof(Cast<,>).MakeGenericType(valueType, typeof(object)))!;
            ActivityReflectionWriter.SetProperty(cast, "Operand", ToTypedInArgument(expression, valueExpressionTypes, valueType));
            return (InArgument<object>)ActivityReflectionWriter.CreateInArgument(typeof(object), cast);
        }

        private static object ToTypedInArgument(ExpressionYaml expression, ValueExpressionTypes valueExpressionTypes, Type valueType)
        {
            var method = typeof(WfActivityBuilderSerializer).GetMethod("ToInArgument", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("ToInArgument helper was not found.");
            return method.MakeGenericMethod(valueType).Invoke(null, new object[] { expression, valueExpressionTypes }) ?? throw new InvalidOperationException("Could not create field value argument.");
        }

        private static Type InferObjectFieldValueType(ExpressionYaml expression)
        {
            var valueType = (expression.ValueType ?? string.Empty).Replace("System.", string.Empty).Replace("x:", string.Empty).Trim();
            if (valueType.Equals("Guid", StringComparison.OrdinalIgnoreCase)) return typeof(Guid);
            if (valueType.Equals("Int32", StringComparison.OrdinalIgnoreCase) || valueType.Equals("Int", StringComparison.OrdinalIgnoreCase)) return typeof(int);
            if (valueType.Equals("Double", StringComparison.OrdinalIgnoreCase)) return typeof(double);
            if (valueType.Equals("Boolean", StringComparison.OrdinalIgnoreCase) || valueType.Equals("Bool", StringComparison.OrdinalIgnoreCase)) return typeof(bool);
            if (expression.Literal is int) return typeof(int);
            if (expression.Literal is bool) return typeof(bool);
            if (expression.Literal is Guid) return typeof(Guid);
            if (expression.Literal is double || expression.Literal is float || expression.Literal is decimal) return typeof(double);
            var type = (expression.Type ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (type == "lookuplistitemintproperty" || type == "lookupsplistitemintproperty" || type == "lookupsplistitemint32property") return typeof(int);
            if (type == "lookuplistitemguid" || type == "lookupsplistitemguid" || type == "getcurrentitemguid" || type == "getcurrentlistid") return typeof(Guid);
            return typeof(string);
        }

        private static bool HasExpression(ExpressionYaml expression) => expression != null && (!string.IsNullOrWhiteSpace(expression.Variable) || !string.IsNullOrWhiteSpace(expression.Type) || expression.ToString != null || expression.Value != null || expression.Literal != null);
    }
}

