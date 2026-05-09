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
        public static void ExportWorkflowYaml(string inputXamlPath, string outputYamlPath)
        {
            WorkflowYamlExporter.Export(inputXamlPath, outputYamlPath);
        }
    }

    internal static class WorkflowYamlExporter
    {
        private const string SpdTechnicalClassSuffix = ".MTW";
        private static readonly XNamespace ActivitiesNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly XNamespace SharePointNamespace = "clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities";

        public static void Export(string inputXamlPath, string outputYamlPath)
        {
            if (!File.Exists(inputXamlPath)) throw new FileNotFoundException("Input XAML not found: " + inputXamlPath, inputXamlPath);
            var document = XDocument.Parse(File.ReadAllText(inputXamlPath));
            var root = document.Root ?? throw new InvalidOperationException("Workflow XAML has no root element.");
            var className = (string?)root.Attribute(XamlNamespace + "Class") ?? "ExportedWorkflow.MTW";
            var name = className.EndsWith(SpdTechnicalClassSuffix, StringComparison.OrdinalIgnoreCase) ? className.Substring(0, className.Length - SpdTechnicalClassSuffix.Length) : className;
            var workflow = new WorkflowYaml { Name = name, TechnicalName = className };
            foreach (var property in root.Element(XamlNamespace + "Members")?.Elements(XamlNamespace + "Property") ?? Enumerable.Empty<XElement>())
            {
                workflow.Variables.Add(new VariableYaml { Name = (string?)property.Attribute("Name") ?? "variable", Type = ((string?)property.Attribute("Type") ?? string.Empty).Contains("Double") ? "Double" : "String" });
            }
            var stageElements = document.Descendants(ActivitiesNamespace + "Sequence").Where(e => !string.IsNullOrWhiteSpace((string?)e.Attribute("DisplayName"))).ToList();
            foreach (var stageElement in stageElements.Take(20))
            {
                var stage = new StageYaml { Name = (string?)stageElement.Attribute("DisplayName") ?? "Stage" };
                foreach (var child in stageElement.Elements().Where(e => e.Name.Namespace == SharePointNamespace))
                {
                    var action = TryExportSharePointAction(child);
                    if (action != null) stage.Actions.Add(action);
                }
                if (stage.Actions.Count > 0) workflow.Stages.Add(stage);
            }
            workflow.ExportWarnings.Add("Partial structural export: supported SharePoint actions are listed, but expressions and list dictionaries may be placeholders when WF deserialization is not used.");
            if (workflow.Stages.Count == 0) workflow.Stages.Add(new StageYaml { Name = "Unsupported XAML", Actions = new System.Collections.Generic.List<WorkflowActionYaml>() });
            workflow.Save(outputYamlPath);
        }

        private static WorkflowActionYaml? TryExportSharePointAction(XElement child)
        {
            if (child.Name.LocalName == "Calc") return new CalcActionYaml { To = ReadCalcTarget(child), Operator = "Add", LValue = new ExpressionYaml { Literal = "<exported>" }, RValue = new ExpressionYaml { Literal = "<exported>" } };
            if (child.Name.LocalName == "WriteToHistory") return new WriteHistoryActionYaml { Message = new ExpressionYaml { Literal = ReadStringAttributeOrPlaceholder(child, "Message") } };
            if (child.Name.LocalName == "SetWorkflowStatus") return new SetStatusActionYaml { Status = (string?)child.Attribute("Status") ?? "<exported>" };
            if (child.Name.LocalName == "Comment") return new CommentActionYaml { Text = new ExpressionYaml { Literal = ReadStringAttributeOrPlaceholder(child, "CommentText") } };
            if (child.Name.LocalName == "DelayFor") return new DelayForActionYaml { Days = ReadExpressionAttributeOrPlaceholder(child, "Days"), Hours = ReadExpressionAttributeOrPlaceholder(child, "Hours"), Minutes = ReadExpressionAttributeOrPlaceholder(child, "Minutes") };
            if (child.Name.LocalName == "DelayUntil") return new DelayUntilActionYaml { Date = ReadExpressionAttributeOrPlaceholder(child, "Date") };
            if (child.Name.LocalName == "SetField") return new SetFieldActionYaml { FieldName = ReadStringAttributeOrPlaceholder(child, "FieldName"), Value = ReadExpressionAttributeOrPlaceholder(child, "FieldValue") };
            if (child.Name.LocalName == "Email") return new SendEmailActionYaml { To = new ExpressionYaml { Literal = "<exported recipients>" }, Cc = new ExpressionYaml { Literal = "<exported recipients>" }, Subject = ReadExpressionAttributeOrPlaceholder(child, "Subject"), Body = ReadExpressionAttributeOrPlaceholder(child, "Body") };
            if (child.Name.LocalName == "CallHTTPWebService") return new CallHttpWebServiceActionYaml { Address = ReadExpressionAttributeOrPlaceholder(child, "Address"), RequestType = ReadExpressionAttributeOrPlaceholder(child, "RequestType"), ResponseStatusCodeTo = ReadOutArgumentTargetOrPlaceholder(child, "ResponseStatusCode") };
            if (child.Name.LocalName == "CreateListItem") return new CreateListItemActionYaml { ListId = new ExpressionYaml { Literal = "<exported list id>" }, Fields = ReadListItemPropertyKeys(child), ItemIdTo = ReadOutArgumentTarget(child, "ItemId"), ItemGuidTo = ReadOutArgumentTarget(child, "ItemGuid") };
            if (child.Name.LocalName == "UpdateListItem") return new UpdateListItemActionYaml { ListId = new ExpressionYaml { Literal = "<exported list id>" }, ItemId = new ExpressionYaml { Literal = "<exported item id>" }, ItemGuid = new ExpressionYaml(), Fields = ReadListItemPropertyKeys(child) };
            if (child.Name.LocalName == "DeleteListItem") return new DeleteListItemActionYaml { ListId = new ExpressionYaml { Literal = "<exported list id>" }, ItemId = new ExpressionYaml { Literal = "<exported item id>" } };
            return null;
        }

        private static ExpressionYaml ReadExpressionAttributeOrPlaceholder(XElement element, string attributeName) => new ExpressionYaml { Literal = ReadStringAttributeOrPlaceholder(element, attributeName) };

        private static string ReadStringAttributeOrPlaceholder(XElement element, string attributeName) => (string?)element.Attribute(attributeName) ?? "<exported expression>";

        private static string ReadOutArgumentTargetOrPlaceholder(XElement element, string propertyName) => ReadOutArgumentTarget(element, propertyName) ?? "<exported>";

        private static string ReadOutArgumentTarget(XElement element, string propertyName)
        {
            var propertyElement = element.Element(element.Name.Namespace + (element.Name.LocalName + "." + propertyName));
            return propertyElement?.Descendants().FirstOrDefault(e => e.Name.LocalName == "ArgumentReference")?.Attribute("ArgumentName")?.Value ?? string.Empty;
        }

        private static Dictionary<string, ExpressionYaml> ReadListItemPropertyKeys(XElement element)
        {
            var fields = new Dictionary<string, ExpressionYaml>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in element.Descendants().Where(e => e.Name.LocalName == "InArgument"))
            {
                var key = ((string?)value.Attribute(XamlNamespace + "Key")) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(key) && !fields.ContainsKey(key)) fields[key] = new ExpressionYaml { Literal = "<exported expression>" };
            }
            if (fields.Count == 0) fields["<exported field>"] = new ExpressionYaml { Literal = "<exported expression>" };
            return fields;
        }

        private static string ReadCalcTarget(XElement calc)
        {
            var text = calc.ToString(SaveOptions.DisableFormatting);
            var marker = "ArgumentReference";
            return text.Contains(marker) ? "calc" : "<exported>";
        }
    }
}
