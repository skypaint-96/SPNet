using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using System.Collections.Generic;

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
            var outerSequence = new Sequence { DisplayName = workflow.Name };
            flowchart.DisplayName = workflow.Name;
            outerSequence.Activities.Add(flowchart);

            var builder = new ActivityBuilder
            {
                Name = string.IsNullOrWhiteSpace(workflow.TechnicalName)
                    ? WfActivityBuilderSerializer.GetDottedWorkflowClassName(workflow.Name)
                    : workflow.TechnicalName,
                Implementation = outerSequence
            };
            AddVariableDeclarations(builder, context.VariableTypes);
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
            FlowStep? previous = null;
            foreach (var stageModel in workflow.Stages)
            {
                var step = BuildStageStep(stageModel, context);
                flowchart.Nodes.Add(step);
                if (flowchart.StartNode == null) flowchart.StartNode = step;
                if (previous != null) previous.Next = step;
                previous = step;
            }

            return flowchart;
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
    }
}
