using System;
using System.Activities;
using System.Activities.Expressions;
using System.Linq;
using Microsoft.VisualBasic.Activities;

namespace SPNet.Workflow.WfSerializer
{
    internal sealed class WorkflowExpressionFactory
    {
        private readonly WfActivityBuilderSerializer.ValueExpressionTypes valueExpressionTypes;
        private readonly Func<LookupListItemStringPropertyActionYaml, Type, WfActivityBuilderSerializer.ValueExpressionTypes, object> buildLookupListItemProperty;

        public WorkflowExpressionFactory(
            WfActivityBuilderSerializer.ValueExpressionTypes valueExpressionTypes,
            Func<LookupListItemStringPropertyActionYaml, Type, WfActivityBuilderSerializer.ValueExpressionTypes, object> buildLookupListItemProperty)
        {
            this.valueExpressionTypes = valueExpressionTypes ?? throw new ArgumentNullException(nameof(valueExpressionTypes));
            this.buildLookupListItemProperty = buildLookupListItemProperty ?? throw new ArgumentNullException(nameof(buildLookupListItemProperty));
        }

        public Activity<bool> BuildBooleanExpression(ComparisonExpressionYaml condition, WfActivityBuilderSerializer.ComparisonExpressionTypes expressionTypes)
        {
            condition = condition ?? new ComparisonExpressionYaml();
            var op = (string.IsNullOrWhiteSpace(condition.Operator) ? condition.Type : condition.Operator).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
            if (op == "islessthan" || op == "lessthan") return CreateComparison(expressionTypes.IsLessThan, condition);
            if (op == "greaterthan" || op == "isgreaterthan") return CreateComparison(expressionTypes.IsGreaterThan, condition);
            if (op == "equals" || op == "equal" || op == "isequal") return CreateComparison(expressionTypes.IsEqualNumber, condition);
            if (op == "lessthanorequal" || op == "islessthanorequal") return CreateComparison(expressionTypes.IsLessThanOrEqual, condition);
            if (op == "greaterthanorequal" || op == "isgreaterthanorequal") return CreateComparison(expressionTypes.IsGreaterThanOrEqual, condition);
            throw new InvalidOperationException("Unsupported comparison condition: " + (condition.Operator ?? condition.Type));
        }

        public InArgument<T> ToInArgument<T>(ExpressionYaml expression)
        {
            expression = expression ?? new ExpressionYaml();
            if (string.Equals(expression.Type, "toString", StringComparison.OrdinalIgnoreCase) && expression.Value != null) expression = new ExpressionYaml { ToString = expression.Value };
            if (!string.IsNullOrWhiteSpace(expression.Variable)) return new InArgument<T>(new ArgumentValue<T>(expression.Variable));
            if (expression.ToString != null)
            {
                var toString = ActivityReflectionWriter.Create(valueExpressionTypes.ToStringExpression);
                ActivityReflectionWriter.SetProperty(toString, "Object", CreateToStringObjectArgument(expression.ToString));
                return (InArgument<T>)ActivityReflectionWriter.CreateInArgument(typeof(T), toString);
            }
            var expressionActivity = CreateLookupExpressionActivity(expression, typeof(T));
            if (expressionActivity != null) return (InArgument<T>)ActivityReflectionWriter.CreateInArgument(typeof(T), expressionActivity);
            return new InArgument<T>((T)Convert.ChangeType(expression.Literal ?? DefaultLiteral(typeof(T)), typeof(T)));
        }

        private Activity<bool> CreateComparison(Type comparisonType, ComparisonExpressionYaml condition)
        {
            var comparison = ActivityReflectionWriter.Create(comparisonType);
            ActivityReflectionWriter.SetProperty(comparison, "Left", ToInArgument<double>(condition.Left));
            ActivityReflectionWriter.SetProperty(comparison, "Right", ToInArgument<double>(condition.Right));
            return (Activity<bool>)comparison;
        }

        private object? CreateLookupExpressionActivity(ExpressionYaml expression, Type resultType)
        {
            var type = (expression.Type ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (type == "formatstring") return CreateFormatStringExpression(expression, resultType);
            if (type == "lookupworkflowcontext" || type == "lookupcontextproperty")
            {
                if (resultType != typeof(string) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to String/Object arguments.");
                if (string.IsNullOrWhiteSpace(expression.PropertyName)) throw new InvalidOperationException(expression.Type + " expression requires 'propertyName'.");
                var lookup = ActivityReflectionWriter.Create(valueExpressionTypes.LookupWorkflowContext);
                ActivityReflectionWriter.SetProperty(lookup, "PropertyName", new InArgument<string>(expression.PropertyName));
                return lookup;
            }

            if (type == "getcurrentlistid")
            {
                if (resultType != typeof(Guid) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to Guid/Object arguments.");
                return ActivityReflectionWriter.Create(valueExpressionTypes.GetCurrentListId);
            }

            if (type == "getcurrentitemguid")
            {
                if (resultType != typeof(Guid) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to Guid/Object arguments.");
                return ActivityReflectionWriter.Create(valueExpressionTypes.GetCurrentItemGuid);
            }

            if (type == "lookuplistitemstringproperty" || type == "lookupsplistitemstringproperty")
            {
                if (resultType != typeof(string) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to String/Object arguments.");
                if (string.IsNullOrWhiteSpace(expression.FieldName) && string.IsNullOrWhiteSpace(expression.PropertyName)) throw new InvalidOperationException(expression.Type + " expression requires 'fieldName' or 'propertyName'.");
                var action = new LookupListItemStringPropertyActionYaml
                {
                    ListId = expression.ListId ?? new ExpressionYaml { Type = "getCurrentListId" },
                    ItemId = expression.ItemId ?? new ExpressionYaml(),
                    ItemGuid = expression.ItemGuid ?? new ExpressionYaml(),
                    FieldName = expression.FieldName ?? string.Empty,
                    PropertyName = expression.PropertyName ?? string.Empty
                };
                action.ValidateExpressionShape();
                return buildLookupListItemProperty(action, valueExpressionTypes.LookupListItemStringProperty, valueExpressionTypes);
            }

            return null;
        }

        private object CreateFormatStringExpression(ExpressionYaml expression, Type resultType)
        {
            if (resultType != typeof(string) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to String/Object arguments.");
            var formatStringType = valueExpressionTypes.ToStringExpression.Assembly.GetType("Microsoft.Activities.Expressions.FormatString", throwOnError: true)!;
            var formatString = ActivityReflectionWriter.Create(formatStringType);
            var format = Convert.ToString(expression.Literal ?? string.Empty) ?? string.Empty;
            ActivityReflectionWriter.SetProperty(formatString, "Format", new InArgument<string>(format.StartsWith("{}", StringComparison.Ordinal) ? format.Substring(2) : format));
            var arguments = ActivityReflectionWriter.GetProperty(formatString, "Arguments", "FormatString does not expose Arguments.");
            var addMethod = arguments.GetType().GetMethods().First(m => m.Name == "Add" && m.GetParameters().Length == 1);
            var values = expression.Values != null && expression.Values.Count > 0 ? expression.Values : expression.Value != null ? new System.Collections.Generic.List<ExpressionYaml> { expression.Value } : new System.Collections.Generic.List<ExpressionYaml>();
            if (values.Count == 0) throw new InvalidOperationException(expression.Type + " expression requires 'value' or 'values'.");
            foreach (var value in values) addMethod.Invoke(arguments, new object[] { ToInArgument<string>(value) });
            return formatString;
        }

        private static object CreateToStringObjectArgument(ExpressionYaml expression)
        {
            if (!string.IsNullOrWhiteSpace(expression.Variable)) return new InArgument<double>(new ArgumentValue<double>(expression.Variable));
            if (expression.Literal is string) return new InArgument<string>(Convert.ToString(expression.Literal));
            return new InArgument<double>(Convert.ToDouble(expression.Literal ?? 0d));
        }

        private static object DefaultLiteral(Type type) => type == typeof(string) ? string.Empty : type == typeof(bool) ? false : 0d;
    }
}
