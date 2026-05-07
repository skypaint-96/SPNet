using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.ObjectFactories;

namespace SPNet.Workflow.WfSerializer
{
    public sealed class SpNetToolConfig
    {
        public string SpdCacheFolder { get; set; } = string.Empty;
        public Dictionary<string, string> SpdMetadataTokens { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static SpNetToolConfig Load(string path)
        {
            var defaults = File.Exists(Path.Combine("config", "spnet.defaults.yml")) ? Path.Combine("config", "spnet.defaults.yml") : string.Empty;
            var config = new SpNetToolConfig();
            if (!string.IsNullOrWhiteSpace(defaults)) config.Merge(Read(defaults));
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) config.Merge(Read(path));
            return config;
        }

        private static SpNetToolConfig Read(string path)
        {
            var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
            return deserializer.Deserialize<SpNetToolConfig>(File.ReadAllText(path)) ?? new SpNetToolConfig();
        }

        private void Merge(SpNetToolConfig other)
        {
            if (!string.IsNullOrWhiteSpace(other.SpdCacheFolder)) SpdCacheFolder = other.SpdCacheFolder;
            foreach (var pair in other.SpdMetadataTokens ?? new Dictionary<string, string>()) SpdMetadataTokens[pair.Key] = pair.Value;
        }
    }

    public sealed class WorkflowYaml
    {
        public string SchemaVersion { get; set; } = "spnet.workflow/v1";
        public string Name { get; set; } = "GeneratedWorkflow";
        public string TechnicalName { get; set; } = string.Empty;
        public StartFlagsYaml Start { get; set; } = new StartFlagsYaml();
        public TargetYaml Target { get; set; } = new TargetYaml();
        public List<VariableYaml> Variables { get; set; } = new List<VariableYaml>();
        public List<StageYaml> Stages { get; set; } = new List<StageYaml>();
        public List<string> ExportWarnings { get; set; } = new List<string>();

        public static WorkflowYaml Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Workflow YAML not found: " + path, path);
            var deserializer = CreateDeserializer();
            var workflow = deserializer.Deserialize<WorkflowYaml>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Workflow YAML is empty.");
            workflow.Validate();
            return workflow;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory);
            var serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).WithTypeConverter(new WorkflowActionYamlTypeConverter()).ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).Build();
            File.WriteAllText(path, serializer.Serialize(this));
        }

        private static IDeserializer CreateDeserializer() => new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new WorkflowActionYamlTypeConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        public void Validate()
        {
            if (!string.Equals(SchemaVersion, "spnet.workflow/v1", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsupported schemaVersion: " + SchemaVersion);
            if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("Workflow name is required.");
            if (Stages == null || Stages.Count == 0) throw new InvalidOperationException("At least one stage is required.");
            foreach (var action in Stages.SelectMany(s => s.Actions ?? new List<WorkflowActionYaml>())) action.Validate();
        }
    }

    public sealed class StartFlagsYaml { public bool Manual { get; set; } = true; public bool AutoStartCreate { get; set; } public bool AutoStartChange { get; set; } }
    public sealed class TargetYaml { public string Type { get; set; } = "Site"; public string ListTitle { get; set; } = string.Empty; }
    public sealed class VariableYaml { public string Name { get; set; } = string.Empty; public string Type { get; set; } = "String"; }
    public sealed class StageYaml { public string Name { get; set; } = "Stage"; public List<WorkflowActionYaml> Actions { get; set; } = new List<WorkflowActionYaml>(); }

    public abstract class WorkflowActionYaml
    {
        public string Type { get; set; } = string.Empty;

        public virtual void Validate()
        {
            if (string.IsNullOrWhiteSpace(Type)) throw new InvalidOperationException("Action type is required.");
        }

        protected void RequireTo()
        {
            if (this is ITargetedActionYaml targeted && string.IsNullOrWhiteSpace(targeted.To)) throw new InvalidOperationException(Type + " action requires 'to'.");
        }
    }

    public interface ITargetedActionYaml { string To { get; set; } }

    public sealed class CalcActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public CalcActionYaml() { Type = "calc"; }
        public ExpressionYaml LValue { get; set; } = new ExpressionYaml();
        public ExpressionYaml RValue { get; set; } = new ExpressionYaml();
        public string Operator { get; set; } = "Add";
        public string To { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            RequireTo();
        }
    }

    public sealed class WriteHistoryActionYaml : WorkflowActionYaml
    {
        public WriteHistoryActionYaml() { Type = "writeHistory"; }
        public ExpressionYaml Message { get; set; } = new ExpressionYaml();

        public override void Validate() => base.Validate();
    }

    public sealed class SetStatusActionYaml : WorkflowActionYaml
    {
        public SetStatusActionYaml() { Type = "setStatus"; }
        public string Status { get; set; } = string.Empty;

        public override void Validate() => base.Validate();
    }

    public sealed class CommentActionYaml : WorkflowActionYaml
    {
        public CommentActionYaml() { Type = "comment"; }
        public ExpressionYaml Text { get; set; } = new ExpressionYaml();

        public override void Validate() => base.Validate();
    }

    public sealed class DelayForActionYaml : WorkflowActionYaml
    {
        public DelayForActionYaml() { Type = "delayFor"; }
        public ExpressionYaml Days { get; set; } = new ExpressionYaml { Literal = 0 };
        public ExpressionYaml Hours { get; set; } = new ExpressionYaml { Literal = 0 };
        public ExpressionYaml Minutes { get; set; } = new ExpressionYaml { Literal = 0 };

        public override void Validate() => base.Validate();
    }

    public sealed class DelayUntilActionYaml : WorkflowActionYaml
    {
        public DelayUntilActionYaml() { Type = "delayUntil"; }
        public ExpressionYaml Date { get; set; } = new ExpressionYaml();

        public override void Validate() => base.Validate();
    }

    public sealed class AssignActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public AssignActionYaml() { Type = "assign"; }
        public string To { get; set; } = string.Empty;
        public ExpressionYaml? Value { get; set; }

        public override void Validate()
        {
            base.Validate();
            RequireTo();
        }
    }

    public sealed class WhileActionYaml : WorkflowActionYaml
    {
        public WhileActionYaml() { Type = "while"; }
        public ComparisonExpressionYaml Condition { get; set; } = new ComparisonExpressionYaml();
        public List<WorkflowActionYaml> Actions { get; set; } = new List<WorkflowActionYaml>();

        public override void Validate()
        {
            base.Validate();
            Condition.Validate(Type);
            foreach (var action in Actions ?? new List<WorkflowActionYaml>()) action.Validate();
        }
    }

    public sealed class IfActionYaml : WorkflowActionYaml
    {
        public IfActionYaml() { Type = "if"; }
        public ComparisonExpressionYaml Condition { get; set; } = new ComparisonExpressionYaml();
        public List<WorkflowActionYaml> Then { get; set; } = new List<WorkflowActionYaml>();
        public List<WorkflowActionYaml> Else { get; set; } = new List<WorkflowActionYaml>();

        public override void Validate()
        {
            base.Validate();
            Condition.Validate(Type);
            foreach (var action in Then ?? new List<WorkflowActionYaml>()) action.Validate();
            foreach (var action in Else ?? new List<WorkflowActionYaml>()) action.Validate();
        }
    }

    public sealed class WorkflowActionYamlTypeConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => typeof(WorkflowActionYaml).IsAssignableFrom(type);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var yamlObject = rootDeserializer(typeof(ActionYamlSurrogate)) as ActionYamlSurrogate ?? throw new InvalidOperationException("Action YAML is empty.");
            var actionType = (yamlObject.Type ?? string.Empty).ToLowerInvariant();
            WorkflowActionYaml action;
            if (actionType == "calc") action = new CalcActionYaml { Type = yamlObject.Type ?? string.Empty, LValue = yamlObject.LValue ?? new ExpressionYaml(), RValue = yamlObject.RValue ?? new ExpressionYaml(), Operator = yamlObject.Operator ?? "Add", To = yamlObject.To ?? string.Empty };
            else if (actionType == "writehistory") action = new WriteHistoryActionYaml { Type = yamlObject.Type ?? string.Empty, Message = yamlObject.Message ?? new ExpressionYaml() };
            else if (actionType == "setstatus") action = new SetStatusActionYaml { Type = yamlObject.Type ?? string.Empty, Status = yamlObject.Status ?? string.Empty };
            else if (actionType == "comment") action = new CommentActionYaml { Type = yamlObject.Type ?? string.Empty, Text = yamlObject.Text ?? yamlObject.Message ?? new ExpressionYaml() };
            else if (actionType == "delayfor") action = new DelayForActionYaml { Type = yamlObject.Type ?? string.Empty, Days = yamlObject.Days ?? new ExpressionYaml { Literal = 0 }, Hours = yamlObject.Hours ?? new ExpressionYaml { Literal = 0 }, Minutes = yamlObject.Minutes ?? new ExpressionYaml { Literal = 0 } };
            else if (actionType == "delayuntil") action = new DelayUntilActionYaml { Type = yamlObject.Type ?? string.Empty, Date = yamlObject.Date ?? new ExpressionYaml() };
            else if (actionType == "assign" || actionType == "setvariable") action = new AssignActionYaml { Type = yamlObject.Type ?? string.Empty, To = yamlObject.To ?? string.Empty, Value = yamlObject.Value };
            else if (actionType == "while" || actionType == "loop") action = new WhileActionYaml { Type = yamlObject.Type ?? string.Empty, Condition = yamlObject.Condition ?? new ComparisonExpressionYaml(), Actions = yamlObject.Actions ?? new List<WorkflowActionYaml>() };
            else if (actionType == "if") action = new IfActionYaml { Type = yamlObject.Type ?? string.Empty, Condition = yamlObject.Condition ?? new ComparisonExpressionYaml(), Then = yamlObject.Then ?? new List<WorkflowActionYaml>(), Else = yamlObject.Else ?? new List<WorkflowActionYaml>() };
            else throw new InvalidOperationException("Unsupported action type: " + yamlObject.Type);
            return action;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));
            if (value is CalcActionYaml calc)
            {
                WriteScalar(emitter, "type", calc.Type); WriteObject(emitter, serializer, "lValue", calc.LValue); WriteScalar(emitter, "operator", calc.Operator); WriteObject(emitter, serializer, "rValue", calc.RValue); WriteScalar(emitter, "to", calc.To);
            }
            else if (value is WriteHistoryActionYaml history)
            {
                WriteScalar(emitter, "type", history.Type); WriteObject(emitter, serializer, "message", history.Message);
            }
            else if (value is SetStatusActionYaml status)
            {
                WriteScalar(emitter, "type", status.Type); WriteScalar(emitter, "status", status.Status);
            }
            else if (value is CommentActionYaml comment)
            {
                WriteScalar(emitter, "type", comment.Type); WriteObject(emitter, serializer, "text", comment.Text);
            }
            else if (value is DelayForActionYaml delayFor)
            {
                WriteScalar(emitter, "type", delayFor.Type); WriteObject(emitter, serializer, "days", delayFor.Days); WriteObject(emitter, serializer, "hours", delayFor.Hours); WriteObject(emitter, serializer, "minutes", delayFor.Minutes);
            }
            else if (value is DelayUntilActionYaml delayUntil)
            {
                WriteScalar(emitter, "type", delayUntil.Type); WriteObject(emitter, serializer, "date", delayUntil.Date);
            }
            else if (value is AssignActionYaml assign)
            {
                WriteScalar(emitter, "type", assign.Type); WriteScalar(emitter, "to", assign.To); WriteObject(emitter, serializer, "value", assign.Value);
            }
            else if (value is WhileActionYaml whileAction)
            {
                WriteScalar(emitter, "type", whileAction.Type); WriteObject(emitter, serializer, "condition", whileAction.Condition); WriteObject(emitter, serializer, "actions", whileAction.Actions);
            }
            else if (value is IfActionYaml ifAction)
            {
                WriteScalar(emitter, "type", ifAction.Type); WriteObject(emitter, serializer, "condition", ifAction.Condition); WriteObject(emitter, serializer, "then", ifAction.Then); WriteObject(emitter, serializer, "else", ifAction.Else);
            }
            else throw new InvalidOperationException("Unsupported action model: " + (value?.GetType().FullName ?? "<null>"));
            emitter.Emit(new MappingEnd());
        }

        private static void WriteScalar(IEmitter emitter, string name, string value)
        {
            emitter.Emit(new Scalar(name));
            emitter.Emit(new Scalar(value ?? string.Empty));
        }

        private static void WriteObject(IEmitter emitter, ObjectSerializer serializer, string name, object? value)
        {
            emitter.Emit(new Scalar(name));
            serializer(value ?? new ExpressionYaml());
        }

        private sealed class ActionYamlSurrogate
        {
            public string Type { get; set; } = string.Empty;
            public ExpressionYaml LValue { get; set; } = new ExpressionYaml();
            public ExpressionYaml RValue { get; set; } = new ExpressionYaml();
            public string Operator { get; set; } = "Add";
            public string To { get; set; } = string.Empty;
            public ExpressionYaml? Value { get; set; }
            public ExpressionYaml Message { get; set; } = new ExpressionYaml();
            public string Status { get; set; } = string.Empty;
            public ExpressionYaml Text { get; set; } = new ExpressionYaml();
            public ExpressionYaml Days { get; set; } = new ExpressionYaml { Literal = 0 };
            public ExpressionYaml Hours { get; set; } = new ExpressionYaml { Literal = 0 };
            public ExpressionYaml Minutes { get; set; } = new ExpressionYaml { Literal = 0 };
            public ExpressionYaml Date { get; set; } = new ExpressionYaml();
            public ComparisonExpressionYaml Condition { get; set; } = new ComparisonExpressionYaml();
            public List<WorkflowActionYaml> Actions { get; set; } = new List<WorkflowActionYaml>();
            public List<WorkflowActionYaml> Then { get; set; } = new List<WorkflowActionYaml>();
            public List<WorkflowActionYaml> Else { get; set; } = new List<WorkflowActionYaml>();
        }
    }

    public sealed class ComparisonExpressionYaml
    {
        public string Type { get; set; } = "isLessThan";
        public string Operator { get; set; } = string.Empty;
        public ExpressionYaml Left { get; set; } = new ExpressionYaml();
        public ExpressionYaml Right { get; set; } = new ExpressionYaml();

        public void Validate(string owner)
        {
            var comparison = string.IsNullOrWhiteSpace(Operator) ? Type : Operator;
            if (string.IsNullOrWhiteSpace(comparison)) throw new InvalidOperationException(owner + " action requires condition.type or condition.operator.");
        }
    }

    public sealed class ExpressionYaml
    {
        public object? Literal { get; set; }
        public string Variable { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public ExpressionYaml? Value { get; set; }
        public new ExpressionYaml? ToString { get; set; }
    }
}
