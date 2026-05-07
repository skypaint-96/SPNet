using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
            var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
            var workflow = deserializer.Deserialize<WorkflowYaml>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Workflow YAML is empty.");
            workflow.Validate();
            return workflow;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory);
            var serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).Build();
            File.WriteAllText(path, serializer.Serialize(this));
        }

        public void Validate()
        {
            if (!string.Equals(SchemaVersion, "spnet.workflow/v1", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsupported schemaVersion: " + SchemaVersion);
            if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("Workflow name is required.");
            if (Stages == null || Stages.Count == 0) throw new InvalidOperationException("At least one stage is required.");
            foreach (var action in Stages.SelectMany(s => s.Actions ?? new List<ActionYaml>())) action.Validate();
        }
    }

    public sealed class StartFlagsYaml { public bool Manual { get; set; } = true; public bool AutoStartCreate { get; set; } public bool AutoStartChange { get; set; } }
    public sealed class TargetYaml { public string Type { get; set; } = "Site"; public string ListTitle { get; set; } = string.Empty; }
    public sealed class VariableYaml { public string Name { get; set; } = string.Empty; public string Type { get; set; } = "String"; }
    public sealed class StageYaml { public string Name { get; set; } = "Stage"; public List<ActionYaml> Actions { get; set; } = new List<ActionYaml>(); }

    public sealed class ActionYaml
    {
        public string Type { get; set; } = string.Empty;
        public ExpressionYaml LValue { get; set; } = new ExpressionYaml();
        public ExpressionYaml RValue { get; set; } = new ExpressionYaml();
        public string Operator { get; set; } = "Add";
        public string To { get; set; } = string.Empty;
        public ExpressionYaml Message { get; set; } = new ExpressionYaml();
        public string Status { get; set; } = string.Empty;

        public void Validate()
        {
            var t = (Type ?? string.Empty).ToLowerInvariant();
            if (t != "calc" && t != "writehistory" && t != "setstatus") throw new InvalidOperationException("Unsupported action type: " + Type);
            if (t == "calc" && string.IsNullOrWhiteSpace(To)) throw new InvalidOperationException("calc action requires 'to'.");
        }
    }

    public sealed class ExpressionYaml
    {
        public object Literal { get; set; } = null;
        public string Variable { get; set; } = string.Empty;
        public ExpressionYaml ToString { get; set; } = null;
    }
}
