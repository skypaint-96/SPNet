using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using System.Reflection;

namespace SPNet.Workflow.WfSerializer
{
    public static partial class WfActivityBuilderSerializer
    {
        internal static Activity<bool> BuildBooleanExpression(ComparisonExpressionYaml condition, ValueExpressionTypes valueExpressionTypes, ComparisonExpressionTypes expressionTypes)
        {
            return CreateExpressionFactory(valueExpressionTypes).BuildBooleanExpression(condition, expressionTypes);
        }

        internal sealed class ValueExpressionTypes
        {
            // These are structured Microsoft.Activities proxy expression activities that SharePoint Workflow Manager accepts.
            // Do not replace them with raw VisualBasicValue/VisualBasicReference/CSharpValue/CSharpReference nodes; those require compilation and fail publish validation.
            public ValueExpressionTypes(Type toString, Type replaceString, Type substring, Type trim, Type? toLowerCase, Type? toUpperCase, Type? stringLength, Type? concatString, Type? currentDate, Type? newGuid, Type? parseGuid, Type lookupWorkflowContext, Type getCurrentListId, Type getCurrentItemGuid, Type lookupListItemStringProperty, Type? lookupListItemIntProperty, Type? lookupListItemDateTimeProperty, Type? lookupListItemGuid, Type? getDynamicValueProperty, Type buildDictionary, Type dynamicValue, Type? parseDate, Type? convertTimeZoneFromSpLocalToUtc, Type? parseDynamicValue, Type? containsDynamicValueProperty, Type? isEmptyDynamicValue, Type? createTimeSpan, Type? addToDate, Type? subtractFromDate, Type? dateInRange)
            {
                ToStringExpression = toString;
                ReplaceStringExpression = replaceString;
                SubstringExpression = substring;
                TrimExpression = trim;
                ToLowerCaseExpression = toLowerCase;
                ToUpperCaseExpression = toUpperCase;
                StringLengthExpression = stringLength;
                ConcatStringExpression = concatString;
                CurrentDateExpression = currentDate;
                NewGuidExpression = newGuid;
                ParseGuidExpression = parseGuid;
                LookupWorkflowContext = lookupWorkflowContext;
                GetCurrentListId = getCurrentListId;
                GetCurrentItemGuid = getCurrentItemGuid;
                LookupListItemStringProperty = lookupListItemStringProperty;
                LookupListItemIntProperty = lookupListItemIntProperty;
                LookupListItemDateTimeProperty = lookupListItemDateTimeProperty;
                LookupListItemGuid = lookupListItemGuid;
                GetDynamicValueProperty = getDynamicValueProperty;
                BuildDictionary = buildDictionary;
                DynamicValue = dynamicValue;
                ParseDate = parseDate;
                ConvertTimeZoneFromSpLocalToUtc = convertTimeZoneFromSpLocalToUtc;
                ParseDynamicValue = parseDynamicValue;
                ContainsDynamicValueProperty = containsDynamicValueProperty;
                IsEmptyDynamicValue = isEmptyDynamicValue;
                CreateTimeSpan = createTimeSpan;
                AddToDate = addToDate;
                SubtractFromDate = subtractFromDate;
                DateInRange = dateInRange;
            }

            public Type ToStringExpression { get; }
            public Type ReplaceStringExpression { get; }
            public Type SubstringExpression { get; }
            public Type TrimExpression { get; }
            public Type? ToLowerCaseExpression { get; }
            public Type? ToUpperCaseExpression { get; }
            public Type? StringLengthExpression { get; }
            public Type? ConcatStringExpression { get; }
            public Type? CurrentDateExpression { get; }
            public Type? NewGuidExpression { get; }
            public Type? ParseGuidExpression { get; }
            public Type LookupWorkflowContext { get; }
            public Type GetCurrentListId { get; }
            public Type GetCurrentItemGuid { get; }
            public Type LookupListItemStringProperty { get; }
            public Type? LookupListItemIntProperty { get; }
            public Type? LookupListItemDateTimeProperty { get; }
            public Type? LookupListItemGuid { get; }
            public Type? GetDynamicValueProperty { get; }
            public Type BuildDictionary { get; }
            public Type DynamicValue { get; }
            public Type? ParseDate { get; }
            public Type? ConvertTimeZoneFromSpLocalToUtc { get; }
            public Type? ParseDynamicValue { get; }
            public Type? ContainsDynamicValueProperty { get; }
            public Type? IsEmptyDynamicValue { get; }
            public Type? CreateTimeSpan { get; }
            public Type? AddToDate { get; }
            public Type? SubtractFromDate { get; }
            public Type? DateInRange { get; }
        }

        internal sealed class ComparisonExpressionTypes
        {
            public ComparisonExpressionTypes(Assembly assembly, Assembly? sharePointAssembly = null)
            {
                // Comparisons are emitted as structured Microsoft.Activities.Expressions nodes, not raw VB/C# language expressions.
                IsLessThan = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsLessThan`1");
                IsGreaterThan = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsGreaterThan`1");
                IsLessThanOrEqual = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsLessThanOrEqual`1");
                IsGreaterThanOrEqual = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsGreaterThanOrEqual`1");
                IsEqualNumber = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsEqualNumber`1");
                IsEqualBoolean = GetRequiredType(assembly, "Microsoft.Activities.Expressions.IsEqualBoolean");
                IsEqualString = GetRequiredType(assembly, "Microsoft.Activities.Expressions.IsEqualString");
                ContainsString = GetRequiredType(assembly, "Microsoft.Activities.Expressions.ContainsString");
                StartsWithString = GetRequiredType(assembly, "Microsoft.Activities.Expressions.StartsWithString");
                EndsWithString = GetRequiredType(assembly, "Microsoft.Activities.Expressions.EndsWithString");
                And = GetRequiredType(assembly, "Microsoft.Activities.Expressions.And");
                Or = GetRequiredType(assembly, "Microsoft.Activities.Expressions.Or");
                Not = GetRequiredType(assembly, "Microsoft.Activities.Expressions.Not");
                if (sharePointAssembly != null)
                {
                    IsEqualDate = sharePointAssembly.GetType("Microsoft.SharePoint.WorkflowServices.Activities.Expressions.IsEqualDate", throwOnError: false, ignoreCase: false);
                    IsEqualDynamicValue = sharePointAssembly.GetType("Microsoft.SharePoint.WorkflowServices.Activities.Expressions.IsEqualDynamicValue", throwOnError: false, ignoreCase: false);
                    IsGreaterThanDateTime = sharePointAssembly.GetType("Microsoft.SharePoint.WorkflowServices.Activities.Expressions.IsGreaterThanDateTime", throwOnError: false, ignoreCase: false);
                    IsGreaterThanOrEqualDateTime = sharePointAssembly.GetType("Microsoft.SharePoint.WorkflowServices.Activities.Expressions.IsGreaterThanOrEqualDateTime", throwOnError: false, ignoreCase: false);
                    IsLessThanDateTime = sharePointAssembly.GetType("Microsoft.SharePoint.WorkflowServices.Activities.Expressions.IsLessThanDateTime", throwOnError: false, ignoreCase: false);
                    IsLessThanOrEqualDateTime = sharePointAssembly.GetType("Microsoft.SharePoint.WorkflowServices.Activities.Expressions.IsLessThanOrEqualDateTime", throwOnError: false, ignoreCase: false);
                }
            }

            public Type IsLessThan { get; }
            public Type IsGreaterThan { get; }
            public Type IsLessThanOrEqual { get; }
            public Type IsGreaterThanOrEqual { get; }
            public Type IsEqualNumber { get; }
            public Type IsEqualBoolean { get; }
            public Type IsEqualString { get; }
            public Type ContainsString { get; }
            public Type StartsWithString { get; }
            public Type EndsWithString { get; }
            public Type And { get; }
            public Type Or { get; }
            public Type Not { get; }
            public Type? IsEqualDate { get; }
            public Type? IsEqualDynamicValue { get; }
            public Type? IsGreaterThanDateTime { get; }
            public Type? IsGreaterThanOrEqualDateTime { get; }
            public Type? IsLessThanDateTime { get; }
            public Type? IsLessThanOrEqualDateTime { get; }

            private static Type GetGenericComparisonType(Assembly assembly, string typeName) => GetRequiredType(assembly, typeName).MakeGenericType(typeof(double));
        }

        private static InArgument<T> ToInArgument<T>(ExpressionYaml expression, ValueExpressionTypes valueExpressionTypes)
        {
            return CreateExpressionFactory(valueExpressionTypes).ToInArgument<T>(expression);
        }

        private static WorkflowExpressionFactory CreateExpressionFactory(ValueExpressionTypes valueExpressionTypes) => new WorkflowExpressionFactory(valueExpressionTypes, BuildLookupListItemPropertyBase);
    }
}

