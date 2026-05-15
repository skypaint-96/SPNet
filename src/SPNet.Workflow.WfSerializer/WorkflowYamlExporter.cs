using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualBasic.Activities;

namespace SPNet.Workflow.WfSerializer
{
    public static partial class WfActivityBuilderSerializer
    {
        public static void ExportWorkflowYaml(string inputXamlPath, string outputYamlPath)
        {
            WorkflowYamlExporter.Export(inputXamlPath, outputYamlPath, string.Empty, string.Empty);
        }

        public static void ExportWorkflowYaml(string inputXamlPath, string outputYamlPath, string formFieldXmlPath)
        {
            WorkflowYamlExporter.Export(inputXamlPath, outputYamlPath, formFieldXmlPath, string.Empty);
        }

        public static void ExportWorkflowYaml(string inputXamlPath, string outputYamlPath, string formFieldXmlPath, string cacheFolder)
        {
            WorkflowYamlExporter.Export(inputXamlPath, outputYamlPath, formFieldXmlPath, cacheFolder);
        }
    }

    internal static class WorkflowYamlExporter
    {
        private const string SpdTechnicalClassSuffix = ".MTW";
        private static readonly XNamespace ActivitiesNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly XNamespace SharePointNamespace = "clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities";

        public static void Export(string inputXamlPath, string outputYamlPath, string formFieldXmlPath, string cacheFolder)
        {
            if (!File.Exists(inputXamlPath)) throw new FileNotFoundException("Input XAML not found: " + inputXamlPath, inputXamlPath);
            var document = XDocument.Parse(File.ReadAllText(inputXamlPath));
            var root = document.Root ?? throw new InvalidOperationException("Workflow XAML has no root element.");
            var className = (string?)root.Attribute(XamlNamespace + "Class") ?? "ExportedWorkflow.MTW";
            var name = className.EndsWith(SpdTechnicalClassSuffix, StringComparison.OrdinalIgnoreCase) ? className.Substring(0, className.Length - SpdTechnicalClassSuffix.Length) : className;
            var workflow = new WorkflowYaml { Name = name, TechnicalName = className };
            var assignedTargets = FindAssignedArgumentNames(document);
            var formFieldParameters = LoadFormFieldParameters(inputXamlPath, formFieldXmlPath);
            var formFieldParameterNames = new HashSet<string>(formFieldParameters.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.Element(XamlNamespace + "Members")?.Elements(XamlNamespace + "Property") ?? Enumerable.Empty<XElement>())
            {
                var propertyName = (string?)property.Attribute("Name") ?? "variable";
                var propertyType = (string?)property.Attribute("Type") ?? string.Empty;
                if (IsExportableParameterType(propertyType) && formFieldParameterNames.Contains(propertyName) && !assignedTargets.Contains(propertyName)) workflow.Parameters.Add(new ParameterYaml { Name = propertyName, Type = MapXamlTypeToParameterType(propertyType), XamlType = propertyType, DisplayName = propertyName });
                else workflow.Variables.Add(new VariableYaml { Name = propertyName, Type = MapXamlTypeToVariableType(propertyType) });
            }
            if (formFieldParameters.Count > 0)
            {
                MergeFormFieldParameters(workflow, formFieldParameters);
                workflow.Metadata.Initiation.FormFields = workflow.Parameters;
                workflow.ExportWarnings.Add("FormField metadata export: parameters were populated from SharePoint workflow definition FormField metadata sidecar, preserving display names, defaults, choices, and field-specific settings where present.");
            }
            var usedObjectModel = false;
            if (!string.IsNullOrWhiteSpace(cacheFolder))
            {
                if (WfActivityBuilderSerializer.TryDeserializeWorkflowXaml(inputXamlPath, cacheFolder, out var builder, out _, out var error))
                {
                    usedObjectModel = TryExportObjectModelFlowchartStages(document, builder, workflow);
                    if (usedObjectModel) workflow.ExportWarnings.Add("WF object-model flowchart export: stage ordering and transitions were reconstructed from System.Activities.Statements.Flowchart, FlowStep.Next, and FlowDecision.True/False object references.");
                    else workflow.ExportWarnings.Add("WF object-model export skipped: deserialized ActivityBuilder did not expose a supported Flowchart stage graph; XML structural export was used.");
                }
                else
                {
                    workflow.ExportWarnings.Add("WF object-model export failed and XML structural export was used: " + error);
                }
            }

            if (!usedObjectModel && !TryExportFlowchartStages(document, workflow))
            {
                var stageElements = document.Descendants(ActivitiesNamespace + "Sequence").Where(e => !string.IsNullOrWhiteSpace((string?)e.Attribute("DisplayName"))).ToList();
                foreach (var stageElement in stageElements.Take(20))
                {
                    var stage = new StageYaml { Name = (string?)stageElement.Attribute("DisplayName") ?? "Stage" };
                    foreach (var action in ExportActions(stageElement)) stage.Actions.Add(action);
                    if (stage.Actions.Count > 0) workflow.Stages.Add(stage);
                }
            }
            MoveAssignedParametersToVariables(workflow);
            workflow.ExportWarnings.Add(usedObjectModel
                ? "Partial object-model export: flow transitions use WF object references, while supported SharePoint actions and expressions are still projected through downloaded XAML markup and may contain placeholders."
                : "Partial structural export: supported SharePoint actions are listed, but expressions and list dictionaries may be placeholders when WF deserialization is not used.");
            if (workflow.Parameters.Count == 0 && formFieldParameters.Count == 0) workflow.ExportWarnings.Add("XAML-only variable export: no SharePoint FormField metadata was present, so x:Members InArgument entries were exported as workflow variables rather than initiation parameters.");
            if (workflow.Stages.Count == 0) workflow.Stages.Add(new StageYaml { Name = "Unsupported XAML", Actions = new System.Collections.Generic.List<WorkflowActionYaml>() });
            workflow.Save(outputYamlPath);
        }

        private static bool TryExportObjectModelFlowchartStages(XDocument document, ActivityBuilder? builder, WorkflowYaml workflow)
        {
            var flowchart = FindFlowchart(builder?.Implementation);
            if (flowchart == null || flowchart.Nodes.Count == 0) return false;

            var xmlFlowchart = document.Descendants(ActivitiesNamespace + "Flowchart").FirstOrDefault();
            if (xmlFlowchart == null) return false;

            var xmlNodes = CreateObjectModelNodeXmlOrder(xmlFlowchart);
            var objectNodes = flowchart.Nodes.ToList();
            var xmlByObject = new Dictionary<FlowNode, XElement>();
            for (var i = 0; i < objectNodes.Count && i < xmlNodes.Count; i++) xmlByObject[objectNodes[i]] = xmlNodes[i];

            var stageNodes = flowchart.Nodes.OfType<FlowStep>()
                .Select(s => new FlowStepStageObject(s, xmlByObject.TryGetValue(s, out var xml) ? xml : null, xmlByObject.TryGetValue(s, out xml) ? FindStageSequence(xml) : null, xmlByObject.TryGetValue(s, out xml) ? FindStageActionContainer(xml) : null))
                .Where(s => s.StageSequence != null && s.ActionContainer != null)
                .ToList();
            if (stageNodes.Count == 0) return false;

            if (flowchart.StartNode is FlowStep || flowchart.StartNode is FlowDecision) stageNodes = OrderObjectStagesFromStart(stageNodes, flowchart.StartNode);

            var targetNames = CreateStageTargetNames(stageNodes.Select(s => new FlowStepStageXml(s.Element!, ReadObjectNodeName(s.Step, s.Element), s.StageSequence, s.ActionContainer)));
            var stages = new List<StageYaml>();
            foreach (var stageNode in stageNodes)
            {
                var nodeName = ReadObjectNodeName(stageNode.Step, stageNode.Element);
                var stage = new StageYaml { Name = targetNames[nodeName] };
                foreach (var action in ExportActions(stageNode.ActionContainer!)) stage.Actions.Add(action);
                stages.Add(stage);
            }

            var stageIndexByNode = stageNodes.Select((s, i) => new { s.Step, Index = i }).ToDictionary(s => s.Step, s => s.Index);
            var explicitTransitions = new Dictionary<int, StageTransitionYaml>();
            var terminalStageIndexes = new List<int>();
            var hasNonLinearTransition = false;
            for (var i = 0; i < stageNodes.Count; i++)
            {
                var next = stageNodes[i].Step.Next;
                var expectedLinearTarget = i + 1 < stageNodes.Count ? stageNodes[i + 1].Step : null;
                if (next == null) terminalStageIndexes.Add(i);
                if (ReferenceEquals(next, expectedLinearTarget)) continue;

                if (TryReadObjectStageTransition(next, targetNames, xmlByObject, out var transition))
                {
                    explicitTransitions[i] = transition;
                    hasNonLinearTransition = true;
                }
            }

            if (hasNonLinearTransition)
            {
                foreach (var index in terminalStageIndexes)
                {
                    if (!explicitTransitions.ContainsKey(index)) explicitTransitions[index] = new StageTransitionYaml { Default = new StageTransitionGotoYaml { Goto = "end" } };
                }

                foreach (var pair in explicitTransitions) stages[pair.Key].Transition = pair.Value;
            }

            foreach (var stage in stages.Where(s => !s.Transition.IsSpecified)) stage.Transition = null!;
            workflow.Stages.AddRange(stages.Where(s => s.Actions.Count > 0 || s.Transition?.IsSpecified == true));
            if (terminalStageIndexes.Count > 0) workflow.ExportWarnings.Add("WF object-model export: null FlowStep.Next or FlowDecision branch references were exported as the end sentinel target 'end'.");
            return workflow.Stages.Count > 0;
        }

        private static List<XElement> CreateObjectModelNodeXmlOrder(XElement xmlFlowchart)
        {
            var byName = xmlFlowchart.Descendants()
                .Where(e => e.Name.LocalName == "FlowStep" || e.Name.LocalName == "FlowDecision")
                .Select(e => new { Name = ReadXamlName(e), Element = e })
                .Where(n => !string.IsNullOrWhiteSpace(n.Name))
                .ToDictionary(n => n.Name, n => n.Element, StringComparer.OrdinalIgnoreCase);
            var result = new List<XElement>();
            foreach (var child in xmlFlowchart.Elements())
            {
                if (child.Name.LocalName == "FlowStep" || child.Name.LocalName == "FlowDecision") result.Add(child);
                else if (child.Name.LocalName == "Reference")
                {
                    var reference = ReadReference((string?)child.Attribute("Name") ?? child.Value ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(reference) && byName.TryGetValue(reference, out var element)) result.Add(element);
                }
            }

            if (result.Count == 0) result.AddRange(xmlFlowchart.Descendants().Where(e => e.Name.LocalName == "FlowStep" || e.Name.LocalName == "FlowDecision"));
            return result;
        }

        private static Flowchart? FindFlowchart(Activity? activity)
        {
            if (activity == null) return null;
            if (activity is Flowchart flowchart) return flowchart;
            if (activity is Sequence sequence)
            {
                foreach (var child in sequence.Activities)
                {
                    var found = FindFlowchart(child);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private static List<FlowStepStageObject> OrderObjectStagesFromStart(List<FlowStepStageObject> stages, FlowNode? startNode)
        {
            var byStep = stages.ToDictionary(s => s.Step);
            var ordered = new List<FlowStepStageObject>();
            var seen = new HashSet<FlowNode>();
            var current = startNode;
            while (current != null && seen.Add(current))
            {
                if (current is FlowStep step && byStep.TryGetValue(step, out var stage))
                {
                    ordered.Add(stage);
                    current = step.Next;
                    continue;
                }

                break;
            }

            foreach (var stage in stages)
            {
                if (seen.Add(stage.Step)) ordered.Add(stage);
            }

            return ordered;
        }

        private static bool TryReadObjectStageTransition(FlowNode? nextNode, IReadOnlyDictionary<string, string> targetNames, IReadOnlyDictionary<FlowNode, XElement> xmlByObject, out StageTransitionYaml transition)
        {
            transition = new StageTransitionYaml();
            if (nextNode == null)
            {
                transition.Default = new StageTransitionGotoYaml { Goto = "end" };
                return true;
            }

            if (nextNode is FlowStep step)
            {
                transition.Default = new StageTransitionGotoYaml { Goto = ResolveStageTarget(ReadObjectNodeName(step, xmlByObject.TryGetValue(step, out var stepXml) ? stepXml : null), targetNames) };
                return true;
            }

            var seenDecisions = new HashSet<FlowDecision>();
            while (nextNode is FlowDecision decision)
            {
                if (!seenDecisions.Add(decision)) return false;
                xmlByObject.TryGetValue(decision, out var decisionXml);
                transition.Branches.Add(new StageTransitionBranchYaml
                {
                    Condition = ReadConditionOrPlaceholder(decisionXml?.Element(ActivitiesNamespace + "FlowDecision.Condition")),
                    Goto = ResolveObjectStageTarget(decision.True, targetNames, xmlByObject)
                });

                if (decision.False == null)
                {
                    transition.Default = new StageTransitionGotoYaml { Goto = "end" };
                    return true;
                }

                if (decision.False is FlowStep falseStep)
                {
                    transition.Default = new StageTransitionGotoYaml { Goto = ResolveStageTarget(ReadObjectNodeName(falseStep, xmlByObject.TryGetValue(falseStep, out var falseXml) ? falseXml : null), targetNames) };
                    return true;
                }

                nextNode = decision.False;
            }

            transition.Default = new StageTransitionGotoYaml { Goto = "end" };
            return transition.Branches.Count > 0;
        }

        private static string ResolveObjectStageTarget(FlowNode? node, IReadOnlyDictionary<string, string> targetNames, IReadOnlyDictionary<FlowNode, XElement> xmlByObject)
        {
            if (node == null) return "end";
            return node is FlowStep step ? ResolveStageTarget(ReadObjectNodeName(step, xmlByObject.TryGetValue(step, out var xml) ? xml : null), targetNames) : "end";
        }

        private static string ReadObjectNodeName(FlowNode node, XElement? element)
        {
            var name = element == null ? string.Empty : ReadXamlName(element);
            return string.IsNullOrWhiteSpace(name) ? "node" + node.GetHashCode().ToString(CultureInfo.InvariantCulture) : name;
        }

        private sealed class FlowStepStageObject
        {
            public FlowStepStageObject(FlowStep step, XElement? element, XElement? stageSequence, XElement? actionContainer) { Step = step; Element = element; StageSequence = stageSequence; ActionContainer = actionContainer; }
            public FlowStep Step { get; }
            public XElement? Element { get; }
            public XElement? StageSequence { get; }
            public XElement? ActionContainer { get; }
        }

        private static bool TryExportFlowchartStages(XDocument document, WorkflowYaml workflow)
        {
            var flowchart = document.Descendants(ActivitiesNamespace + "Flowchart").FirstOrDefault();
            if (flowchart == null) return false;

            var nodesByName = flowchart.Elements()
                .Where(e => e.Name.LocalName == "FlowStep" || e.Name.LocalName == "FlowDecision")
                .Select(e => new FlowNodeXml(e, ReadXamlName(e)))
                .Where(n => !string.IsNullOrWhiteSpace(n.Name))
                .ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
            if (nodesByName.Count == 0) return false;

            var stageNodes = flowchart.Elements(ActivitiesNamespace + "FlowStep")
                .Select(e => new FlowStepStageXml(e, ReadXamlName(e), FindStageSequence(e), FindStageActionContainer(e)))
                .Where(s => !string.IsNullOrWhiteSpace(s.NodeName) && s.StageSequence != null && s.ActionContainer != null)
                .ToList();
            if (stageNodes.Count == 0) return false;

            var startReference = ReadReference((string?)flowchart.Attribute("StartNode") ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(startReference)) stageNodes = OrderStagesFromStart(stageNodes, nodesByName, startReference);

            var targetNames = CreateStageTargetNames(stageNodes);
            var stages = new List<StageYaml>();
            foreach (var stageNode in stageNodes)
            {
                var stage = new StageYaml { Name = targetNames[stageNode.NodeName] };
                foreach (var action in ExportActions(stageNode.ActionContainer!)) stage.Actions.Add(action);
                stages.Add(stage);
            }

            var stageIndexByNodeName = stageNodes.Select((s, i) => new { s.NodeName, Index = i }).ToDictionary(s => s.NodeName, s => s.Index, StringComparer.OrdinalIgnoreCase);
            var explicitTransitions = new Dictionary<int, StageTransitionYaml>();
            var terminalStageIndexes = new List<int>();
            var hasNonLinearTransition = false;
            for (var i = 0; i < stageNodes.Count; i++)
            {
                var nextReference = NormalizeTargetReference(ReadFlowStepNextReference(stageNodes[i].Element), nodesByName, targetNames);
                var expectedLinearTarget = i + 1 < stageNodes.Count ? stageNodes[i + 1].NodeName : string.Empty;
                if (string.IsNullOrWhiteSpace(nextReference)) terminalStageIndexes.Add(i);
                if (IsLinearTarget(nextReference, expectedLinearTarget)) continue;

                if (TryReadStageTransition(nextReference, nodesByName, targetNames, out var transition))
                {
                    explicitTransitions[i] = transition;
                    hasNonLinearTransition = true;
                }
            }

            if (hasNonLinearTransition)
            {
                foreach (var index in terminalStageIndexes)
                {
                    if (!explicitTransitions.ContainsKey(index)) explicitTransitions[index] = new StageTransitionYaml { Default = new StageTransitionGotoYaml { Goto = "end" } };
                }

                foreach (var pair in explicitTransitions) stages[pair.Key].Transition = pair.Value;
            }

            foreach (var stage in stages.Where(s => !s.Transition.IsSpecified)) stage.Transition = null!;
            workflow.Stages.AddRange(stages.Where(s => s.Actions.Count > 0 || s.Transition?.IsSpecified == true));
            return workflow.Stages.Count > 0;
        }

        private static List<FlowStepStageXml> OrderStagesFromStart(List<FlowStepStageXml> stages, IReadOnlyDictionary<string, FlowNodeXml> nodesByName, string startReference)
        {
            var byName = stages.ToDictionary(s => s.NodeName, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<FlowStepStageXml>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = startReference;
            while (!string.IsNullOrWhiteSpace(current) && byName.TryGetValue(current, out var stage) && seen.Add(current))
            {
                ordered.Add(stage);
                var next = ReadFlowStepNextReference(stage.Element);
                if (string.IsNullOrWhiteSpace(next)) break;
                if (byName.ContainsKey(next)) current = next;
                else if (nodesByName.TryGetValue(next, out var node) && node.Element.Name.LocalName == "FlowDecision") break;
                else break;
            }

            foreach (var stage in stages)
            {
                if (seen.Add(stage.NodeName)) ordered.Add(stage);
            }

            return ordered;
        }

        private static Dictionary<string, string> CreateStageTargetNames(IEnumerable<FlowStepStageXml> stageNodes)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var stageNode in stageNodes)
            {
                var baseName = ReadStageDisplayName(stageNode.StageSequence!, stageNode.ActionContainer!) ?? stageNode.NodeName;
                var name = string.IsNullOrWhiteSpace(baseName) ? "Stage" : baseName.Trim();
                var unique = name;
                var suffix = 2;
                while (!used.Add(unique)) unique = name + " " + suffix++;
                result[stageNode.NodeName] = unique;
            }

            return result;
        }

        private static bool TryReadStageTransition(string nextReference, IReadOnlyDictionary<string, FlowNodeXml> nodesByName, IReadOnlyDictionary<string, string> targetNames, out StageTransitionYaml transition)
        {
            transition = new StageTransitionYaml();
            if (string.IsNullOrWhiteSpace(nextReference))
            {
                transition.Default = new StageTransitionGotoYaml { Goto = "end" };
                return true;
            }

            if (!nodesByName.TryGetValue(nextReference, out var node)) return false;
            if (node.Element.Name.LocalName == "FlowStep")
            {
                transition.Default = new StageTransitionGotoYaml { Goto = ResolveStageTarget(nextReference, targetNames) };
                return true;
            }

            if (node.Element.Name.LocalName != "FlowDecision") return false;
            var seenDecisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (node.Element.Name.LocalName == "FlowDecision")
            {
                if (!seenDecisions.Add(node.Name)) return false;
                var trueReference = ReadFlowDecisionTargetReference(node.Element, "True");
                transition.Branches.Add(new StageTransitionBranchYaml
                {
                    Condition = ReadConditionOrPlaceholder(node.Element.Element(ActivitiesNamespace + "FlowDecision.Condition")),
                    Goto = ResolveStageTarget(trueReference, targetNames)
                });

                var falseReference = ReadFlowDecisionTargetReference(node.Element, "False");
                if (string.IsNullOrWhiteSpace(falseReference))
                {
                    transition.Default = new StageTransitionGotoYaml { Goto = "end" };
                    return true;
                }

                if (!nodesByName.TryGetValue(falseReference, out node))
                {
                    transition.Default = new StageTransitionGotoYaml { Goto = ResolveStageTarget(falseReference, targetNames) };
                    return true;
                }
            }

            transition.Default = new StageTransitionGotoYaml { Goto = ResolveStageTarget(node.Name, targetNames) };
            return transition.Branches.Count > 0;
        }

        private static bool IsLinearTarget(string actualReference, string expectedReference) =>
            string.IsNullOrWhiteSpace(actualReference)
                ? string.IsNullOrWhiteSpace(expectedReference)
                : string.Equals(actualReference, expectedReference, StringComparison.OrdinalIgnoreCase);

        private static string NormalizeTargetReference(string reference, IReadOnlyDictionary<string, FlowNodeXml> nodesByName, IReadOnlyDictionary<string, string> targetNames)
        {
            if (string.IsNullOrWhiteSpace(reference)) return string.Empty;
            if (nodesByName.ContainsKey(reference) || targetNames.ContainsKey(reference)) return reference;
            return string.Empty;
        }

        private static string ResolveStageTarget(string reference, IReadOnlyDictionary<string, string> targetNames)
        {
            if (string.IsNullOrWhiteSpace(reference)) return "end";
            return targetNames.TryGetValue(reference, out var target) ? target : "end";
        }

        private static XElement? FindStageSequence(XElement flowStep) => flowStep.Elements(ActivitiesNamespace + "Sequence").FirstOrDefault();

        private static XElement? FindStageActionContainer(XElement flowStep)
        {
            var sequence = FindStageSequence(flowStep);
            if (sequence == null) return null;
            if (!HasStageContainerMetadata(sequence)) return sequence;
            return sequence.Elements(ActivitiesNamespace + "Sequence")
                .FirstOrDefault(e => !IsStageFooter(e) && ExportActions(e).Any())
                ?? sequence.Elements(ActivitiesNamespace + "Sequence").FirstOrDefault(e => !IsStageFooter(e))
                ?? sequence;
        }

        private static bool HasStageContainerMetadata(XElement sequence) => sequence.Elements().Any(e => e.Name.LocalName == "SPDesignerXamlWriter.CustomAttributes" && e.Descendants().Any(d => d.Name.LocalName == "String" && string.Equals((string?)d.Attribute(XamlNamespace + "Key"), "StageAttribute", StringComparison.OrdinalIgnoreCase) && ((string?)d ?? string.Empty).StartsWith("StageContainer-", StringComparison.OrdinalIgnoreCase)));

        private static bool IsStageFooter(XElement sequence) => sequence.Elements().Any(e => e.Name.LocalName == "SPDesignerXamlWriter.CustomAttributes" && e.Descendants().Any(d => d.Name.LocalName == "String" && string.Equals((string?)d.Attribute(XamlNamespace + "Key"), "StageAttribute", StringComparison.OrdinalIgnoreCase) && ((string?)d ?? string.Empty).StartsWith("StageFooter-", StringComparison.OrdinalIgnoreCase)));

        private static string? ReadStageDisplayName(XElement stageSequence, XElement actionContainer)
        {
            var bodyName = (string?)actionContainer.Attribute("DisplayName");
            if (!string.IsNullOrWhiteSpace(bodyName)) return bodyName;
            var status = stageSequence.Elements().FirstOrDefault(e => e.Name.LocalName == "SetWorkflowStatus")?.Attribute("Status")?.Value;
            if (!string.IsNullOrWhiteSpace(status)) return status;
            return (string?)stageSequence.Attribute("DisplayName");
        }

        private static string ReadFlowStepNextReference(XElement flowStep) => ReadNodeTargetReference(flowStep, "Next");

        private static string ReadFlowDecisionTargetReference(XElement flowDecision, string propertyName) => ReadNodeTargetReference(flowDecision, propertyName);

        private static string ReadNodeTargetReference(XElement node, string propertyName)
        {
            var attributeReference = ReadReference((string?)node.Attribute(propertyName) ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(attributeReference)) return attributeReference;
            var propertyElement = node.Element(ActivitiesNamespace + (node.Name.LocalName + "." + propertyName)) ?? node.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(node.Name.LocalName + "." + propertyName, StringComparison.OrdinalIgnoreCase));
            var referenceElement = propertyElement?.Elements().FirstOrDefault(e => e.Name.LocalName == "Reference");
            return ReadReference((string?)referenceElement?.Attribute("Name") ?? referenceElement?.Value ?? string.Empty);
        }

        private static string ReadXamlName(XElement element) => (string?)element.Attribute(XamlNamespace + "Name") ?? (string?)element.Attribute("Name") ?? string.Empty;

        private static string ReadReference(string value)
        {
            value = (value ?? string.Empty).Trim();
            const string marker = "{x:Reference ";
            if (value.StartsWith(marker, StringComparison.OrdinalIgnoreCase) && value.EndsWith("}", StringComparison.Ordinal)) return value.Substring(marker.Length, value.Length - marker.Length - 1).Trim();
            return value;
        }

        private sealed class FlowNodeXml
        {
            public FlowNodeXml(XElement element, string name) { Element = element; Name = name; }
            public XElement Element { get; }
            public string Name { get; }
        }

        private sealed class FlowStepStageXml
        {
            public FlowStepStageXml(XElement element, string nodeName, XElement? stageSequence, XElement? actionContainer) { Element = element; NodeName = nodeName; StageSequence = stageSequence; ActionContainer = actionContainer; }
            public XElement Element { get; }
            public string NodeName { get; }
            public XElement? StageSequence { get; }
            public XElement? ActionContainer { get; }
        }

        private static void MoveAssignedParametersToVariables(WorkflowYaml workflow)
        {
            var assigned = new HashSet<string>(workflow.Stages.SelectMany(s => s.Actions ?? new List<WorkflowActionYaml>()).SelectMany(EnumerateActions).OfType<ITargetedActionYaml>().Select(a => a.To ?? string.Empty).Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
            if (workflow.Stages.SelectMany(s => s.Actions ?? new List<WorkflowActionYaml>()).SelectMany(EnumerateActions).OfType<CallHttpWebServiceActionYaml>().Any()) assigned.Add("varRequestHeaders");
            if (assigned.Count == 0) return;

            var remainingParameters = new List<ParameterYaml>();
            foreach (var parameter in workflow.Parameters ?? new List<ParameterYaml>())
            {
                if (assigned.Contains(parameter.Name)) workflow.Variables.Add(new VariableYaml { Name = parameter.Name, Type = string.IsNullOrWhiteSpace(parameter.Type) ? MapXamlTypeToVariableType(parameter.XamlType) : parameter.Type });
                else remainingParameters.Add(parameter);
            }

            workflow.Parameters = remainingParameters;
        }

        private static IEnumerable<WorkflowActionYaml> EnumerateActions(WorkflowActionYaml action)
        {
            yield return action;
            if (action is IfActionYaml ifAction)
            {
                foreach (var child in (ifAction.Then ?? new List<WorkflowActionYaml>()).SelectMany(EnumerateActions)) yield return child;
                foreach (var child in (ifAction.Else ?? new List<WorkflowActionYaml>()).SelectMany(EnumerateActions)) yield return child;
            }
            else if (action is WhileActionYaml whileAction)
            {
                foreach (var child in (whileAction.Actions ?? new List<WorkflowActionYaml>()).SelectMany(EnumerateActions)) yield return child;
            }
        }

        private static List<ParameterYaml> LoadFormFieldParameters(string inputXamlPath, string explicitFormFieldXmlPath)
        {
            var formFieldPath = ResolveFormFieldXmlPath(inputXamlPath, explicitFormFieldXmlPath);
            return string.IsNullOrWhiteSpace(formFieldPath) ? new List<ParameterYaml>() : WorkflowParameterFormFieldSerializer.Parse(File.ReadAllText(formFieldPath));
        }

        internal static string ResolveFormFieldXmlPath(string inputXamlPath, string explicitFormFieldXmlPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitFormFieldXmlPath))
            {
                if (!File.Exists(explicitFormFieldXmlPath)) throw new FileNotFoundException("FormField XML not found: " + explicitFormFieldXmlPath, explicitFormFieldXmlPath);
                return explicitFormFieldXmlPath;
            }

            if (string.IsNullOrWhiteSpace(inputXamlPath)) return string.Empty;
            var sidecarPath = inputXamlPath + ".formfield.xml";
            return File.Exists(sidecarPath) ? sidecarPath : string.Empty;
        }

        private static void MergeFormFieldParameters(WorkflowYaml workflow, List<ParameterYaml> formFieldParameters)
        {
            var xamlParameters = workflow.Parameters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            var merged = new List<ParameterYaml>();
            var mergedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var formFieldParameter in formFieldParameters)
            {
                if (xamlParameters.TryGetValue(formFieldParameter.Name, out var xamlParameter) && string.IsNullOrWhiteSpace(formFieldParameter.XamlType)) formFieldParameter.XamlType = xamlParameter.XamlType;
                merged.Add(formFieldParameter);
                mergedNames.Add(formFieldParameter.Name);
            }

            foreach (var xamlParameter in workflow.Parameters)
            {
                if (!mergedNames.Contains(xamlParameter.Name)) merged.Add(xamlParameter);
            }

            workflow.Parameters = merged;
        }

        private static bool IsExportableParameterType(string propertyType) =>
            propertyType.IndexOf("InArgument", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (propertyType.IndexOf("String", StringComparison.OrdinalIgnoreCase) >= 0 ||
             propertyType.IndexOf("Boolean", StringComparison.OrdinalIgnoreCase) >= 0 ||
             propertyType.IndexOf("Double", StringComparison.OrdinalIgnoreCase) >= 0 ||
             propertyType.IndexOf("DateTime", StringComparison.OrdinalIgnoreCase) >= 0 ||
             propertyType.IndexOf("DynamicValue", StringComparison.OrdinalIgnoreCase) >= 0);

        private static HashSet<string> FindAssignedArgumentNames(XDocument document)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in document.Descendants().Where(e => e.Name.LocalName == "ArgumentReference"))
            {
                var parent = reference.Parent;
                while (parent != null)
                {
                    if (parent.Name.LocalName == "OutArgument")
                    {
                        var name = (string?)reference.Attribute("ArgumentName") ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(name)) targets.Add(name);
                        break;
                    }

                    parent = parent.Parent;
                }
            }

            return targets;
        }

        private static string MapXamlTypeToParameterType(string propertyType)
        {
            if (propertyType.IndexOf("Boolean", StringComparison.OrdinalIgnoreCase) >= 0) return "Boolean";
            if (propertyType.IndexOf("Double", StringComparison.OrdinalIgnoreCase) >= 0) return "Number";
            if (propertyType.IndexOf("DateTime", StringComparison.OrdinalIgnoreCase) >= 0) return "DateTime";
            if (propertyType.IndexOf("DynamicValue", StringComparison.OrdinalIgnoreCase) >= 0) return "DynamicValue";
            return "Text";
        }

        private static string MapXamlTypeToVariableType(string propertyType)
        {
            if (propertyType.IndexOf("Boolean", StringComparison.OrdinalIgnoreCase) >= 0) return "Boolean";
            if (propertyType.IndexOf("Double", StringComparison.OrdinalIgnoreCase) >= 0) return "Double";
            if (propertyType.IndexOf("DateTime", StringComparison.OrdinalIgnoreCase) >= 0) return "DateTime";
            if (propertyType.IndexOf("DynamicValue", StringComparison.OrdinalIgnoreCase) >= 0) return "DynamicValue";
            if (propertyType.IndexOf("Int32", StringComparison.OrdinalIgnoreCase) >= 0) return "Int32";
            if (propertyType.IndexOf("Guid", StringComparison.OrdinalIgnoreCase) >= 0) return "Guid";
            return "String";
        }

        private static IEnumerable<WorkflowActionYaml> ExportActions(XElement container)
        {
            foreach (var child in container.Elements())
            {
                var action = TryExportAction(child);
                if (action != null) yield return action;
            }
        }

        private static WorkflowActionYaml? TryExportAction(XElement child)
        {
            if (child.Name.Namespace == SharePointNamespace) return TryExportSharePointAction(child);
            if (child.Name.LocalName == "BuildDynamicValue") return TryExportBuildDynamicValueAction(child);
            if (child.Name.LocalName == "GetDynamicValueProperty") return TryExportGetDynamicValuePropertyAction(child);
            if (child.Name.LocalName == "CountDynamicValueItems") return TryExportCountDynamicValueItemsAction(child);
            if (child.Name.Namespace == ActivitiesNamespace && child.Name.LocalName == "Assign") return TryExportAssignAction(child);
            if (child.Name.Namespace == ActivitiesNamespace && child.Name.LocalName == "While") return TryExportWhileAction(child);
            if (child.Name.Namespace == ActivitiesNamespace && child.Name.LocalName == "If") return TryExportIfAction(child);
            return null;
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
            if (child.Name.LocalName == "CallHTTPWebService") return new CallHttpWebServiceActionYaml { Address = ReadActivityPropertyExpression(child, "Address"), RequestType = ReadHttpRequestTypeExpression(child), RequestContent = ReadArgumentName(child, "RequestContent"), RequestHeaders = ReadArgumentName(child, "RequestHeaders"), ResponseStatusCodeTo = ReadOutArgumentTargetOrPlaceholder(child, "ResponseStatusCode"), ResponseContentTo = ReadOutArgumentTarget(child, "ResponseContent") ?? string.Empty, ResponseHeadersTo = ReadOutArgumentTarget(child, "ResponseHeaders") ?? string.Empty };
            if (child.Name.LocalName == "SingleTask") return new SingleTaskActionYaml { AssignedTo = ReadExpressionAttributeOrPlaceholder(child, "AssignedTo"), Title = ReadExpressionAttributeOrPlaceholder(child, "Title"), Body = ReadExpressionAttributeOrPlaceholder(child, "Body"), DueDate = ReadExpressionAttributeOrPlaceholder(child, "DueDate"), TaskIdTo = ReadOutArgumentTarget(child, "TaskId"), OutcomeTo = ReadOutArgumentTarget(child, "Outcome") };
            if (child.Name.LocalName == "CreateListItem") return new CreateListItemActionYaml { ListId = ReadActivityPropertyExpression(child, "ListId"), Fields = ReadListItemProperties(child), ItemIdTo = ReadOutArgumentTarget(child, "ItemId"), ItemGuidTo = ReadOutArgumentTarget(child, "ItemGuid") ?? ReadOutArgumentTarget(child, "Result") };
            if (child.Name.LocalName == "UpdateListItem") return new UpdateListItemActionYaml { ListId = ReadActivityPropertyExpression(child, "ListId"), ItemId = ReadOptionalActivityPropertyExpression(child, "ItemId") ?? new ExpressionYaml(), ItemGuid = ReadOptionalActivityPropertyExpression(child, "ItemGuid") ?? new ExpressionYaml(), Fields = ReadListItemProperties(child) };
            if (child.Name.LocalName == "DeleteListItem") return new DeleteListItemActionYaml { ListId = ReadActivityPropertyExpression(child, "ListId"), ItemId = ReadOptionalActivityPropertyExpression(child, "ItemId") ?? new ExpressionYaml(), ItemGuid = ReadOptionalActivityPropertyExpression(child, "ItemGuid") ?? new ExpressionYaml() };
            return null;
        }

        private static WorkflowActionYaml? TryExportBuildDynamicValueAction(XElement child)
        {
            var target = ReadOutArgumentTarget(child, "Result") ?? ReadInOutArgumentTarget(child, "Result");
            if (string.IsNullOrWhiteSpace(target)) return null;
            var entries = new List<DynamicValueEntryYaml>();
            var propertiesElement = child.Element(child.Name.Namespace + (child.Name.LocalName + ".Properties")) ?? child.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(child.Name.LocalName + ".Properties", StringComparison.OrdinalIgnoreCase));
            foreach (var argument in propertiesElement?.Descendants().Where(e => e.Name.LocalName == "InArgument") ?? Enumerable.Empty<XElement>())
            {
                var key = (string?)argument.Attribute(XamlNamespace + "Key") ?? (string?)argument.Attribute("Key") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) continue;
                entries.Add(new DynamicValueEntryYaml { Key = key, Value = ReadExpressionElementOrPlaceholder(argument), ValueType = MapXamlTypeArgumentToVariableType((string?)argument.Attribute(XamlNamespace + "TypeArguments") ?? string.Empty) });
            }

            return entries.Count == 0 ? null : new BuildDynamicValueActionYaml { To = target ?? string.Empty, Entries = entries };
        }

        private static WorkflowActionYaml? TryExportGetDynamicValuePropertyAction(XElement child)
        {
            var target = ReadOutArgumentTarget(child, "Result");
            var source = ReadArgumentName(child, "Source");
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(source)) return null;
            return new GetDynamicValuePropertyActionYaml { Source = source ?? string.Empty, PropertyName = ReadActivityStringPropertyExpression(child, "PropertyName"), To = target ?? string.Empty, ValueType = MapXamlTypeArgumentToVariableType(ReadXamlTypeArgument(child)) };
        }

        private static WorkflowActionYaml? TryExportCountDynamicValueItemsAction(XElement child)
        {
            var target = ReadOutArgumentTarget(child, "Result");
            var source = ReadArgumentName(child, "Source");
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(source)) return null;
            return new CountDynamicValueItemsActionYaml { Source = source ?? string.Empty, To = target ?? string.Empty };
        }

        private static WorkflowActionYaml? TryExportAssignAction(XElement assign)
        {
            var target = assign.Element(ActivitiesNamespace + "Assign.To")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "ArgumentReference")?.Attribute("ArgumentName")?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target)) return null;

            var valueElement = assign.Element(ActivitiesNamespace + "Assign.Value");
            var expressionActivity = valueElement?.Descendants().FirstOrDefault(IsSupportedStringExpressionActivity);
            if (expressionActivity != null)
            {
                if (expressionActivity.Name.LocalName.Equals("ReplaceString", StringComparison.OrdinalIgnoreCase)) return new StringReplaceActionYaml { Text = ReadActivityPropertyExpression(expressionActivity, "Input"), OldValue = ReadActivityStringPropertyExpression(expressionActivity, "Pattern"), NewValue = ReadActivityStringPropertyExpression(expressionActivity, "Replacement"), To = target };
                if (expressionActivity.Name.LocalName.Equals("Substring", StringComparison.OrdinalIgnoreCase)) return new StringSubstringActionYaml { Text = ReadActivityPropertyExpression(expressionActivity, "Input"), StartIndex = ReadActivityPropertyExpression(expressionActivity, "StartIndex"), Length = ReadOptionalActivityPropertyExpression(expressionActivity, "Length"), To = target };
                if (expressionActivity.Name.LocalName.Equals("Trim", StringComparison.OrdinalIgnoreCase)) return new StringTrimActionYaml { Text = ReadActivityPropertyExpression(expressionActivity, "Input"), To = target };
            }

            // Legacy/import compatibility only: older/downloaded XAML may contain raw VisualBasicValue text.
            // Generated SPNet XAML must not emit raw VB/C# language expression activities because Workflow Manager rejects them.
            var expressionText = valueElement?.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualBasicValue")?.Attribute("ExpressionText")?.Value ?? string.Empty;
            if (expressionText.IndexOf(".Replace(", StringComparison.OrdinalIgnoreCase) >= 0) return new StringReplaceActionYaml { Text = ExportedStringExpression(), OldValue = ExportedStringExpression(), NewValue = ExportedStringExpression(), To = target };
            if (expressionText.IndexOf(".Substring(", StringComparison.OrdinalIgnoreCase) >= 0) return new StringSubstringActionYaml { Text = ExportedStringExpression(), StartIndex = new ExpressionYaml { Literal = 0 }, Length = new ExpressionYaml(), To = target };
            if (expressionText.IndexOf(".Trim()", StringComparison.OrdinalIgnoreCase) >= 0 || expressionText.IndexOf(".Trim(", StringComparison.OrdinalIgnoreCase) >= 0) return new StringTrimActionYaml { Text = ExportedStringExpression(), To = target };
            return new AssignActionYaml { To = target, Value = ReadExpressionElementOrPlaceholder(valueElement) };
        }

        private static WorkflowActionYaml TryExportWhileAction(XElement whileElement) => new WhileActionYaml
        {
            Condition = ReadConditionOrPlaceholder(whileElement.Element(ActivitiesNamespace + "While.Condition")),
            Actions = ExportActions(FindWhileBodyContainer(whileElement)).ToList()
        };

        private static XElement FindWhileBodyContainer(XElement whileElement)
        {
            var explicitBody = whileElement.Element(ActivitiesNamespace + "While.Body")?.Elements().FirstOrDefault();
            if (explicitBody != null) return explicitBody;

            var conditionElement = whileElement.Element(ActivitiesNamespace + "While.Condition");
            return whileElement.Elements().FirstOrDefault(e => !object.ReferenceEquals(e, conditionElement) && !e.Name.LocalName.Equals("While.Condition", StringComparison.OrdinalIgnoreCase)) ?? whileElement;
        }

        private static WorkflowActionYaml TryExportIfAction(XElement ifElement) => new IfActionYaml
        {
            Condition = ReadConditionOrPlaceholder(ifElement.Element(ActivitiesNamespace + "If.Condition")),
            Then = ExportActions(ifElement.Element(ActivitiesNamespace + "If.Then")?.Elements().FirstOrDefault() ?? new XElement("empty")).ToList(),
            Else = ExportActions(ifElement.Element(ActivitiesNamespace + "If.Else")?.Elements().FirstOrDefault() ?? new XElement("empty")).ToList()
        };

        private static ExpressionYaml ExportedStringExpression() => new ExpressionYaml { Literal = "<exported expression>" };

        private static ExpressionYaml ReadHttpRequestTypeExpression(XElement element)
        {
            var expression = ReadActivityPropertyExpression(element, "RequestType");
            if (string.IsNullOrWhiteSpace(expression.Variable)) return expression;

            var normalized = expression.Variable.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToUpperInvariant();
            return normalized == "GET" || normalized == "POST" || normalized == "PUT" || normalized == "DELETE" || normalized == "HTTPGET" || normalized == "HTTPPOST" || normalized == "HTTPPUT" || normalized == "HTTPDELETE"
                ? new ExpressionYaml { Literal = expression.Variable }
                : expression;
        }

        private static ExpressionYaml ReadExpressionAttributeOrPlaceholder(XElement element, string attributeName) => element.Attribute(attributeName) is XAttribute attribute ? ReadExpressionText(attribute.Value) : new ExpressionYaml { Literal = "<exported expression>" };

        private static ExpressionYaml ReadExpressionElementOrPlaceholder(XElement? element)
        {
            if (element == null) return new ExpressionYaml { Literal = "<exported expression>" };
            var nestedExpression = element.Descendants().FirstOrDefault(IsSupportedNestedExpressionActivity);
            if (nestedExpression != null) return ReadNestedExpressionActivity(nestedExpression);
            var argumentReference = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "ArgumentReference")?.Attribute("ArgumentName")?.Value;
            if (!string.IsNullOrWhiteSpace(argumentReference)) return new ExpressionYaml { Variable = argumentReference ?? string.Empty };
            var argumentValue = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "ArgumentValue")?.Attribute("ArgumentName")?.Value;
            if (!string.IsNullOrWhiteSpace(argumentValue)) return new ExpressionYaml { Variable = argumentValue ?? string.Empty };
            // Legacy/import compatibility only. This recognizes raw VB expression text in downloaded XAML but does not endorse it as output.
            var visualBasic = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualBasicValue")?.Attribute("ExpressionText")?.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(visualBasic)) return ReadExpressionText(visualBasic);
            var literal = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "Literal")?.Attribute("Value")?.Value ?? string.Empty;
            if (!element.Elements().Any() && !string.IsNullOrWhiteSpace(element.Value)) return ReadExpressionText(element.Value);
            return !string.IsNullOrWhiteSpace(literal) ? ReadExpressionText(literal) : new ExpressionYaml { Literal = "<exported expression>" };
        }

        private static ExpressionYaml ReadExpressionText(string text)
        {
            text = WebUtility.HtmlDecode(text ?? string.Empty).Trim();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"') return new ExpressionYaml { Literal = text.Substring(1, text.Length - 2).Replace("\"\"", "\"") };
            if (bool.TryParse(text, out var boolean)) return new ExpressionYaml { Literal = boolean };
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return new ExpressionYaml { Literal = number };
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime)) return new ExpressionYaml { Literal = dateTime };
            if (Guid.TryParse(text, out var guid)) return new ExpressionYaml { Literal = guid.ToString("D") };
            if (text.Equals("GetCurrentListId()", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "getCurrentListId" };
            if (text.Equals("GetCurrentItemGuid()", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "getCurrentItemGuid" };
            if (text.StartsWith("LookupWorkflowContext(", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "lookupWorkflowContext", PropertyName = ReadFirstQuotedArgument(text) };
            return IsIdentifier(text) ? new ExpressionYaml { Variable = text } : new ExpressionYaml { Literal = text };
        }

        private static ComparisonExpressionYaml ReadConditionOrPlaceholder(XElement? conditionElement)
        {
            var activity = conditionElement?.Descendants().FirstOrDefault(IsSupportedBooleanExpressionActivity);
            return activity == null ? new ComparisonExpressionYaml { Type = "isEqual", Left = new ExpressionYaml { Literal = 1 }, Right = new ExpressionYaml { Literal = 1 } } : ReadBooleanExpressionActivity(activity);
        }

        private static ComparisonExpressionYaml ReadBooleanExpressionActivity(XElement activity)
        {
            var name = activity.Name.LocalName;
            if (name.Equals("And", StringComparison.OrdinalIgnoreCase) || name.Equals("Or", StringComparison.OrdinalIgnoreCase))
            {
                return new ComparisonExpressionYaml
                {
                    Type = name.Equals("And", StringComparison.OrdinalIgnoreCase) ? "and" : "or",
                    LeftCondition = ReadNestedBooleanCondition(activity, "Left"),
                    RightCondition = ReadNestedBooleanCondition(activity, "Right")
                };
            }

            if (name.Equals("Not", StringComparison.OrdinalIgnoreCase)) return new ComparisonExpressionYaml { Type = "not", Operand = ReadNestedBooleanCondition(activity, "Operand") };
            if (name.Equals("IsEqualBoolean", StringComparison.OrdinalIgnoreCase)) return new ComparisonExpressionYaml { Type = "isEqual", ValueType = "Boolean", Left = ReadActivityPropertyExpression(activity, "Left"), Right = ReadActivityPropertyExpression(activity, "Right") };
            if (name.Equals("IsEqualDynamicValue", StringComparison.OrdinalIgnoreCase)) return new ComparisonExpressionYaml { Type = "isEqual", ValueType = "DynamicValue", Left = ReadActivityPropertyExpression(activity, "Left"), Right = ReadActivityPropertyExpression(activity, "Right") };
            if (name.Equals("IsEqualString", StringComparison.OrdinalIgnoreCase) || name.Equals("IsEqualStringIgnoreCase", StringComparison.OrdinalIgnoreCase)) return new ComparisonExpressionYaml { Type = name.EndsWith("IgnoreCase", StringComparison.OrdinalIgnoreCase) ? "isEqualStringIgnoreCase" : "isEqualString", ValueType = name.EndsWith("IgnoreCase", StringComparison.OrdinalIgnoreCase) ? "StringIgnoreCase" : "String", Left = ReadActivityPropertyExpression(activity, "Input"), Right = ReadActivityStringPropertyExpression(activity, "Text") };
            if (name.Equals("ContainsString", StringComparison.OrdinalIgnoreCase) || name.Equals("ContainsStringIgnoreCase", StringComparison.OrdinalIgnoreCase)) return new ComparisonExpressionYaml { Type = name.EndsWith("IgnoreCase", StringComparison.OrdinalIgnoreCase) ? "containsStringIgnoreCase" : "containsString", ValueType = name.EndsWith("IgnoreCase", StringComparison.OrdinalIgnoreCase) ? "StringIgnoreCase" : "String", Left = ReadActivityPropertyExpression(activity, "Input"), Right = ReadActivityStringPropertyExpression(activity, "SearchValue") };
            if (name.Equals("StartsWithString", StringComparison.OrdinalIgnoreCase)) return new ComparisonExpressionYaml { Type = "startsWithString", ValueType = "String", Left = ReadActivityPropertyExpression(activity, "Input"), Right = ReadActivityStringPropertyExpression(activity, "SearchValue") };
            if (name.Equals("EndsWithString", StringComparison.OrdinalIgnoreCase)) return new ComparisonExpressionYaml { Type = "endsWithString", ValueType = "String", Left = ReadActivityPropertyExpression(activity, "Input"), Right = ReadActivityStringPropertyExpression(activity, "SearchValue") };
            if (IsDateComparisonExpressionActivity(activity)) return new ComparisonExpressionYaml { Type = NormalizeComparisonType(name), ValueType = "DateTime", Left = ReadActivityPropertyExpression(activity, "Left"), Right = ReadActivityPropertyExpression(activity, "Right") };
            return new ComparisonExpressionYaml
            {
                Type = NormalizeComparisonType(name),
                Left = ReadActivityPropertyExpression(activity, "Left"),
                Right = ReadActivityPropertyExpression(activity, "Right")
            };
        }

        private static ComparisonExpressionYaml ReadNestedBooleanCondition(XElement activity, string propertyName)
        {
            var propertyElement = activity.Element(activity.Name.Namespace + (activity.Name.LocalName + "." + propertyName)) ?? activity.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(activity.Name.LocalName + "." + propertyName, StringComparison.OrdinalIgnoreCase));
            var nested = propertyElement?.Descendants().FirstOrDefault(IsSupportedBooleanExpressionActivity);
            return nested == null ? new ComparisonExpressionYaml { Type = "isEqual", Left = new ExpressionYaml { Literal = 1 }, Right = new ExpressionYaml { Literal = 1 } } : ReadBooleanExpressionActivity(nested);
        }

        private static bool IsSupportedBooleanExpressionActivity(XElement element) => element.Name.LocalName.Equals("And", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("Or", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("Not", StringComparison.OrdinalIgnoreCase) || IsComparisonExpressionActivity(element);

        private static bool IsComparisonExpressionActivity(XElement element)
        {
            var name = element.Name.LocalName;
            return (name.StartsWith("Is", StringComparison.OrdinalIgnoreCase) && (name.IndexOf("Than", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Equal", StringComparison.OrdinalIgnoreCase) >= 0)) || name.Equals("Equal", StringComparison.OrdinalIgnoreCase) || name.Equals("IsEqualNumber", StringComparison.OrdinalIgnoreCase) || name.Equals("ContainsString", StringComparison.OrdinalIgnoreCase) || name.Equals("ContainsStringIgnoreCase", StringComparison.OrdinalIgnoreCase) || name.Equals("StartsWithString", StringComparison.OrdinalIgnoreCase) || name.Equals("EndsWithString", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDateComparisonExpressionActivity(XElement element) => element.Name.LocalName.EndsWith("Date", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.EndsWith("DateTime", StringComparison.OrdinalIgnoreCase);

        private static bool IsSupportedStringExpressionActivity(XElement element) => element.Name.LocalName.Equals("ReplaceString", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("Substring", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("Trim", StringComparison.OrdinalIgnoreCase);

        private static bool IsSupportedNestedExpressionActivity(XElement element) => element.Name.LocalName.Equals("LookupWorkflowContextProperty", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("GetCurrentListId", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("GetCurrentItemGuid", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("LookupSPListItemStringProperty", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("LookupSPListItemInt32Property", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("LookupSPListItemIntProperty", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("LookupSPListItemGuid", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("FormatString", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("ToString", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("Cast", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("ParseDate", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("ConvertTimeZoneFromSPLocalToUtc", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("ParseDynamicValue", StringComparison.OrdinalIgnoreCase);

        private static ExpressionYaml ReadActivityPropertyExpression(XElement activity, string propertyName) => ReadOptionalActivityPropertyExpression(activity, propertyName) ?? new ExpressionYaml { Literal = "<exported expression>" };

        private static ExpressionYaml? ReadOptionalActivityPropertyExpression(XElement activity, string propertyName)
        {
            var attribute = activity.Attribute(propertyName);
            if (attribute != null) return IsXamlNull(attribute.Value) ? null : ReadExpressionText(attribute.Value);
            var propertyElement = activity.Element(activity.Name.Namespace + (activity.Name.LocalName + "." + propertyName)) ?? activity.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(activity.Name.LocalName + "." + propertyName, StringComparison.OrdinalIgnoreCase));
            if (propertyElement == null) return null;
            return ReadExpressionElementOrPlaceholder(propertyElement);
        }

        private static bool IsXamlNull(string text) => string.Equals((text ?? string.Empty).Trim(), "{x:Null}", StringComparison.OrdinalIgnoreCase);

        private static ExpressionYaml ReadNestedExpressionActivity(XElement activity)
        {
            if (activity.Name.LocalName.Equals("LookupWorkflowContextProperty", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "lookupWorkflowContext", PropertyName = ReadActivityStringProperty(activity, "PropertyName") };
            if (activity.Name.LocalName.Equals("GetCurrentListId", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "getCurrentListId" };
            if (activity.Name.LocalName.Equals("GetCurrentItemGuid", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "getCurrentItemGuid" };
            if (activity.Name.LocalName.Equals("LookupSPListItemStringProperty", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "lookupListItemStringProperty", ListId = ReadActivityPropertyExpression(activity, "ListId"), ItemId = ReadActivityPropertyExpression(activity, "ItemId"), ItemGuid = ReadOptionalActivityPropertyExpression(activity, "ItemGuid") ?? new ExpressionYaml(), FieldName = ReadActivityStringProperty(activity, "FieldName"), PropertyName = ReadActivityStringProperty(activity, "PropertyName") };
            if (activity.Name.LocalName.Equals("LookupSPListItemInt32Property", StringComparison.OrdinalIgnoreCase) || activity.Name.LocalName.Equals("LookupSPListItemIntProperty", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "lookupListItemIntProperty", ListId = ReadActivityPropertyExpression(activity, "ListId"), ItemId = ReadOptionalActivityPropertyExpression(activity, "ItemId") ?? new ExpressionYaml(), ItemGuid = ReadOptionalActivityPropertyExpression(activity, "ItemGuid") ?? new ExpressionYaml(), FieldName = ReadActivityStringProperty(activity, "FieldName"), PropertyName = ReadActivityStringProperty(activity, "PropertyName"), ValueType = "Int32" };
            if (activity.Name.LocalName.Equals("LookupSPListItemGuid", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "lookupListItemGuid", ListId = ReadActivityPropertyExpression(activity, "ListId"), ItemId = ReadOptionalActivityPropertyExpression(activity, "ItemId") ?? new ExpressionYaml(), FieldName = ReadActivityStringProperty(activity, "FieldName"), PropertyName = ReadActivityStringProperty(activity, "PropertyName"), Value = ReadOptionalActivityPropertyExpression(activity, "PropertyValue"), ValueType = "Guid" };
            if (activity.Name.LocalName.Equals("FormatString", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "formatString", Literal = ReadActivityStringProperty(activity, "Format"), Values = ReadFormatStringArguments(activity), ValueType = "String" };
            if (activity.Name.LocalName.Equals("ToString", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { ToString = ReadActivityPropertyExpression(activity, "Object") };
            if (activity.Name.LocalName.Equals("Cast", StringComparison.OrdinalIgnoreCase)) return ReadCastOperandExpression(activity);
            if (activity.Name.LocalName.Equals("ConvertTimeZoneFromSPLocalToUtc", StringComparison.OrdinalIgnoreCase)) return ReadActivityPropertyExpression(activity, "Input");
            if (activity.Name.LocalName.Equals("ParseDate", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "parseDate", Value = ReadActivityPropertyExpression(activity, "Value"), ValueType = "DateTime", DesignerId = ReadDesignerId(activity) };
            if (activity.Name.LocalName.Equals("ParseDynamicValue", StringComparison.OrdinalIgnoreCase)) return new ExpressionYaml { Type = "parseDynamicValue", Value = ReadActivityPropertyExpression(activity, "Json"), ValueType = "DynamicValue", DesignerId = ReadDesignerId(activity) };
            return new ExpressionYaml { Literal = "<exported expression>" };
        }

        private static string ReadDesignerId(XElement activity)
        {
            return activity.Elements().FirstOrDefault(e => e.Name.LocalName == "SPDesignerXamlWriter.CustomAttributes")
                ?.Descendants().FirstOrDefault(e => e.Name.LocalName == "String" && string.Equals((string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")), "Id", StringComparison.OrdinalIgnoreCase))
                ?.Value ?? string.Empty;
        }

        private static ExpressionYaml ReadCastOperandExpression(XElement activity)
        {
            var attribute = activity.Attribute("Operand");
            if (attribute != null) return new ExpressionYaml { Literal = WebUtility.HtmlDecode(attribute.Value ?? string.Empty) };
            return ReadActivityPropertyExpression(activity, "Operand");
        }

        private static List<ExpressionYaml> ReadFormatStringArguments(XElement activity)
        {
            var argumentsElement = activity.Element(activity.Name.Namespace + (activity.Name.LocalName + ".Arguments")) ?? activity.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(activity.Name.LocalName + ".Arguments", StringComparison.OrdinalIgnoreCase));
            return (argumentsElement?.Elements().Where(e => e.Name.LocalName == "InArgument").Select(ReadExpressionElementOrPlaceholder).ToList()) ?? new List<ExpressionYaml>();
        }

        private static ExpressionYaml ReadActivityStringPropertyExpression(XElement activity, string propertyName) => new ExpressionYaml { Literal = ReadActivityStringProperty(activity, propertyName) };

        private static string ReadActivityStringProperty(XElement activity, string propertyName)
        {
            var attribute = activity.Attribute(propertyName);
            if (attribute != null) return WebUtility.HtmlDecode(attribute.Value ?? string.Empty);
            var expression = ReadOptionalActivityPropertyExpression(activity, propertyName);
            return Convert.ToString(expression?.Literal ?? string.Empty) ?? string.Empty;
        }

        private static string NormalizeComparisonType(string name) => name.IndexOf("LessThanOrEqual", StringComparison.OrdinalIgnoreCase) >= 0 ? "isLessThanOrEqual" : name.IndexOf("GreaterThanOrEqual", StringComparison.OrdinalIgnoreCase) >= 0 ? "isGreaterThanOrEqual" : name.IndexOf("LessThan", StringComparison.OrdinalIgnoreCase) >= 0 ? "isLessThan" : name.IndexOf("GreaterThan", StringComparison.OrdinalIgnoreCase) >= 0 ? "isGreaterThan" : "isEqual";

        private static string ReadFirstQuotedArgument(string text)
        {
            var first = text.IndexOf('"');
            var last = text.IndexOf('"', first + 1);
            return first >= 0 && last > first ? text.Substring(first + 1, last - first - 1) : string.Empty;
        }

        private static bool IsIdentifier(string text) => !string.IsNullOrWhiteSpace(text) && text.All(c => char.IsLetterOrDigit(c) || c == '_') && !char.IsDigit(text[0]);

        private static string ReadStringAttributeOrPlaceholder(XElement element, string attributeName) => (string?)element.Attribute(attributeName) ?? "<exported expression>";

        private static string ReadOutArgumentTargetOrPlaceholder(XElement element, string propertyName) => ReadOutArgumentTarget(element, propertyName) ?? "<exported>";

        private static string ReadOutArgumentTarget(XElement element, string propertyName)
        {
            var propertyElement = element.Element(element.Name.Namespace + (element.Name.LocalName + "." + propertyName));
            return propertyElement?.Descendants().FirstOrDefault(e => e.Name.LocalName == "ArgumentReference")?.Attribute("ArgumentName")?.Value ?? string.Empty;
        }

        private static string ReadInOutArgumentTarget(XElement element, string propertyName)
        {
            var propertyElement = element.Element(element.Name.Namespace + (element.Name.LocalName + "." + propertyName)) ?? element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(element.Name.LocalName + "." + propertyName, StringComparison.OrdinalIgnoreCase));
            return propertyElement?.Descendants().FirstOrDefault(e => e.Name.LocalName == "ArgumentReference")?.Attribute("ArgumentName")?.Value ?? string.Empty;
        }

        private static string ReadArgumentName(XElement element, string propertyName)
        {
            var propertyElement = element.Element(element.Name.Namespace + (element.Name.LocalName + "." + propertyName)) ?? element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(element.Name.LocalName + "." + propertyName, StringComparison.OrdinalIgnoreCase));
            return propertyElement?.Descendants().FirstOrDefault(e => e.Name.LocalName == "ArgumentValue" || e.Name.LocalName == "ArgumentReference")?.Attribute("ArgumentName")?.Value ?? string.Empty;
        }

        private static string ReadXamlTypeArgument(XElement element) => (string?)element.Attribute(XamlNamespace + "TypeArguments") ?? string.Empty;

        private static string MapXamlTypeArgumentToVariableType(string typeArgument)
        {
            if (typeArgument.IndexOf("DynamicValue", StringComparison.OrdinalIgnoreCase) >= 0) return "DynamicValue";
            if (typeArgument.IndexOf("Boolean", StringComparison.OrdinalIgnoreCase) >= 0) return "Boolean";
            if (typeArgument.IndexOf("Int32", StringComparison.OrdinalIgnoreCase) >= 0) return "Int32";
            if (typeArgument.IndexOf("Double", StringComparison.OrdinalIgnoreCase) >= 0) return "Double";
            if (typeArgument.IndexOf("DateTime", StringComparison.OrdinalIgnoreCase) >= 0) return "DateTime";
            if (typeArgument.IndexOf("Guid", StringComparison.OrdinalIgnoreCase) >= 0) return "Guid";
            return "String";
        }

        private static Dictionary<string, ExpressionYaml> ReadListItemProperties(XElement element)
        {
            var fields = new Dictionary<string, ExpressionYaml>(StringComparer.OrdinalIgnoreCase);
            var listProperties = element.Element(element.Name.Namespace + (element.Name.LocalName + ".ListItemProperties")) ?? element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(element.Name.LocalName + ".ListItemProperties", StringComparison.OrdinalIgnoreCase));
            foreach (var value in (listProperties ?? element).Descendants().Where(e => e.Name.LocalName == "InArgument" && e.Attribute(XamlNamespace + "Key") != null))
            {
                var key = ((string?)value.Attribute(XamlNamespace + "Key")) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(key) && !fields.ContainsKey(key)) fields[key] = ReadExpressionElementOrPlaceholder(value);
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
