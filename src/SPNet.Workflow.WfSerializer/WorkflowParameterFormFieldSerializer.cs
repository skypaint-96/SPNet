using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace SPNet.Workflow.WfSerializer
{
    public static class WorkflowParameterFormFieldSerializer
    {
        public static string Serialize(IEnumerable<ParameterYaml> parameters) => CreateDocument(parameters).ToString(SaveOptions.DisableFormatting);

        public static XDocument CreateDocument(IEnumerable<ParameterYaml> parameters)
        {
            var fields = new XElement("Fields");
            foreach (var parameter in parameters ?? Enumerable.Empty<ParameterYaml>()) fields.Add(CreateField(parameter));
            return new XDocument(fields);
        }

        public static List<ParameterYaml> Parse(string formFieldXml)
        {
            if (string.IsNullOrWhiteSpace(formFieldXml)) return new List<ParameterYaml>();
            var document = XDocument.Parse(formFieldXml);
            return document.Root?.Elements("Field").Select(ParseField).ToList() ?? new List<ParameterYaml>();
        }

        private static XElement CreateField(ParameterYaml parameter)
        {
            var field = new XElement("Field");
            AddAttribute(field, "Name", parameter.Name);
            AddAttribute(field, "FormType", string.IsNullOrWhiteSpace(parameter.FormType) ? "Initiation" : parameter.FormType);
            AddAttribute(field, "MaxLength", parameter.MaxLength);
            AddAttribute(field, "Format", parameter.Format);
            AddAttribute(field, "Type", parameter.Type);
            AddAttribute(field, "BaseType", parameter.BaseType);
            AddAttribute(field, "List", parameter.List);
            AddAttribute(field, "ShowField", parameter.ShowField);
            AddAttribute(field, "Mult", parameter.Mult);
            AddAttribute(field, "NumLines", parameter.NumLines);
            AddAttribute(field, "Sortable", parameter.Sortable);
            AddAttribute(field, "RichTextMode", parameter.RichTextMode);
            AddAttribute(field, "UserSelectionMode", parameter.UserSelectionMode);
            AddAttribute(field, "UserSelectionScope", parameter.UserSelectionScope);
            AddAttribute(field, "DisplayName", string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Name : parameter.DisplayName);
            AddAttribute(field, "Description", parameter.Description ?? string.Empty, allowEmpty: true);
            AddAttribute(field, "Direction", string.IsNullOrWhiteSpace(parameter.Direction) ? "None" : parameter.Direction);
            foreach (var pair in parameter.Attributes ?? new Dictionary<string, string>()) if (field.Attribute(pair.Key) == null) AddAttribute(field, pair.Key, pair.Value, allowEmpty: true);
            if (parameter.Default != null) field.Add(new XElement("Default", FormatDefault(parameter)));
            if (parameter.Choices != null && parameter.Choices.Count > 0)
            {
                field.Add(new XElement("CHOICES", parameter.Choices.Select(c => new XElement("CHOICE", string.IsNullOrEmpty(c.DisplayName) ? null : new XAttribute("DisplayName", c.DisplayName), c.Value ?? string.Empty))));
            }

            return field;
        }

        private static ParameterYaml ParseField(XElement field)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Name", "FormType", "Type", "DisplayName", "Description", "Direction", "Format", "BaseType", "MaxLength", "NumLines", "Sortable", "RichTextMode", "List", "ShowField", "Mult", "UserSelectionMode", "UserSelectionScope" };
            var parameter = new ParameterYaml
            {
                Name = ReadAttribute(field, "Name"),
                FormType = ReadAttribute(field, "FormType", "Initiation"),
                Type = ReadAttribute(field, "Type", "Text"),
                DisplayName = ReadAttribute(field, "DisplayName"),
                Description = ReadAttribute(field, "Description"),
                Direction = ReadAttribute(field, "Direction", "None"),
                Format = ReadAttribute(field, "Format"),
                BaseType = ReadAttribute(field, "BaseType"),
                MaxLength = ReadAttribute(field, "MaxLength"),
                NumLines = ReadAttribute(field, "NumLines"),
                Sortable = ReadAttribute(field, "Sortable"),
                RichTextMode = ReadAttribute(field, "RichTextMode"),
                List = ReadAttribute(field, "List"),
                ShowField = ReadAttribute(field, "ShowField"),
                Mult = ReadAttribute(field, "Mult"),
                UserSelectionMode = ReadAttribute(field, "UserSelectionMode"),
                UserSelectionScope = ReadAttribute(field, "UserSelectionScope"),
                Default = field.Element("Default")?.Value,
                Choices = field.Element("CHOICES")?.Elements("CHOICE").Select(c => new ChoiceYaml { Value = c.Value, DisplayName = ReadAttribute(c, "DisplayName") }).ToList() ?? new List<ChoiceYaml>()
            };
            parameter.XamlType = WorkflowTypeMapper.MapParameterType(parameter).Name;
            foreach (var attribute in field.Attributes()) if (!known.Contains(attribute.Name.LocalName)) parameter.Attributes[attribute.Name.LocalName] = attribute.Value;
            return parameter;
        }

        private static void AddAttribute(XElement element, string name, string value, bool allowEmpty = false)
        {
            if (allowEmpty || !string.IsNullOrWhiteSpace(value)) element.SetAttributeValue(name, value ?? string.Empty);
        }

        private static string ReadAttribute(XElement element, string name, string fallback = "") => (string?)element.Attribute(name) ?? fallback;

        private static string FormatDefault(ParameterYaml parameter)
        {
            if (parameter.Default == null) return string.Empty;
            var parameterType = WorkflowTypeMapper.MapParameterType(parameter);
            if (parameterType == typeof(bool)) return Convert.ToBoolean(parameter.Default, CultureInfo.InvariantCulture) ? "1" : "0";
            if (parameterType == typeof(double)) return Convert.ToDouble(parameter.Default, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            if (parameterType == typeof(DateTime)) return Convert.ToDateTime(parameter.Default, CultureInfo.InvariantCulture).ToString("s", CultureInfo.InvariantCulture);
            return Convert.ToString(parameter.Default, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
