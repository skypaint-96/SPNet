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
            return new InArgument<object>((Activity<object>)new Cast<string, object> { Operand = ToInArgument<string>(expression, valueExpressionTypes) });
        }

        private static bool HasExpression(ExpressionYaml expression) => expression != null && (!string.IsNullOrWhiteSpace(expression.Variable) || !string.IsNullOrWhiteSpace(expression.Type) || expression.ToString != null || expression.Value != null || expression.Literal != null);
    }
}

