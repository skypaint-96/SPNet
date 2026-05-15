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
        private static Activity BuildGetDynamicValueProperty(GetDynamicValuePropertyActionYaml action, Type getDynamicValuePropertyType, Type dynamicValueType, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (!variableTypes.TryGetValue(action.Source ?? string.Empty, out var sourceType)) throw new InvalidOperationException(action.Type + " action source variable is not declared: " + action.Source);
            if (sourceType != dynamicValueType) throw new InvalidOperationException(action.Type + " action source must be a DynamicValue response variable: " + action.Source);
            if (!variableTypes.TryGetValue(action.To ?? string.Empty, out var targetType)) throw new InvalidOperationException(action.Type + " action target variable is not declared: " + action.To);

            var lookupType = getDynamicValuePropertyType.IsGenericTypeDefinition ? getDynamicValuePropertyType.MakeGenericType(targetType) : getDynamicValuePropertyType;
            var lookup = ActivityReflectionWriter.Create(lookupType);
            ((Activity)lookup).DisplayName = "Get DynamicValue property";
            ActivityReflectionWriter.SetProperty(lookup, "Source", Activator.CreateInstance(typeof(InArgument<>).MakeGenericType(dynamicValueType), Activator.CreateInstance(typeof(ArgumentValue<>).MakeGenericType(dynamicValueType), action.Source)!)!);
            ActivityReflectionWriter.SetProperty(lookup, "PropertyName", ToInArgument<string>(action.PropertyName, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(lookup, "Result", ActivityReflectionWriter.CreateOutArgument(targetType, action.To));
            return (Activity)lookup;
        }

        private static Activity BuildSetDynamicValueProperty(SetDynamicValuePropertyActionYaml action, Type setDynamicValuePropertyType, Type dynamicValueType, ValueExpressionTypes valueExpressionTypes, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (!variableTypes.TryGetValue(action.Source ?? string.Empty, out var sourceType)) throw new InvalidOperationException(action.Type + " action source variable is not declared: " + action.Source);
            if (sourceType != dynamicValueType) throw new InvalidOperationException(action.Type + " action source must be a DynamicValue variable: " + action.Source);

            var resultName = string.IsNullOrWhiteSpace(action.To) ? action.Source : action.To;
            if (!variableTypes.TryGetValue(resultName ?? string.Empty, out var targetType)) throw new InvalidOperationException(action.Type + " action target variable is not declared: " + resultName);
            if (targetType != dynamicValueType) throw new InvalidOperationException(action.Type + " action target must be a DynamicValue variable: " + resultName);

            var set = ActivityReflectionWriter.Create(setDynamicValuePropertyType);
            ((Activity)set).DisplayName = "Set DynamicValue property";
            ActivityReflectionWriter.SetProperty(set, "Source", ActivityReflectionWriter.CreateInArgumentReference(dynamicValueType, action.Source));
            ActivityReflectionWriter.SetProperty(set, "PropertyName", ToInArgument<string>(action.PropertyName, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(set, "PropertyValue", ToDynamicValuePropertyArgument(action.Value ?? new ExpressionYaml(), action.ValueType, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(set, "Result", ActivityReflectionWriter.CreateOutArgument(dynamicValueType, resultName));
            return (Activity)set;
        }

        private static Activity BuildDynamicValue(BuildDynamicValueActionYaml action, Type buildDynamicValueType, ValueExpressionTypes valueExpressionTypes, Type dynamicValueType, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (!variableTypes.TryGetValue(action.To ?? string.Empty, out var targetType)) throw new InvalidOperationException(action.Type + " action target variable is not declared: " + action.To);
            if (targetType != dynamicValueType) throw new InvalidOperationException(action.Type + " action target must be a DynamicValue variable: " + action.To);

            var build = ActivityReflectionWriter.Create(buildDynamicValueType);
            ((Activity)build).DisplayName = "Build DynamicValue";
            var values = ActivityReflectionWriter.GetProperty(build, "Properties", "BuildDynamicValue does not expose Properties.");
            var addMethod = values.GetType().GetMethods().First(m => m.Name == "Add" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));
            foreach (var entry in action.Entries ?? new List<DynamicValueEntryYaml>())
            {
                addMethod.Invoke(values, new object[] { entry.Key, ToDynamicValuePropertyArgument(entry.Value ?? new ExpressionYaml(), entry.ValueType, valueExpressionTypes) });
            }

            ActivityReflectionWriter.SetProperty(build, "Result", ActivityReflectionWriter.CreateInOutArgument(dynamicValueType, action.To));
            return (Activity)build;
        }

        private static Activity BuildCountDynamicValueItems(CountDynamicValueItemsActionYaml action, Type countDynamicValueItemsType, Type dynamicValueType, System.Collections.Generic.IReadOnlyDictionary<string, Type> variableTypes)
        {
            if (!variableTypes.TryGetValue(action.Source ?? string.Empty, out var sourceType)) throw new InvalidOperationException(action.Type + " action source variable is not declared: " + action.Source);
            if (sourceType != dynamicValueType) throw new InvalidOperationException(action.Type + " action source must be a DynamicValue variable: " + action.Source);
            if (!variableTypes.TryGetValue(action.To ?? string.Empty, out var targetType)) throw new InvalidOperationException(action.Type + " action target variable is not declared: " + action.To);
            if (targetType != typeof(int)) throw new InvalidOperationException(action.Type + " action target must be an Int32 variable: " + action.To);

            var count = ActivityReflectionWriter.Create(countDynamicValueItemsType);
            ((Activity)count).DisplayName = "Count DynamicValue items";
            ActivityReflectionWriter.SetProperty(count, "Source", Activator.CreateInstance(typeof(InArgument<>).MakeGenericType(dynamicValueType), Activator.CreateInstance(typeof(ArgumentValue<>).MakeGenericType(dynamicValueType), action.Source)!)!);
            ActivityReflectionWriter.SetProperty(count, "Result", new OutArgument<int>(new ArgumentReference<int>(action.To)));
            return (Activity)count;
        }

        private static InArgument<object> ToDictionaryObjectArgument(DynamicValueEntryYaml entry, ValueExpressionTypes valueExpressionTypes)
        {
            var expression = entry.Value ?? new ExpressionYaml();
            var valueType = (entry.ValueType ?? expression.ValueType ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (valueType == "boolean" || valueType == "bool" || expression.Literal is bool) return new InArgument<object>((Activity<object>)new Cast<bool, object> { Operand = ToInArgument<bool>(expression, valueExpressionTypes) });
            if (valueType == "int32" || valueType == "int" || valueType == "integer" || expression.Literal is int) return new InArgument<object>((Activity<object>)new Cast<int, object> { Operand = ToInArgument<int>(expression, valueExpressionTypes) });
            if (valueType == "datetime" || valueType == "date" || expression.Literal is DateTime) return new InArgument<object>((Activity<object>)new Cast<DateTime, object> { Operand = ToInArgument<DateTime>(expression, valueExpressionTypes) });
            if (valueType == "guid" || expression.Literal is Guid) return new InArgument<object>((Activity<object>)new Cast<Guid, object> { Operand = ToInArgument<Guid>(expression, valueExpressionTypes) });
            if (valueType == "double" || valueType == "number") return new InArgument<object>((Activity<object>)new Cast<double, object> { Operand = ToInArgument<double>(expression, valueExpressionTypes) });
            return new InArgument<object>((Activity<object>)new Cast<string, object> { Operand = ToInArgument<string>(expression, valueExpressionTypes) });
        }

        private static Argument ToDynamicValuePropertyArgument(ExpressionYaml expression, string valueType, ValueExpressionTypes valueExpressionTypes)
        {
            var normalized = (valueType ?? expression.ValueType ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (normalized == "dynamicvalue" || normalized == "dictionary") return (Argument)ActivityReflectionWriter.CreateInArgumentReference(valueExpressionTypes.DynamicValue, expression.Variable ?? string.Empty);
            if (normalized == "boolean" || normalized == "bool" || expression.Literal is bool) return ToInArgument<bool>(expression, valueExpressionTypes);
            if (normalized == "int32" || normalized == "int" || normalized == "integer" || expression.Literal is int) return ToInArgument<int>(expression, valueExpressionTypes);
            if (normalized == "datetime" || normalized == "date" || expression.Literal is DateTime) return ToInArgument<DateTime>(expression, valueExpressionTypes);
            if (normalized == "guid" || expression.Literal is Guid) return ToInArgument<Guid>(expression, valueExpressionTypes);
            if (normalized == "double" || normalized == "number") return ToInArgument<double>(expression, valueExpressionTypes);
            return ToInArgument<string>(expression, valueExpressionTypes);
        }

        private static Activity BuildCallHttpWebService(CallHttpWebServiceActionYaml action, Type callHttpWebServiceType, Type dynamicValueType, ValueExpressionTypes valueExpressionTypes)
        {
            var call = ActivityReflectionWriter.Create(callHttpWebServiceType);
            ActivityReflectionWriter.SetProperty(call, "Address", ToInArgument<string>(action.Address, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(call, "RequestType", ToInArgument<string>(NormalizeHttpRequestType(action.RequestType), valueExpressionTypes));
            ActivityReflectionWriter.SetDynamicInArgumentReferenceIfWritable(call, "RequestContent", dynamicValueType, string.IsNullOrWhiteSpace(action.RequestContent) ? SpdEmptyDynamicValueArgumentName : action.RequestContent);
            ActivityReflectionWriter.SetDynamicInArgumentReferenceIfWritable(call, "RequestHeaders", dynamicValueType, string.IsNullOrWhiteSpace(action.RequestHeaders) ? SpdRequestHeadersArgumentName : action.RequestHeaders);
            if (!string.IsNullOrWhiteSpace(action.ResponseStatusCodeTo)) ActivityReflectionWriter.SetProperty(call, "ResponseStatusCode", new OutArgument<string>(new ArgumentReference<string>(action.ResponseStatusCodeTo)));
            if (!string.IsNullOrWhiteSpace(action.ResponseContentTo)) ActivityReflectionWriter.SetProperty(call, "ResponseContent", ActivityReflectionWriter.CreateOutArgument(dynamicValueType, action.ResponseContentTo));
            if (!string.IsNullOrWhiteSpace(action.ResponseHeadersTo)) ActivityReflectionWriter.SetProperty(call, "ResponseHeaders", ActivityReflectionWriter.CreateOutArgument(dynamicValueType, action.ResponseHeadersTo));
            return (Activity)call;
        }

        private static Activity BuildSendEmail(SendEmailActionYaml action, Type emailType, Type expandInitFormUsersType, ValueExpressionTypes valueExpressionTypes)
        {
            var email = ActivityReflectionWriter.Create(emailType);
            ActivityReflectionWriter.SetProperty(email, "To", ToExpandedUserCollectionArgument(action.To, expandInitFormUsersType, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(email, "CC", ToExpandedUserCollectionArgument(action.Cc ?? new ExpressionYaml { Literal = string.Empty }, expandInitFormUsersType, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(email, "Subject", ToInArgument<string>(action.Subject ?? new ExpressionYaml { Literal = string.Empty }, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(email, "Body", ToInArgument<string>(action.Body ?? new ExpressionYaml { Literal = string.Empty }, valueExpressionTypes));
            return (Activity)email;
        }

        private static InArgument<System.Collections.ObjectModel.Collection<string>> ToExpandedUserCollectionArgument(ExpressionYaml expression, Type expandInitFormUsersType, ValueExpressionTypes valueExpressionTypes)
        {
            expression = expression ?? new ExpressionYaml { Literal = string.Empty };
            var value = Convert.ToString(expression.Literal ?? string.Empty) ?? string.Empty;
            var expand = ActivityReflectionWriter.Create(expandInitFormUsersType);
            ActivityReflectionWriter.SetProperty(expand, "Users", ToBuildCollectionArgument(expression, value, valueExpressionTypes));
            return new InArgument<System.Collections.ObjectModel.Collection<string>>((Activity<System.Collections.ObjectModel.Collection<string>>)expand);
        }

        private static InArgument<System.Collections.ObjectModel.Collection<string>> ToBuildCollectionArgument(ExpressionYaml expression, string value, ValueExpressionTypes valueExpressionTypes)
        {
            var buildCollectionType = GetRequiredType(valueExpressionTypes.ToStringExpression.Assembly, "Microsoft.Activities.BuildCollection`1").MakeGenericType(typeof(string));
            var buildCollection = ActivityReflectionWriter.Create(buildCollectionType);
            var values = ActivityReflectionWriter.GetProperty(buildCollection, "Values", "BuildCollection does not expose Values.");
            var addMethod = values.GetType().GetMethods().First(m => m.Name == "Add" && m.GetParameters().Length == 1);
            if (!string.IsNullOrWhiteSpace(expression.Variable) || !string.IsNullOrWhiteSpace(expression.Type) || expression.ToString != null || expression.Value != null || (expression.Values != null && expression.Values.Count > 0))
            {
                addMethod.Invoke(values, new object[] { ToInArgument<string>(expression, valueExpressionTypes) });
                return new InArgument<System.Collections.ObjectModel.Collection<string>>((Activity<System.Collections.ObjectModel.Collection<string>>)buildCollection);
            }

            foreach (var recipient in value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0))
            {
                addMethod.Invoke(values, new object[] { new InArgument<string>(recipient) });
            }

            return new InArgument<System.Collections.ObjectModel.Collection<string>>((Activity<System.Collections.ObjectModel.Collection<string>>)buildCollection);
        }

        private static Activity BuildSingleTask(SingleTaskActionYaml action, Type singleTaskType, ValueExpressionTypes valueExpressionTypes)
        {
            var task = ActivityReflectionWriter.Create(singleTaskType);
            ActivityReflectionWriter.SetProperty(task, "AssignedTo", ToInArgument<string>(action.AssignedTo, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(task, "Title", ToInArgument<string>(action.Title, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(task, "Body", ToInArgument<string>(action.Body ?? new ExpressionYaml { Literal = string.Empty }, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(task, "ContentTypeId", new InArgument<string>(string.IsNullOrWhiteSpace(action.ContentTypeId) ? "0x0108003365C4474CAE8C42BCE396314E88E51F" : action.ContentTypeId));
            ActivityReflectionWriter.SetProperty(task, "OutcomeFieldName", new InArgument<string>(string.IsNullOrWhiteSpace(action.OutcomeFieldName) ? "TaskOutcome" : action.OutcomeFieldName));
            ActivityReflectionWriter.SetProperty(task, "CompletedStatus", new InArgument<string>(string.IsNullOrWhiteSpace(action.CompletedStatus) ? "Completed" : action.CompletedStatus));
            ActivityReflectionWriter.SetProperty(task, "WaitForTaskCompletion", new InArgument<bool>(action.WaitForTaskCompletion));
            ActivityReflectionWriter.SetProperty(task, "PreserveIncompleteTasks", new InArgument<bool>(false));
            ActivityReflectionWriter.SetProperty(task, "WaiveAssignmentEmail", new InArgument<bool>(action.WaiveAssignmentEmail));
            ActivityReflectionWriter.SetProperty(task, "SendReminderEmail", new InArgument<bool>(false));
            ActivityReflectionWriter.SetProperty(task, "WaiveCancelationEmail", new InArgument<bool>(action.WaiveCancelationEmail));
            ActivityReflectionWriter.SetProperty(task, "DefaultTaskOutcome", new InArgument<int>(0));
            ActivityReflectionWriter.SetProperty(task, "OverdueReminderRepeat", new InArgument<int>(1));
            ActivityReflectionWriter.SetProperty(task, "OverdueRepeatTimes", new InArgument<int>(1));
            ActivityReflectionWriter.SetProperty(task, "CancelationEmailSubject", new InArgument<string>("Task Canceled - %Task: Title%"));
            ActivityReflectionWriter.SetProperty(task, "CancelationEmailBody", new InArgument<string>(DefaultTaskCancelationEmailBody));
            ActivityReflectionWriter.SetProperty(task, "AssignmentEmailSubject", ToInArgument<string>(action.AssignmentEmailSubject ?? new ExpressionYaml { Literal = "Task Assigned - %Task: Title%" }, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(task, "AssignmentEmailBody", ToInArgument<string>(action.AssignmentEmailBody ?? new ExpressionYaml { Literal = DefaultTaskAssignmentEmailBody }, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(task, "OverdueEmailSubject", new InArgument<string>("Task Overdue - %Task: Title%"));
            ActivityReflectionWriter.SetProperty(task, "OverdueEmailBody", new InArgument<string>(DefaultTaskOverdueEmailBody));
            if (HasExpression(action.DueDate)) ActivityReflectionWriter.SetProperty(task, "DueDate", ToInArgument<DateTime>(action.DueDate, valueExpressionTypes));
            if (!string.IsNullOrWhiteSpace(action.TaskIdTo)) ActivityReflectionWriter.SetProperty(task, "TaskId", new OutArgument<string>(new ArgumentReference<string>(action.TaskIdTo)));
            if (!string.IsNullOrWhiteSpace(action.OutcomeTo)) ActivityReflectionWriter.SetProperty(task, "Outcome", new OutArgument<int>(new ArgumentReference<int>(action.OutcomeTo)));
            return (Activity)task;
        }

        private const string DefaultTaskAssignmentEmailBody = "<html><body><div>You have been assigned a task.</div><a href=\"%TaskSpecial: TaskUrl%\">%Task: Title%</a><div>%Task: Body%</div></body></html>";
        private const string DefaultTaskCancelationEmailBody = "<html><body><div>One of your tasks was canceled and deleted. You do not need to take any further action on that task.</div><div>%Task: Title%</div></body></html>";
        private const string DefaultTaskOverdueEmailBody = "<html><body><div>You have an overdue task.</div><a href=\"%TaskSpecial: TaskUrl%\">%Task: Title%</a><div>%Task: Body%</div></body></html>";

        private static ExpressionYaml NormalizeHttpRequestType(ExpressionYaml requestType)
        {
            requestType = requestType ?? new ExpressionYaml { Literal = "HTTPGET" };
            if (!string.IsNullOrWhiteSpace(requestType.Variable) || !string.IsNullOrWhiteSpace(requestType.Type) || requestType.ToString != null || requestType.Value != null) return requestType;
            var value = Convert.ToString(requestType.Literal ?? string.Empty)?.Trim() ?? string.Empty;
            var normalized = value.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToUpperInvariant();
            if (normalized == "GET") return new ExpressionYaml { Literal = "HTTPGET" };
            if (normalized == "POST") return new ExpressionYaml { Literal = "HTTPPOST" };
            if (normalized == "PUT") return new ExpressionYaml { Literal = "HTTPPUT" };
            if (normalized == "DELETE") return new ExpressionYaml { Literal = "HTTPDELETE" };
            if (normalized == "HTTPGET" || normalized == "HTTPPOST" || normalized == "HTTPPUT" || normalized == "HTTPDELETE") return new ExpressionYaml { Literal = normalized };
            return requestType;
        }

        private static Activity BuildLookupRestPropertyName(LookupRestPropertyNameActionYaml action, Type lookupRestPropertyNameType, ValueExpressionTypes valueExpressionTypes)
        {
            var lookup = ActivityReflectionWriter.Create(lookupRestPropertyNameType);
            ((Activity)lookup).DisplayName = "Lookup REST property name";
            ActivityReflectionWriter.SetProperty(lookup, "ListId", ToInArgument<Guid>(action.ListId, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(lookup, "PropertyName", ToInArgument<string>(action.PropertyName, valueExpressionTypes));
            ActivityReflectionWriter.SetProperty(lookup, "Result", new OutArgument<string>(new ArgumentReference<string>(action.To)));
            return (Activity)lookup;
        }
    }
}

