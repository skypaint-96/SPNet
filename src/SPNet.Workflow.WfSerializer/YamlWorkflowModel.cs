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
        /// <summary>Gets or sets workflow start options used by publishing tooling.</summary>
        public StartFlagsYaml Start { get; set; } = new StartFlagsYaml();
        /// <summary>Gets or sets workflow target metadata used by publishing tooling.</summary>
        public TargetYaml Target { get; set; } = new TargetYaml();
        /// <summary>Gets or sets declared workflow variables.</summary>
        public List<VariableYaml> Variables { get; set; } = new List<VariableYaml>();
        /// <summary>Gets or sets workflow stages and their actions.</summary>
        public List<StageYaml> Stages { get; set; } = new List<StageYaml>();
        /// <summary>Gets or sets warnings produced by partial XAML export.</summary>
        public List<string> ExportWarnings { get; set; } = new List<string>();

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
            var serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).WithTypeConverter(new WorkflowActionYamlTypeConverter()).ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).Build();
            File.WriteAllText(path, serializer.Serialize(this));
        }

        private static IDeserializer CreateDeserializer() => new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new WorkflowActionYamlTypeConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        /// <summary>
        /// Validates schema version, required workflow fields, and contained action models.
        /// </summary>
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

    public sealed class GetDynamicValuePropertyActionYaml : WorkflowActionYaml, ITargetedActionYaml
    {
        public GetDynamicValuePropertyActionYaml() { Type = "getDynamicValueProperty"; }
        public string Source { get; set; } = string.Empty;
        public ExpressionYaml PropertyName { get; set; } = new ExpressionYaml();
        public string To { get; set; } = string.Empty;

        public override void Validate()
        {
            base.Validate();
            if (string.IsNullOrWhiteSpace(Source)) throw new InvalidOperationException(Type + " action requires 'source'.");
            RequireTo();
        }
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
        /// <inheritdoc />
        public bool Accepts(Type type) => typeof(WorkflowActionYaml).IsAssignableFrom(type);

        /// <inheritdoc />
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
            else if (actionType == "lookupworkflowcontext" || actionType == "lookupcontextproperty") action = new LookupWorkflowContextActionYaml { Type = yamlObject.Type ?? string.Empty, PropertyName = Convert.ToString(yamlObject.PropertyName?.Literal) ?? string.Empty, To = yamlObject.To ?? string.Empty };
            else if (actionType == "getcurrentlistid") action = new GetCurrentListIdActionYaml { Type = yamlObject.Type ?? string.Empty, To = yamlObject.To ?? string.Empty };
            else if (actionType == "getcurrentitemguid") action = new GetCurrentItemGuidActionYaml { Type = yamlObject.Type ?? string.Empty, To = yamlObject.To ?? string.Empty };
            else if (actionType == "setfield") action = new SetFieldActionYaml { Type = yamlObject.Type ?? string.Empty, FieldName = yamlObject.FieldName ?? string.Empty, Value = yamlObject.Value ?? new ExpressionYaml() };
            else if (actionType == "callhttpwebservice" || actionType == "callhttp" || actionType == "http") action = new CallHttpWebServiceActionYaml { Type = yamlObject.Type ?? string.Empty, Address = yamlObject.Address ?? new ExpressionYaml(), RequestType = yamlObject.RequestType ?? new ExpressionYaml { Literal = "GET" }, ResponseStatusCodeTo = yamlObject.ResponseStatusCodeTo ?? yamlObject.StatusCodeTo ?? string.Empty, ResponseContentTo = yamlObject.ResponseContentTo ?? yamlObject.ContentTo ?? string.Empty, ResponseHeadersTo = yamlObject.ResponseHeadersTo ?? yamlObject.HeadersTo ?? string.Empty };
            else if (actionType == "getdynamicvalueproperty" || actionType == "getdictionaryitem" || actionType == "getdictionaryvalue" || actionType == "getresponseproperty") action = new GetDynamicValuePropertyActionYaml { Type = yamlObject.Type ?? string.Empty, Source = yamlObject.Source ?? yamlObject.From ?? string.Empty, PropertyName = yamlObject.PropertyName ?? yamlObject.Key ?? new ExpressionYaml(), To = yamlObject.To ?? string.Empty };
            else if (actionType == "lookuprestpropertyname" || actionType == "lookupspgetitempropertynameinrest") action = new LookupRestPropertyNameActionYaml { Type = yamlObject.Type ?? string.Empty, ListId = yamlObject.ListId ?? new ExpressionYaml(), PropertyName = yamlObject.PropertyName ?? new ExpressionYaml(), To = yamlObject.To ?? string.Empty };
            else if (actionType == "while" || actionType == "loop") action = new WhileActionYaml { Type = yamlObject.Type ?? string.Empty, Condition = yamlObject.Condition ?? new ComparisonExpressionYaml(), Actions = yamlObject.Actions ?? new List<WorkflowActionYaml>() };
            else if (actionType == "if") action = new IfActionYaml { Type = yamlObject.Type ?? string.Empty, Condition = yamlObject.Condition ?? new ComparisonExpressionYaml(), Then = yamlObject.Then ?? new List<WorkflowActionYaml>(), Else = yamlObject.Else ?? new List<WorkflowActionYaml>() };
            else throw new InvalidOperationException("Unsupported action type: " + yamlObject.Type);
            return action;
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
            else if (value is CallHttpWebServiceActionYaml callHttp)
            {
                WriteScalar(emitter, "type", callHttp.Type); WriteObject(emitter, serializer, "address", callHttp.Address); WriteObject(emitter, serializer, "requestType", callHttp.RequestType); WriteScalar(emitter, "responseStatusCodeTo", callHttp.ResponseStatusCodeTo); WriteScalar(emitter, "responseContentTo", callHttp.ResponseContentTo); WriteScalar(emitter, "responseHeadersTo", callHttp.ResponseHeadersTo);
            }
            else if (value is GetDynamicValuePropertyActionYaml dynamicProperty)
            {
                WriteScalar(emitter, "type", dynamicProperty.Type); WriteScalar(emitter, "source", dynamicProperty.Source); WriteObject(emitter, serializer, "propertyName", dynamicProperty.PropertyName); WriteScalar(emitter, "to", dynamicProperty.To);
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

        private sealed class ActionYamlSurrogate
        {
            public string Type { get; set; } = string.Empty;
            public ExpressionYaml LValue { get; set; } = new ExpressionYaml();
            public ExpressionYaml RValue { get; set; } = new ExpressionYaml();
            public string Operator { get; set; } = "Add";
            public string To { get; set; } = string.Empty;
            public ExpressionYaml PropertyName { get; set; } = new ExpressionYaml();
            public ExpressionYaml Key { get; set; } = new ExpressionYaml();
            public string FieldName { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
            public string From { get; set; } = string.Empty;
            public ExpressionYaml? Value { get; set; }
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
        /// <summary>Gets or sets the left comparison operand.</summary>
        public ExpressionYaml Left { get; set; } = new ExpressionYaml();
        /// <summary>Gets or sets the right comparison operand.</summary>
        public ExpressionYaml Right { get; set; } = new ExpressionYaml();

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
        /// <summary>Gets or sets a nested expression value used by expression wrappers such as formatting and string conversion.</summary>
        public ExpressionYaml? Value { get; set; }
        /// <summary>Gets or sets a nested expression to convert to string.</summary>
        public new ExpressionYaml? ToString { get; set; }
    }
}
