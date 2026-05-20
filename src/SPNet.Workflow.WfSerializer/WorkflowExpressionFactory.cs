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
            if (op == "and") return CreateLogical(expressionTypes.And, condition.LeftCondition, condition.RightCondition, expressionTypes);
            if (op == "or") return CreateLogical(expressionTypes.Or, condition.LeftCondition, condition.RightCondition, expressionTypes);
            if (op == "not") return CreateNot(expressionTypes.Not, condition.Operand, expressionTypes);
            if (op == "contains" || op == "containsstring") return CreateStringComparison(expressionTypes.ContainsString, condition, "SearchValue");
            if (op == "startswith" || op == "startswithstring") return CreateStringComparison(expressionTypes.StartsWithString, condition, "SearchValue");
            if (op == "endswith" || op == "endswithstring") return CreateStringComparison(expressionTypes.EndsWithString, condition, "SearchValue");
            if (op == "isequalstring" || op == "equalsstring") return CreateStringComparison(expressionTypes.IsEqualString, condition, "Text");
            if (op == "isequalboolean" || op == "equalsboolean") return CreateBooleanComparison(expressionTypes.IsEqualBoolean, condition);
            if (op == "isnotequal" || op == "notequals" || op == "notequal") return CreateNot(expressionTypes.Not, new ComparisonExpressionYaml { Type = "isEqual", ValueType = condition.ValueType, Left = condition.Left, Right = condition.Right }, expressionTypes);
            if (IsDynamicValueCondition(condition)) return CreateDynamicValueComparison(expressionTypes, condition);
            if (IsDateCondition(condition, op)) return CreateDateComparison(GetDateComparisonType(expressionTypes, op), condition);
            if (op == "islessthan" || op == "lessthan") return CreateComparison(expressionTypes.IsLessThan, condition);
            if (op == "greaterthan" || op == "isgreaterthan") return CreateComparison(expressionTypes.IsGreaterThan, condition);
            if (op == "equals" || op == "equal" || op == "isequal") return CreateTypedEquality(condition, expressionTypes);
            if (op == "lessthanorequal" || op == "islessthanorequal") return CreateComparison(expressionTypes.IsLessThanOrEqual, condition);
            if (op == "greaterthanorequal" || op == "isgreaterthanorequal") return CreateComparison(expressionTypes.IsGreaterThanOrEqual, condition);
            throw new InvalidOperationException("Unsupported comparison condition: " + (condition.Operator ?? condition.Type));
        }

        public InArgument<T> ToInArgument<T>(ExpressionYaml expression)
        {
            expression = expression ?? new ExpressionYaml();
            if (string.Equals(expression.Type, "toString", StringComparison.OrdinalIgnoreCase) && expression.Value != null) expression = new ExpressionYaml { ToString = expression.Value };
            if (!string.IsNullOrWhiteSpace(expression.Variable)) return new InArgument<T>(new ArgumentValue<T>(expression.Variable));
            if (expression.Literal is string literal && string.Equals(literal, "<exported expression>", StringComparison.OrdinalIgnoreCase)) return new InArgument<T>((T)Convert.ChangeType(DefaultLiteral(typeof(T)), typeof(T)));
            if (expression.ToString != null)
            {
                var toString = ActivityReflectionWriter.Create(valueExpressionTypes.GetToStringExpressionType());
                ActivityReflectionWriter.SetProperty(toString, "Object", CreateToStringObjectArgument(expression.ToString));
                if (!string.IsNullOrWhiteSpace(expression.Format)) ActivityReflectionWriter.SetPropertyIfWritable(toString, "Format", new InArgument<string>(expression.Format));
                if (expression.CultureName != null) ActivityReflectionWriter.SetPropertyIfWritable(toString, "CultureName", CreateOptionalStringInArgument(expression.CultureName));
                return (InArgument<T>)ActivityReflectionWriter.CreateInArgument(typeof(T), toString);
            }
            var expressionActivity = CreateLookupExpressionActivity(expression, typeof(T));
            if (expressionActivity != null) return (InArgument<T>)ActivityReflectionWriter.CreateInArgument(typeof(T), expressionActivity);
            return new InArgument<T>((T)Convert.ChangeType(expression.Literal ?? DefaultLiteral(typeof(T)), typeof(T)));
        }

        private object ToInArgument(ExpressionYaml expression, Type resultType)
        {
            var method = GetType().GetMethod(nameof(ToInArgument), new[] { typeof(ExpressionYaml) }) ?? throw new InvalidOperationException("ToInArgument method was not found.");
            return method.MakeGenericMethod(resultType).Invoke(this, new object[] { expression }) ?? throw new InvalidOperationException("Could not create typed InArgument.");
        }

        private Activity<bool> CreateComparison(Type comparisonType, ComparisonExpressionYaml condition)
        {
            var comparison = ActivityReflectionWriter.Create(comparisonType);
            ActivityReflectionWriter.SetProperty(comparison, "Left", ToInArgument<double>(condition.Left));
            ActivityReflectionWriter.SetProperty(comparison, "Right", ToInArgument<double>(condition.Right));
            return (Activity<bool>)comparison;
        }

        private Activity<bool> CreateTypedEquality(ComparisonExpressionYaml condition, WfActivityBuilderSerializer.ComparisonExpressionTypes expressionTypes)
        {
            if (IsBooleanCondition(condition)) return CreateBooleanComparison(expressionTypes.IsEqualBoolean, condition);
            if (IsDateCondition(condition, "isequal")) return CreateDateComparison(GetDateComparisonType(expressionTypes, "isequal"), condition);
            if (IsStringCondition(condition)) return CreateStringComparison(expressionTypes.IsEqualString, condition, "Text");
            return CreateComparison(expressionTypes.IsEqualNumber, condition);
        }

        private Activity<bool> CreateLogical(Type logicalType, ComparisonExpressionYaml? left, ComparisonExpressionYaml? right, WfActivityBuilderSerializer.ComparisonExpressionTypes expressionTypes)
        {
            var logical = ActivityReflectionWriter.Create(logicalType);
            ActivityReflectionWriter.SetProperty(logical, "Left", (InArgument<bool>)ActivityReflectionWriter.CreateInArgument(typeof(bool), BuildBooleanExpression(left ?? new ComparisonExpressionYaml { Type = "isEqual", Left = new ExpressionYaml { Literal = 1 }, Right = new ExpressionYaml { Literal = 1 } }, expressionTypes)));
            ActivityReflectionWriter.SetProperty(logical, "Right", (InArgument<bool>)ActivityReflectionWriter.CreateInArgument(typeof(bool), BuildBooleanExpression(right ?? new ComparisonExpressionYaml { Type = "isEqual", Left = new ExpressionYaml { Literal = 1 }, Right = new ExpressionYaml { Literal = 1 } }, expressionTypes)));
            return (Activity<bool>)logical;
        }

        private Activity<bool> CreateNot(Type notType, ComparisonExpressionYaml? operand, WfActivityBuilderSerializer.ComparisonExpressionTypes expressionTypes)
        {
            var not = ActivityReflectionWriter.Create(notType);
            ActivityReflectionWriter.SetProperty(not, "Operand", (InArgument<bool>)ActivityReflectionWriter.CreateInArgument(typeof(bool), BuildBooleanExpression(operand ?? new ComparisonExpressionYaml { Type = "isEqual", Left = new ExpressionYaml { Literal = 1 }, Right = new ExpressionYaml { Literal = 1 } }, expressionTypes)));
            return (Activity<bool>)not;
        }

        private Activity<bool> CreateStringComparison(Type comparisonType, ComparisonExpressionYaml condition, string rightPropertyName)
        {
            var comparison = ActivityReflectionWriter.Create(comparisonType);
            ActivityReflectionWriter.SetProperty(comparison, "Input", ToInArgument<string>(condition.Left));
            ActivityReflectionWriter.SetProperty(comparison, rightPropertyName, ToInArgument<string>(condition.Right));
            ActivityReflectionWriter.SetPropertyIfWritable(comparison, "IgnoreCase", IsIgnoreCase(condition));
            return (Activity<bool>)comparison;
        }

        private Activity<bool> CreateBooleanComparison(Type comparisonType, ComparisonExpressionYaml condition)
        {
            var comparison = ActivityReflectionWriter.Create(comparisonType);
            ActivityReflectionWriter.SetProperty(comparison, "Left", ToInArgument<bool>(condition.Left));
            ActivityReflectionWriter.SetProperty(comparison, "Right", ToInArgument<bool>(condition.Right));
            return (Activity<bool>)comparison;
        }

        private Activity<bool> CreateDateComparison(Type comparisonType, ComparisonExpressionYaml condition)
        {
            var comparison = ActivityReflectionWriter.Create(comparisonType);
            ActivityReflectionWriter.SetProperty(comparison, "Left", ToInArgument<DateTime>(condition.Left));
            ActivityReflectionWriter.SetProperty(comparison, "Right", ToInArgument<DateTime>(condition.Right));
            return (Activity<bool>)comparison;
        }

        private Activity<bool> CreateDynamicValueComparison(WfActivityBuilderSerializer.ComparisonExpressionTypes expressionTypes, ComparisonExpressionYaml condition)
        {
            if (expressionTypes.IsEqualDynamicValue == null) throw new InvalidOperationException("DynamicValue comparisons are not supported by the local SharePoint Designer proxy assembly.");
            var comparison = ActivityReflectionWriter.Create(expressionTypes.IsEqualDynamicValue);
            ActivityReflectionWriter.SetProperty(comparison, "Left", ToInArgument(condition.Left, valueExpressionTypes.DynamicValue));
            ActivityReflectionWriter.SetProperty(comparison, "Right", ToInArgument(condition.Right, valueExpressionTypes.DynamicValue));
            return (Activity<bool>)comparison;
        }

        private static Type GetDateComparisonType(WfActivityBuilderSerializer.ComparisonExpressionTypes expressionTypes, string op)
        {
            Type? type = op == "isequal" || op == "equals" || op == "equal" ? expressionTypes.IsEqualDate : op.Contains("greaterthanorequal") ? expressionTypes.IsGreaterThanOrEqualDateTime : op.Contains("lessthanorequal") ? expressionTypes.IsLessThanOrEqualDateTime : op.Contains("greaterthan") ? expressionTypes.IsGreaterThanDateTime : expressionTypes.IsLessThanDateTime;
            return type ?? throw new InvalidOperationException("DateTime comparisons are not supported by the local SharePoint Designer proxy assembly.");
        }

        private static bool IsBooleanCondition(ComparisonExpressionYaml condition) => IsValueType(condition, "Boolean", "Bool") || condition.Left.Literal is bool || condition.Right.Literal is bool;
        private static bool IsDateCondition(ComparisonExpressionYaml condition, string op) => IsValueType(condition, "DateTime", "Date") || condition.Left.Literal is DateTime || condition.Right.Literal is DateTime || op.Contains("datetime") || op.Contains("date");
        private static bool IsDynamicValueCondition(ComparisonExpressionYaml condition) => IsValueType(condition, "DynamicValue");
        private static bool IsStringCondition(ComparisonExpressionYaml condition) => IsValueType(condition, "String", "Text") || condition.Left.Literal is string || condition.Right.Literal is string;
        private static bool IsValueType(ComparisonExpressionYaml condition, params string[] names) => names.Any(n => string.Equals(condition.ValueType, n, StringComparison.OrdinalIgnoreCase));
        private static bool IsIgnoreCase(ComparisonExpressionYaml condition) => string.Equals(condition.ValueType, "StringIgnoreCase", StringComparison.OrdinalIgnoreCase) || (condition.Operator ?? condition.Type ?? string.Empty).IndexOf("IgnoreCase", StringComparison.OrdinalIgnoreCase) >= 0;

        private object? CreateLookupExpressionActivity(ExpressionYaml expression, Type resultType)
        {
            var type = (expression.Type ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (type == "formatstring") return CreateFormatStringExpression(expression, resultType);
            if (type == "replacestring" || type == "replace") return CreateReplaceStringExpression(expression, resultType);
            if (type == "substring" || type == "substringstring") return CreateSubstringExpression(expression, resultType);
            if (type == "trim" || type == "trimstring") return CreateUnaryExpression(expression, resultType, typeof(string), typeof(string), valueExpressionTypes.TrimExpression, "Input");
            if (type == "tolowercase" || type == "lowercase" || type == "tolower") return CreateUnaryExpression(expression, resultType, typeof(string), typeof(string), valueExpressionTypes.ToLowerCaseExpression, "Input");
            if (type == "touppercase" || type == "uppercase" || type == "toupper") return CreateUnaryExpression(expression, resultType, typeof(string), typeof(string), valueExpressionTypes.ToUpperCaseExpression, "Input");
            if (type == "stringlength" || type == "lengthstring") return CreateUnaryExpression(expression, resultType, typeof(string), typeof(int), valueExpressionTypes.StringLengthExpression, "Input");
            if (type == "indexofstring" || type == "indexof") return CreateStringSearchValueExpression(expression, resultType, typeof(int), "Microsoft.Activities.Expressions.IndexOfString");
            if (type == "isemptystring" || type == "stringisempty") return CreateUnaryExpression(expression, resultType, typeof(string), typeof(bool), FindOptionalExpressionType("Microsoft.Activities.Expressions.IsEmptyString"), "Input");
            if (type == "containsstring" || type == "contains") return CreateStringSearchValueExpression(expression, resultType, typeof(bool), "Microsoft.Activities.Expressions.ContainsString");
            if (type == "startswithstring" || type == "startswith") return CreateStringSearchValueExpression(expression, resultType, typeof(bool), "Microsoft.Activities.Expressions.StartsWithString");
            if (type == "endswithstring" || type == "endswith") return CreateStringSearchValueExpression(expression, resultType, typeof(bool), "Microsoft.Activities.Expressions.EndsWithString");
            if (type == "parseboolean" || type == "parsebool") return CreateUnaryExpression(expression, resultType, typeof(string), typeof(bool), FindOptionalExpressionType("Microsoft.Activities.Expressions.ParseBoolean"), "Input");
            if (type == "concatstring" || type == "concat") return CreateConcatStringExpression(expression, resultType);
            if (type == "currentdate") return CreateParameterlessExpression(expression, resultType, typeof(DateTime), valueExpressionTypes.CurrentDateExpression);
            if (type == "newguid") return CreateParameterlessExpression(expression, resultType, typeof(Guid), valueExpressionTypes.NewGuidExpression);
            if (type == "parseguid") return CreateUnaryExpression(expression, resultType, typeof(string), typeof(Guid), valueExpressionTypes.ParseGuidExpression, "Value");
            if (type == "createtimespan") return CreateCreateTimeSpanExpression(expression, resultType);
            if (type == "addtodate") return CreateDateOffsetExpression(expression, resultType, valueExpressionTypes.AddToDate);
            if (type == "subtractfromdate") return CreateDateOffsetExpression(expression, resultType, valueExpressionTypes.SubtractFromDate);
            if (type == "dateinrange") return CreateDateInRangeExpression(expression, resultType);
            if (type == "getdynamicvalueproperty" || type == "getdictionaryitem" || type == "getdictionaryvalue" || type == "getresponseproperty") return CreateGetDynamicValuePropertyExpression(expression, resultType);
            if (type == "containsdynamicvalueproperty" || type == "containsdictionaryproperty") return CreateContainsDynamicValuePropertyExpression(expression, resultType);
            if (type == "isemptydynamicvalue" || type == "isemptydictionary") return CreateIsEmptyDynamicValueExpression(expression, resultType);
            if (type == "parsedate" || type == "parseutcdate" || type == "parseSpDate".ToLowerInvariant()) return CreateParseDateExpression(expression, resultType);
            if (type == "parsedynamicvalue") return CreateParseDynamicValueExpression(expression, resultType);
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

            if (type == "lookuplistitemintproperty" || type == "lookupsplistitemintproperty" || type == "lookupsplistitemint32property")
            {
                if (valueExpressionTypes.LookupListItemIntProperty == null) throw new InvalidOperationException(expression.Type + " is not supported by the local SharePoint Designer proxy assembly.");
                if (resultType != typeof(int) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to Int32/Object arguments.");
                return CreateLookupListItemPropertyActivity(expression, valueExpressionTypes.LookupListItemIntProperty);
            }

            if (type == "lookuplistitemdatetimeproperty" || type == "lookupsplistitemdatetimeproperty")
            {
                if (valueExpressionTypes.LookupListItemDateTimeProperty == null) throw new InvalidOperationException(expression.Type + " is not supported by the local SharePoint Designer proxy assembly.");
                if (resultType != typeof(DateTime) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to DateTime/Object arguments.");
                return CreateLookupListItemPropertyActivity(expression, valueExpressionTypes.LookupListItemDateTimeProperty);
            }

            if (type == "lookuplistitemguid" || type == "lookupsplistitemguid")
            {
                if (valueExpressionTypes.LookupListItemGuid == null) throw new InvalidOperationException(expression.Type + " is not supported by the local SharePoint Designer proxy assembly.");
                if (resultType != typeof(Guid) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to Guid/Object arguments.");
                return CreateLookupListItemPropertyActivity(expression, valueExpressionTypes.LookupListItemGuid);
            }

            return null;
        }

        private object CreateReplaceStringExpression(ExpressionYaml expression, Type resultType)
        {
            EnsureAssignableExpressionResult(expression.Type, resultType, typeof(string));
            var replace = ActivityReflectionWriter.Create(valueExpressionTypes.ReplaceStringExpression);
            ActivityReflectionWriter.SetProperty(replace, "Input", ToInArgument<string>(expression.Value ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetProperty(replace, "Pattern", ToInArgument<string>(expression.Pattern ?? expression.OldValue ?? expression.Find ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetProperty(replace, "Replacement", ToInArgument<string>(expression.Replacement ?? expression.NewValue ?? expression.ReplaceWith ?? new ExpressionYaml { Literal = string.Empty }));
            return replace;
        }

        private object CreateSubstringExpression(ExpressionYaml expression, Type resultType)
        {
            EnsureAssignableExpressionResult(expression.Type, resultType, typeof(string));
            var substring = ActivityReflectionWriter.Create(valueExpressionTypes.SubstringExpression);
            ActivityReflectionWriter.SetProperty(substring, "Input", ToInArgument<string>(expression.Value ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetProperty(substring, "StartIndex", ToInArgument<int>(expression.StartIndex ?? new ExpressionYaml { Literal = 0 }));
            if (HasExpression(expression.Length)) ActivityReflectionWriter.SetProperty(substring, "Length", ToInArgument<int>(expression.Length ?? new ExpressionYaml()));
            return substring;
        }

        private object CreateStringSearchValueExpression(ExpressionYaml expression, Type resultType, Type expressionResultType, string typeName)
        {
            EnsureAssignableExpressionResult(expression.Type, resultType, expressionResultType);
            var expressionType = FindOptionalExpressionType(typeName) ?? throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            var activity = ActivityReflectionWriter.Create(expressionType);
            ActivityReflectionWriter.SetProperty(activity, "Input", ToInArgument<string>(expression.Value ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetProperty(activity, "SearchValue", ToInArgument<string>(expression.SearchValue ?? expression.Pattern ?? expression.Find ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetPropertyIfWritable(activity, "IgnoreCase", string.Equals(expression.ValueType, "StringIgnoreCase", StringComparison.OrdinalIgnoreCase));
            return activity;
        }

        private Type? FindOptionalExpressionType(string typeName) => valueExpressionTypes.ToStringExpression.Assembly.GetType(typeName, throwOnError: false, ignoreCase: false);

        private object CreateUnaryExpression(ExpressionYaml expression, Type requestedResultType, Type inputType, Type expressionResultType, Type? expressionType, string inputPropertyName)
        {
            if (expressionType == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            EnsureAssignableExpressionResult(expression.Type, requestedResultType, expressionResultType);
            var activity = ActivityReflectionWriter.Create(expressionType);
            ActivityReflectionWriter.SetProperty(activity, inputPropertyName, ToInArgument(expression.Value ?? new ExpressionYaml(), inputType));
            return activity;
        }

        private object CreateParameterlessExpression(ExpressionYaml expression, Type requestedResultType, Type expressionResultType, Type? expressionType)
        {
            if (expressionType == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            EnsureAssignableExpressionResult(expression.Type, requestedResultType, expressionResultType);
            return ActivityReflectionWriter.Create(expressionType);
        }

        private object CreateConcatStringExpression(ExpressionYaml expression, Type resultType)
        {
            if (valueExpressionTypes.ConcatStringExpression == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            EnsureAssignableExpressionResult(expression.Type, resultType, typeof(string));
            var concat = ActivityReflectionWriter.Create(valueExpressionTypes.ConcatStringExpression);
            var inputs = ActivityReflectionWriter.GetProperty(concat, "Inputs", "ConcatString does not expose Inputs.");
            var addMethod = inputs.GetType().GetMethods().First(m => m.Name == "Add" && m.GetParameters().Length == 1);
            var values = expression.Values != null && expression.Values.Count > 0 ? expression.Values : expression.Value != null ? new System.Collections.Generic.List<ExpressionYaml> { expression.Value } : new System.Collections.Generic.List<ExpressionYaml>();
            if (values.Count == 0) throw new InvalidOperationException(expression.Type + " expression requires 'value' or 'values'.");
            foreach (var value in values) addMethod.Invoke(inputs, new object[] { ToInArgument<string>(value) });
            return concat;
        }

        private object CreateContainsDynamicValuePropertyExpression(ExpressionYaml expression, Type resultType)
        {
            if (valueExpressionTypes.ContainsDynamicValueProperty == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            EnsureAssignableExpressionResult(expression.Type, resultType, typeof(bool));
            var contains = ActivityReflectionWriter.Create(valueExpressionTypes.ContainsDynamicValueProperty);
            ActivityReflectionWriter.SetProperty(contains, "Source", CreateDynamicValueObjectArgument(expression.Source ?? expression.Value ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetProperty(contains, "PropertyName", ToInArgument<string>(string.IsNullOrWhiteSpace(expression.PropertyName) ? new ExpressionYaml { Literal = Convert.ToString(expression.Literal ?? string.Empty) ?? string.Empty } : new ExpressionYaml { Literal = expression.PropertyName }));
            return contains;
        }

        private object CreateGetDynamicValuePropertyExpression(ExpressionYaml expression, Type resultType)
        {
            if (valueExpressionTypes.GetDynamicValueProperty == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            if (!HasExpression(expression.Source)) throw new InvalidOperationException(expression.Type + " expression requires 'source'.");
            var lookupType = valueExpressionTypes.GetDynamicValueProperty.IsGenericTypeDefinition ? valueExpressionTypes.GetDynamicValueProperty.MakeGenericType(resultType) : valueExpressionTypes.GetDynamicValueProperty;
            var lookup = ActivityReflectionWriter.Create(lookupType);
            ActivityReflectionWriter.SetProperty(lookup, "Source", ToInArgument(expression.Source ?? new ExpressionYaml(), valueExpressionTypes.DynamicValue));
            ActivityReflectionWriter.SetProperty(lookup, "PropertyName", ToInArgument<string>(string.IsNullOrWhiteSpace(expression.PropertyName) ? new ExpressionYaml { Literal = Convert.ToString(expression.Literal ?? string.Empty) ?? string.Empty } : new ExpressionYaml { Literal = expression.PropertyName }));
            return lookup;
        }

        private object CreateCreateTimeSpanExpression(ExpressionYaml expression, Type resultType)
        {
            if (valueExpressionTypes.CreateTimeSpan == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            EnsureAssignableExpressionResult(expression.Type, resultType, typeof(TimeSpan));
            var timeSpan = ActivityReflectionWriter.Create(valueExpressionTypes.CreateTimeSpan);
            ActivityReflectionWriter.SetProperty(timeSpan, "Days", ToInArgument<double>(expression.Days ?? new ExpressionYaml { Literal = 0 }));
            ActivityReflectionWriter.SetProperty(timeSpan, "Hours", ToInArgument<double>(expression.Hours ?? new ExpressionYaml { Literal = 0 }));
            ActivityReflectionWriter.SetProperty(timeSpan, "Minutes", ToInArgument<double>(expression.Minutes ?? new ExpressionYaml { Literal = 0 }));
            ActivityReflectionWriter.SetProperty(timeSpan, "Seconds", ToInArgument<double>(expression.Seconds ?? new ExpressionYaml { Literal = 0 }));
            return timeSpan;
        }

        private object CreateDateOffsetExpression(ExpressionYaml expression, Type resultType, Type? expressionType)
        {
            if (expressionType == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            EnsureAssignableExpressionResult(expression.Type, resultType, typeof(DateTime));
            var offset = ActivityReflectionWriter.Create(expressionType);
            ActivityReflectionWriter.SetProperty(offset, "Input", ToInArgument<DateTime>(expression.Input ?? expression.Value ?? new ExpressionYaml()));
            if (HasExpression(expression.TimeSpan)) ActivityReflectionWriter.SetProperty(offset, "TimeSpan", ToInArgument<TimeSpan>(expression.TimeSpan ?? new ExpressionYaml()));
            SetNumericInArgumentIfWritable(offset, "Days", expression.Days ?? new ExpressionYaml { Literal = 0 });
            SetNumericInArgumentIfWritable(offset, "Hours", expression.Hours ?? new ExpressionYaml { Literal = 0 });
            SetNumericInArgumentIfWritable(offset, "Minutes", expression.Minutes ?? new ExpressionYaml { Literal = 0 });
            SetNumericInArgumentIfWritable(offset, "Seconds", expression.Seconds ?? new ExpressionYaml { Literal = 0 });
            return offset;
        }

        private void SetNumericInArgumentIfWritable(object activity, string propertyName, ExpressionYaml expression)
        {
            var property = activity.GetType().GetProperty(propertyName);
            if (property == null || !property.CanWrite) return;
            if (property.PropertyType == typeof(InArgument<int>)) ActivityReflectionWriter.SetProperty(activity, propertyName, ToInArgument<int>(expression));
            else ActivityReflectionWriter.SetProperty(activity, propertyName, ToInArgument<double>(expression));
        }

        private object CreateDateInRangeExpression(ExpressionYaml expression, Type resultType)
        {
            if (valueExpressionTypes.DateInRange == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            EnsureAssignableExpressionResult(expression.Type, resultType, typeof(bool));
            var range = ActivityReflectionWriter.Create(valueExpressionTypes.DateInRange);
            ActivityReflectionWriter.SetProperty(range, "Input", ToInArgument<DateTime>(expression.Input ?? expression.Value ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetProperty(range, "Start", ToInArgument<DateTime>(expression.Start ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetProperty(range, "End", ToInArgument<DateTime>(expression.End ?? new ExpressionYaml()));
            return range;
        }

        private object CreateIsEmptyDynamicValueExpression(ExpressionYaml expression, Type resultType)
        {
            if (valueExpressionTypes.IsEmptyDynamicValue == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            EnsureAssignableExpressionResult(expression.Type, resultType, typeof(bool));
            var isEmpty = ActivityReflectionWriter.Create(valueExpressionTypes.IsEmptyDynamicValue);
            ActivityReflectionWriter.SetProperty(isEmpty, "Input", CreateDynamicValueObjectArgument(expression.Source ?? expression.Value ?? new ExpressionYaml()));
            return isEmpty;
        }

        private object CreateDynamicValueObjectArgument(ExpressionYaml expression)
        {
            return ToInArgument(expression, valueExpressionTypes.DynamicValue);
        }

        private static void EnsureAssignableExpressionResult(string expressionType, Type requestedResultType, Type expressionResultType)
        {
            if (requestedResultType != expressionResultType && requestedResultType != typeof(object)) throw new InvalidOperationException(expressionType + " expressions can only be assigned to " + expressionResultType.Name + "/Object arguments.");
        }

        private object CreateParseDynamicValueExpression(ExpressionYaml expression, Type resultType)
        {
            if (resultType != typeof(object) && resultType != valueExpressionTypes.DynamicValue) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to DynamicValue/Object arguments.");
            if (valueExpressionTypes.ParseDynamicValue == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            var parseDynamicValue = ActivityReflectionWriter.Create(valueExpressionTypes.ParseDynamicValue);
            ActivityReflectionWriter.SetProperty(parseDynamicValue, "Json", ToInArgument<string>(expression.Value ?? new ExpressionYaml()));
            return parseDynamicValue;
        }

        private object CreateParseDateExpression(ExpressionYaml expression, Type resultType)
        {
            if (resultType != typeof(DateTime) && resultType != typeof(object)) throw new InvalidOperationException(expression.Type + " expressions can only be assigned to DateTime/Object arguments.");
            if (valueExpressionTypes.ParseDate == null) throw new InvalidOperationException(expression.Type + " is not supported by the local Microsoft.Activities proxy assembly.");
            var parseDate = ActivityReflectionWriter.Create(valueExpressionTypes.ParseDate);
            ActivityReflectionWriter.SetProperty(parseDate, "Value", ToInArgument<string>(expression.Value ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetProperty(parseDate, "CultureName", new InArgument<string>("en-US"));
            if (valueExpressionTypes.ConvertTimeZoneFromSpLocalToUtc == null) return parseDate;
            var convert = ActivityReflectionWriter.Create(valueExpressionTypes.ConvertTimeZoneFromSpLocalToUtc);
            ActivityReflectionWriter.SetProperty(convert, "Input", (InArgument<DateTime>)ActivityReflectionWriter.CreateInArgument(typeof(DateTime), parseDate));
            return convert;
        }

        private object CreateLookupListItemPropertyActivity(ExpressionYaml expression, Type lookupType)
        {
            if (string.IsNullOrWhiteSpace(expression.FieldName) && string.IsNullOrWhiteSpace(expression.PropertyName)) throw new InvalidOperationException(expression.Type + " expression requires 'fieldName' or 'propertyName'.");
            var lookup = ActivityReflectionWriter.Create(lookupType);
            ActivityReflectionWriter.SetProperty(lookup, "ListId", ToInArgument<Guid>(expression.ListId ?? new ExpressionYaml { Type = "getCurrentListId" }));
            if (HasExpression(expression.ItemGuid)) ActivityReflectionWriter.SetProperty(lookup, "ItemGuid", ToInArgument<Guid>(expression.ItemGuid ?? new ExpressionYaml()));
            if (HasExpression(expression.ItemId)) ActivityReflectionWriter.SetProperty(lookup, "ItemId", ToInArgument<int>(expression.ItemId ?? new ExpressionYaml()));
            ActivityReflectionWriter.SetProperty(lookup, "PropertyName", new InArgument<string>(string.IsNullOrWhiteSpace(expression.PropertyName) ? expression.FieldName : expression.PropertyName));
            if (HasExpression(expression.Value)) ActivityReflectionWriter.SetProperty(lookup, "PropertyValue", ToInArgument<string>(expression.Value ?? new ExpressionYaml()));
            return lookup;
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

        private object CreateToStringObjectArgument(ExpressionYaml expression)
        {
            if (!string.IsNullOrWhiteSpace(expression.Variable))
            {
                var valueType = (expression.ValueType ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
                if (valueType == "boolean" || valueType == "bool") return new InArgument<bool>(new ArgumentValue<bool>(expression.Variable));
                if (valueType == "int32" || valueType == "int" || valueType == "integer") return new InArgument<int>(new ArgumentValue<int>(expression.Variable));
                if (valueType == "datetime" || valueType == "date") return new InArgument<DateTime>(new ArgumentValue<DateTime>(expression.Variable));
                if (valueType == "guid") return new InArgument<Guid>(new ArgumentValue<Guid>(expression.Variable));
                if (valueType == "string" || valueType == "text") return new InArgument<string>(new ArgumentValue<string>(expression.Variable));
                return new InArgument<double>(new ArgumentValue<double>(expression.Variable));
            }
            var type = (expression.Type ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (type == "lookuplistitemdatetimeproperty" || type == "lookupsplistitemdatetimeproperty" || type == "currentdate" || type == "parsedate" || type == "parseutcdate" || type == "parsespdate") return ToInArgument<DateTime>(expression);
            if (type == "lookuplistitemintproperty" || type == "lookupsplistitemintproperty" || type == "lookupsplistitemint32property" || type == "stringlength" || type == "lengthstring" || type == "indexofstring" || type == "indexof") return ToInArgument<int>(expression);
            if (type == "lookuplistitemguid" || type == "lookupsplistitemguid" || type == "getcurrentlistid" || type == "getcurrentitemguid" || type == "newguid" || type == "parseguid") return ToInArgument<Guid>(expression);
            if (type == "lookupworkflowcontext" || type == "lookupcontextproperty" || type == "lookuplistitemstringproperty" || type == "lookupsplistitemstringproperty" || type.EndsWith("string", StringComparison.Ordinal) || type == "replace" || type == "trim" || type == "tolower" || type == "lowercase" || type == "toupper" || type == "uppercase" || type == "concat") return ToInArgument<string>(expression);
            if (type == "parseboolean" || type == "parsebool" || type.EndsWith("dynamicvalueproperty", StringComparison.Ordinal) || type.StartsWith("is", StringComparison.Ordinal) || type.StartsWith("contains", StringComparison.Ordinal) || type.StartsWith("starts", StringComparison.Ordinal) || type.StartsWith("ends", StringComparison.Ordinal)) return ToInArgument<bool>(expression);
            if (expression.Literal is bool boolValue) return new InArgument<bool>(boolValue);
            if (expression.Literal is int intValue) return new InArgument<int>(intValue);
            if (expression.Literal is DateTime dateTimeValue) return new InArgument<DateTime>(dateTimeValue);
            if (expression.Literal is Guid guidValue) return new InArgument<Guid>(guidValue);
            if (expression.Literal is string) return new InArgument<string>(Convert.ToString(expression.Literal));
            return new InArgument<double>(Convert.ToDouble(expression.Literal ?? 0d));
        }

        private static InArgument<string> CreateOptionalStringInArgument(string value) => string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "{x:Null}", StringComparison.OrdinalIgnoreCase) ? new InArgument<string>((string?)null) : new InArgument<string>(value);

        private static object DefaultLiteral(Type type) => type == typeof(string) ? string.Empty : type == typeof(bool) ? false : 0d;

        private static bool HasExpression(ExpressionYaml? expression) => expression != null && (!string.IsNullOrWhiteSpace(expression.Variable) || !string.IsNullOrWhiteSpace(expression.Type) || expression.ToString != null || expression.Value != null || expression.Literal != null);
    }
}
