using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace SPNet.Workflow.WfSerializer
{
    public sealed class WorkflowDefinitionMetadataYaml
    {
        public string DisplayName { get; set; } = string.Empty;
        public string TechnicalName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TargetYaml Target { get; set; } = new TargetYaml();
        public WorkflowStartOptionsYaml Start { get; set; } = new WorkflowStartOptionsYaml();
        public WorkflowInitiationMetadataYaml Initiation { get; set; } = new WorkflowInitiationMetadataYaml();

        public string ToJson() => WorkflowDefinitionMetadataJson.Serialize(this);

        public static WorkflowDefinitionMetadataYaml FromJson(string json) => WorkflowDefinitionMetadataJson.Deserialize(json);
    }

    public sealed class WorkflowStartOptionsYaml
    {
        public bool? Manual { get; set; }
        public bool? OnCreated { get; set; }
        public bool? OnUpdated { get; set; }
    }

    public sealed class WorkflowInitiationMetadataYaml
    {
        public bool? RequiresForm { get; set; }
        public string Url { get; set; } = string.Empty;
        public List<ParameterYaml> FormFields { get; set; } = new List<ParameterYaml>();
    }

    public static class WorkflowDefinitionMetadataJson
    {
        public static string Serialize(WorkflowDefinitionMetadataYaml metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(ToDictionary(metadata));
        }

        public static WorkflowDefinitionMetadataYaml Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new WorkflowDefinitionMetadataYaml();
            var serializer = new JavaScriptSerializer();
            var map = serializer.DeserializeObject(json) as Dictionary<string, object> ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            return FromDictionary(map);
        }

        private static Dictionary<string, object> ToDictionary(WorkflowDefinitionMetadataYaml metadata)
        {
            var value = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(metadata.DisplayName)) value["displayName"] = metadata.DisplayName;
            if (!string.IsNullOrWhiteSpace(metadata.TechnicalName)) value["technicalName"] = metadata.TechnicalName;
            if (!string.IsNullOrWhiteSpace(metadata.Description)) value["description"] = metadata.Description;
            if (metadata.Target != null) value["target"] = new Dictionary<string, object> { ["type"] = metadata.Target.Type, ["listTitle"] = metadata.Target.ListTitle };
            if (metadata.Start != null) value["start"] = new Dictionary<string, object> { ["manual"] = metadata.Start.Manual, ["onCreated"] = metadata.Start.OnCreated, ["onUpdated"] = metadata.Start.OnUpdated };
            if (metadata.Initiation != null)
            {
                var initiation = new Dictionary<string, object> { ["requiresForm"] = metadata.Initiation.RequiresForm, ["url"] = metadata.Initiation.Url };
                initiation["formFields"] = (metadata.Initiation.FormFields ?? new List<ParameterYaml>()).Select(ToParameterDictionary).ToList();
                value["initiation"] = initiation;
            }

            return value;
        }

        private static Dictionary<string, object> ToParameterDictionary(ParameterYaml parameter)
        {
            var value = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(parameter.Name)) value["name"] = parameter.Name;
            if (!string.IsNullOrWhiteSpace(parameter.FormType)) value["formType"] = parameter.FormType;
            if (!string.IsNullOrWhiteSpace(parameter.Type)) value["type"] = parameter.Type;
            if (!string.IsNullOrWhiteSpace(parameter.XamlType)) value["xamlType"] = parameter.XamlType;
            if (!string.IsNullOrWhiteSpace(parameter.DisplayName)) value["displayName"] = parameter.DisplayName;
            if (!string.IsNullOrWhiteSpace(parameter.Description)) value["description"] = parameter.Description;
            if (!string.IsNullOrWhiteSpace(parameter.Direction)) value["direction"] = parameter.Direction;
            if (parameter.Default != null) value["default"] = parameter.Default;
            if (parameter.Choices != null && parameter.Choices.Count > 0) value["choices"] = parameter.Choices.Select(choice => new Dictionary<string, object> { ["value"] = choice.Value, ["displayName"] = choice.DisplayName }).ToList();
            if (!string.IsNullOrWhiteSpace(parameter.Format)) value["format"] = parameter.Format;
            if (!string.IsNullOrWhiteSpace(parameter.BaseType)) value["baseType"] = parameter.BaseType;
            if (!string.IsNullOrWhiteSpace(parameter.MaxLength)) value["maxLength"] = parameter.MaxLength;
            if (!string.IsNullOrWhiteSpace(parameter.NumLines)) value["numLines"] = parameter.NumLines;
            if (!string.IsNullOrWhiteSpace(parameter.Sortable)) value["sortable"] = parameter.Sortable;
            if (!string.IsNullOrWhiteSpace(parameter.RichTextMode)) value["richTextMode"] = parameter.RichTextMode;
            if (!string.IsNullOrWhiteSpace(parameter.List)) value["list"] = parameter.List;
            if (!string.IsNullOrWhiteSpace(parameter.ShowField)) value["showField"] = parameter.ShowField;
            if (!string.IsNullOrWhiteSpace(parameter.Mult)) value["mult"] = parameter.Mult;
            if (!string.IsNullOrWhiteSpace(parameter.UserSelectionMode)) value["userSelectionMode"] = parameter.UserSelectionMode;
            if (!string.IsNullOrWhiteSpace(parameter.UserSelectionScope)) value["userSelectionScope"] = parameter.UserSelectionScope;
            return value;
        }

        private static WorkflowDefinitionMetadataYaml FromDictionary(Dictionary<string, object> raw)
        {
            var map = new Dictionary<string, object>(raw, StringComparer.OrdinalIgnoreCase);
            var metadata = new WorkflowDefinitionMetadataYaml
            {
                DisplayName = ReadString(map, "displayName"),
                TechnicalName = ReadString(map, "technicalName"),
                Description = ReadString(map, "description")
            };
            if (ReadMap(map, "target") is Dictionary<string, object> target) metadata.Target = new TargetYaml { Type = ReadString(target, "type", "Site"), ListTitle = ReadString(target, "listTitle") };
            if (ReadMap(map, "start") is Dictionary<string, object> start) metadata.Start = new WorkflowStartOptionsYaml { Manual = ReadBool(start, "manual"), OnCreated = ReadBool(start, "onCreated"), OnUpdated = ReadBool(start, "onUpdated") };
            if (ReadMap(map, "initiation") is Dictionary<string, object> initiation)
            {
                metadata.Initiation = new WorkflowInitiationMetadataYaml { RequiresForm = ReadBool(initiation, "requiresForm"), Url = ReadString(initiation, "url") };
                metadata.Initiation.FormFields = ReadParameters(initiation, "formFields");
            }

            return metadata;
        }

        private static Dictionary<string, object>? ReadMap(Dictionary<string, object> map, string key) => map.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;
        private static string ReadString(Dictionary<string, object> map, string key, string fallback = "") => map.TryGetValue(key, out var value) && value != null ? Convert.ToString(value) ?? fallback : fallback;
        private static bool? ReadBool(Dictionary<string, object> map, string key) => map.TryGetValue(key, out var value) && value != null ? Convert.ToBoolean(value) : (bool?)null;

        private static List<ParameterYaml> ReadParameters(Dictionary<string, object> map, string key)
        {
            var parameters = new List<ParameterYaml>();
            if (!map.TryGetValue(key, out var value) || !(value is object[] array)) return parameters;
            foreach (var item in array)
            {
                if (!(item is Dictionary<string, object> raw)) continue;
                raw = new Dictionary<string, object>(raw, StringComparer.OrdinalIgnoreCase);
                var parameter = new ParameterYaml
                {
                    Name = ReadString(raw, "name"),
                    FormType = ReadString(raw, "formType", "Initiation"),
                    Type = ReadString(raw, "type", "Text"),
                    XamlType = ReadString(raw, "xamlType"),
                    DisplayName = ReadString(raw, "displayName"),
                    Description = ReadString(raw, "description"),
                    Direction = ReadString(raw, "direction", "None"),
                    Default = raw.TryGetValue("default", out var defaultValue) ? defaultValue : null,
                    Format = ReadString(raw, "format"),
                    BaseType = ReadString(raw, "baseType"),
                    MaxLength = ReadString(raw, "maxLength"),
                    NumLines = ReadString(raw, "numLines"),
                    Sortable = ReadString(raw, "sortable"),
                    RichTextMode = ReadString(raw, "richTextMode"),
                    List = ReadString(raw, "list"),
                    ShowField = ReadString(raw, "showField"),
                    Mult = ReadString(raw, "mult"),
                    UserSelectionMode = ReadString(raw, "userSelectionMode"),
                    UserSelectionScope = ReadString(raw, "userSelectionScope"),
                    Choices = ReadChoices(raw, "choices")
                };
                parameters.Add(parameter);
            }

            return parameters;
        }

        private static List<ChoiceYaml> ReadChoices(Dictionary<string, object> map, string key)
        {
            var choices = new List<ChoiceYaml>();
            if (!map.TryGetValue(key, out var value) || !(value is object[] array)) return choices;
            foreach (var item in array)
            {
                if (item is string text) choices.Add(new ChoiceYaml { Value = text, DisplayName = text });
                else if (item is Dictionary<string, object> raw)
                {
                    raw = new Dictionary<string, object>(raw, StringComparer.OrdinalIgnoreCase);
                    choices.Add(new ChoiceYaml { Value = ReadString(raw, "value"), DisplayName = ReadString(raw, "displayName") });
                }
            }
            return choices;
        }
    }
}
