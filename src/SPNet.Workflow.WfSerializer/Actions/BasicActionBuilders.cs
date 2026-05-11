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
            if (targetType.FullName == "Microsoft.Activities.DynamicValue") return new Assign<object> { To = new OutArgument<object>(new ArgumentReference<object>(action.To)), Value = ToInArgument<object>(value, valueExpressionTypes) };
            return new Assign<string> { To = new OutArgument<string>(new ArgumentReference<string>(action.To)), Value = ToInArgument<string>(value, valueExpressionTypes) };
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

        private static void ValidateStringManipulationTarget(ITargetedActionYaml action, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (!variableTypes.TryGetValue(action.To ?? string.Empty, out var targetType)) throw new InvalidOperationException(action.GetType().Name + " target variable is not declared: " + action.To);
            if (targetType != typeof(string)) throw new InvalidOperationException(action.GetType().Name + " target variable must be String: " + action.To);
        }

        private static bool HasStringExpression(ExpressionYaml expression) => expression != null && (!string.IsNullOrWhiteSpace(expression.Variable) || !string.IsNullOrWhiteSpace(expression.Type) || expression.ToString != null || expression.Value != null || expression.Literal != null);
    }
}

