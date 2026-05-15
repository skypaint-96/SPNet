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
        private static Activity BuildSetField(SetFieldActionYaml action, Type setFieldType, ValueExpressionTypes valueExpressionTypes)
        {
            var setField = ActivityReflectionWriter.Create(setFieldType);
            ActivityReflectionWriter.SetProperty(setField, "FieldName", new InArgument<string>(action.FieldName ?? string.Empty));
            ActivityReflectionWriter.SetProperty(setField, "FieldValue", ToSetFieldValueArgument(action.Value, valueExpressionTypes));
            return (Activity)setField;
        }

        private static InArgument<object> ToSetFieldValueArgument(ExpressionYaml expression, ValueExpressionTypes valueExpressionTypes)
        {
            expression = expression ?? new ExpressionYaml();
            if (!string.IsNullOrWhiteSpace(expression.Variable) || expression.ToString != null || !string.IsNullOrWhiteSpace(expression.Type)) return ToInArgument<object>(expression, valueExpressionTypes);
            if (expression.Literal is string || expression.Literal == null) return new InArgument<object>((Activity<object>)new Cast<string, object> { Operand = ToInArgument<string>(expression, valueExpressionTypes) });
            if (expression.Literal is int) return new InArgument<object>((Activity<object>)new Cast<int, object> { Operand = ToInArgument<int>(expression, valueExpressionTypes) });
            if (expression.Literal is bool) return new InArgument<object>((Activity<object>)new Cast<bool, object> { Operand = ToInArgument<bool>(expression, valueExpressionTypes) });
            if (expression.Literal is DateTime) return new InArgument<object>((Activity<object>)new Cast<DateTime, object> { Operand = ToInArgument<DateTime>(expression, valueExpressionTypes) });
            if (expression.Literal is Guid) return new InArgument<object>((Activity<object>)new Cast<Guid, object> { Operand = ToInArgument<Guid>(expression, valueExpressionTypes) });
            return new InArgument<object>((Activity<object>)new Cast<double, object> { Operand = ToInArgument<double>(expression, valueExpressionTypes) });
        }

        private static Activity BuildLookupWorkflowContext(LookupWorkflowContextActionYaml action, Type lookupWorkflowContextType)
        {
            var lookup = ActivityReflectionWriter.Create(lookupWorkflowContextType);
            ActivityReflectionWriter.SetProperty(lookup, "PropertyName", new InArgument<string>(action.PropertyName ?? string.Empty));
            ActivityReflectionWriter.SetProperty(lookup, "Result", new OutArgument<string>(new ArgumentReference<string>(action.To)));
            return (Activity)lookup;
        }

        private static Activity BuildGetCurrentListId(GetCurrentListIdActionYaml action, Type getCurrentListIdType)
        {
            var lookup = ActivityReflectionWriter.Create(getCurrentListIdType);
            ActivityReflectionWriter.SetProperty(lookup, "Result", new OutArgument<Guid>(new ArgumentReference<Guid>(action.To)));
            return (Activity)lookup;
        }

        private static Activity BuildGetCurrentItemGuid(GetCurrentItemGuidActionYaml action, Type getCurrentItemGuidType)
        {
            var lookup = ActivityReflectionWriter.Create(getCurrentItemGuidType);
            ActivityReflectionWriter.SetProperty(lookup, "Result", new OutArgument<Guid>(new ArgumentReference<Guid>(action.To)));
            return (Activity)lookup;
        }

        private static Activity BuildCalc(CalcActionYaml action, Type calcType, ValueExpressionTypes valueExpressionTypes)
        {
            var calc = ActivityReflectionWriter.Create(calcType);
            ActivityReflectionWriter.SetProperty(calc, "LValue", ToInArgument<double>(action.LValue, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(calc, "RValue", ToInArgument<double>(action.RValue, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(calc, "Operator", new InArgument<string>(action.Operator ?? "Add"));
            ActivityReflectionWriter.SetProperty(calc, "To", new OutArgument<double>(new ArgumentReference<double>(action.To)));
            return (Activity)calc;
        }

        private static Activity BuildWriteHistory(WriteHistoryActionYaml action, Type writeToHistoryType, ValueExpressionTypes valueExpressionTypes)
        {
            var write = ActivityReflectionWriter.Create(writeToHistoryType);
            ActivityReflectionWriter.SetProperty(write, "Message", ToInArgument<string>(action.Message, valueExpressionTypes));
            return (Activity)write;
        }

        private static Activity BuildSetStatus(SetStatusActionYaml action, Type setStatusType)
        {
            var status = ActivityReflectionWriter.Create(setStatusType);
            ActivityReflectionWriter.SetProperty(status, "Status", new InArgument<string>(action.Status ?? string.Empty));
            return (Activity)status;
        }

        private static Activity BuildComment(CommentActionYaml action, Type commentType, ValueExpressionTypes valueExpressionTypes)
        {
            var comment = ActivityReflectionWriter.Create(commentType);
            ActivityReflectionWriter.SetProperty(comment, "CommentText", ToInArgument<string>(action.Text, valueExpressionTypes));
            return (Activity)comment;
        }

        private static Activity BuildDelayFor(DelayForActionYaml action, Type delayForType, ValueExpressionTypes valueExpressionTypes)
        {
            var delay = ActivityReflectionWriter.Create(delayForType);
            ActivityReflectionWriter.SetProperty(delay, "Days", ToInArgument<double>(action.Days, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(delay, "Hours", ToInArgument<double>(action.Hours, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(delay, "Minutes", ToInArgument<double>(action.Minutes, valueExpressionTypes));
            return (Activity)delay;
        }

        private static Activity BuildDelayUntil(DelayUntilActionYaml action, Type delayUntilType, ValueExpressionTypes valueExpressionTypes)
        {
            var delay = ActivityReflectionWriter.Create(delayUntilType);
            ActivityReflectionWriter.SetProperty(delay, "Date", ToInArgument<DateTime>(action.Date, valueExpressionTypes));
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
            if (targetType.FullName == "Microsoft.Activities.DynamicValue") return BuildDynamicValueAssign(action.To, value, targetType, valueExpressionTypes);
            return new Assign<string> { To = new OutArgument<string>(new ArgumentReference<string>(action.To)), Value = ToInArgument<string>(value, valueExpressionTypes) };
        }

        private static Activity BuildDynamicValueAssign(string variableName, ExpressionYaml value, Type dynamicValueType, ValueExpressionTypes valueExpressionTypes)
        {
            var assign = Activator.CreateInstance(typeof(Assign<>).MakeGenericType(dynamicValueType)) ?? throw new InvalidOperationException("Could not create DynamicValue assign activity.");
            ActivityReflectionWriter.SetProperty(assign, "To", ActivityReflectionWriter.CreateOutArgument(dynamicValueType, variableName));
            ActivityReflectionWriter.SetProperty(assign, "Value", ToTypedDynamicValueInArgument(value, valueExpressionTypes));
            return (Activity)assign;
        }

        private static Activity BuildStringReplace(StringReplaceActionYaml action, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateStringManipulationTarget(action, variableTypes);
            var replace = ActivityReflectionWriter.Create(valueExpressionTypes.ReplaceStringExpression);
            ActivityReflectionWriter.SetProperty(replace, "Input", ToInArgument<string>(action.Text, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(replace, "Pattern", ToInArgument<string>(action.OldValue, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(replace, "Replacement", ToInArgument<string>(action.NewValue, valueExpressionTypes));
            return new Assign<string>
            {
                To = new OutArgument<string>(new ArgumentReference<string>(action.To)),
                Value = ActivityReflectionWriter.CreateStringInArgumentFromActivity(replace)
            };
        }

        private static Activity BuildStringSubstring(StringSubstringActionYaml action, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateStringManipulationTarget(action, variableTypes);
            var hasLength = HasStringExpression(action.Length);
            var substring = ActivityReflectionWriter.Create(valueExpressionTypes.SubstringExpression);
            ActivityReflectionWriter.SetProperty(substring, "Input", ToInArgument<string>(action.Text, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(substring, "StartIndex", ToInArgument<int>(action.StartIndex, valueExpressionTypes));
            if (hasLength) ActivityReflectionWriter.SetProperty(substring, "Length", ToInArgument<int>(action.Length, valueExpressionTypes));
            return new Assign<string>
            {
                To = new OutArgument<string>(new ArgumentReference<string>(action.To)),
                Value = ActivityReflectionWriter.CreateStringInArgumentFromActivity(substring)
            };
        }

        private static Activity BuildStringTrim(StringTrimActionYaml action, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateStringManipulationTarget(action, variableTypes);
            var trim = ActivityReflectionWriter.Create(valueExpressionTypes.TrimExpression);
            ActivityReflectionWriter.SetProperty(trim, "Input", ToInArgument<string>(action.Text, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(trim, "Characters", new InArgument<string>(string.Empty));
            return new Assign<string>
            {
                To = new OutArgument<string>(new ArgumentReference<string>(action.To)),
                Value = ActivityReflectionWriter.CreateStringInArgumentFromActivity(trim)
            };
        }

        private static Activity BuildBuildUri(BuildUriActionYaml action, Type buildUriType, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateStringManipulationTarget(action, variableTypes);
            var buildUri = ActivityReflectionWriter.Create(buildUriType);
            if (HasStringExpression(action.Source)) ActivityReflectionWriter.SetProperty(buildUri, "Source", ToInArgument<string>(action.Source, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(buildUri, "Scheme", ToInArgument<string>(action.Scheme, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(buildUri, "Host", ToInArgument<string>(action.Host, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(buildUri, "Port", ToInArgument<int>(action.Port, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(buildUri, "Path", ToInArgument<string>(action.Path, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(buildUri, "Query", ToInArgument<string>(action.Query, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(buildUri, "Fragment", ToInArgument<string>(action.Fragment, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(buildUri, "Result", new OutArgument<string>(new ArgumentReference<string>(action.To)));
            return (Activity)buildUri;
        }

        private static Activity BuildGetConfigurationValue(GetConfigurationValueActionYaml action, Type getConfigurationValueType, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateStringManipulationTarget(action, variableTypes);
            var configuration = ActivityReflectionWriter.Create(getConfigurationValueType);
            ActivityReflectionWriter.SetProperty(configuration, "Name", action.Name ?? string.Empty);
            ActivityReflectionWriter.SetProperty(configuration, "DefaultValue", ToInArgument<string>(action.DefaultValue, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(configuration, "Result", new OutArgument<string>(new ArgumentReference<string>(action.To)));
            return (Activity)configuration;
        }

        private static Activity BuildGetInstanceAddress(GetInstanceAddressActionYaml action, Type getInstanceAddressType, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateStringManipulationTarget(action, variableTypes);
            var instanceAddress = ActivityReflectionWriter.Create(getInstanceAddressType);
            ActivityReflectionWriter.SetProperty(instanceAddress, "Result", new OutArgument<string>(new ArgumentReference<string>(action.To)));
            return (Activity)instanceAddress;
        }

        private static Activity BuildSetUserStatus(SetUserStatusActionYaml action, Type setUserStatusType, ValueExpressionTypes valueExpressionTypes)
        {
            var status = ActivityReflectionWriter.Create(setUserStatusType);
            ActivityReflectionWriter.SetProperty(status, "Description", ToInArgument<string>(action.Description, valueExpressionTypes));
            return (Activity)status;
        }

        private static Activity BuildCreateTimeSpan(CreateTimeSpanActionYaml action, Type createTimeSpanType, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateTargetType(action, variableTypes, typeof(TimeSpan));
            var timeSpan = ActivityReflectionWriter.Create(createTimeSpanType);
            ActivityReflectionWriter.SetProperty(timeSpan, "Days", ToInArgument<double>(action.Days, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(timeSpan, "Hours", ToInArgument<double>(action.Hours, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(timeSpan, "Minutes", ToInArgument<double>(action.Minutes, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(timeSpan, "Seconds", ToInArgument<double>(action.Seconds, valueExpressionTypes));
            return new Assign<TimeSpan> { To = new OutArgument<TimeSpan>(new ArgumentReference<TimeSpan>(action.To)), Value = ActivityReflectionWriter.CreateTimeSpanInArgumentFromActivity(timeSpan) };
        }

        private static Activity BuildGetTimeSpanFields(GetTimeSpanFieldsActionYaml action, Type getTimeSpanFieldsType, ValueExpressionTypes valueExpressionTypes)
        {
            var fields = ActivityReflectionWriter.Create(getTimeSpanFieldsType);
            ActivityReflectionWriter.SetProperty(fields, "Input", ToInArgument<TimeSpan>(action.Input, valueExpressionTypes));
            SetOptionalOutArgument<int>(fields, "Days", action.DaysTo);
            SetOptionalOutArgument<int>(fields, "Hours", action.HoursTo);
            SetOptionalOutArgument<int>(fields, "Minutes", action.MinutesTo);
            SetOptionalOutArgument<int>(fields, "Seconds", action.SecondsTo);
            SetOptionalOutArgument<double>(fields, "TotalDays", action.TotalDaysTo);
            SetOptionalOutArgument<double>(fields, "TotalHours", action.TotalHoursTo);
            SetOptionalOutArgument<double>(fields, "TotalMinutes", action.TotalMinutesTo);
            SetOptionalOutArgument<double>(fields, "TotalSeconds", action.TotalSecondsTo);
            return (Activity)fields;
        }

        private static Activity BuildDateOffset(DateOffsetActionYaml action, Type dateOffsetType, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateTargetType(action, variableTypes, typeof(DateTime));
            var offset = ActivityReflectionWriter.Create(dateOffsetType);
            ActivityReflectionWriter.SetProperty(offset, "Input", ToInArgument<DateTime>(action.Input, valueExpressionTypes));
            if (HasStringExpression(action.TimeSpan)) ActivityReflectionWriter.SetProperty(offset, "TimeSpan", ToInArgument<TimeSpan>(action.TimeSpan, valueExpressionTypes));
            SetNumericInArgumentIfWritable(offset, "Days", action.Days, valueExpressionTypes);
            SetNumericInArgumentIfWritable(offset, "Hours", action.Hours, valueExpressionTypes);
            SetNumericInArgumentIfWritable(offset, "Minutes", action.Minutes, valueExpressionTypes);
            SetNumericInArgumentIfWritable(offset, "Seconds", action.Seconds, valueExpressionTypes);
            return new Assign<DateTime> { To = new OutArgument<DateTime>(new ArgumentReference<DateTime>(action.To)), Value = ActivityReflectionWriter.CreateDateTimeInArgumentFromActivity(offset) };
        }

        private static Activity BuildDateInRange(DateInRangeActionYaml action, Type dateInRangeType, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            ValidateTargetType(action, variableTypes, typeof(bool));
            var range = ActivityReflectionWriter.Create(dateInRangeType);
            ActivityReflectionWriter.SetProperty(range, "Input", ToInArgument<DateTime>(action.Input, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(range, "Start", ToInArgument<DateTime>(action.Start, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(range, "End", ToInArgument<DateTime>(action.End, valueExpressionTypes));
            return new Assign<bool> { To = new OutArgument<bool>(new ArgumentReference<bool>(action.To)), Value = ActivityReflectionWriter.CreateBooleanInArgumentFromActivity(range) };
        }

        private static void SetOptionalOutArgument<T>(object activity, string propertyName, string variableName)
        {
            if (!string.IsNullOrWhiteSpace(variableName)) ActivityReflectionWriter.SetProperty(activity, propertyName, new OutArgument<T>(new ArgumentReference<T>(variableName)));
        }

        private static void SetNumericInArgumentIfWritable(object activity, string propertyName, ExpressionYaml expression, ValueExpressionTypes valueExpressionTypes)
        {
            var property = activity.GetType().GetProperty(propertyName);
            if (property == null || !property.CanWrite) return;
            var propertyType = property.PropertyType;
            if (propertyType == typeof(InArgument<int>)) ActivityReflectionWriter.SetProperty(activity, propertyName, ToInArgument<int>(expression, valueExpressionTypes));
            else ActivityReflectionWriter.SetProperty(activity, propertyName, ToInArgument<double>(expression, valueExpressionTypes));
        }

        private static void ValidateTargetType(ITargetedActionYaml action, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes, Type expectedType)
        {
            if (!variableTypes.TryGetValue(action.To ?? string.Empty, out var targetType)) throw new InvalidOperationException(action.GetType().Name + " target variable is not declared: " + action.To);
            if (targetType != expectedType) throw new InvalidOperationException(action.GetType().Name + " target variable must be " + expectedType.Name + ": " + action.To);
        }

        private static void ValidateStringManipulationTarget(ITargetedActionYaml action, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (!variableTypes.TryGetValue(action.To ?? string.Empty, out var targetType)) throw new InvalidOperationException(action.GetType().Name + " target variable is not declared: " + action.To);
            if (targetType != typeof(string)) throw new InvalidOperationException(action.GetType().Name + " target variable must be String: " + action.To);
        }

        private static bool HasStringExpression(ExpressionYaml expression) => expression != null && (!string.IsNullOrWhiteSpace(expression.Variable) || !string.IsNullOrWhiteSpace(expression.Type) || expression.ToString != null || expression.Value != null || expression.Literal != null);
    }
}

