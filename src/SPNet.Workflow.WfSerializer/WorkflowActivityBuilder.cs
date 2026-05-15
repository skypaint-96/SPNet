using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SPNet.Workflow.WfSerializer
{
    internal sealed class WorkflowActivityBuilder
    {
        private readonly Func<WorkflowActionYaml, WorkflowActivityBuildContext, Activity> buildAction;

        public WorkflowActivityBuilder(Func<WorkflowActionYaml, WorkflowActivityBuildContext, Activity> buildAction)
        {
            this.buildAction = buildAction ?? throw new ArgumentNullException(nameof(buildAction));
        }

        public ActivityBuilder Build(WorkflowYaml workflow, WorkflowActivityBuildContext context)
        {
            if (workflow == null) throw new ArgumentNullException(nameof(workflow));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var flowchart = BuildFlowchart(workflow, context);
            var outerSequence = new Sequence { DisplayName = workflow.EffectiveDisplayName };
            flowchart.DisplayName = workflow.EffectiveDisplayName;
            outerSequence.Activities.Add(flowchart);

            var builder = new ActivityBuilder
            {
                Name = string.IsNullOrWhiteSpace(workflow.EffectiveTechnicalName)
                    ? WfActivityBuilderSerializer.GetDottedWorkflowClassName(workflow.EffectiveDisplayName)
                    : workflow.EffectiveTechnicalName,
                Implementation = outerSequence
            };
            AddVariableDeclarations(builder, context.VariableTypes);
            AddParameterDeclarations(builder, workflow.EffectiveFormFields, context.ParameterTypes);
            return builder;
        }

        public ActivityBuilder BuildProofOfConcept(string workflowName, string workflowClassName, Type calcType, Type writeToHistoryType, Type toStringType)
        {
            var calc = ActivityReflectionWriter.Create(calcType);
            ActivityReflectionWriter.SetProperty(calc, "LValue", new InArgument<double>(1.0));
            ActivityReflectionWriter.SetProperty(calc, "RValue", new InArgument<double>(1.0));
            ActivityReflectionWriter.SetProperty(calc, "Operator", new InArgument<string>("Add"));
            ActivityReflectionWriter.SetProperty(calc, "To", new OutArgument<double>(new ArgumentReference<double>("calc")));

            var toString = ActivityReflectionWriter.Create(toStringType);
            ActivityReflectionWriter.SetProperty(toString, "Object", new InArgument<double>(new ArgumentValue<double>("calc")));

            var writeToHistory = ActivityReflectionWriter.Create(writeToHistoryType);
            ActivityReflectionWriter.SetProperty(writeToHistory, "Message", ActivityReflectionWriter.CreateInArgument(typeof(string), toString));

            var stageSequence = new Sequence { DisplayName = "Stage 1" };
            stageSequence.Activities.Add((Activity)calc);
            stageSequence.Activities.Add((Activity)writeToHistory);

            var flowStep = new FlowStep { Action = stageSequence };
            var flowchart = new Flowchart { StartNode = flowStep };
            flowchart.Nodes.Add(flowStep);

            var outerSequence = new Sequence { DisplayName = workflowName };
            outerSequence.Activities.Add(flowchart);

            var builder = new ActivityBuilder
            {
                Name = workflowClassName,
                Implementation = outerSequence
            };
            builder.Properties.Add(CreateVariableDeclaration("calc", typeof(double)));
            return builder;
        }

        private Flowchart BuildFlowchart(WorkflowYaml workflow, WorkflowActivityBuildContext context)
        {
            var flowchart = new Flowchart();
            var stages = workflow.Stages ?? new List<StageYaml>();
            var stepsByStage = new Dictionary<StageYaml, FlowStep>();
            var targetMap = new Dictionary<string, FlowStep>(StringComparer.OrdinalIgnoreCase);
            foreach (var stageModel in stages)
            {
                var step = BuildStageStep(stageModel, context);
                stepsByStage[stageModel] = step;
                flowchart.Nodes.Add(step);
                if (flowchart.StartNode == null) flowchart.StartNode = step;
                if (!string.IsNullOrWhiteSpace(stageModel.Id)) targetMap[stageModel.Id] = step;
                if (!string.IsNullOrWhiteSpace(stageModel.Name) && !targetMap.ContainsKey(stageModel.Name)) targetMap[stageModel.Name] = step;
            }

            var hasExplicitTransitions = stages.Any(s => s.Transition != null && s.Transition.IsSpecified);
            for (var i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                var step = stepsByStage[stage];
                var linearNext = i + 1 < stages.Count ? stepsByStage[stages[i + 1]] : null;
                if (!hasExplicitTransitions || stage.Transition == null || !stage.Transition.IsSpecified)
                {
                    step.Next = linearNext;
                    continue;
                }

                step.Next = BuildTransitionNode(stage, stage.Transition, targetMap, flowchart, context);
            }

            return flowchart;
        }

        private static FlowNode? BuildTransitionNode(StageYaml stage, StageTransitionYaml transition, IReadOnlyDictionary<string, FlowStep> targetMap, Flowchart flowchart, WorkflowActivityBuildContext context)
        {
            FlowNode? next = ResolveTransitionTarget(stage, transition.Default.Goto, targetMap);
            var branches = transition.Branches ?? new List<StageTransitionBranchYaml>();
            for (var i = branches.Count - 1; i >= 0; i--)
            {
                var branch = branches[i];
                var decision = new FlowDecision
                {
                    DisplayName = (string.IsNullOrWhiteSpace(stage.Name) ? "Stage" : stage.Name) + " transition",
                    Condition = WfActivityBuilderSerializer.BuildBooleanExpression(branch.Condition, context.ValueExpressionTypes, context.ComparisonExpressionTypes),
                    True = ResolveTransitionTarget(stage, branch.Goto, targetMap),
                    False = next
                };
                flowchart.Nodes.Add(decision);
                next = decision;
            }

            return next;
        }

        private static FlowNode? ResolveTransitionTarget(StageYaml stage, string target, IReadOnlyDictionary<string, FlowStep> targetMap)
        {
            if (string.Equals(target, "end", StringComparison.OrdinalIgnoreCase)) return null;
            if (targetMap.TryGetValue(target, out var node)) return node;
            throw new InvalidOperationException("Stage transition on '" + stage.Name + "' references unknown goto target: " + target);
        }

        private FlowStep BuildStageStep(StageYaml stageModel, WorkflowActivityBuildContext context)
        {
            var sequence = new Sequence { DisplayName = string.IsNullOrWhiteSpace(stageModel.Name) ? "Stage" : stageModel.Name };
            foreach (var action in stageModel.Actions ?? new List<WorkflowActionYaml>()) sequence.Activities.Add(buildAction(action, context));
            return new FlowStep { Action = sequence };
        }

        private static void AddVariableDeclarations(ActivityBuilder builder, IReadOnlyDictionary<string, Type> variableTypes)
        {
            foreach (var variable in variableTypes)
            {
                builder.Properties.Add(CreateVariableDeclaration(variable.Key, variable.Value));
            }
        }

        private static DynamicActivityProperty CreateVariableDeclaration(string name, Type valueType) =>
            new DynamicActivityProperty { Name = name, Type = typeof(InArgument<>).MakeGenericType(valueType) };

        private static void AddParameterDeclarations(ActivityBuilder builder, IReadOnlyList<ParameterYaml> parameters, IReadOnlyDictionary<string, Type> parameterTypes)
        {
            foreach (var parameter in parameters)
            {
                if (!parameterTypes.TryGetValue(parameter.Name, out var valueType)) valueType = WorkflowTypeMapper.MapParameterType(parameter);
                var property = new DynamicActivityProperty { Name = parameter.Name, Type = typeof(InArgument<>).MakeGenericType(valueType) };
                if (parameter.Default != null) property.Value = CreateInArgument(valueType, parameter.Default);
                builder.Properties.Add(property);
            }
        }

        private static object CreateInArgument(Type valueType, object defaultValue)
        {
            if (valueType == typeof(string)) return new InArgument<string>(Convert.ToString(defaultValue, CultureInfo.InvariantCulture) ?? string.Empty);
            if (valueType == typeof(bool)) return new InArgument<bool>(Convert.ToBoolean(defaultValue, CultureInfo.InvariantCulture));
            if (valueType == typeof(double)) return new InArgument<double>(Convert.ToDouble(defaultValue, CultureInfo.InvariantCulture));
            if (valueType == typeof(DateTime)) return new InArgument<DateTime>(Convert.ToDateTime(defaultValue, CultureInfo.InvariantCulture));
            if (valueType == typeof(Guid)) return new InArgument<Guid>(defaultValue is Guid guid ? guid : Guid.Parse(Convert.ToString(defaultValue, CultureInfo.InvariantCulture) ?? string.Empty));
            if (valueType == typeof(int)) return new InArgument<int>(Convert.ToInt32(defaultValue, CultureInfo.InvariantCulture));
            if (valueType == typeof(object)) return new InArgument<object>(defaultValue);
            throw new InvalidOperationException("Unsupported parameter default type: " + valueType.FullName);
        }
    }
}
