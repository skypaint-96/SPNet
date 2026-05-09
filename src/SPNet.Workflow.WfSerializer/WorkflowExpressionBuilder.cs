using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using System.Reflection;

namespace SPNet.Workflow.WfSerializer
{
    public static partial class WfActivityBuilderSerializer
    {
        private static Activity<bool> BuildBooleanExpression(ComparisonExpressionYaml condition, ValueExpressionTypes valueExpressionTypes, ComparisonExpressionTypes expressionTypes)
        {
            return CreateExpressionFactory(valueExpressionTypes).BuildBooleanExpression(condition, expressionTypes);
        }

        internal sealed class ValueExpressionTypes
        {
            public ValueExpressionTypes(Type toString, Type lookupWorkflowContext, Type getCurrentListId, Type getCurrentItemGuid, Type lookupListItemStringProperty, Type buildDictionary)
            {
                ToStringExpression = toString;
                LookupWorkflowContext = lookupWorkflowContext;
                GetCurrentListId = getCurrentListId;
                GetCurrentItemGuid = getCurrentItemGuid;
                LookupListItemStringProperty = lookupListItemStringProperty;
                BuildDictionary = buildDictionary;
            }

            public Type ToStringExpression { get; }
            public Type LookupWorkflowContext { get; }
            public Type GetCurrentListId { get; }
            public Type GetCurrentItemGuid { get; }
            public Type LookupListItemStringProperty { get; }
            public Type BuildDictionary { get; }
        }

        internal sealed class ComparisonExpressionTypes
        {
            public ComparisonExpressionTypes(Assembly assembly)
            {
                IsLessThan = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsLessThan`1");
                IsGreaterThan = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsGreaterThan`1");
                IsLessThanOrEqual = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsLessThanOrEqual`1");
                IsGreaterThanOrEqual = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsGreaterThanOrEqual`1");
                IsEqualNumber = GetGenericComparisonType(assembly, "Microsoft.Activities.Expressions.IsEqualNumber`1");
            }

            public Type IsLessThan { get; }
            public Type IsGreaterThan { get; }
            public Type IsLessThanOrEqual { get; }
            public Type IsGreaterThanOrEqual { get; }
            public Type IsEqualNumber { get; }

            private static Type GetGenericComparisonType(Assembly assembly, string typeName) => GetRequiredType(assembly, typeName).MakeGenericType(typeof(double));
        }

        private static InArgument<T> ToInArgument<T>(ExpressionYaml expression, ValueExpressionTypes valueExpressionTypes)
        {
            return CreateExpressionFactory(valueExpressionTypes).ToInArgument<T>(expression);
        }

        private static WorkflowExpressionFactory CreateExpressionFactory(ValueExpressionTypes valueExpressionTypes) => new WorkflowExpressionFactory(valueExpressionTypes, BuildLookupListItemPropertyBase);
    }
}

