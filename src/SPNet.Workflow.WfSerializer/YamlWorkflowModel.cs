using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.ObjectFactories;

namespace SPNet.Workflow.WfSerializer
{
    /// <summary>
    /// Represents SPNet tool configuration loaded from committed defaults and optional local overrides.
    /// </summary>
    public sealed class SpNetToolConfig
    {
        /// <summary>
        /// Gets or sets the SharePoint Designer WebsiteCache folder used to resolve legacy proxy assemblies.
        /// </summary>
        public string SpdCacheFolder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets SharePoint Designer metadata token overrides used by serializer tooling.
        /// </summary>
        public Dictionary<string, string> SpdMetadataTokens { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Loads tool configuration from committed defaults and an optional local configuration file.
        /// </summary>
        /// <param name="path">Optional path to a local configuration file.</param>
        /// <returns>The merged tool configuration.</returns>
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

    /// <summary>
    /// Root YAML model for an SPNet workflow definition.
    /// </summary>
    public sealed class WorkflowYaml
    {
        /// <summary>Gets or sets the workflow YAML schema version.</summary>
        public string SchemaVersion { get; set; } = "spnet.workflow/v1";
        /// <summary>Gets or sets the friendly workflow name.</summary>
        public string Name { get; set; } = "GeneratedWorkflow";
        /// <summary>Gets or sets the optional WF technical class name.</summary>
        public string TechnicalName { get; set; } = string.Empty;
        /// <summary>Gets or sets canonical non-XAML workflow definition metadata.</summary>
        public WorkflowDefinitionMetadataYaml Metadata { get; set; } = new WorkflowDefinitionMetadataYaml();
        /// <summary>Gets or sets workflow start options used by publishing tooling.</summary>
        public StartFlagsYaml Start { get; set; } = new StartFlagsYaml();
        /// <summary>Gets or sets workflow target metadata used by publishing tooling.</summary>
        public TargetYaml Target { get; set; } = new TargetYaml();
        /// <summary>Gets or sets declared workflow variables.</summary>
        public List<VariableYaml> Variables { get; set; } = new List<VariableYaml>();
        /// <summary>Gets or sets SharePoint initiation parameters backed by DefinitionInfo.FormField metadata.</summary>
        public List<ParameterYaml> Parameters { get; set; } = new List<ParameterYaml>();
        /// <summary>Gets or sets workflow stages and their actions.</summary>
        public List<StageYaml> Stages { get; set; } = new List<StageYaml>();
        /// <summary>Gets or sets warnings produced by partial XAML export.</summary>
        public List<string> ExportWarnings { get; set; } = new List<string>();

        public string EffectiveDisplayName => !string.IsNullOrWhiteSpace(Metadata?.DisplayName) ? Metadata.DisplayName : Name;
        public string EffectiveTechnicalName => !string.IsNullOrWhiteSpace(Metadata?.TechnicalName) ? Metadata.TechnicalName : TechnicalName;
        public TargetYaml EffectiveTarget => Metadata?.Target ?? Target ?? new TargetYaml();
        public bool EffectiveStartManual => Metadata?.Start?.Manual ?? Start?.Manual ?? true;
        public bool EffectiveStartOnCreated => Metadata?.Start?.OnCreated ?? Start?.AutoStartCreate ?? false;
        public bool EffectiveStartOnUpdated => Metadata?.Start?.OnUpdated ?? Start?.AutoStartChange ?? false;
        public List<ParameterYaml> EffectiveFormFields => Metadata?.Initiation?.FormFields != null && Metadata.Initiation.FormFields.Count > 0 ? Metadata.Initiation.FormFields : Parameters ?? new List<ParameterYaml>();

        public WorkflowDefinitionMetadataYaml ToEffectiveMetadata()
        {
            return new WorkflowDefinitionMetadataYaml
            {
                DisplayName = EffectiveDisplayName,
                TechnicalName = EffectiveTechnicalName,
                Description = Metadata?.Description ?? string.Empty,
                Target = EffectiveTarget,
                Start = new WorkflowStartOptionsYaml { Manual = EffectiveStartManual, OnCreated = EffectiveStartOnCreated, OnUpdated = EffectiveStartOnUpdated },
                Initiation = new WorkflowInitiationMetadataYaml { RequiresForm = EffectiveFormFields.Count > 0, Url = Metadata?.Initiation?.Url ?? string.Empty, FormFields = EffectiveFormFields }
            };
        }

        /// <summary>
        /// Loads and validates a workflow YAML document.
        /// </summary>
        /// <param name="path">Path to the workflow YAML file.</param>
        /// <returns>The deserialized workflow model.</returns>
        public static WorkflowYaml Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Workflow YAML not found: " + path, path);
            var deserializer = CreateDeserializer();
            var workflow = deserializer.Deserialize<WorkflowYaml>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Workflow YAML is empty.");
            workflow.Validate();
            return workflow;
        }

        /// <summary>
        /// Saves the workflow model as YAML.
        /// </summary>
        /// <param name="path">Path where YAML should be written.</param>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory);
            var serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).WithTypeConverter(new WorkflowActionYamlTypeConverter()).WithTypeConverter(new ExpressionYamlTypeConverter()).ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).Build();
            File.WriteAllText(path, serializer.Serialize(this));
        }

        private static IDeserializer CreateDeserializer() => new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new WorkflowActionYamlTypeConverter())
            .WithTypeConverter(new ExpressionYamlTypeConverter())
            .WithTypeConverter(new WorkflowParameterYamlTypeConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        /// <summary>
        /// Validates schema version, required workflow fields, and contained action models.
        /// </summary>
        public void Validate()
        {
            if (!string.Equals(SchemaVersion, "spnet.workflow/v1", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsupported schemaVersion: " + SchemaVersion);
            if (string.IsNullOrWhiteSpace(EffectiveDisplayName)) throw new InvalidOperationException("Workflow name is required.");
            if (Stages == null || Stages.Count == 0) throw new InvalidOperationException("At least one stage is required.");
            ValidateNamesAndParameters();
            foreach (var action in Stages.SelectMany(s => s.Actions ?? new List<WorkflowActionYaml>())) action.Validate();
        }

        private void ValidateNamesAndParameters()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in Variables ?? new List<VariableYaml>())
            {
                if (string.IsNullOrWhiteSpace(variable.Name)) throw new InvalidOperationException("Variable name is required.");
                if (!names.Add(variable.Name)) throw new InvalidOperationException("Duplicate variable/parameter name: " + variable.Name);
                WorkflowTypeMapper.MapDeclaredVariableType(variable.Type);
            }

            foreach (var parameter in EffectiveFormFields)
            {
                parameter.Validate();
                if (!names.Add(parameter.Name)) throw new InvalidOperationException("Duplicate variable/parameter name: " + parameter.Name);
            }

            var parameterNames = new HashSet<string>(EffectiveFormFields.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var action in EnumerateActions(Stages.SelectMany(s => s.Actions ?? new List<WorkflowActionYaml>())))
            {
                if (action is ITargetedActionYaml targeted && parameterNames.Contains(targeted.To ?? string.Empty)) throw new InvalidOperationException(action.Type + " action cannot assign to initiation parameter: " + targeted.To);
            }
        }

        private static IEnumerable<WorkflowActionYaml> EnumerateActions(IEnumerable<WorkflowActionYaml> actions)
        {
            foreach (var action in actions)
            {
                yield return action;
                if (action is IfActionYaml ifAction)
                {
                    foreach (var child in EnumerateActions(ifAction.Then ?? new List<WorkflowActionYaml>())) yield return child;
                    foreach (var child in EnumerateActions(ifAction.Else ?? new List<WorkflowActionYaml>())) yield return child;
                }
                else if (action is WhileActionYaml whileAction)
                {
                    foreach (var child in EnumerateActions(whileAction.Actions ?? new List<WorkflowActionYaml>())) yield return child;
                }
            }
        }
    }

    public sealed class StartFlagsYaml { public bool Manual { get; set; } = true; public bool AutoStartCreate { get; set; } public bool AutoStartChange { get; set; } }
    public sealed class TargetYaml { public string Type { get; set; } = "Site"; public string ListTitle { get; set; } = string.Empty; }
    public sealed class VariableYaml { public string Name { get; set; } = string.Empty; public string Type { get; set; } = "String"; }
    public sealed class ParameterYaml
    {
        public string Name { get; set; } = string.Empty;
        public string FormType { get; set; } = "Initiation";
        public string Type { get; set; } = "Text";
        public string XamlType { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Direction { get; set; } = "None";
        public object? Default { get; set; }
        public List<ChoiceYaml> Choices { get; set; } = new List<ChoiceYaml>();
        public string Format { get; set; } = string.Empty;
        public string BaseType { get; set; } = string.Empty;
        public string MaxLength { get; set; } = string.Empty;
        public string NumLines { get; set; } = string.Empty;
        public string Sortable { get; set; } = string.Empty;
        public string RichTextMode { get; set; } = string.Empty;
        public string List { get; set; } = string.Empty;
        public string ShowField { get; set; } = string.Empty;
        public string Mult { get; set; } = string.Empty;
        public string UserSelectionMode { get; set; } = string.Empty;
        public string UserSelectionScope { get; set; } = string.Empty;
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException("Parameter name is required.");
            if (string.IsNullOrWhiteSpace(Type) && string.IsNullOrWhiteSpace(XamlType)) throw new InvalidOperationException("Parameter type is required: " + Name);
            WorkflowTypeMapper.MapParameterType(this);
        }
    }

    public sealed class ChoiceYaml { public string Value { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; }
    public sealed class StageYaml { public string Name { get; set; } = "Stage"; public List<WorkflowActionYaml> Actions { get; set; } = new List<WorkflowActionYaml>(); }

    public sealed class WorkflowParameterYamlTypeConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(List<ParameterYaml>);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            if (parser.Current is SequenceStart) return ((ParameterYaml[]?)rootDeserializer(typeof(ParameterYaml[])))?.ToList() ?? new List<ParameterYaml>();
            var map = rootDeserializer(typeof(Dictionary<string, ParameterYaml>)) as Dictionary<string, ParameterYaml> ?? new Dictionary<string, ParameterYaml>(StringComparer.OrdinalIgnoreCase);
            var list = new List<ParameterYaml>();
            foreach (var pair in map)
            {
                var parameter = pair.Value ?? new ParameterYaml();
                if (string.IsNullOrWhiteSpace(parameter.Name)) parameter.Name = pair.Key;
                list.Add(parameter);
            }

            return list;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) => serializer(value);
    }

    internal static class WorkflowTypeMapper
    {
        public static Type MapDeclaredVariableType(string type)
        {
            if (string.Equals(type, "Double", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Number", StringComparison.OrdinalIgnoreCase)) return typeof(double);
            if (string.Equals(type, "Boolean", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Bool", StringComparison.OrdinalIgnoreCase)) return typeof(bool);
            if (string.Equals(type, "DateTime", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Date", StringComparison.OrdinalIgnoreCase)) return typeof(DateTime);
            if (string.Equals(type, "Guid", StringComparison.OrdinalIgnoreCase)) return typeof(Guid);
            if (string.Equals(type, "DynamicValue", StringComparison.OrdinalIgnoreCase)) return Type.GetType("Microsoft.Activities.DynamicValue, Microsoft.Activities.Proxy", throwOnError: false) ?? typeof(object);
            if (string.Equals(type, "Int32", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Int", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Integer", StringComparison.OrdinalIgnoreCase)) return typeof(int);
            if (string.Equals(type, "String", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Text", StringComparison.OrdinalIgnoreCase)) return typeof(string);
            throw new InvalidOperationException("Unsupported variable type: " + type);
        }

        public static Type MapParameterType(ParameterYaml parameter)
        {
            var xamlType = parameter.XamlType ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(xamlType))
            {
                if (xamlType.Equals("Object", StringComparison.OrdinalIgnoreCase) && string.Equals(parameter.Type, "DynamicValue", StringComparison.OrdinalIgnoreCase)) return Type.GetType("Microsoft.Activities.DynamicValue, Microsoft.Activities.Proxy", throwOnError: false) ?? typeof(object);
                if (xamlType.IndexOf("Boolean", StringComparison.OrdinalIgnoreCase) >= 0) return typeof(bool);
                if (xamlType.IndexOf("Double", StringComparison.OrdinalIgnoreCase) >= 0) return typeof(double);
                if (xamlType.IndexOf("DateTime", StringComparison.OrdinalIgnoreCase) >= 0) return typeof(DateTime);
                if (xamlType.IndexOf("DynamicValue", StringComparison.OrdinalIgnoreCase) >= 0) return Type.GetType("Microsoft.Activities.DynamicValue, Microsoft.Activities.Proxy", throwOnError: false) ?? typeof(object);
                if (xamlType.IndexOf("String", StringComparison.OrdinalIgnoreCase) >= 0) return typeof(string);
                throw new InvalidOperationException("Unsupported parameter xamlType for " + parameter.Name + ": " + parameter.XamlType);
            }

            var type = parameter.Type ?? string.Empty;
            if (string.Equals(type, "Text", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Choice", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Note", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "URL", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "UserMulti", StringComparison.OrdinalIgnoreCase)) return typeof(string);
            if (string.Equals(type, "Boolean", StringComparison.OrdinalIgnoreCase)) return typeof(bool);
            if (string.Equals(type, "Number", StringComparison.OrdinalIgnoreCase)) return typeof(double);
            if (string.Equals(type, "DateTime", StringComparison.OrdinalIgnoreCase)) return typeof(DateTime);
            if (string.Equals(type, "DynamicValue", StringComparison.OrdinalIgnoreCase)) return Type.GetType("Microsoft.Activities.DynamicValue, Microsoft.Activities.Proxy", throwOnError: false) ?? typeof(object);
            throw new InvalidOperationException("Unsupported parameter type for " + parameter.Name + ": " + parameter.Type);
        }
    }

    /// <summary>
    /// Base class for all supported SPNet YAML workflow actions.
    /// </summary>
    public abstract class WorkflowActionYaml
    {
        /// <summary>Gets or sets the action discriminator from YAML.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Validates common action requirements.
        /// </summary>
        public virtual void Validate()
        {
            if (string.IsNullOrWhiteSpace(Type)) throw new InvalidOperationException("Action type is required.");
        }

        protected void RequireTo()
        {
            if (this is ITargetedActionYaml targeted && string.IsNullOrWhiteSpace(targeted.To)) throw new InvalidOperationException(Type + " action requires 'to'.");
        }
    }

    /// <summary>
    /// Identifies action models that write a result to a workflow variable.
    /// </summary>
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

    public sealed class StringReplaceActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public StringReplaceActionYaml() { Type = "replaceString"; }
        public ExpressionYaml Text { get; set; } = new ExpressionYaml();
        public ExpressionYaml OldValue { get; set; } = new ExpressionYaml();
        public ExpressionYaml NewValue { get; set; } = new ExpressionYaml { Literal = string.Empty };
        public string To { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            RequireTo();
        }
    }

    public sealed class StringSubstringActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public StringSubstringActionYaml() { Type = "substring"; }
        public ExpressionYaml Text { get; set; } = new ExpressionYaml();
        public ExpressionYaml StartIndex { get; set; } = new ExpressionYaml { Literal = 0 };
        public ExpressionYaml Length { get; set; } = new ExpressionYaml();
        public string To { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            RequireTo();
        }
    }

    public sealed class StringTrimActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public StringTrimActionYaml() { Type = "trimString"; }
        public ExpressionYaml Text { get; set; } = new ExpressionYaml();
        public string To { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            RequireTo();
        }
    }

    public sealed class LookupWorkflowContextActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public LookupWorkflowContextActionYaml() { Type = "lookupWorkflowContext"; }
        public string PropertyName { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            throw new InvalidOperationException(Type + " is not SPD-safe as a top-level action. Use an assign/setVariable action with value: { type: lookupWorkflowContext, propertyName: ... } instead.");
        }
    }

    public sealed class GetCurrentListIdActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public GetCurrentListIdActionYaml() { Type = "getCurrentListId"; }
        public string To { get; set; } = string.Empty;
        public override void Validate() { base.Validate(); throw new InvalidOperationException(Type + " is not SPD-safe as a top-level action. Use an assign/setVariable action with value: { type: getCurrentListId } instead."); }
    }

    public sealed class GetCurrentItemGuidActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public GetCurrentItemGuidActionYaml() { Type = "getCurrentItemGuid"; }
        public string To { get; set; } = string.Empty;
        public override void Validate() { base.Validate(); throw new InvalidOperationException(Type + " is not SPD-safe as a top-level action. Use an assign/setVariable action with value: { type: getCurrentItemGuid } instead."); }
    }

    public sealed class SetFieldActionYaml : WorkflowActionYaml
    {
        public SetFieldActionYaml() { Type = "setField"; }
        public string FieldName { get; set; } = string.Empty;
        public ExpressionYaml Value { get; set; } = new ExpressionYaml();

        public override void Validate()
        {
            base.Validate();
            if (string.IsNullOrWhiteSpace(FieldName)) throw new InvalidOperationException(Type + " action requires 'fieldName'.");
        }
    }

    public abstract class ListItemLifecycleActionYaml : WorkflowActionYaml
    {
        public ExpressionYaml ListId { get; set; } = new ExpressionYaml { Type = "getCurrentListId" };
        public Dictionary<string, ExpressionYaml> Fields { get; set; } = new Dictionary<string, ExpressionYaml>(StringComparer.OrdinalIgnoreCase);

        protected void RequireFields()
        {
            if (Fields == null || Fields.Count == 0) throw new InvalidOperationException(Type + " action requires at least one field in 'fields'.");
            if (Fields.Keys.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException(Type + " action field names cannot be empty.");
        }

        protected void RequireListId()
        {
            if (ListId == null) throw new InvalidOperationException(Type + " action requires 'listId' or a current-list default.");
        }
    }

    public sealed class CreateListItemActionYaml : ListItemLifecycleActionYaml
    {
        public CreateListItemActionYaml() { Type = "createListItem"; }
        public string ItemIdTo { get; set; } = string.Empty;
        public string ItemGuidTo { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            RequireListId();
            RequireFields();
        }
    }

    public abstract class TargetedListItemActionYaml : ListItemLifecycleActionYaml
    {
        public ExpressionYaml ItemId { get; set; } = new ExpressionYaml();
        public ExpressionYaml ItemGuid { get; set; } = new ExpressionYaml();

        protected void RequireItemIdentity()
        {
            var hasId = ItemId != null && (!string.IsNullOrWhiteSpace(ItemId.Variable) || !string.IsNullOrWhiteSpace(ItemId.Type) || ItemId.ToString != null || ItemId.Value != null || ItemId.Literal != null);
            var hasGuid = ItemGuid != null && (!string.IsNullOrWhiteSpace(ItemGuid.Variable) || !string.IsNullOrWhiteSpace(ItemGuid.Type) || ItemGuid.ToString != null || ItemGuid.Value != null || ItemGuid.Literal != null);
            if (!hasId && !hasGuid) throw new InvalidOperationException(Type + " action requires 'itemId' or 'itemGuid'.");
        }
    }

    public sealed class UpdateListItemActionYaml : TargetedListItemActionYaml
    {
        public UpdateListItemActionYaml() { Type = "updateListItem"; }

        public override void Validate()
        {
            base.Validate();
            RequireListId();
            RequireItemIdentity();
            RequireFields();
        }
    }

    public sealed class DeleteListItemActionYaml : TargetedListItemActionYaml
    {
        public DeleteListItemActionYaml() { Type = "deleteListItem"; }

        public override void Validate()
        {
            base.Validate();
            RequireListId();
            RequireItemIdentity();
        }
    }

    public abstract class LookupListItemPropertyActionYaml : TargetedListItemActionYaml, ITargetedActionYaml
    {
        public string FieldName { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;

        public void ValidateExpressionShape()
        {
            RequireListId();
            RequireItemIdentity();
            RequirePropertyName();
        }

        protected void RequirePropertyName()
        {
            if (string.IsNullOrWhiteSpace(FieldName) && string.IsNullOrWhiteSpace(PropertyName)) throw new InvalidOperationException(Type + " action requires 'fieldName' or 'propertyName'.");
        }
    }

    public sealed class LookupListItemStringPropertyActionYaml : LookupListItemPropertyActionYaml
    {
        public LookupListItemStringPropertyActionYaml() { Type = "lookupListItemStringProperty"; }

        public override void Validate()
        {
            base.Validate();
            throw new InvalidOperationException(Type + " is not SPD-safe as a top-level action. Use an assign/setVariable action with value: { type: lookupListItemStringProperty, listId: { type: getCurrentListId }, itemId: ..., fieldName: ... } instead.");
        }
    }

    public sealed class LookupListItemIntPropertyActionYaml : LookupListItemPropertyActionYaml
    {
        public LookupListItemIntPropertyActionYaml() { Type = "lookupListItemIntProperty"; }

        public override void Validate()
        {
            base.Validate();
            throw new InvalidOperationException(Type + " is not SPD-safe as a top-level action, and the tested PMteamblog WebsiteCache proxy does not expose LookupSPListItemIntProperty. Do not publish this action as a visible stage action.");
        }
    }

    public sealed class CallHttpWebServiceActionYaml : WorkflowActionYaml
    {
        public CallHttpWebServiceActionYaml() { Type = "callHttpWebService"; }
        public ExpressionYaml Address { get; set; } = new ExpressionYaml();
        public ExpressionYaml RequestType { get; set; } = new ExpressionYaml { Literal = "HTTPGET" };
        public string ResponseStatusCodeTo { get; set; } = string.Empty;
        public string ResponseContentTo { get; set; } = string.Empty;
        public string ResponseHeadersTo { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            if (string.IsNullOrWhiteSpace(ResponseStatusCodeTo) && string.IsNullOrWhiteSpace(ResponseContentTo) && string.IsNullOrWhiteSpace(ResponseHeadersTo)) throw new InvalidOperationException(Type + " action requires at least one response target: responseStatusCodeTo, responseContentTo, or responseHeadersTo.");
            if (RequestType != null && string.IsNullOrWhiteSpace(RequestType.Variable) && string.IsNullOrWhiteSpace(RequestType.Type) && RequestType.ToString == null && RequestType.Value == null)
            {
                var value = Convert.ToString(RequestType.Literal ?? string.Empty)?.Trim() ?? string.Empty;
                var normalized = value.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToUpperInvariant();
                if (normalized != "GET" && normalized != "POST" && normalized != "PUT" && normalized != "DELETE" && normalized != "HTTPGET" && normalized != "HTTPPOST" && normalized != "HTTPPUT" && normalized != "HTTPDELETE") throw new InvalidOperationException(Type + " requestType must be GET, POST, PUT, DELETE, HTTPGET, HTTPPOST, HTTPPUT, or HTTPDELETE for literal methods.");
            }
        }
    }

    public sealed class SendEmailActionYaml : WorkflowActionYaml
    {
        public SendEmailActionYaml() { Type = "sendEmail"; }
        public ExpressionYaml To { get; set; } = new ExpressionYaml();
        public ExpressionYaml Cc { get; set; } = new ExpressionYaml();
        public ExpressionYaml Subject { get; set; } = new ExpressionYaml { Literal = string.Empty };
        public ExpressionYaml Body { get; set; } = new ExpressionYaml { Literal = string.Empty };

        public override void Validate()
        {
            base.Validate();
            if (To == null || (string.IsNullOrWhiteSpace(To.Variable) && string.IsNullOrWhiteSpace(To.Type) && To.ToString == null && To.Value == null && To.Literal == null)) throw new InvalidOperationException(Type + " action requires 'to'.");
        }
    }

    public sealed class SingleTaskActionYaml : WorkflowActionYaml
    {
        public SingleTaskActionYaml() { Type = "singleTask"; }
        public ExpressionYaml AssignedTo { get; set; } = new ExpressionYaml();
        public ExpressionYaml Title { get; set; } = new ExpressionYaml();
        public ExpressionYaml Body { get; set; } = new ExpressionYaml { Literal = string.Empty };
        public ExpressionYaml DueDate { get; set; } = new ExpressionYaml();
        public ExpressionYaml AssignmentEmailSubject { get; set; } = new ExpressionYaml { Literal = "Task Assigned - %Task: Title%" };
        public ExpressionYaml AssignmentEmailBody { get; set; } = new ExpressionYaml { Literal = string.Empty };
        public bool WaitForTaskCompletion { get; set; } = true;
        public bool WaiveAssignmentEmail { get; set; } = true;
        public bool WaiveCancelationEmail { get; set; } = true;
        public string ContentTypeId { get; set; } = "0x0108003365C4474CAE8C42BCE396314E88E51F";
        public string OutcomeFieldName { get; set; } = "TaskOutcome";
        public string CompletedStatus { get; set; } = "Completed";
        public string TaskIdTo { get; set; } = string.Empty;
        public string OutcomeTo { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            if (AssignedTo == null || (string.IsNullOrWhiteSpace(AssignedTo.Variable) && string.IsNullOrWhiteSpace(AssignedTo.Type) && AssignedTo.ToString == null && AssignedTo.Value == null && AssignedTo.Literal == null)) throw new InvalidOperationException(Type + " action requires 'assignedTo'.");
            if (Title == null || (string.IsNullOrWhiteSpace(Title.Variable) && string.IsNullOrWhiteSpace(Title.Type) && Title.ToString == null && Title.Value == null && Title.Literal == null)) throw new InvalidOperationException(Type + " action requires 'title'.");
        }
    }

    public sealed class GetDynamicValuePropertyActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public GetDynamicValuePropertyActionYaml() { Type = "getDynamicValueProperty"; }
        public string Source { get; set; } = string.Empty;
        public ExpressionYaml PropertyName { get; set; } = new ExpressionYaml();
        public string To { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            if (string.IsNullOrWhiteSpace(Source)) throw new InvalidOperationException(Type + " action requires 'source'.");
            RequireTo();
        }
    }

    public sealed class CountDynamicValueItemsActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public CountDynamicValueItemsActionYaml() { Type = "countDynamicValueItems"; }
        public string Source { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            if (string.IsNullOrWhiteSpace(Source)) throw new InvalidOperationException(Type + " action requires 'source'.");
            RequireTo();
        }
    }

    public sealed class SetDynamicValuePropertyActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public SetDynamicValuePropertyActionYaml() { Type = "setDynamicValueProperty"; }
        public string Source { get; set; } = string.Empty;
        public ExpressionYaml PropertyName { get; set; } = new ExpressionYaml();
        public ExpressionYaml Value { get; set; } = new ExpressionYaml();
        public string ValueType { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            if (string.IsNullOrWhiteSpace(Source)) throw new InvalidOperationException(Type + " action requires 'source'.");
        }
    }

    public sealed class BuildDynamicValueActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public BuildDynamicValueActionYaml() { Type = "buildDynamicValue"; }
        public List<DynamicValueEntryYaml> Entries { get; set; } = new List<DynamicValueEntryYaml>();
        public string To { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            RequireTo();
            if (Entries == null || Entries.Count == 0) throw new InvalidOperationException(Type + " action requires at least one entry.");
            if (Entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Key))) throw new InvalidOperationException(Type + " action entries require non-empty keys.");
        }
    }

    public sealed class DynamicValueEntryYaml
    {
        public string Key { get; set; } = string.Empty;
        public ExpressionYaml Value { get; set; } = new ExpressionYaml();
        public string ValueType { get; set; } = string.Empty;
    }

    public sealed class LookupRestPropertyNameActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public LookupRestPropertyNameActionYaml() { Type = "lookupRestPropertyName"; }
        public ExpressionYaml ListId { get; set; } = new ExpressionYaml();
        public ExpressionYaml PropertyName { get; set; } = new ExpressionYaml();
        public string To { get; set; } = string.Empty;

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

    /// <summary>
    /// Converts polymorphic workflow action YAML mappings into typed action models.
    /// </summary>
    public sealed class WorkflowActionYamlTypeConverter : IYamlTypeConverter
    {
        private delegate WorkflowActionYaml ActionFactory(ActionYamlSurrogate yamlObject);

        private static readonly IReadOnlyDictionary<string, ActionFactory> ActionFactories = CreateActionFactories();

        /// <inheritdoc />
        public bool Accepts(Type type) => typeof(WorkflowActionYaml).IsAssignableFrom(type);

        /// <inheritdoc />
        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var yamlObject = rootDeserializer(typeof(ActionYamlSurrogate)) as ActionYamlSurrogate ?? throw new InvalidOperationException("Action YAML is empty.");
            var actionType = (yamlObject.Type ?? string.Empty).ToLowerInvariant();
            if (!ActionFactories.TryGetValue(actionType, out var actionFactory)) throw new InvalidOperationException("Unsupported action type: " + yamlObject.Type);
            return actionFactory(yamlObject);
        }

        private static IReadOnlyDictionary<string, ActionFactory> CreateActionFactories()
        {
            var factories = new Dictionary<string, ActionFactory>(StringComparer.OrdinalIgnoreCase);
            Register(factories, y => new CalcActionYaml { Type = y.Type ?? string.Empty, LValue = y.LValue ?? new ExpressionYaml(), RValue = y.RValue ?? new ExpressionYaml(), Operator = y.Operator ?? "Add", To = ReadString(y.To) }, "calc");
            Register(factories, y => new WriteHistoryActionYaml { Type = y.Type ?? string.Empty, Message = y.Message ?? new ExpressionYaml() }, "writeHistory");
            Register(factories, y => new SetStatusActionYaml { Type = y.Type ?? string.Empty, Status = y.Status ?? string.Empty }, "setStatus");
            Register(factories, y => new CommentActionYaml { Type = y.Type ?? string.Empty, Text = y.Text ?? y.Message ?? new ExpressionYaml() }, "comment");
            Register(factories, y => new DelayForActionYaml { Type = y.Type ?? string.Empty, Days = y.Days ?? new ExpressionYaml { Literal = 0 }, Hours = y.Hours ?? new ExpressionYaml { Literal = 0 }, Minutes = y.Minutes ?? new ExpressionYaml { Literal = 0 } }, "delayFor");
            Register(factories, y => new DelayUntilActionYaml { Type = y.Type ?? string.Empty, Date = y.Date ?? new ExpressionYaml() }, "delayUntil");
            Register(factories, y => new AssignActionYaml { Type = y.Type ?? string.Empty, To = ReadString(y.To), Value = y.Value }, "assign", "setVariable");
            Register(factories, y => new StringReplaceActionYaml { Type = y.Type ?? string.Empty, Text = y.Text ?? y.Value ?? new ExpressionYaml(), OldValue = y.OldValue ?? y.Find ?? new ExpressionYaml(), NewValue = y.NewValue ?? y.ReplaceWith ?? new ExpressionYaml { Literal = string.Empty }, To = ReadString(y.To) }, "replaceString", "stringReplace");
            Register(factories, y => new StringSubstringActionYaml { Type = y.Type ?? string.Empty, Text = y.Text ?? y.Value ?? new ExpressionYaml(), StartIndex = y.StartIndex ?? new ExpressionYaml { Literal = 0 }, Length = y.Length ?? new ExpressionYaml(), To = ReadString(y.To) }, "substring", "substringString");
            Register(factories, y => new StringTrimActionYaml { Type = y.Type ?? string.Empty, Text = y.Text ?? y.Value ?? new ExpressionYaml(), To = ReadString(y.To) }, "trimString", "stringTrim");
            Register(factories, y => new LookupWorkflowContextActionYaml { Type = y.Type ?? string.Empty, PropertyName = Convert.ToString(y.PropertyName?.Literal) ?? string.Empty, To = ReadString(y.To) }, "lookupWorkflowContext", "lookupContextProperty");
            Register(factories, y => new GetCurrentListIdActionYaml { Type = y.Type ?? string.Empty, To = ReadString(y.To) }, "getCurrentListId");
            Register(factories, y => new GetCurrentItemGuidActionYaml { Type = y.Type ?? string.Empty, To = ReadString(y.To) }, "getCurrentItemGuid");
            Register(factories, y => new SetFieldActionYaml { Type = y.Type ?? string.Empty, FieldName = y.FieldName ?? string.Empty, Value = y.Value ?? new ExpressionYaml() }, "setField");
            Register(factories, y => new CreateListItemActionYaml { Type = y.Type ?? string.Empty, ListId = y.ListId ?? new ExpressionYaml { Type = "getCurrentListId" }, Fields = y.Fields ?? new Dictionary<string, ExpressionYaml>(StringComparer.OrdinalIgnoreCase), ItemIdTo = y.ItemIdTo ?? string.Empty, ItemGuidTo = y.ItemGuidTo ?? string.Empty }, "createListItem");
            Register(factories, y => new UpdateListItemActionYaml { Type = y.Type ?? string.Empty, ListId = y.ListId ?? new ExpressionYaml { Type = "getCurrentListId" }, ItemId = y.ItemId ?? new ExpressionYaml(), ItemGuid = y.ItemGuid ?? new ExpressionYaml(), Fields = y.Fields ?? new Dictionary<string, ExpressionYaml>(StringComparer.OrdinalIgnoreCase) }, "updateListItem");
            Register(factories, y => new DeleteListItemActionYaml { Type = y.Type ?? string.Empty, ListId = y.ListId ?? new ExpressionYaml { Type = "getCurrentListId" }, ItemId = y.ItemId ?? new ExpressionYaml(), ItemGuid = y.ItemGuid ?? new ExpressionYaml() }, "deleteListItem");
            Register(factories, y => new LookupListItemStringPropertyActionYaml { Type = y.Type ?? string.Empty, ListId = y.ListId ?? new ExpressionYaml { Type = "getCurrentListId" }, ItemId = y.ItemId ?? new ExpressionYaml(), ItemGuid = y.ItemGuid ?? new ExpressionYaml(), FieldName = y.FieldName ?? string.Empty, PropertyName = Convert.ToString(y.PropertyName?.Literal) ?? string.Empty, To = ReadString(y.To) }, "lookupListItemStringProperty", "lookupSPListItemStringProperty");
            Register(factories, y => new LookupListItemIntPropertyActionYaml { Type = y.Type ?? string.Empty, ListId = y.ListId ?? new ExpressionYaml { Type = "getCurrentListId" }, ItemId = y.ItemId ?? new ExpressionYaml(), ItemGuid = y.ItemGuid ?? new ExpressionYaml(), FieldName = y.FieldName ?? string.Empty, PropertyName = Convert.ToString(y.PropertyName?.Literal) ?? string.Empty, To = ReadString(y.To) }, "lookupListItemIntProperty", "lookupSPListItemIntProperty");
            Register(factories, y => new CallHttpWebServiceActionYaml { Type = y.Type ?? string.Empty, Address = y.Address ?? new ExpressionYaml(), RequestType = y.RequestType ?? new ExpressionYaml { Literal = "GET" }, ResponseStatusCodeTo = y.ResponseStatusCodeTo ?? y.StatusCodeTo ?? string.Empty, ResponseContentTo = y.ResponseContentTo ?? y.ContentTo ?? string.Empty, ResponseHeadersTo = y.ResponseHeadersTo ?? y.HeadersTo ?? string.Empty }, "callHttpWebService", "callHttp", "http");
            Register(factories, y => new SendEmailActionYaml { Type = y.Type ?? string.Empty, To = y.To ?? new ExpressionYaml(), Cc = y.Cc ?? new ExpressionYaml { Literal = string.Empty }, Subject = y.Subject ?? new ExpressionYaml { Literal = string.Empty }, Body = y.Body ?? y.BodyExpression ?? new ExpressionYaml { Literal = string.Empty } }, "sendEmail", "email");
            Register(factories, y => new SingleTaskActionYaml { Type = y.Type ?? string.Empty, AssignedTo = y.AssignedTo ?? new ExpressionYaml(), Title = y.Title ?? new ExpressionYaml(), Body = y.TaskBody ?? y.BodyExpression ?? y.Body ?? new ExpressionYaml { Literal = string.Empty }, DueDate = y.DueDate ?? new ExpressionYaml(), AssignmentEmailSubject = y.AssignmentEmailSubject ?? new ExpressionYaml { Literal = "Task Assigned - %Task: Title%" }, AssignmentEmailBody = y.AssignmentEmailBody ?? new ExpressionYaml(), WaitForTaskCompletion = y.WaitForTaskCompletion, WaiveAssignmentEmail = y.WaiveAssignmentEmail, WaiveCancelationEmail = y.WaiveCancelationEmail, ContentTypeId = y.ContentTypeId ?? string.Empty, OutcomeFieldName = y.OutcomeFieldName ?? string.Empty, CompletedStatus = y.CompletedStatus ?? string.Empty, TaskIdTo = y.TaskIdTo ?? string.Empty, OutcomeTo = y.OutcomeTo ?? string.Empty }, "singleTask", "task");
            Register(factories, y => new GetDynamicValuePropertyActionYaml { Type = y.Type ?? string.Empty, Source = y.Source ?? y.From ?? string.Empty, PropertyName = y.PropertyName ?? y.Key ?? new ExpressionYaml(), To = ReadString(y.To), ValueType = y.ValueType ?? string.Empty }, "getDynamicValueProperty", "getDictionaryItem", "getDictionaryValue", "getResponseProperty");
            Register(factories, y => new SetDynamicValuePropertyActionYaml { Type = y.Type ?? string.Empty, Source = y.Source ?? y.From ?? string.Empty, PropertyName = y.PropertyName ?? y.Key ?? new ExpressionYaml(), Value = y.Value ?? new ExpressionYaml(), ValueType = y.ValueType ?? string.Empty, To = ReadString(y.To) }, "setDynamicValueProperty", "setDictionaryItem", "setDictionaryValue", "setResponseProperty");
            Register(factories, y => new CountDynamicValueItemsActionYaml { Type = y.Type ?? string.Empty, Source = y.Source ?? y.From ?? string.Empty, To = ReadString(y.To) }, "countDynamicValueItems", "countDictionaryItems");
            Register(factories, y => new BuildDynamicValueActionYaml { Type = y.Type ?? string.Empty, Entries = y.Entries ?? new List<DynamicValueEntryYaml>(), To = ReadString(y.To) }, "buildDynamicValue", "buildDictionary", "createDictionary");
            Register(factories, y => new LookupRestPropertyNameActionYaml { Type = y.Type ?? string.Empty, ListId = y.ListId ?? new ExpressionYaml(), PropertyName = y.PropertyName ?? new ExpressionYaml(), To = ReadString(y.To) }, "lookupRestPropertyName", "lookupSPGetItemPropertyNameInREST", "lookupSPListItemPropertyNameInREST");
            Register(factories, y => new WhileActionYaml { Type = y.Type ?? string.Empty, Condition = y.Condition ?? new ComparisonExpressionYaml(), Actions = y.Actions ?? new List<WorkflowActionYaml>() }, "while", "loop");
            Register(factories, y => new IfActionYaml { Type = y.Type ?? string.Empty, Condition = y.Condition ?? new ComparisonExpressionYaml(), Then = y.Then ?? new List<WorkflowActionYaml>(), Else = y.Else ?? new List<WorkflowActionYaml>() }, "if");
            return factories;
        }

        private static void Register(IDictionary<string, ActionFactory> factories, ActionFactory factory, params string[] actionTypes)
        {
            foreach (var actionType in actionTypes) factories[actionType] = factory;
        }

        /// <inheritdoc />
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
            else if (value is StringReplaceActionYaml replaceString)
            {
                WriteScalar(emitter, "type", replaceString.Type); WriteObject(emitter, serializer, "text", replaceString.Text); WriteObject(emitter, serializer, "oldValue", replaceString.OldValue); WriteObject(emitter, serializer, "newValue", replaceString.NewValue); WriteScalar(emitter, "to", replaceString.To);
            }
            else if (value is StringSubstringActionYaml substring)
            {
                WriteScalar(emitter, "type", substring.Type); WriteObject(emitter, serializer, "text", substring.Text); WriteObject(emitter, serializer, "startIndex", substring.StartIndex); WriteObject(emitter, serializer, "length", substring.Length); WriteScalar(emitter, "to", substring.To);
            }
            else if (value is StringTrimActionYaml trimString)
            {
                WriteScalar(emitter, "type", trimString.Type); WriteObject(emitter, serializer, "text", trimString.Text); WriteScalar(emitter, "to", trimString.To);
            }
            else if (value is LookupWorkflowContextActionYaml context)
            {
                WriteScalar(emitter, "type", context.Type); WriteScalar(emitter, "propertyName", context.PropertyName); WriteScalar(emitter, "to", context.To);
            }
            else if (value is GetCurrentListIdActionYaml listId)
            {
                WriteScalar(emitter, "type", listId.Type); WriteScalar(emitter, "to", listId.To);
            }
            else if (value is GetCurrentItemGuidActionYaml itemGuid)
            {
                WriteScalar(emitter, "type", itemGuid.Type); WriteScalar(emitter, "to", itemGuid.To);
            }
            else if (value is SetFieldActionYaml setField)
            {
                WriteScalar(emitter, "type", setField.Type); WriteScalar(emitter, "fieldName", setField.FieldName); WriteObject(emitter, serializer, "value", setField.Value);
            }
            else if (value is CreateListItemActionYaml createListItem)
            {
                WriteScalar(emitter, "type", createListItem.Type); WriteObject(emitter, serializer, "listId", createListItem.ListId); WriteObject(emitter, serializer, "fields", createListItem.Fields); WriteScalar(emitter, "itemIdTo", createListItem.ItemIdTo); WriteScalar(emitter, "itemGuidTo", createListItem.ItemGuidTo);
            }
            else if (value is UpdateListItemActionYaml updateListItem)
            {
                WriteScalar(emitter, "type", updateListItem.Type); WriteObject(emitter, serializer, "listId", updateListItem.ListId); WriteObject(emitter, serializer, "itemId", updateListItem.ItemId); WriteObject(emitter, serializer, "itemGuid", updateListItem.ItemGuid); WriteObject(emitter, serializer, "fields", updateListItem.Fields);
            }
            else if (value is DeleteListItemActionYaml deleteListItem)
            {
                WriteScalar(emitter, "type", deleteListItem.Type); WriteObject(emitter, serializer, "listId", deleteListItem.ListId); WriteObject(emitter, serializer, "itemId", deleteListItem.ItemId); WriteObject(emitter, serializer, "itemGuid", deleteListItem.ItemGuid);
            }
            else if (value is LookupListItemPropertyActionYaml lookupListItemProperty)
            {
                WriteScalar(emitter, "type", lookupListItemProperty.Type); WriteObject(emitter, serializer, "listId", lookupListItemProperty.ListId); WriteObject(emitter, serializer, "itemId", lookupListItemProperty.ItemId); WriteObject(emitter, serializer, "itemGuid", lookupListItemProperty.ItemGuid); WriteScalar(emitter, "fieldName", lookupListItemProperty.FieldName); WriteScalar(emitter, "propertyName", lookupListItemProperty.PropertyName); WriteScalar(emitter, "to", lookupListItemProperty.To);
            }
            else if (value is CallHttpWebServiceActionYaml callHttp)
            {
                WriteScalar(emitter, "type", callHttp.Type); WriteObject(emitter, serializer, "address", callHttp.Address); WriteObject(emitter, serializer, "requestType", callHttp.RequestType); WriteScalar(emitter, "responseStatusCodeTo", callHttp.ResponseStatusCodeTo); WriteScalar(emitter, "responseContentTo", callHttp.ResponseContentTo); WriteScalar(emitter, "responseHeadersTo", callHttp.ResponseHeadersTo);
            }
            else if (value is SendEmailActionYaml email)
            {
                WriteScalar(emitter, "type", email.Type); WriteObject(emitter, serializer, "to", email.To); WriteObject(emitter, serializer, "cc", email.Cc); WriteObject(emitter, serializer, "subject", email.Subject); WriteObject(emitter, serializer, "body", email.Body);
            }
            else if (value is SingleTaskActionYaml singleTask)
            {
                WriteScalar(emitter, "type", singleTask.Type); WriteObject(emitter, serializer, "assignedTo", singleTask.AssignedTo); WriteObject(emitter, serializer, "title", singleTask.Title); WriteObject(emitter, serializer, "taskBody", singleTask.Body); WriteObject(emitter, serializer, "dueDate", singleTask.DueDate); WriteScalar(emitter, "waitForTaskCompletion", singleTask.WaitForTaskCompletion.ToString()); WriteScalar(emitter, "waiveAssignmentEmail", singleTask.WaiveAssignmentEmail.ToString()); WriteScalar(emitter, "waiveCancelationEmail", singleTask.WaiveCancelationEmail.ToString()); WriteScalar(emitter, "taskIdTo", singleTask.TaskIdTo); WriteScalar(emitter, "outcomeTo", singleTask.OutcomeTo);
            }
            else if (value is GetDynamicValuePropertyActionYaml dynamicProperty)
            {
                WriteScalar(emitter, "type", dynamicProperty.Type); WriteScalar(emitter, "source", dynamicProperty.Source); WriteObject(emitter, serializer, "propertyName", dynamicProperty.PropertyName); WriteScalar(emitter, "to", dynamicProperty.To); WriteScalar(emitter, "valueType", dynamicProperty.ValueType);
            }
            else if (value is CountDynamicValueItemsActionYaml countDynamicValueItems)
            {
                WriteScalar(emitter, "type", countDynamicValueItems.Type); WriteScalar(emitter, "source", countDynamicValueItems.Source); WriteScalar(emitter, "to", countDynamicValueItems.To);
            }
            else if (value is SetDynamicValuePropertyActionYaml setDynamicProperty)
            {
                WriteScalar(emitter, "type", setDynamicProperty.Type); WriteScalar(emitter, "source", setDynamicProperty.Source); WriteObject(emitter, serializer, "propertyName", setDynamicProperty.PropertyName); WriteObject(emitter, serializer, "value", setDynamicProperty.Value); WriteScalar(emitter, "valueType", setDynamicProperty.ValueType); WriteScalar(emitter, "to", setDynamicProperty.To);
            }
            else if (value is BuildDynamicValueActionYaml buildDynamicValue)
            {
                WriteScalar(emitter, "type", buildDynamicValue.Type); WriteObject(emitter, serializer, "entries", buildDynamicValue.Entries); WriteScalar(emitter, "to", buildDynamicValue.To);
            }
            else if (value is LookupRestPropertyNameActionYaml restProperty)
            {
                WriteScalar(emitter, "type", restProperty.Type); WriteObject(emitter, serializer, "listId", restProperty.ListId); WriteObject(emitter, serializer, "propertyName", restProperty.PropertyName); WriteScalar(emitter, "to", restProperty.To);
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

        private static string ReadString(ExpressionYaml? value) => Convert.ToString(value?.Literal ?? string.Empty) ?? string.Empty;

        private sealed class ActionYamlSurrogate
        {
            public string Type { get; set; } = string.Empty;
            public ExpressionYaml LValue { get; set; } = new ExpressionYaml();
            public ExpressionYaml RValue { get; set; } = new ExpressionYaml();
            public string Operator { get; set; } = "Add";
            public ExpressionYaml To { get; set; } = new ExpressionYaml();
            public ExpressionYaml Cc { get; set; } = new ExpressionYaml();
            public ExpressionYaml Subject { get; set; } = new ExpressionYaml();
            public ExpressionYaml Body { get; set; } = new ExpressionYaml();
            public ExpressionYaml BodyExpression { get; set; } = new ExpressionYaml();
            public ExpressionYaml TaskBody { get; set; } = new ExpressionYaml();
            public ExpressionYaml AssignedTo { get; set; } = new ExpressionYaml();
            public ExpressionYaml Title { get; set; } = new ExpressionYaml();
            public ExpressionYaml DueDate { get; set; } = new ExpressionYaml();
            public ExpressionYaml AssignmentEmailSubject { get; set; } = new ExpressionYaml();
            public ExpressionYaml AssignmentEmailBody { get; set; } = new ExpressionYaml();
            public bool WaitForTaskCompletion { get; set; } = true;
            public bool WaiveAssignmentEmail { get; set; } = true;
            public bool WaiveCancelationEmail { get; set; } = true;
            public string ContentTypeId { get; set; } = string.Empty;
            public string OutcomeFieldName { get; set; } = string.Empty;
            public string CompletedStatus { get; set; } = string.Empty;
            public string TaskIdTo { get; set; } = string.Empty;
            public string OutcomeTo { get; set; } = string.Empty;
            public ExpressionYaml PropertyName { get; set; } = new ExpressionYaml();
            public ExpressionYaml Key { get; set; } = new ExpressionYaml();
            public string ValueType { get; set; } = string.Empty;
            public List<DynamicValueEntryYaml> Entries { get; set; } = new List<DynamicValueEntryYaml>();
            public string FieldName { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
            public string From { get; set; } = string.Empty;
            public ExpressionYaml? Value { get; set; }
            public Dictionary<string, ExpressionYaml> Fields { get; set; } = new Dictionary<string, ExpressionYaml>(StringComparer.OrdinalIgnoreCase);
            public ExpressionYaml ItemId { get; set; } = new ExpressionYaml();
            public ExpressionYaml ItemGuid { get; set; } = new ExpressionYaml();
            public string ItemIdTo { get; set; } = string.Empty;
            public string ItemGuidTo { get; set; } = string.Empty;
            public ExpressionYaml Address { get; set; } = new ExpressionYaml();
            public ExpressionYaml RequestType { get; set; } = new ExpressionYaml { Literal = "HTTPGET" };
            public ExpressionYaml ListId { get; set; } = new ExpressionYaml();
            public string ResponseStatusCodeTo { get; set; } = string.Empty;
            public string ResponseContentTo { get; set; } = string.Empty;
            public string ResponseHeadersTo { get; set; } = string.Empty;
            public string StatusCodeTo { get; set; } = string.Empty;
            public string ContentTo { get; set; } = string.Empty;
            public string HeadersTo { get; set; } = string.Empty;
            public ExpressionYaml Message { get; set; } = new ExpressionYaml();
            public string Status { get; set; } = string.Empty;
            public ExpressionYaml Text { get; set; } = new ExpressionYaml();
            public ExpressionYaml OldValue { get; set; } = new ExpressionYaml();
            public ExpressionYaml NewValue { get; set; } = new ExpressionYaml();
            public ExpressionYaml Find { get; set; } = new ExpressionYaml();
            public ExpressionYaml ReplaceWith { get; set; } = new ExpressionYaml();
            public ExpressionYaml StartIndex { get; set; } = new ExpressionYaml();
            public ExpressionYaml Length { get; set; } = new ExpressionYaml();
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

    /// <summary>
    /// Represents a numeric comparison expression used by control-flow actions.
    /// </summary>
    public sealed class ComparisonExpressionYaml
    {
        /// <summary>Gets or sets the comparison expression type.</summary>
        public string Type { get; set; } = "isLessThan";
        /// <summary>Gets or sets an optional operator alias for the comparison.</summary>
        public string Operator { get; set; } = string.Empty;
        /// <summary>Gets or sets an optional value type hint for selecting typed SharePoint Designer comparison expressions.</summary>
        public string ValueType { get; set; } = string.Empty;
        /// <summary>Gets or sets the left comparison operand.</summary>
        public ExpressionYaml Left { get; set; } = new ExpressionYaml();
        /// <summary>Gets or sets the right comparison operand.</summary>
        public ExpressionYaml Right { get; set; } = new ExpressionYaml();
        /// <summary>Gets or sets the left nested boolean condition for logical operators.</summary>
        public ComparisonExpressionYaml? LeftCondition { get; set; }
        /// <summary>Gets or sets the right nested boolean condition for logical operators.</summary>
        public ComparisonExpressionYaml? RightCondition { get; set; }
        /// <summary>Gets or sets the nested boolean operand for negation.</summary>
        public ComparisonExpressionYaml? Operand { get; set; }

        /// <summary>
        /// Validates that the comparison specifies a supported operator name.
        /// </summary>
        /// <param name="owner">Action type that owns the condition, used in error messages.</param>
        public void Validate(string owner)
        {
            var comparison = string.IsNullOrWhiteSpace(Operator) ? Type : Operator;
            if (string.IsNullOrWhiteSpace(comparison)) throw new InvalidOperationException(owner + " action requires condition.type or condition.operator.");
        }
    }

    /// <summary>
    /// Represents a literal, variable reference, or supported nested expression value.
    /// </summary>
    public sealed class ExpressionYaml
    {
        /// <summary>Gets or sets a scalar literal value.</summary>
        public object? Literal { get; set; }
        /// <summary>Gets or sets a workflow variable reference.</summary>
        public string Variable { get; set; } = string.Empty;
        /// <summary>Gets or sets the nested expression type discriminator.</summary>
        public string Type { get; set; } = string.Empty;
        /// <summary>Gets or sets a SharePoint context or REST property name for lookup expressions.</summary>
        public string PropertyName { get; set; } = string.Empty;
        /// <summary>Gets or sets a SharePoint list field internal/display name for list item lookup expressions.</summary>
        public string FieldName { get; set; } = string.Empty;
        /// <summary>Gets or sets the list Guid expression for list item lookup expressions.</summary>
        public ExpressionYaml? ListId { get; set; }
        /// <summary>Gets or sets the list item integer ID expression for list item lookup expressions.</summary>
        public ExpressionYaml? ItemId { get; set; }
        /// <summary>Gets or sets the list item Guid expression for list item lookup expressions.</summary>
        public ExpressionYaml? ItemGuid { get; set; }
        /// <summary>Gets or sets a nested expression value used by expression wrappers such as formatting and string conversion.</summary>
        public ExpressionYaml? Value { get; set; }
        /// <summary>Gets or sets ordered nested expression values used by formatString.</summary>
        public List<ExpressionYaml> Values { get; set; } = new List<ExpressionYaml>();
        /// <summary>Gets or sets optional CLR/XAML type metadata used when an object-valued field expression must preserve a non-string type.</summary>
        public string ValueType { get; set; } = string.Empty;
        /// <summary>Gets or sets the optional SharePoint Designer custom attribute Id for expression activities.</summary>
        public string DesignerId { get; set; } = string.Empty;
        /// <summary>Gets or sets a nested expression to convert to string.</summary>
        public new ExpressionYaml? ToString { get; set; }
    }

    public sealed class ExpressionYamlTypeConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(ExpressionYaml);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            if (parser.Current is Scalar scalar)
            {
                parser.MoveNext();
                return new ExpressionYaml { Literal = scalar.Value ?? string.Empty };
            }

            return rootDeserializer(typeof(ExpressionYamlSurrogate)) is ExpressionYamlSurrogate s
                ? new ExpressionYaml { Literal = s.Literal, Variable = s.Variable ?? string.Empty, Type = s.Type ?? string.Empty, PropertyName = s.PropertyName ?? string.Empty, FieldName = s.FieldName ?? string.Empty, ListId = s.ListId, ItemId = s.ItemId, ItemGuid = s.ItemGuid, Value = s.Value, Values = s.Values ?? new List<ExpressionYaml>(), ValueType = s.ValueType ?? string.Empty, DesignerId = s.DesignerId ?? string.Empty, ToString = s.ToString }
                : new ExpressionYaml();
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        {
            var expression = value as ExpressionYaml ?? new ExpressionYaml();
            if (IsSimpleLiteral(expression))
            {
                serializer(expression.Literal);
                return;
            }

            emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));
            if (expression.Literal != null) WriteObject(emitter, serializer, "literal", expression.Literal);
            if (!string.IsNullOrWhiteSpace(expression.Variable)) WriteScalar(emitter, "variable", expression.Variable);
            if (!string.IsNullOrWhiteSpace(expression.Type)) WriteScalar(emitter, "type", expression.Type);
            if (!string.IsNullOrWhiteSpace(expression.PropertyName)) WriteScalar(emitter, "propertyName", expression.PropertyName);
            if (!string.IsNullOrWhiteSpace(expression.FieldName)) WriteScalar(emitter, "fieldName", expression.FieldName);
            if (expression.ListId != null) WriteObject(emitter, serializer, "listId", expression.ListId);
            if (expression.ItemId != null) WriteObject(emitter, serializer, "itemId", expression.ItemId);
            if (expression.ItemGuid != null) WriteObject(emitter, serializer, "itemGuid", expression.ItemGuid);
            if (expression.Value != null) WriteObject(emitter, serializer, "value", expression.Value);
            if (expression.Values != null && expression.Values.Count > 0) WriteObject(emitter, serializer, "values", expression.Values);
            if (!string.IsNullOrWhiteSpace(expression.ValueType)) WriteScalar(emitter, "valueType", expression.ValueType);
            if (!string.IsNullOrWhiteSpace(expression.DesignerId)) WriteScalar(emitter, "designerId", expression.DesignerId);
            if (expression.ToString != null) WriteObject(emitter, serializer, "toString", expression.ToString);
            emitter.Emit(new MappingEnd());
        }

        private static bool IsSimpleLiteral(ExpressionYaml expression) =>
            expression.Literal != null &&
            string.IsNullOrWhiteSpace(expression.Variable) &&
            string.IsNullOrWhiteSpace(expression.Type) &&
            string.IsNullOrWhiteSpace(expression.PropertyName) &&
            string.IsNullOrWhiteSpace(expression.FieldName) &&
            expression.ListId == null &&
            expression.ItemId == null &&
            expression.ItemGuid == null &&
            expression.Value == null &&
            (expression.Values == null || expression.Values.Count == 0) &&
            string.IsNullOrWhiteSpace(expression.ValueType) &&
            string.IsNullOrWhiteSpace(expression.DesignerId) &&
            expression.ToString == null;

        private static void WriteScalar(IEmitter emitter, string name, string value)
        {
            emitter.Emit(new Scalar(name));
            emitter.Emit(new Scalar(value ?? string.Empty));
        }

        private static void WriteObject(IEmitter emitter, ObjectSerializer serializer, string name, object? value)
        {
            emitter.Emit(new Scalar(name));
            serializer(value);
        }

        private sealed class ExpressionYamlSurrogate
        {
            public object? Literal { get; set; }
            public string Variable { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string PropertyName { get; set; } = string.Empty;
            public string FieldName { get; set; } = string.Empty;
            public ExpressionYaml? ListId { get; set; }
            public ExpressionYaml? ItemId { get; set; }
            public ExpressionYaml? ItemGuid { get; set; }
            public ExpressionYaml? Value { get; set; }
            public List<ExpressionYaml> Values { get; set; } = new List<ExpressionYaml>();
            public string ValueType { get; set; } = string.Empty;
            public string DesignerId { get; set; } = string.Empty;
            public new ExpressionYaml? ToString { get; set; }
        }
    }
}
