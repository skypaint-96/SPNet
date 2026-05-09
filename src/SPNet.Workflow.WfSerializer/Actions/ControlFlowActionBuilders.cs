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
        private static Activity BuildWhile(WhileActionYaml action, WorkflowActivityBuildContext context)
        {
            return new While
            {
                DisplayName = string.Equals(action.Type, "loop", StringComparison.OrdinalIgnoreCase) ? "loop" : "while",
                Condition = BuildBooleanExpression(action.Condition, context.ValueExpressionTypes, context.ComparisonExpressionTypes),
                Body = BuildSequence(action.Actions, context)
            };
        }

        private static Activity BuildIf(IfActionYaml action, WorkflowActivityBuildContext context)
        {
            return new If
            {
                DisplayName = "if",
                Condition = BuildBooleanExpression(action.Condition, context.ValueExpressionTypes, context.ComparisonExpressionTypes),
                Then = BuildSequence(action.Then, context),
                Else = action.Else == null || action.Else.Count == 0 ? null : BuildSequence(action.Else, context)
            };
        }

        private static Sequence BuildSequence(System.Collections.Generic.IEnumerable<WorkflowActionYaml> actions, WorkflowActivityBuildContext context)
        {
            var sequence = new Sequence();
            foreach (var child in actions ?? Enumerable.Empty<WorkflowActionYaml>()) sequence.Activities.Add(BuildAction(child, context));
            return sequence;
        }
    }
}
