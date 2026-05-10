using System;
using System.Collections.Generic;

namespace SPNet.Workflow.WfSerializer
{
    internal sealed class WorkflowActivityBuildContext
    {
        public WorkflowActivityBuildContext(
            IReadOnlyDictionary<string, Type> proxyActivityTypes,
            WfActivityBuilderSerializer.ValueExpressionTypes valueExpressionTypes,
            WfActivityBuilderSerializer.ComparisonExpressionTypes comparisonExpressionTypes,
            Type dynamicValueType,
            IReadOnlyDictionary<string, Type> variableTypes,
            IReadOnlyDictionary<string, Type> parameterTypes)
        {
            ProxyActivityTypes = proxyActivityTypes ?? throw new ArgumentNullException(nameof(proxyActivityTypes));
            ValueExpressionTypes = valueExpressionTypes ?? throw new ArgumentNullException(nameof(valueExpressionTypes));
            ComparisonExpressionTypes = comparisonExpressionTypes ?? throw new ArgumentNullException(nameof(comparisonExpressionTypes));
            DynamicValueType = dynamicValueType ?? throw new ArgumentNullException(nameof(dynamicValueType));
            VariableTypes = variableTypes ?? throw new ArgumentNullException(nameof(variableTypes));
            ParameterTypes = parameterTypes ?? throw new ArgumentNullException(nameof(parameterTypes));
        }

        public IReadOnlyDictionary<string, Type> ProxyActivityTypes { get; }

        public WfActivityBuilderSerializer.ValueExpressionTypes ValueExpressionTypes { get; }

        public WfActivityBuilderSerializer.ComparisonExpressionTypes ComparisonExpressionTypes { get; }

        public Type DynamicValueType { get; }

        public IReadOnlyDictionary<string, Type> VariableTypes { get; }

        public IReadOnlyDictionary<string, Type> ParameterTypes { get; }

        public Type GetProxyActivityType(string key)
        {
            if (!ProxyActivityTypes.TryGetValue(key, out var type)) throw new InvalidOperationException("Proxy activity type was not loaded: " + key);
            return type;
        }

        public Type? GetOptionalProxyActivityType(string key)
        {
            ProxyActivityTypes.TryGetValue(key, out var type);
            return type;
        }
    }
}
