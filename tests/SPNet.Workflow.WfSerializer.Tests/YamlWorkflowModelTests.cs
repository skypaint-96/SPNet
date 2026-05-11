using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using YamlDotNet.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SPNet.Workflow.WfSerializer;

namespace SPNet.Workflow.WfSerializer.Tests
{
    [TestClass]
    public sealed class YamlWorkflowModelTests
    {
        [DataTestMethod]
        [DataRow("assign", typeof(AssignActionYaml))]
        [DataRow("setVariable", typeof(AssignActionYaml))]
        [DataRow("callHttpWebService", typeof(CallHttpWebServiceActionYaml))]
        [DataRow("callHttp", typeof(CallHttpWebServiceActionYaml))]
        [DataRow("http", typeof(CallHttpWebServiceActionYaml))]
        [DataRow("sendEmail", typeof(SendEmailActionYaml))]
        [DataRow("email", typeof(SendEmailActionYaml))]
        [DataRow("singleTask", typeof(SingleTaskActionYaml))]
        [DataRow("task", typeof(SingleTaskActionYaml))]
        [DataRow("while", typeof(WhileActionYaml))]
        [DataRow("loop", typeof(WhileActionYaml))]
        [DataRow("lookupRestPropertyName", typeof(LookupRestPropertyNameActionYaml))]
        [DataRow("lookupSPListItemPropertyNameInREST", typeof(LookupRestPropertyNameActionYaml))]
        [DataRow("getDynamicValueProperty", typeof(GetDynamicValuePropertyActionYaml))]
        [DataRow("getDictionaryItem", typeof(GetDynamicValuePropertyActionYaml))]
        [DataRow("getDictionaryValue", typeof(GetDynamicValuePropertyActionYaml))]
        [DataRow("getResponseProperty", typeof(GetDynamicValuePropertyActionYaml))]
        [DataRow("setDynamicValueProperty", typeof(SetDynamicValuePropertyActionYaml))]
        [DataRow("setDictionaryItem", typeof(SetDynamicValuePropertyActionYaml))]
        [DataRow("setDictionaryValue", typeof(SetDynamicValuePropertyActionYaml))]
        [DataRow("setResponseProperty", typeof(SetDynamicValuePropertyActionYaml))]
        [DataRow("buildDynamicValue", typeof(BuildDynamicValueActionYaml))]
        [DataRow("buildDictionary", typeof(BuildDynamicValueActionYaml))]
        [DataRow("createDictionary", typeof(BuildDynamicValueActionYaml))]
        [DataRow("createListItem", typeof(CreateListItemActionYaml))]
        [DataRow("updateListItem", typeof(UpdateListItemActionYaml))]
        [DataRow("deleteListItem", typeof(DeleteListItemActionYaml))]
        [DataRow("replaceString", typeof(StringReplaceActionYaml))]
        [DataRow("stringReplace", typeof(StringReplaceActionYaml))]
        [DataRow("substring", typeof(StringSubstringActionYaml))]
        [DataRow("substringString", typeof(StringSubstringActionYaml))]
        [DataRow("trimString", typeof(StringTrimActionYaml))]
        [DataRow("stringTrim", typeof(StringTrimActionYaml))]
        [DataRow("buildUri", typeof(BuildUriActionYaml))]
        [DataRow("getConfigurationValue", typeof(GetConfigurationValueActionYaml))]
        [DataRow("getInstanceAddress", typeof(GetInstanceAddressActionYaml))]
        [DataRow("setUserStatus", typeof(SetUserStatusActionYaml))]
        [DataRow("createTimeSpan", typeof(CreateTimeSpanActionYaml))]
        [DataRow("getTimeSpanFields", typeof(GetTimeSpanFieldsActionYaml))]
        [DataRow("addToDate", typeof(AddToDateActionYaml))]
        [DataRow("subtractFromDate", typeof(SubtractFromDateActionYaml))]
        [DataRow("dateInRange", typeof(DateInRangeActionYaml))]
        public void Load_RecognizesSupportedActionAliases(string actionType, Type expectedModelType)
        {
            var workflow = LoadYaml(CreateWorkflowYaml(CreateActionYaml(actionType)));

            Assert.AreEqual(1, workflow.Stages[0].Actions.Count);
            Assert.IsInstanceOfType(workflow.Stages[0].Actions[0], expectedModelType);
        }

        [DataTestMethod]
        [DataRow(typeof(CalcActionYaml))]
        [DataRow(typeof(WriteHistoryActionYaml))]
        [DataRow(typeof(AssignActionYaml))]
        [DataRow(typeof(SetFieldActionYaml))]
        [DataRow(typeof(CallHttpWebServiceActionYaml))]
        [DataRow(typeof(SendEmailActionYaml))]
        [DataRow(typeof(SingleTaskActionYaml))]
        [DataRow(typeof(StringReplaceActionYaml))]
        [DataRow(typeof(StringSubstringActionYaml))]
        [DataRow(typeof(StringTrimActionYaml))]
        [DataRow(typeof(BuildUriActionYaml))]
        [DataRow(typeof(GetConfigurationValueActionYaml))]
        [DataRow(typeof(GetInstanceAddressActionYaml))]
        [DataRow(typeof(SetUserStatusActionYaml))]
        [DataRow(typeof(CreateTimeSpanActionYaml))]
        [DataRow(typeof(GetTimeSpanFieldsActionYaml))]
        [DataRow(typeof(AddToDateActionYaml))]
        [DataRow(typeof(SubtractFromDateActionYaml))]
        [DataRow(typeof(DateInRangeActionYaml))]
        [DataRow(typeof(BuildDynamicValueActionYaml))]
        [DataRow(typeof(SetDynamicValuePropertyActionYaml))]
        [DataRow(typeof(WhileActionYaml))]
        [DataRow(typeof(IfActionYaml))]
        public void BuildDispatch_RegistersRepresentativeSupportedActions(Type actionModelType)
        {
            Assert.IsTrue(WfActivityBuilderSerializer.CanBuildActionForTest(actionModelType), actionModelType.Name + " should be registered for build dispatch.");
        }

        [DataTestMethod]
        [DataRow("lookupWorkflowContext")]
        [DataRow("lookupContextProperty")]
        [DataRow("getCurrentListId")]
        [DataRow("getCurrentItemGuid")]
        [DataRow("lookupListItemStringProperty")]
        [DataRow("lookupSPListItemStringProperty")]
        [DataRow("lookupListItemIntProperty")]
        [DataRow("lookupSPListItemIntProperty")]
        public void Load_RejectsUnsafeTopLevelLookupActions(string actionType)
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() => LoadYaml(CreateWorkflowYaml(CreateActionYaml(actionType))));

            StringAssert.Contains(ex.Message, "top-level action");
        }

        [TestMethod]
        public void Load_AllowsNestedSafeLookupExpressionsInAssignments()
        {
            var workflow = LoadYaml(CreateWorkflowYaml(@"
      - type: assign
        to: currentWebUrl
        value:
          type: lookupWorkflowContext
          propertyName: CurrentWebUrl
      - type: assign
        to: currentListId
        value:
          type: getCurrentListId
      - type: assign
        to: currentItemGuid
        value:
          type: getCurrentItemGuid
      - type: assign
        to: readBackTitle
        value:
          type: lookupListItemStringProperty
          listId:
            type: getCurrentListId
          itemId:
            literal: 1
          fieldName: Title"));

            Assert.AreEqual(4, workflow.Stages[0].Actions.Count);
            Assert.IsTrue(workflow.Stages[0].Actions.TrueForAll(a => a is AssignActionYaml));
        }

        [TestMethod]
        public void Load_AllowsDevOnlyMicrosoftActivitiesExpressionsInAssignments()
        {
            var workflow = LoadYaml(@"schemaVersion: spnet.workflow/v1
name: DevOnlyExpressions
variables:
  - name: textValue
    type: String
  - name: lengthValue
    type: Int32
  - name: stringBoolValue
    type: Boolean
  - name: dateValue
    type: DateTime
  - name: guidValue
    type: Guid
  - name: boolValue
    type: Boolean
  - name: payload
    type: DynamicValue
stages:
  - name: Stage 1
    actions:
      - type: assign
        to: textValue
        value:
          type: concatString
          values:
            - value A
            -
              type: toUpperCase
              value: value b
      - type: assign
        to: lengthValue
        value:
          type: stringLength
          value:
            type: toLowerCase
            value: ABC
      - type: assign
        to: textValue
        value:
          type: replaceString
          value:
            variable: textValue
          oldValue: A
          newValue: B
      - type: assign
        to: textValue
        value:
          type: substring
          value:
            variable: textValue
          startIndex: 0
          length: 2
      - type: assign
        to: textValue
        value:
          type: trimString
          value: '  spaced  '
      - type: assign
        to: lengthValue
        value:
          type: indexOfString
          value:
            variable: textValue
          searchValue: B
      - type: assign
        to: stringBoolValue
        value:
          type: isEmptyString
          value:
            variable: textValue
      - type: assign
        to: stringBoolValue
        value:
          type: containsString
          value:
            variable: textValue
          searchValue: B
      - type: assign
        to: stringBoolValue
        value:
          type: startsWithString
          value:
            variable: textValue
          searchValue: B
      - type: assign
        to: stringBoolValue
        value:
          type: endsWithString
          value:
            variable: textValue
          searchValue: B
      - type: assign
        to: stringBoolValue
        value:
          type: parseBoolean
          value: true
      - type: assign
        to: dateValue
        value:
          type: currentDate
      - type: assign
        to: guidValue
        value:
          type: parseGuid
          value: 00000000-0000-0000-0000-000000000001
      - type: assign
        to: guidValue
        value:
          type: newGuid
      - type: assign
        to: boolValue
        value:
          type: containsDynamicValueProperty
          source:
            variable: payload
          propertyName: Title
      - type: assign
        to: boolValue
        value:
          type: isEmptyDynamicValue
          source:
            variable: payload
");

            var assignments = workflow.Stages.Single().Actions.OfType<AssignActionYaml>().ToList();
            Assert.AreEqual(16, assignments.Count);
            Assert.AreEqual("concatString", assignments[0].Value.Type);
            Assert.AreEqual("toUpperCase", assignments[0].Value.Values[1].Type);
            Assert.AreEqual("stringLength", assignments[1].Value.Type);
            Assert.AreEqual("toLowerCase", assignments[1].Value.Value!.Type);
            Assert.AreEqual("replaceString", assignments[2].Value.Type);
            Assert.AreEqual("A", assignments[2].Value.OldValue!.Literal);
            Assert.AreEqual("substring", assignments[3].Value.Type);
            Assert.AreEqual("trimString", assignments[4].Value.Type);
            Assert.AreEqual("indexOfString", assignments[5].Value.Type);
            Assert.AreEqual("isEmptyString", assignments[6].Value.Type);
            Assert.AreEqual("containsString", assignments[7].Value.Type);
            Assert.AreEqual("startsWithString", assignments[8].Value.Type);
            Assert.AreEqual("endsWithString", assignments[9].Value.Type);
            Assert.AreEqual("parseBoolean", assignments[10].Value.Type);
            Assert.AreEqual("currentDate", assignments[11].Value.Type);
            Assert.AreEqual("parseGuid", assignments[12].Value.Type);
            Assert.AreEqual("newGuid", assignments[13].Value.Type);
            Assert.AreEqual("containsDynamicValueProperty", assignments[14].Value.Type);
            Assert.AreEqual("payload", assignments[14].Value.Source!.Variable);
            Assert.AreEqual("isEmptyDynamicValue", assignments[15].Value.Type);
        }

        [DataTestMethod]
        [DataRow("assign", "assign action requires 'to'.")]
        [DataRow("calc", "calc action requires 'to'.")]
        [DataRow("setField", "setField action requires 'fieldName'.")]
        [DataRow("callHttpWebService", "requires at least one response target")]
        [DataRow("sendEmail", "sendEmail action requires 'to'.")]
        [DataRow("singleTask", "singleTask action requires 'assignedTo'.")]
        [DataRow("getDynamicValueProperty", "getDynamicValueProperty action requires 'source'.")]
        [DataRow("setDynamicValueProperty", "setDynamicValueProperty action requires 'source'.")]
        [DataRow("buildDynamicValue", "buildDynamicValue action requires 'to'.")]
        [DataRow("lookupRestPropertyName", "lookupRestPropertyName action requires 'to'.")]
        [DataRow("replaceString", "replaceString action requires 'to'.")]
        [DataRow("substring", "substring action requires 'to'.")]
        [DataRow("trimString", "trimString action requires 'to'.")]
        [DataRow("buildUri", "buildUri action requires 'to'.")]
        [DataRow("getConfigurationValue", "getConfigurationValue action requires 'name'.")]
        [DataRow("getInstanceAddress", "getInstanceAddress action requires 'to'.")]
        [DataRow("createTimeSpan", "createTimeSpan action requires 'to'.")]
        [DataRow("dateInRange", "dateInRange action requires 'to'.")]
        public void Load_ValidatesRequiredActionFields(string actionType, string expectedMessage)
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() => LoadYaml(CreateWorkflowYaml("      - type: " + actionType)));

            StringAssert.Contains(ex.Message, expectedMessage);
        }

        [TestMethod]
        public void Load_RejectsUnsupportedActionType()
        {
            var ex = AssertThrowsWorkflowException(() => LoadYaml(CreateWorkflowYaml("      - type: recycleSiteCollection")));

            StringAssert.Contains(ex.Message, "Unsupported action type");
        }

        [TestMethod]
        public void Load_RejectsUnsupportedLiteralHttpMethod()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() => LoadYaml(CreateWorkflowYaml(@"
      - type: http
        address: https://example.invalid
        requestType: PATCH
        responseStatusCodeTo: statusCode")));

            StringAssert.Contains(ex.Message, "requestType must be GET");
        }

        [TestMethod]
        public void Load_AllowsDynamicValueVariablesAndBuildDynamicValueEntries()
        {
            var workflow = LoadYaml(@"schemaVersion: spnet.workflow/v1
name: DynamicArrayShape
variables:
  - name: requestPayload
    type: DynamicValue
  - name: title
    type: String
stages:
  - name: Stage 1
    actions:
      - type: buildDynamicValue
        to: requestPayload
        entries:
          - key: Title
            value:
              variable: title
          - key: Count
            value: 2
            valueType: Int32
");

            var action = workflow.Stages.Single().Actions.OfType<BuildDynamicValueActionYaml>().Single();
            Assert.AreEqual("requestPayload", action.To);
            Assert.AreEqual(2, action.Entries.Count);
            Assert.AreEqual("Count", action.Entries[1].Key);
            Assert.AreEqual("Int32", action.Entries[1].ValueType);
        }

        [TestMethod]
        public void Load_AllowsDevOnlyBatch1AndTimeSpanActions()
        {
            var workflow = LoadYaml(@"schemaVersion: spnet.workflow/v1
name: DevOnlyBatch1AndTimeSpan
variables:
  - name: uriResult
    type: String
  - name: configValue
    type: String
  - name: instanceAddress
    type: String
  - name: duration
    type: TimeSpan
  - name: dateValue
    type: DateTime
  - name: boolValue
    type: Boolean
  - name: days
    type: Int32
stages:
  - name: Stage 1
    actions:
      - type: buildUri
        scheme: https
        host: example.invalid
        path: /api/test
        to: uriResult
      - type: getConfigurationValue
        name: Microsoft.SharePoint.ActivationProperties.CultureName
        defaultValue: en-US
        to: configValue
      - type: getInstanceAddress
        to: instanceAddress
      - type: setUserStatus
        description: Dev-only status
      - type: createTimeSpan
        days: 1
        hours: 2
        to: duration
      - type: getTimeSpanFields
        input:
          variable: duration
        daysTo: days
      - type: addToDate
        input: 2026-05-10T00:00:00Z
        timeSpan:
          variable: duration
        to: dateValue
      - type: subtractFromDate
        input:
          variable: dateValue
        days: 1
        to: dateValue
      - type: dateInRange
        input:
          variable: dateValue
        start: 2026-05-01T00:00:00Z
        end: 2026-05-31T00:00:00Z
        to: boolValue
");

            var actions = workflow.Stages.Single().Actions;
            Assert.IsTrue(actions.OfType<BuildUriActionYaml>().Any());
            Assert.IsTrue(actions.OfType<GetConfigurationValueActionYaml>().Any());
            Assert.IsTrue(actions.OfType<GetInstanceAddressActionYaml>().Any());
            Assert.IsTrue(actions.OfType<SetUserStatusActionYaml>().Any());
            Assert.IsTrue(actions.OfType<CreateTimeSpanActionYaml>().Any());
            Assert.IsTrue(actions.OfType<GetTimeSpanFieldsActionYaml>().Any());
            Assert.IsTrue(actions.OfType<AddToDateActionYaml>().Any());
            Assert.IsTrue(actions.OfType<SubtractFromDateActionYaml>().Any());
            Assert.IsTrue(actions.OfType<DateInRangeActionYaml>().Any());
        }

        [TestMethod]
        public void Load_ValidatesBuildDynamicValueEntries()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() => LoadYaml(CreateWorkflowYaml(@"
      - type: buildDynamicValue
        to: responseContent
        entries:
          - value: missing key")));

            StringAssert.Contains(ex.Message, "entries require non-empty keys");
        }

        [TestMethod]
        public void ExportWorkflowYaml_RecognizesReferenceBackedLifecycleActions()
        {
            var inputPath = Path.Combine("artifacts", "SPNetYamlListLifecycleManual.pretty.xaml");
            if (!File.Exists(inputPath)) Assert.Inconclusive("Reference lifecycle XAML artifact is not available in this checkout.");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var actions = workflow.Stages.SelectMany(s => s.Actions).ToList();

                Assert.IsTrue(actions.Any(a => a is CreateListItemActionYaml), "Expected exported createListItem action.");
                Assert.IsTrue(actions.Any(a => a is UpdateListItemActionYaml), "Expected exported updateListItem action.");
                Assert.IsTrue(actions.Any(a => a is WriteHistoryActionYaml), "Expected exported writeHistory action.");
                Assert.IsTrue(workflow.ExportWarnings.Any(w => w.Contains("Partial structural export")), "Expected partial export warning.");
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_RecognizesReferenceBackedDeleteAction()
        {
            var inputPath = Path.Combine("artifacts", "diagnostics-TestListExample2", "TestListExample2.pretty.xaml");
            if (!File.Exists(inputPath)) Assert.Inconclusive("Reference diagnostics XAML artifact is not available in this checkout.");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);

                Assert.IsTrue(workflow.Stages.SelectMany(s => s.Actions).Any(a => a is DeleteListItemActionYaml), "Expected exported deleteListItem action.");
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_EmitsFaithfulExpressionsForTestWFAutoStartListActions()
        {
            var inputPath = FindRepoFile("artifacts", "diagnostics-TestListExample2", "TestListExample2.pretty.xaml");
            if (!File.Exists(inputPath)) Assert.Inconclusive("Reference diagnostics XAML artifact is not available in this checkout.");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var actions = workflow.Stages.SelectMany(s => s.Actions).ToList();
                var create = actions.OfType<CreateListItemActionYaml>().Single();
                var targeted = actions.OfType<TargetedListItemActionYaml>().ToList();
                var updates = actions.OfType<UpdateListItemActionYaml>().ToList();
                var firstUpdateTitle = updates[0].Fields["Title"];

                Assert.IsTrue(create.Fields.ContainsKey("TestWFAutoStart"), "Expected exported createListItem fields to include TestWFAutoStart.");
                Assert.AreEqual("rgvedrfgr", create.Fields["TestWFAutoStart"].Literal, "Expected exported field values to preserve real literals.");
                Assert.AreEqual("tielsdg", create.Fields["Title"].Literal, "Expected exported create field values to preserve real literals.");
                Assert.AreEqual("getCurrentListId", create.ListId.Type, "Expected createListItem listId to preserve current-list lookup expression.");
                Assert.IsTrue(targeted.Count >= 1, "Expected at least one exported targeted list-item action.");
                Assert.IsTrue(targeted.All(a => string.Equals("getCurrentListId", a.ListId.Type, StringComparison.OrdinalIgnoreCase)), "Expected targeted list actions to preserve current-list expressions.");
                Assert.IsTrue(targeted.All(a => a.ItemId.Literal == null), "Expected targeted list actions not to synthesize itemId: 1 when real identity expressions are present.");
                Assert.IsTrue(targeted.Any(a => string.Equals("getCurrentItemGuid", a.ItemGuid.Type, StringComparison.OrdinalIgnoreCase)), "Expected targeted list actions to preserve current-item Guid identity expressions.");
                Assert.AreEqual("formatString", firstUpdateTitle.Type, "Expected nested formatted string field value to be exported.");
                Assert.AreEqual("custom string being built and lookup {0}", firstUpdateTitle.Literal);
                Assert.AreEqual(1, firstUpdateTitle.Values.Count);
                Assert.AreEqual("lookupListItemIntProperty", firstUpdateTitle.Values[0].ToString.Type, "Expected nested list item integer lookup to be exported through ToString.");
                Assert.AreEqual("ID", firstUpdateTitle.Values[0].ToString.PropertyName);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_SupportsDownloadedDynamicArrayWorkflowShape()
        {
            var inputPath = FindRepoFile("artifacts", "DynamicArrayWFEx.downloaded.xaml");
            if (!File.Exists(inputPath)) Assert.Inconclusive("Downloaded DynamicArrayWFEx XAML artifact is not available in this checkout.");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var actions = workflow.Stages.SelectMany(s => s.Actions).ToList();

                Assert.AreEqual("DynamicArrayWFEx", workflow.Name);
                Assert.IsTrue(workflow.Variables.Any(v => v.Name == "varRequestHeaders" && v.Type == "DynamicValue"), "Expected DynamicValue request-headers variable.");
                Assert.AreEqual(0, workflow.Parameters.Count, "Downloaded DynamicArrayWFEx has no FormField metadata, so x:Members should not be promoted to parameters.");
                Assert.IsTrue(workflow.Variables.Any(v => v.Name == "dictionary" && v.Type == "DynamicValue"), "Expected DynamicValue dictionary runtime variable.");
                Assert.IsTrue(workflow.Variables.Any(v => v.Name == "varResults" && v.Type == "DynamicValue"), "Expected DynamicValue response array variable.");
                Assert.IsTrue(actions.OfType<CallHttpWebServiceActionYaml>().Any(), "Expected exported HTTP action.");
                Assert.IsTrue(actions.OfType<GetDynamicValuePropertyActionYaml>().Any(a => string.Equals(a.To, "varResults", StringComparison.OrdinalIgnoreCase) && string.Equals(a.ValueType, "DynamicValue", StringComparison.OrdinalIgnoreCase)), "Expected exported DynamicValue array extraction.");
                Assert.IsTrue(actions.OfType<CountDynamicValueItemsActionYaml>().Any(a => string.Equals(a.Source, "varResults", StringComparison.OrdinalIgnoreCase) && string.Equals(a.To, "count", StringComparison.OrdinalIgnoreCase)), "Expected exported DynamicValue array item count.");
                var loop = actions.OfType<WhileActionYaml>().Single(w => string.Equals(w.Condition.Type, "isLessThan", StringComparison.OrdinalIgnoreCase));
                Assert.AreEqual(4, loop.Actions.Count, "Expected exported array loop body actions.");
                CollectionAssert.AreEquivalent(new[] { "varNumericProp", "varBoolprop", "varIntProp", "varDateProp" }, loop.Actions.OfType<GetDynamicValuePropertyActionYaml>().Select(a => a.To).ToArray(), "Expected DynamicArrayWFEx loop body dynamic property reads.");
                Assert.IsFalse(workflow.ExportWarnings.Any(w => w.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0), "DynamicArrayWFEx export should not report unsupported constructs.");
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_ReexportsDynamicArrayRoundtripLoopBodyActions()
        {
            var inputPath = FindRepoFile("artifacts", "DynamicArrayWFEx.roundtrip.xaml");
            if (!File.Exists(inputPath)) Assert.Inconclusive("Roundtrip DynamicArrayWFEx XAML artifact is not available in this checkout.");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-dynamic-array-reexport-" + Guid.NewGuid().ToString("N") + ".yml");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var loop = workflow.Stages.SelectMany(s => s.Actions).OfType<WhileActionYaml>().Single();

                Assert.IsTrue(workflow.Variables.Any(v => v.Name == "varNumericProp" && v.Type == "Double"), "Expected numeric property to remain a workflow variable.");
                Assert.IsTrue(workflow.Variables.Any(v => v.Name == "varBoolprop" && v.Type == "Boolean"), "Expected boolean property to remain a workflow variable.");
                Assert.IsFalse(workflow.Parameters.Any(p => p.Name == "varNumericProp" || p.Name == "varBoolprop" || p.Name == "varDateProp"), "Loop body result variables must not be exported as initiation parameters.");
                Assert.AreEqual(4, loop.Actions.Count, "Expected roundtrip re-export to preserve loop body actions.");
                CollectionAssert.AreEquivalent(new[] { "varNumericProp", "varBoolprop", "varIntProp", "varDateProp" }, loop.Actions.OfType<GetDynamicValuePropertyActionYaml>().Select(a => a.To).ToArray(), "Expected DynamicArrayWFEx loop body dynamic property reads to survive re-export.");
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void PrepareXamlForDeserialization_MapsGenericMicrosoftExpressionComparisons()
        {
            var prepared = WfActivityBuilderSerializer.PrepareXamlForDeserializationForTest(@"<Activity x:Class=""ComparisonWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:p=""http://schemas.microsoft.com/workflow/2012/07/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""><While><While.Condition><p:IsLessThan x:TypeArguments=""x:Double""><p:IsLessThan.Left><InArgument x:TypeArguments=""x:Double"">1</InArgument></p:IsLessThan.Left><p:IsLessThan.Right><InArgument x:TypeArguments=""x:Double""><p:Convert x:TypeArguments=""x:Int32, x:Double""><p:Convert.Input><InArgument x:TypeArguments=""x:Int32"">2</InArgument></p:Convert.Input></p:Convert></InArgument></p:IsLessThan.Right></p:IsLessThan></While.Condition></While></Activity>");
            var document = XDocument.Parse(prepared);
            XNamespace expressionProxy = "clr-namespace:Microsoft.Activities.Expressions;assembly=Microsoft.Activities.Proxy";

            Assert.IsTrue(document.Descendants(expressionProxy + "IsLessThan").Any(), "Expected IsLessThan to map to Microsoft.Activities.Expressions proxy namespace.");
            Assert.IsTrue(document.Descendants(expressionProxy + "Convert").Any(), "Expected nested Convert to map to Microsoft.Activities.Expressions proxy namespace.");
            Assert.IsTrue(document.Descendants(expressionProxy + "IsLessThan").Any(e => ((string?)e.Attribute(XName.Get("TypeArguments", "http://schemas.microsoft.com/winfx/2006/xaml"))) == "x:Double"), "Expected x:Double type arguments to be preserved for WF generic closure.");
        }

        [TestMethod]
        [TestCategory("WebsiteCacheIntegration")]
        public void InspectWorkflowXaml_DeserializesDownloadedDynamicArrayWorkflow()
        {
            var cacheFolder = GetConfiguredWebsiteCacheOrInconclusive();
            var inputPath = FindRepoFile("artifacts", "DynamicArrayWFEx.downloaded.xaml");
            if (!File.Exists(inputPath)) Assert.Inconclusive("Downloaded DynamicArrayWFEx XAML artifact is not available in this checkout.");

            var report = WfActivityBuilderSerializer.InspectWorkflowXaml(inputPath, cacheFolder);

            StringAssert.Contains(report, "LoadedType: System.Activities.ActivityBuilder");
            StringAssert.Contains(report, "Property: varIndex Type=System.Activities.InArgument`1[System.Double]");
            StringAssert.Contains(report, "Microsoft.Activities.GetDynamicValueProperty`1[[Microsoft.Activities.DynamicValue");
            Assert.IsFalse(report.Contains("WF deserialization failed"), report);
        }

        private static string FindRepoFile(params string[] relativeParts)
        {
            var current = new DirectoryInfo(Environment.CurrentDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(new[] { current.FullName }.Concat(relativeParts).ToArray());
                if (File.Exists(candidate)) return candidate;
                current = current.Parent;
            }

            return Path.Combine(relativeParts);
        }

        private static string ReadDesignerId(XElement element)
        {
            return element.Elements().FirstOrDefault(e => e.Name.LocalName == "SPDesignerXamlWriter.CustomAttributes")
                ?.Descendants().FirstOrDefault(e => e.Name.LocalName == "String" && string.Equals((string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")), "Id", StringComparison.OrdinalIgnoreCase))
                ?.Value ?? string.Empty;
        }

        [TestMethod]
        public void ExportWorkflowYaml_RecognizesSingleTaskAction()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), "spnet-single-task-" + Guid.NewGuid().ToString("N") + ".xaml");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(inputPath, @"<Activity x:Class=""TaskWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities""><Sequence DisplayName=""Task Stage""><local:SingleTask AssignedTo=""spnet-workflow-test@example.invalid"" Title=""Review request"" Body=""Please review"" /></Sequence></Activity>");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var task = workflow.Stages.SelectMany(s => s.Actions).OfType<SingleTaskActionYaml>().Single();

                Assert.AreEqual("spnet-workflow-test@example.invalid", task.AssignedTo.Literal);
                Assert.AreEqual("Review request", task.Title.Literal);
                Assert.AreEqual("Please review", task.Body.Literal);
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_RecognizesStringManipulationAssignActions()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), "spnet-string-actions-" + Guid.NewGuid().ToString("N") + ".xaml");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(inputPath, @"<Activity x:Class=""StringWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities"" xmlns:mva=""clr-namespace:Microsoft.VisualBasic.Activities;assembly=System.Activities""><Sequence DisplayName=""String Stage""><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""replaceResult"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><mva:VisualBasicValue x:TypeArguments=""x:String"" ExpressionText=""inputText.Replace(&amp;quot;a&amp;quot;, &amp;quot;b&amp;quot;)"" /></InArgument></Assign.Value></Assign><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""substringResult"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><mva:VisualBasicValue x:TypeArguments=""x:String"" ExpressionText=""inputText.Substring(1, 2)"" /></InArgument></Assign.Value></Assign><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""trimResult"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><mva:VisualBasicValue x:TypeArguments=""x:String"" ExpressionText=""inputText.Trim()"" /></InArgument></Assign.Value></Assign></Sequence></Activity>");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var actions = workflow.Stages.SelectMany(s => s.Actions).ToList();

                Assert.IsTrue(actions.OfType<StringReplaceActionYaml>().Any(a => a.To == "replaceResult"), "Expected exported replaceString action.");
                Assert.IsTrue(actions.OfType<StringSubstringActionYaml>().Any(a => a.To == "substringResult"), "Expected exported substring action.");
                Assert.IsTrue(actions.OfType<StringTrimActionYaml>().Any(a => a.To == "trimResult"), "Expected exported trimString action.");
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_RecognizesPlainAssignAndControlFlow()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), "spnet-control-flow-" + Guid.NewGuid().ToString("N") + ".xaml");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(inputPath, @"<Activity x:Class=""ControlWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities"" xmlns:mva=""clr-namespace:Microsoft.VisualBasic.Activities;assembly=System.Activities""><Sequence DisplayName=""Control Stage""><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""currentWebUrl"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><mva:VisualBasicValue x:TypeArguments=""x:String"" ExpressionText=""&amp;quot;hello&amp;quot;"" /></InArgument></Assign.Value></Assign><While><While.Condition><mva:VisualBasicValue x:TypeArguments=""x:Boolean"" ExpressionText=""unsupportedCondition"" /></While.Condition><While.Body><Sequence><local:WriteToHistory Message=""inside loop"" /></Sequence></While.Body></While><If><If.Condition><mva:VisualBasicValue x:TypeArguments=""x:Boolean"" ExpressionText=""unsupportedCondition"" /></If.Condition><If.Then><Sequence><local:WriteToHistory Message=""then branch"" /></Sequence></If.Then><If.Else><Sequence><local:WriteToHistory Message=""else branch"" /></Sequence></If.Else></If></Sequence></Activity>");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var actions = workflow.Stages.SelectMany(s => s.Actions).ToList();

                var assign = actions.OfType<AssignActionYaml>().Single();
                Assert.AreEqual("currentWebUrl", assign.To);
                Assert.AreEqual("hello", assign.Value?.Literal);
                Assert.IsTrue(actions.OfType<WhileActionYaml>().Single().Actions.OfType<WriteHistoryActionYaml>().Any(a => Convert.ToString(a.Message.Literal) == "inside loop"));
                var ifAction = actions.OfType<IfActionYaml>().Single();
                Assert.IsTrue(ifAction.Then.OfType<WriteHistoryActionYaml>().Any(a => Convert.ToString(a.Message.Literal) == "then branch"));
                Assert.IsTrue(ifAction.Else.OfType<WriteHistoryActionYaml>().Any(a => Convert.ToString(a.Message.Literal) == "else branch"));
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_ReconstructsGeneratedComparisonAndStringExpressionShapes()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), "spnet-generated-shapes-" + Guid.NewGuid().ToString("N") + ".xaml");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(inputPath, @"<Activity x:Class=""GeneratedShapes.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:p=""clr-namespace:Microsoft.Activities.Expressions;assembly=Microsoft.Activities.Proxy"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities""><Sequence DisplayName=""Generated Stage""><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""replaceResult"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><p:ReplaceString Input=""inputText"" Pattern=""old"" Replacement=""new"" /></InArgument></Assign.Value></Assign><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""substringResult"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><p:Substring Input=""inputText"" StartIndex=""1"" Length=""2"" /></InArgument></Assign.Value></Assign><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""trimResult"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><p:Trim Input=""inputText"" Characters="""" /></InArgument></Assign.Value></Assign><While><While.Condition><p:IsLessThan x:TypeArguments=""x:Double"" Left=""1"" Right=""2"" /></While.Condition><While.Body><Sequence><local:WriteToHistory Message=""inside generated loop"" /></Sequence></While.Body></While><If><If.Condition><p:IsGreaterThanOrEqual x:TypeArguments=""x:Double"" Left=""counter"" Right=""10"" /></If.Condition><If.Then><Sequence><local:WriteToHistory Message=""generated then"" /></Sequence></If.Then></If></Sequence></Activity>");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var actions = workflow.Stages.SelectMany(s => s.Actions).ToList();

                var replace = actions.OfType<StringReplaceActionYaml>().Single();
                Assert.AreEqual("inputText", replace.Text.Variable);
                Assert.AreEqual("old", replace.OldValue.Literal);
                Assert.AreEqual("new", replace.NewValue.Literal);
                var substring = actions.OfType<StringSubstringActionYaml>().Single();
                Assert.AreEqual("inputText", substring.Text.Variable);
                Assert.AreEqual(1d, Convert.ToDouble(substring.StartIndex.Literal));
                Assert.AreEqual(2d, Convert.ToDouble(substring.Length.Literal));
                Assert.AreEqual("inputText", actions.OfType<StringTrimActionYaml>().Single().Text.Variable);
                var whileAction = actions.OfType<WhileActionYaml>().Single();
                Assert.AreEqual("isLessThan", whileAction.Condition.Type);
                Assert.AreEqual(1d, Convert.ToDouble(whileAction.Condition.Left.Literal));
                Assert.AreEqual(2d, Convert.ToDouble(whileAction.Condition.Right.Literal));
                var ifAction = actions.OfType<IfActionYaml>().Single();
                Assert.AreEqual("isGreaterThanOrEqual", ifAction.Condition.Type);
                Assert.AreEqual("counter", ifAction.Condition.Left.Variable);
                Assert.AreEqual(10d, Convert.ToDouble(ifAction.Condition.Right.Literal));
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void Load_SupportsExampleConditionalsLogicalAndTypedConditions()
        {
            var workflow = LoadYaml(CreateWorkflowYaml(@"
      - type: if
        condition:
          type: and
          leftCondition:
            type: or
            leftCondition:
              type: isEqualString
              valueType: String
              left:
                variable: currentWebUrl
              right: dfgdf
            rightCondition:
              type: isEqual
              valueType: Boolean
              left:
                variable: approvedFlag
              right: true
          rightCondition:
            type: not
            operand:
              type: endsWithString
              valueType: String
              left:
                variable: currentWebUrl
              right: dfv
        then:
          - type: writeHistory
            message: condition matched
      - type: if
        condition:
          type: isGreaterThan
          valueType: DateTime
          left:
            variable: dueDate
          right: 2026-05-10T17:46:00Z
        then:
          - type: writeHistory
            message: date matched"));

            var first = workflow.Stages[0].Actions.OfType<IfActionYaml>().First();
            Assert.AreEqual("and", first.Condition.Type);
            Assert.AreEqual("or", first.Condition.LeftCondition.Type);
            Assert.AreEqual("isEqualString", first.Condition.LeftCondition.LeftCondition.Type);
            Assert.AreEqual("String", first.Condition.LeftCondition.LeftCondition.ValueType);
            Assert.AreEqual("not", first.Condition.RightCondition.Type);
            Assert.AreEqual("endsWithString", first.Condition.RightCondition.Operand.Type);
            var second = workflow.Stages[0].Actions.OfType<IfActionYaml>().Last();
            Assert.AreEqual("DateTime", second.Condition.ValueType);
        }

        [TestMethod]
        public void ExportWorkflowYaml_ReconstructsExampleConditionalsBooleanTree()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), "spnet-example-conditionals-" + Guid.NewGuid().ToString("N") + ".xaml");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(inputPath, @"<Activity x:Class=""ExampleConditionals.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:s=""clr-namespace:System;assembly=mscorlib"" xmlns:p=""http://schemas.microsoft.com/workflow/2012/07/xaml/activities"" xmlns:local1=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities.Expressions"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities""><Sequence DisplayName=""Stage 1""><If><If.Condition><InArgument x:TypeArguments=""x:Boolean""><p:And><p:And.Left><InArgument x:TypeArguments=""x:Boolean""><p:Or><p:Or.Left><InArgument x:TypeArguments=""x:Boolean""><p:IsEqualString Text=""dfgdf""><p:IsEqualString.Input><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:IsEqualString.Input></p:IsEqualString></InArgument></p:Or.Left><p:Or.Right><InArgument x:TypeArguments=""x:Boolean""><p:IsEqualBoolean Right=""True""><p:IsEqualBoolean.Left><InArgument x:TypeArguments=""x:Boolean""><ArgumentValue x:TypeArguments=""x:Boolean"" ArgumentName=""varboolex"" /></InArgument></p:IsEqualBoolean.Left></p:IsEqualBoolean></InArgument></p:Or.Right></p:Or></InArgument></p:And.Left><p:And.Right><InArgument x:TypeArguments=""x:Boolean""><p:Not><p:Not.Operand><InArgument x:TypeArguments=""x:Boolean""><p:EndsWithString SearchValue=""dfv""><p:EndsWithString.Input><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:EndsWithString.Input></p:EndsWithString></InArgument></p:Not.Operand></p:Not></InArgument></p:And.Right></p:And></InArgument></If.Condition><If.Then><Sequence DisplayName=""Then"" /></If.Then></If><If><If.Condition><InArgument x:TypeArguments=""x:Boolean""><local1:IsGreaterThanDateTime><local1:IsGreaterThanDateTime.Left><InArgument x:TypeArguments=""s:DateTime""><ArgumentValue x:TypeArguments=""s:DateTime"" ArgumentName=""vardateex"" /></InArgument></local1:IsGreaterThanDateTime.Left><local1:IsGreaterThanDateTime.Right><InArgument x:TypeArguments=""s:DateTime""><Literal x:TypeArguments=""s:DateTime"" Value=""2026-05-10T17:46Z"" /></InArgument></local1:IsGreaterThanDateTime.Right></local1:IsGreaterThanDateTime></InArgument></If.Condition><If.Then><Sequence DisplayName=""Then"" /></If.Then></If></Sequence></Activity>");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var actions = workflow.Stages.SelectMany(s => s.Actions).OfType<IfActionYaml>().ToList();

                Assert.AreEqual("and", actions[0].Condition.Type);
                Assert.AreEqual("or", actions[0].Condition.LeftCondition.Type);
                Assert.AreEqual("isEqualString", actions[0].Condition.LeftCondition.LeftCondition.Type);
                Assert.AreEqual("varstrex", actions[0].Condition.LeftCondition.LeftCondition.Left.Variable);
                Assert.AreEqual("dfgdf", actions[0].Condition.LeftCondition.LeftCondition.Right.Literal);
                Assert.AreEqual("Boolean", actions[0].Condition.LeftCondition.RightCondition.ValueType);
                Assert.AreEqual(true, Convert.ToBoolean(actions[0].Condition.LeftCondition.RightCondition.Right.Literal));
                Assert.AreEqual("not", actions[0].Condition.RightCondition.Type);
                Assert.AreEqual("endsWithString", actions[0].Condition.RightCondition.Operand.Type);
                Assert.AreEqual("DateTime", actions[1].Condition.ValueType);
                Assert.AreEqual("isGreaterThan", actions[1].Condition.Type);
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_ReconstructsRecognizableNestedLookupExpressionShapes()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), "spnet-nested-expression-shapes-" + Guid.NewGuid().ToString("N") + ".xaml");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-export-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(inputPath, @"<Activity x:Class=""NestedExpressions.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:p=""clr-namespace:Microsoft.Activities.Expressions;assembly=Microsoft.Activities.Proxy"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities""><Sequence DisplayName=""Nested Stage""><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""currentWebUrl"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><p:LookupWorkflowContextProperty PropertyName=""CurrentWebUrl"" /></InArgument></Assign.Value></Assign><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""readBackTitle"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><p:LookupSPListItemStringProperty FieldName=""Title"" ItemId=""1""><p:LookupSPListItemStringProperty.ListId><InArgument x:TypeArguments=""x:Guid""><p:GetCurrentListId /></InArgument></p:LookupSPListItemStringProperty.ListId></p:LookupSPListItemStringProperty></InArgument></Assign.Value></Assign></Sequence></Activity>");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);
                var assignments = workflow.Stages.SelectMany(s => s.Actions).OfType<AssignActionYaml>().ToList();

                var context = assignments.Single(a => a.To == "currentWebUrl").Value;
                Assert.AreEqual("lookupWorkflowContext", context.Type);
                Assert.AreEqual("CurrentWebUrl", context.PropertyName);
                var lookup = assignments.Single(a => a.To == "readBackTitle").Value;
                Assert.AreEqual("lookupListItemStringProperty", lookup.Type);
                Assert.AreEqual("getCurrentListId", lookup.ListId.Type);
                Assert.AreEqual(1d, Convert.ToDouble(lookup.ItemId.Literal));
                Assert.AreEqual("Title", lookup.FieldName);
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [DataTestMethod]
        [DataRow("VisualBasicValue")]
        [DataRow("VisualBasicReference")]
        [DataRow("CSharpValue")]
        [DataRow("CSharpReference")]
        public void AddSharePointDesignerMetadata_RejectsRawLanguageExpressionActivities(string expressionActivityName)
        {
            var xaml = @"<Activity x:Class=""RawExpressionWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:mva=""clr-namespace:Microsoft.VisualBasic.Activities;assembly=System.Activities"" xmlns:mca=""clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities""><Sequence><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""target"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><mva:" + expressionActivityName + @" x:TypeArguments=""x:String"" ExpressionText=""&amp;quot;blocked&amp;quot;"" /></InArgument></Assign.Value></Assign></Sequence></Activity>";

            var ex = Assert.ThrowsException<InvalidOperationException>(() => WfActivityBuilderSerializer.AddSharePointDesignerMetadata(xaml, "RawExpressionWorkflow"));

            StringAssert.Contains(ex.Message, expressionActivityName);
            StringAssert.Contains(ex.Message, "raw WF language expression");
        }

        [TestMethod]
        public void AddSharePointDesignerMetadata_AllowsStructuredMicrosoftActivitiesExpressions()
        {
            var xaml = @"<Activity x:Class=""StructuredExpressionWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:p=""clr-namespace:Microsoft.Activities.Expressions;assembly=Microsoft.Activities.Proxy""><Flowchart><FlowStep><Sequence DisplayName=""Stage 1""><Assign x:TypeArguments=""x:String""><Assign.To><OutArgument x:TypeArguments=""x:String""><ArgumentReference x:TypeArguments=""x:String"" ArgumentName=""target"" /></OutArgument></Assign.To><Assign.Value><InArgument x:TypeArguments=""x:String""><p:ReplaceString Input=""inputText"" Pattern=""old"" Replacement=""new"" /></InArgument></Assign.Value></Assign></Sequence></FlowStep></Flowchart></Activity>";

            var normalized = WfActivityBuilderSerializer.AddSharePointDesignerMetadata(xaml, "StructuredExpressionWorkflow");

            StringAssert.Contains(normalized, "ReplaceString");
            Assert.IsFalse(normalized.Contains("VisualBasicValue"), "Generated metadata output should not contain raw VisualBasicValue.");
            Assert.IsFalse(normalized.Contains("VisualBasicReference"), "Generated metadata output should not contain raw VisualBasicReference.");
            Assert.IsFalse(normalized.Contains("CSharpValue"), "Generated metadata output should not contain raw CSharpValue.");
            Assert.IsFalse(normalized.Contains("CSharpReference"), "Generated metadata output should not contain raw CSharpReference.");
        }

        [TestMethod]
        public void AddSharePointDesignerMetadata_AddsResultPlaceholdersToConditionExpressions()
        {
            var xaml = @"<Activity x:Class=""ConditionExpressionWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:p=""http://schemas.microsoft.com/workflow/2012/07/xaml/activities"" xmlns:local1=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities.Expressions;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy""><Flowchart><FlowStep><Sequence DisplayName=""Stage 1""><If><If.Condition><InArgument x:TypeArguments=""x:Boolean""><p:And><p:And.Left><InArgument x:TypeArguments=""x:Boolean""><p:IsEqualString Pattern=""{x:Null}"" IgnoreCase=""False"" Text=""Alpha""><p:IsEqualString.Input><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""testText"" /></InArgument></p:IsEqualString.Input></p:IsEqualString></InArgument></p:And.Left><p:And.Right><InArgument x:TypeArguments=""x:Boolean""><local1:IsGreaterThanDateTime><local1:IsGreaterThanDateTime.Left><InArgument x:TypeArguments=""s:DateTime"" xmlns:s=""clr-namespace:System;assembly=mscorlib""><ArgumentValue x:TypeArguments=""s:DateTime"" ArgumentName=""testDate"" /></InArgument></local1:IsGreaterThanDateTime.Left></local1:IsGreaterThanDateTime></InArgument></p:And.Right></p:And></InArgument></If.Condition><If.Then><Sequence /></If.Then></If></Sequence></FlowStep></Flowchart></Activity>";

            var normalized = WfActivityBuilderSerializer.AddSharePointDesignerMetadata(xaml, "ConditionExpressionWorkflow");
            var document = XDocument.Parse(normalized);
            var conditionExpressions = document.Descendants()
                .Where(e => e.Name.LocalName == "And" || e.Name.LocalName == "IsEqualString" || e.Name.LocalName == "IsGreaterThanDateTime")
                .ToList();

            Assert.AreEqual(3, conditionExpressions.Count);
            Assert.IsTrue(conditionExpressions.All(e => (string)e.Attribute("Result") == "{x:Null}"), "SPD-authored condition expressions include explicit null Result placeholders so Designer can render the condition text.");
        }

        [TestMethod]
        public void AddSharePointDesignerMetadata_AddsResultPlaceholdersAndIdsToOperandConversionExpressions()
        {
            var xaml = @"<Activity x:Class=""OperandExpressionWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:s=""clr-namespace:System;assembly=mscorlib"" xmlns:p=""http://schemas.microsoft.com/workflow/2012/07/xaml/activities"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy"" xmlns:local1=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities.Expressions;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy""><Flowchart><FlowStep><Sequence DisplayName=""Stage 1""><If><If.Condition><InArgument x:TypeArguments=""x:Boolean""><p:And><p:And.Left><InArgument x:TypeArguments=""x:Boolean""><local1:IsEqualDate><local1:IsEqualDate.Left><InArgument x:TypeArguments=""s:DateTime""><ArgumentValue x:TypeArguments=""s:DateTime"" ArgumentName=""vardateex"" /></InArgument></local1:IsEqualDate.Left><local1:IsEqualDate.Right><InArgument x:TypeArguments=""s:DateTime""><local:ConvertTimeZoneFromSPLocalToUtc><local:ConvertTimeZoneFromSPLocalToUtc.Input><InArgument x:TypeArguments=""s:DateTime""><p:ParseDate CultureName=""en-US"" DateTimeStyles=""{x:Null}""><p:ParseDate.Value><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:ParseDate.Value></p:ParseDate></InArgument></local:ConvertTimeZoneFromSPLocalToUtc.Input></local:ConvertTimeZoneFromSPLocalToUtc></InArgument></local1:IsEqualDate.Right></local1:IsEqualDate></InArgument></p:And.Left><p:And.Right><InArgument x:TypeArguments=""x:Boolean""><local1:IsEqualDynamicValue><local1:IsEqualDynamicValue.Left><InArgument x:TypeArguments=""p:DynamicValue""><ArgumentValue x:TypeArguments=""p:DynamicValue"" ArgumentName=""vardictex"" /></InArgument></local1:IsEqualDynamicValue.Left><local1:IsEqualDynamicValue.Right><InArgument x:TypeArguments=""p:DynamicValue""><p:ParseDynamicValue><p:ParseDynamicValue.Json><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:ParseDynamicValue.Json></p:ParseDynamicValue></InArgument></local1:IsEqualDynamicValue.Right></local1:IsEqualDynamicValue></InArgument></p:And.Right></p:And></InArgument></If.Condition><If.Then><Sequence /></If.Then></If></Sequence></FlowStep></Flowchart></Activity>";

            var normalized = WfActivityBuilderSerializer.AddSharePointDesignerMetadata(xaml, "OperandExpressionWorkflow");
            var document = XDocument.Parse(normalized);
            var expressionNames = new[] { "IsEqualDate", "ConvertTimeZoneFromSPLocalToUtc", "ParseDate", "IsEqualDynamicValue", "ParseDynamicValue" };
            var expressions = document.Descendants().Where(e => expressionNames.Contains(e.Name.LocalName)).ToList();

            Assert.AreEqual(5, expressions.Count);
            Assert.IsTrue(expressions.All(e => (string)e.Attribute("Result") == "{x:Null}"), "SPD-authored date and DynamicValue operand expression subtrees include explicit null Result placeholders.");
            Assert.IsTrue(document.Descendants().Where(e => e.Name.LocalName == "ParseDate" || e.Name.LocalName == "ParseDynamicValue" || e.Name.LocalName == "ConvertTimeZoneFromSPLocalToUtc").All(e => e.Descendants().Any(d => d.Name.LocalName == "String" && (string)d.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "Id")), "SPD operand conversion expressions require Designer Id custom attributes for operand text rendering.");
        }

        [TestMethod]
        public void AddSharePointDesignerMetadata_NormalizesConditionRhsOperandSubtreeShapeForDesigner()
        {
            var xaml = @"<Activity x:Class=""OperandShapeWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:s=""clr-namespace:System;assembly=mscorlib"" xmlns:p=""http://schemas.microsoft.com/workflow/2012/07/xaml/activities"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy"" xmlns:local1=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities.Expressions;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy""><Flowchart><FlowStep><Sequence DisplayName=""Stage 1""><If><If.Condition><InArgument x:TypeArguments=""x:Boolean""><p:And><p:And.Left><InArgument x:TypeArguments=""x:Boolean""><local1:IsEqualDate><local1:IsEqualDate.Left><InArgument x:TypeArguments=""s:DateTime""><ArgumentValue x:TypeArguments=""s:DateTime"" ArgumentName=""vardateex"" /></InArgument></local1:IsEqualDate.Left><local1:IsEqualDate.Right><InArgument x:TypeArguments=""s:DateTime""><local:ConvertTimeZoneFromSPLocalToUtc><local:ConvertTimeZoneFromSPLocalToUtc.Input><InArgument x:TypeArguments=""s:DateTime""><p:ParseDate CultureName=""en-US"" DateTimeStyles=""{x:Null}""><p:ParseDate.Value><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:ParseDate.Value></p:ParseDate></InArgument></local:ConvertTimeZoneFromSPLocalToUtc.Input></local:ConvertTimeZoneFromSPLocalToUtc></InArgument></local1:IsEqualDate.Right></local1:IsEqualDate></InArgument></p:And.Left><p:And.Right><InArgument x:TypeArguments=""x:Boolean""><local1:IsEqualDynamicValue><local1:IsEqualDynamicValue.Left><InArgument x:TypeArguments=""p:DynamicValue""><ArgumentValue x:TypeArguments=""p:DynamicValue"" ArgumentName=""vardictex"" /></InArgument></local1:IsEqualDynamicValue.Left><local1:IsEqualDynamicValue.Right><InArgument x:TypeArguments=""p:DynamicValue""><p:ParseDynamicValue><p:ParseDynamicValue.Json><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:ParseDynamicValue.Json></p:ParseDynamicValue></InArgument></local1:IsEqualDynamicValue.Right></local1:IsEqualDynamicValue></InArgument></p:And.Right></p:And></InArgument></If.Condition><If.Then><Sequence /></If.Then></If></Sequence></FlowStep></Flowchart></Activity>";

            var normalized = WfActivityBuilderSerializer.AddSharePointDesignerMetadata(xaml, "OperandShapeWorkflow");
            var document = XDocument.Parse(normalized);
            var parseDate = document.Descendants().Single(e => e.Name.LocalName == "ParseDate");
            var cultureName = parseDate.Elements().Single(e => e.Name.LocalName == "ParseDate.CultureName");
            var rhsArgumentValues = document.Descendants()
                .Where(e => e.Name.LocalName == "IsEqualDate.Right" || e.Name.LocalName == "IsEqualDynamicValue.Right")
                .SelectMany(e => e.Descendants().Where(d => d.Name.LocalName == "ArgumentValue"))
                .ToList();

            Assert.IsNull(parseDate.Attribute("CultureName"), "SPD-authored date RHS uses ParseDate.CultureName property-element form instead of a flattened CultureName attribute.");
            Assert.IsTrue(cultureName.Descendants().Any(e => e.Name.LocalName == "GetConfigurationValue" && (string)e.Attribute("Name") == "Microsoft.SharePoint.ActivationProperties.CultureName"), "SPD-authored date RHS resolves CultureName through GetConfigurationValue so Designer can render the operand token.");
            Assert.AreEqual(2, rhsArgumentValues.Count, "Expected RHS date ParseDate.Value and DynamicValue ParseDynamicValue.Json variable references.");
            Assert.IsTrue(rhsArgumentValues.All(e => e.Elements().Any(child => child.Name.LocalName == "ArgumentValue.Result" && child.Elements().Any(outArg => outArg.Name.LocalName == "OutArgument" && (string)outArg.Attribute(XName.Get("TypeArguments", "http://schemas.microsoft.com/winfx/2006/xaml")) == (string)e.Attribute(XName.Get("TypeArguments", "http://schemas.microsoft.com/winfx/2006/xaml"))))), "SPD-authored RHS ArgumentValue nodes include explicit ArgumentValue.Result/OutArgument wrappers rather than self-closing variable references.");
        }

        [TestMethod]
        public void AddSharePointDesignerMetadata_MatchesOriginalConditionRhsCustomAttributePlacement()
        {
            var xaml = @"<Activity x:Class=""OperandExactWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:s=""clr-namespace:System;assembly=mscorlib"" xmlns:p=""http://schemas.microsoft.com/workflow/2012/07/xaml/activities"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy"" xmlns:local1=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities.Expressions;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy""><Flowchart><FlowStep><Sequence DisplayName=""Stage 1""><If><If.Condition><InArgument x:TypeArguments=""x:Boolean""><p:And><p:And.Left><InArgument x:TypeArguments=""x:Boolean""><local1:IsEqualDate><local1:IsEqualDate.Left><InArgument x:TypeArguments=""s:DateTime""><ArgumentValue x:TypeArguments=""s:DateTime"" ArgumentName=""vardateex"" /></InArgument></local1:IsEqualDate.Left><local1:IsEqualDate.Right><InArgument x:TypeArguments=""s:DateTime""><local:ConvertTimeZoneFromSPLocalToUtc><local:ConvertTimeZoneFromSPLocalToUtc.Input><InArgument x:TypeArguments=""s:DateTime""><p:ParseDate CultureName=""en-US"" DateTimeStyles=""{x:Null}""><p:ParseDate.Value><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:ParseDate.Value></p:ParseDate></InArgument></local:ConvertTimeZoneFromSPLocalToUtc.Input></local:ConvertTimeZoneFromSPLocalToUtc></InArgument></local1:IsEqualDate.Right></local1:IsEqualDate></InArgument></p:And.Left><p:And.Right><InArgument x:TypeArguments=""x:Boolean""><local1:IsEqualDynamicValue><local1:IsEqualDynamicValue.Left><InArgument x:TypeArguments=""p:DynamicValue""><ArgumentValue x:TypeArguments=""p:DynamicValue"" ArgumentName=""vardictex"" /></InArgument></local1:IsEqualDynamicValue.Left><local1:IsEqualDynamicValue.Right><InArgument x:TypeArguments=""p:DynamicValue""><p:ParseDynamicValue><p:ParseDynamicValue.Json><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:ParseDynamicValue.Json></p:ParseDynamicValue></InArgument></local1:IsEqualDynamicValue.Right></local1:IsEqualDynamicValue></InArgument></p:And.Right></p:And></InArgument></If.Condition><If.Then><Sequence /></If.Then></If></Sequence></FlowStep></Flowchart></Activity>";

            var normalized = WfActivityBuilderSerializer.AddSharePointDesignerMetadata(xaml, "OperandExactWorkflow");
            var document = XDocument.Parse(normalized);
            var convert = document.Descendants().Single(e => e.Name.LocalName == "ConvertTimeZoneFromSPLocalToUtc");
            var parseDate = document.Descendants().Single(e => e.Name.LocalName == "ParseDate");
            var parseDynamicValue = document.Descendants().Single(e => e.Name.LocalName == "ParseDynamicValue");

            Assert.AreEqual("{x:Null}", (string)convert.Attribute("Result"));
            Assert.IsFalse(convert.Elements().Any(e => e.Name.LocalName == "SPDesignerXamlWriter.CustomAttributes"), "Original SPD-authored ExampleConditionals keeps ConvertTimeZoneFromSPLocalToUtc as a plain wrapper with only Result and Input.");
            Assert.AreEqual("ConvertTimeZoneFromSPLocalToUtc.Input", convert.Elements().First().Name.LocalName, "The first ConvertTimeZoneFromSPLocalToUtc child must be the Input property element, matching original SPD serialization order.");
            Assert.IsTrue(parseDate.Elements().Any(e => e.Name.LocalName == "SPDesignerXamlWriter.CustomAttributes"), "Original SPD-authored ParseDate contains the expression Id custom attribute.");
            Assert.IsTrue(parseDynamicValue.Elements().Any(e => e.Name.LocalName == "SPDesignerXamlWriter.CustomAttributes"), "Original SPD-authored ParseDynamicValue contains the expression Id custom attribute.");
        }

        [TestMethod]
        public void AddSharePointDesignerMetadata_PreservesConditionRhsDesignerIdsByExpressionType()
        {
            var xaml = @"<Activity x:Class=""OperandPreserveIdsWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:s=""clr-namespace:System;assembly=mscorlib"" xmlns:p=""http://schemas.microsoft.com/workflow/2012/07/xaml/activities"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy"" xmlns:local1=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities.Expressions;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy""><Flowchart><FlowStep><Sequence DisplayName=""Stage 1""><If><If.Condition><InArgument x:TypeArguments=""x:Boolean""><p:And><p:And.Left><InArgument x:TypeArguments=""x:Boolean""><local1:IsEqualDate><local1:IsEqualDate.Left><InArgument x:TypeArguments=""s:DateTime""><ArgumentValue x:TypeArguments=""s:DateTime"" ArgumentName=""vardateex"" /></InArgument></local1:IsEqualDate.Left><local1:IsEqualDate.Right><InArgument x:TypeArguments=""s:DateTime""><local:ConvertTimeZoneFromSPLocalToUtc><local:ConvertTimeZoneFromSPLocalToUtc.Input><InArgument x:TypeArguments=""s:DateTime""><p:ParseDate CultureName=""en-US"" DateTimeStyles=""{x:Null}""><p:ParseDate.Value><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:ParseDate.Value></p:ParseDate></InArgument></local:ConvertTimeZoneFromSPLocalToUtc.Input></local:ConvertTimeZoneFromSPLocalToUtc></InArgument></local1:IsEqualDate.Right></local1:IsEqualDate></InArgument></p:And.Left><p:And.Right><InArgument x:TypeArguments=""x:Boolean""><local1:IsEqualDynamicValue><local1:IsEqualDynamicValue.Left><InArgument x:TypeArguments=""p:DynamicValue""><ArgumentValue x:TypeArguments=""p:DynamicValue"" ArgumentName=""vardictex"" /></InArgument></local1:IsEqualDynamicValue.Left><local1:IsEqualDynamicValue.Right><InArgument x:TypeArguments=""p:DynamicValue""><p:ParseDynamicValue><p:ParseDynamicValue.Json><InArgument x:TypeArguments=""x:String""><ArgumentValue x:TypeArguments=""x:String"" ArgumentName=""varstrex"" /></InArgument></p:ParseDynamicValue.Json></p:ParseDynamicValue></InArgument></local1:IsEqualDynamicValue.Right></local1:IsEqualDynamicValue></InArgument></p:And.Right></p:And></InArgument></If.Condition></If></Sequence></FlowStep></Flowchart></Activity>";
            var workflow = new WorkflowYaml
            {
                Name = "OperandPreserveIdsWorkflow",
                Stages =
                {
                    new StageYaml
                    {
                        Name = "Stage 1",
                        Actions =
                        {
                            new IfActionYaml
                            {
                                Condition = new ComparisonExpressionYaml
                                {
                                    Type = "and",
                                    LeftCondition = new ComparisonExpressionYaml { Type = "isEqual", ValueType = "DateTime", Left = new ExpressionYaml { Variable = "vardateex" }, Right = new ExpressionYaml { Type = "parseDate", ValueType = "DateTime", Value = new ExpressionYaml { Variable = "varstrex" }, DesignerId = "092C790F-34E1-4779-A8F0-76273B83670B" } },
                                    RightCondition = new ComparisonExpressionYaml { Type = "isEqual", ValueType = "DynamicValue", Left = new ExpressionYaml { Variable = "vardictex" }, Right = new ExpressionYaml { Type = "parseDynamicValue", ValueType = "DynamicValue", Value = new ExpressionYaml { Variable = "varstrex" }, DesignerId = "53016204-D02E-4555-81C1-843464410038" } }
                                }
                            }
                        }
                    }
                }
            };

            var normalized = WfActivityBuilderSerializer.AddSharePointDesignerMetadataForTest(xaml, "OperandPreserveIdsWorkflow", workflow);
            var document = XDocument.Parse(normalized);

            Assert.AreEqual("092C790F-34E1-4779-A8F0-76273B83670B", ReadDesignerId(document.Descendants().Single(e => e.Name.LocalName == "ParseDate")));
            Assert.AreEqual("53016204-D02E-4555-81C1-843464410038", ReadDesignerId(document.Descendants().Single(e => e.Name.LocalName == "ParseDynamicValue")));
        }

        [TestMethod]
        public void Load_PreservesNestedExpressionDesignerIdsFromYaml()
        {
            var workflow = LoadYaml(@"schemaVersion: spnet.workflow/v1
name: PreserveIds
stages:
- name: Stage 1
  actions:
  - type: if
    condition:
      type: and
      leftCondition:
        type: isEqual
        valueType: DateTime
        left:
          variable: vardateex
        right:
          type: parseDate
          value:
            variable: varstrex
          valueType: DateTime
          designerId: 092C790F-34E1-4779-A8F0-76273B83670B
      rightCondition:
        type: isEqual
        valueType: DynamicValue
        left:
          variable: vardictex
        right:
          type: parseDynamicValue
          value:
            variable: varstrex
          valueType: DynamicValue
          designerId: 53016204-D02E-4555-81C1-843464410038
    then: []
");
            var condition = workflow.Stages.Single().Actions.OfType<IfActionYaml>().Single().Condition;

            Assert.AreEqual("092C790F-34E1-4779-A8F0-76273B83670B", condition.LeftCondition.Right.DesignerId);
            Assert.AreEqual("53016204-D02E-4555-81C1-843464410038", condition.RightCondition.Right.DesignerId);
        }

        [TestMethod]
        public void Load_DeserializesMapStyleParameters()
        {
            var workflow = LoadYaml(@"schemaVersion: spnet.workflow/v1
name: ParameterWorkflow
parameters:
  titleParam:
    type: Text
    displayName: Title Parameter
    default: hello
  approved:
    type: Boolean
    default: true
stages:
  - name: Stage 1
    actions:
      - type: writeHistory
        message:
          variable: titleParam");

            Assert.AreEqual(2, workflow.Parameters.Count);
            Assert.AreEqual("titleParam", workflow.Parameters[0].Name);
            Assert.AreEqual("Title Parameter", workflow.Parameters[0].DisplayName);
            Assert.AreEqual("approved", workflow.Parameters[1].Name);
            Assert.AreEqual("Boolean", workflow.Parameters[1].Type);
        }

        [TestMethod]
        public void Load_DeserializesListStyleParameters()
        {
            var workflow = LoadYaml(@"schemaVersion: spnet.workflow/v1
name: ParameterWorkflow
parameters:
  - name: titleParam
    type: Text
    displayName: Title Parameter
    default: hello
  - name: approved
    type: Boolean
    default: true
stages:
  - name: Stage 1
    actions:
      - type: writeHistory
        message:
          variable: titleParam");

            Assert.AreEqual(2, workflow.Parameters.Count);
            Assert.AreEqual("titleParam", workflow.Parameters[0].Name);
            Assert.AreEqual("approved", workflow.Parameters[1].Name);
        }

        [TestMethod]
        public void Load_RejectsDuplicateParameterAndVariableName()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() => LoadYaml(@"schemaVersion: spnet.workflow/v1
name: ParameterWorkflow
variables:
  - name: duplicateName
    type: String
parameters:
  duplicateName:
    type: Text
stages:
  - name: Stage 1
    actions:
      - type: writeHistory
        message: hello"));

            StringAssert.Contains(ex.Message, "Duplicate variable/parameter name");
        }

        [TestMethod]
        public void Load_RejectsAssignmentToParameter()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() => LoadYaml(@"schemaVersion: spnet.workflow/v1
name: ParameterWorkflow
parameters:
  readonlyParam:
    type: Text
stages:
  - name: Stage 1
    actions:
      - type: assign
        to: readonlyParam
        value: changed"));

            StringAssert.Contains(ex.Message, "cannot assign to initiation parameter");
        }

        [TestMethod]
        public void FormFieldSerializer_GeneratesParameterisedWFExMetadataShape()
        {
            var xml = WorkflowParameterFormFieldSerializer.Serialize(CreateParameterisedWFExParameters());
            var document = XDocument.Parse(xml);
            var fields = document.Root.Elements("Field").ToList();

            Assert.AreEqual(9, fields.Count);
            Assert.AreEqual("ExampleStringParam", (string)fields[0].Attribute("Name"));
            Assert.AreEqual("Text", (string)fields[0].Attribute("Type"));
            Assert.AreEqual("255", (string)fields[0].Attribute("MaxLength"));
            Assert.AreEqual("defaultvaluieaac", fields[0].Element("Default")?.Value);
            var choice = fields.Single(f => (string)f.Attribute("Name") == "examplechoice");
            Assert.AreEqual("Dropdown", (string)choice.Attribute("Format"));
            Assert.AreEqual("Choice2", choice.Element("Default")?.Value);
            Assert.AreEqual(3, choice.Element("CHOICES")?.Elements("CHOICE").Count());
            var person = fields.Single(f => (string)f.Attribute("Name") == "exampleperson");
            Assert.AreEqual("UserMulti", (string)person.Attribute("Type"));
            Assert.AreEqual("TRUE", (string)person.Attribute("Mult"));
            var note = fields.Single(f => (string)f.Attribute("Name") == "examplemultilineparam");
            Assert.AreEqual("6", (string)note.Attribute("NumLines"));
            Assert.AreEqual("Compatible", (string)note.Attribute("RichTextMode"));
        }

        [TestMethod]
        public void ExportWorkflowYaml_ExportsXamlOnlyInArgumentsAsVariablesWithoutFormFieldMetadata()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), "spnet-parameter-export-" + Guid.NewGuid().ToString("N") + ".xaml");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-parameter-export-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(inputPath, @"<Activity x:Class=""ParameterWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:s=""clr-namespace:System;assembly=mscorlib"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities""><x:Members><x:Property Name=""titleParam"" Type=""InArgument(x:String)"" /><x:Property Name=""approved"" Type=""InArgument(x:Boolean)"" /><x:Property Name=""amount"" Type=""InArgument(x:Double)"" /><x:Property Name=""dueDate"" Type=""InArgument(s:DateTime)"" /></x:Members><Sequence DisplayName=""Stage 1""><local:WriteToHistory Message=""hello"" /></Sequence></Activity>");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);

                Assert.AreEqual(0, workflow.Parameters.Count);
                Assert.AreEqual("String", workflow.Variables.Single(v => v.Name == "titleParam").Type);
                Assert.AreEqual("Boolean", workflow.Variables.Single(v => v.Name == "approved").Type);
                Assert.AreEqual("Double", workflow.Variables.Single(v => v.Name == "amount").Type);
                Assert.AreEqual("DateTime", workflow.Variables.Single(v => v.Name == "dueDate").Type);
                Assert.IsTrue(workflow.ExportWarnings.Any(w => w.Contains("XAML-only variable export")));
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_UsesExplicitFormFieldMetadataForParameters()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), "spnet-parameter-formfield-export-" + Guid.NewGuid().ToString("N") + ".xaml");
            var formFieldPath = Path.Combine(Path.GetTempPath(), "spnet-parameter-formfield-export-" + Guid.NewGuid().ToString("N") + ".formfield.xml");
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-parameter-formfield-export-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(inputPath, @"<Activity x:Class=""ParameterWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:s=""clr-namespace:System;assembly=mscorlib"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities""><x:Members><x:Property Name=""titleParam"" Type=""InArgument(x:String)"" /><x:Property Name=""approved"" Type=""InArgument(x:Boolean)"" /><x:Property Name=""amount"" Type=""InArgument(x:Double)"" /><x:Property Name=""legacyOnly"" Type=""InArgument(x:String)"" /></x:Members><Sequence DisplayName=""Stage 1""><local:WriteToHistory Message=""hello"" /></Sequence></Activity>");
            File.WriteAllText(formFieldPath, @"<Fields><Field Name=""titleParam"" FormType=""Initiation"" Type=""Text"" DisplayName=""Request title"" Description=""Title description"" Direction=""None"" MaxLength=""255""><Default>default title</Default></Field><Field Name=""approved"" FormType=""Initiation"" Type=""Boolean"" DisplayName=""Approved?"" Direction=""None""><Default>1</Default></Field><Field Name=""choiceParam"" FormType=""Initiation"" Type=""Choice"" Format=""Dropdown"" BaseType=""Text"" DisplayName=""Choice parameter"" Direction=""None"" CustomAttribute=""preserved""><Default>Choice2</Default><CHOICES><CHOICE DisplayName=""Choice One"">Choice1</CHOICE><CHOICE>Choice2</CHOICE></CHOICES></Field><Field Name=""personParam"" FormType=""Initiation"" Type=""UserMulti"" List=""UserInfo"" ShowField=""Name"" Mult=""TRUE"" UserSelectionMode=""PeopleAndGroups"" UserSelectionScope=""0"" DisplayName=""People"" Direction=""None"" /><Field Name=""noteParam"" FormType=""Initiation"" Type=""Note"" NumLines=""6"" Sortable=""FALSE"" RichTextMode=""Compatible"" DisplayName=""Notes"" Direction=""None"" /><Field Name=""urlParam"" FormType=""Initiation"" Type=""URL"" Format=""Image"" DisplayName=""Picture"" Direction=""None"" /><Field Name=""amount"" FormType=""Initiation"" Type=""Number"" DisplayName=""Amount"" Direction=""None""><Default>12.5</Default></Field></Fields>");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath, formFieldPath);
                var workflow = WorkflowYaml.Load(outputPath);

                Assert.AreEqual("Request title", workflow.Parameters.Single(p => p.Name == "titleParam").DisplayName);
                Assert.AreEqual("Title description", workflow.Parameters.Single(p => p.Name == "titleParam").Description);
                Assert.AreEqual("255", workflow.Parameters.Single(p => p.Name == "titleParam").MaxLength);
                Assert.AreEqual("default title", workflow.Parameters.Single(p => p.Name == "titleParam").Default);
                Assert.AreEqual("1", workflow.Parameters.Single(p => p.Name == "approved").Default);
                var choice = workflow.Parameters.Single(p => p.Name == "choiceParam");
                Assert.AreEqual("Choice", choice.Type);
                Assert.AreEqual("Dropdown", choice.Format);
                Assert.AreEqual("Text", choice.BaseType);
                Assert.AreEqual("preserved", choice.Attributes["CustomAttribute"]);
                Assert.AreEqual(2, choice.Choices.Count);
                Assert.AreEqual("Choice One", choice.Choices[0].DisplayName);
                var person = workflow.Parameters.Single(p => p.Name == "personParam");
                Assert.AreEqual("UserMulti", person.Type);
                Assert.AreEqual("UserInfo", person.List);
                Assert.AreEqual("TRUE", person.Mult);
                Assert.AreEqual("PeopleAndGroups", person.UserSelectionMode);
                Assert.AreEqual("0", person.UserSelectionScope);
                var note = workflow.Parameters.Single(p => p.Name == "noteParam");
                Assert.AreEqual("6", note.NumLines);
                Assert.AreEqual("FALSE", note.Sortable);
                Assert.AreEqual("Compatible", note.RichTextMode);
                Assert.AreEqual("Image", workflow.Parameters.Single(p => p.Name == "urlParam").Format);
                Assert.IsTrue(workflow.Variables.Any(v => v.Name == "legacyOnly" && v.Type == "String"), "XAML-only members not present in FormField should remain variables.");
                Assert.IsTrue(workflow.ExportWarnings.Any(w => w.Contains("FormField metadata export")), "Expected FormField metadata warning.");
                Assert.IsFalse(workflow.ExportWarnings.Any(w => w.Contains("XAML-only parameter export")), "FormField export should not emit the conservative XAML-only parameter warning.");
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(formFieldPath)) File.Delete(formFieldPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void ExportWorkflowYaml_AutoDiscoversFormFieldSidecarPath()
        {
            var inputPath = Path.Combine(Path.GetTempPath(), "spnet-parameter-sidecar-export-" + Guid.NewGuid().ToString("N") + ".xaml");
            var formFieldPath = inputPath + ".formfield.xml";
            var outputPath = Path.Combine(Path.GetTempPath(), "spnet-parameter-sidecar-export-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(inputPath, @"<Activity x:Class=""ParameterWorkflow.MTW"" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities""><x:Members><x:Property Name=""titleParam"" Type=""InArgument(x:String)"" /></x:Members><Sequence DisplayName=""Stage 1""><local:WriteToHistory Message=""hello"" /></Sequence></Activity>");
            File.WriteAllText(formFieldPath, @"<Fields><Field Name=""titleParam"" FormType=""Initiation"" Type=""Text"" DisplayName=""Auto-discovered title"" Direction=""None"" /></Fields>");
            try
            {
                WfActivityBuilderSerializer.ExportWorkflowYaml(inputPath, outputPath);
                var workflow = WorkflowYaml.Load(outputPath);

                Assert.AreEqual("Auto-discovered title", workflow.Parameters.Single(p => p.Name == "titleParam").DisplayName);
            }
            finally
            {
                if (File.Exists(inputPath)) File.Delete(inputPath);
                if (File.Exists(formFieldPath)) File.Delete(formFieldPath);
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void WfSerializerOptionsParse_AcceptsExportFormFieldXmlArgument()
        {
            var options = WfSerializerOptions.Parse(new[] { "export", "--xaml", "workflow.xaml", "--out", "workflow.yml", "--form-field-xml", "workflow.xaml.formfield.xml" });

            Assert.AreEqual("workflow.xaml.formfield.xml", options.FormFieldXmlPath);
        }

        private static ParameterYaml[] CreateParameterisedWFExParameters() => new[]
        {
            new ParameterYaml { Name = "ExampleStringParam", Type = "Text", MaxLength = "255", DisplayName = "ExampleStringParam", Description = string.Empty, Direction = "None", Default = "defaultvaluieaac" },
            new ParameterYaml { Name = "exampleboolparam", Type = "Boolean", DisplayName = "exampleboolparam", Description = string.Empty, Direction = "None", Default = true },
            new ParameterYaml { Name = "examplechoice", Type = "Choice", Format = "Dropdown", BaseType = "Text", DisplayName = "examplechoice", Description = string.Empty, Direction = "None", Default = "Choice2", Choices = { new ChoiceYaml { DisplayName = "Choice 1", Value = "choice1value" }, new ChoiceYaml { DisplayName = "Choice2", Value = "Choice2" }, new ChoiceYaml { DisplayName = "Param Title", Value = "wfvarname" } } },
            new ParameterYaml { Name = "exampleperson", Type = "UserMulti", List = "UserInfo", ShowField = "Name", Mult = "TRUE", UserSelectionMode = "PeopleAndGroups", UserSelectionScope = "386", DisplayName = "exampleperson", Description = string.Empty, Direction = "None" },
            new ParameterYaml { Name = "examplemultilineparam", Type = "Note", NumLines = "6", Sortable = "FALSE", RichTextMode = "Compatible", DisplayName = "examplemultilineparam", Description = string.Empty, Direction = "None" },
            new ParameterYaml { Name = "Example", Type = "URL", Format = "Hyperlink", DisplayName = "Example", Description = string.Empty, Direction = "None" },
            new ParameterYaml { Name = "examplepicture", Type = "URL", Format = "Image", DisplayName = "examplepicture", Description = string.Empty, Direction = "None" },
            new ParameterYaml { Name = "Example1", Type = "Number", DisplayName = "Example1", Description = string.Empty, Direction = "None", Default = 0d },
            new ParameterYaml { Name = "exampledate", Type = "DateTime", DisplayName = "exampledate", Description = string.Empty, Direction = "None" }
        };

        private static WorkflowYaml LoadYaml(string yaml)
        {
            var path = Path.Combine(Path.GetTempPath(), "spnet-" + Guid.NewGuid().ToString("N") + ".yml");
            try
            {
                File.WriteAllText(path, yaml);
                return WorkflowYaml.Load(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void Load_DeserializesMetadataAndUsesItAsEffectiveValues()
        {
            var workflow = LoadYaml(@"schemaVersion: spnet.workflow/v1
name: LegacyWorkflow
start:
  manual: true
metadata:
  displayName: Metadata Workflow
  technicalName: Metadata.Workflow.MTW
  description: Metadata description
  target:
    type: List
    listTitle: Requests
  start:
    manual: false
    onCreated: true
    onUpdated: true
  initiation:
    formFields:
      - name: titleParam
        type: Text
        displayName: Request title
stages:
  - name: Stage 1
    actions:
      - type: writeHistory
        message: hello");

            Assert.AreEqual("Metadata Workflow", workflow.EffectiveDisplayName);
            Assert.AreEqual("Metadata.Workflow.MTW", workflow.EffectiveTechnicalName);
            Assert.AreEqual("Requests", workflow.EffectiveTarget.ListTitle);
            Assert.IsFalse(workflow.EffectiveStartManual);
            Assert.IsTrue(workflow.EffectiveStartOnCreated);
            Assert.IsTrue(workflow.EffectiveStartOnUpdated);
            Assert.AreEqual("titleParam", workflow.EffectiveFormFields.Single().Name);
        }

        [TestMethod]
        public void Save_RoundTripsMetadataYaml()
        {
            var path = Path.Combine(Path.GetTempPath(), "spnet-metadata-roundtrip-" + Guid.NewGuid().ToString("N") + ".yml");
            try
            {
                var workflow = LoadYaml(@"schemaVersion: spnet.workflow/v1
name: LegacyWorkflow
metadata:
  displayName: Metadata Workflow
  description: Metadata description
  initiation:
    formFields:
      - name: titleParam
        type: Text
        default: hello
stages:
  - name: Stage 1
    actions:
      - type: writeHistory
        message: hello");

                workflow.Save(path);
                var roundTripped = WorkflowYaml.Load(path);

                Assert.AreEqual("Metadata Workflow", roundTripped.Metadata.DisplayName);
                Assert.AreEqual("hello", roundTripped.Metadata.Initiation.FormFields.Single().Default);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void MetadataFormFields_GenerateSameFormFieldXmlAsLegacyParameters()
        {
            var legacy = CreateParameterisedWFExParameters();
            var workflow = new WorkflowYaml();
            workflow.Metadata.Initiation.FormFields.AddRange(legacy);

            Assert.AreEqual(WorkflowParameterFormFieldSerializer.Serialize(legacy), WorkflowParameterFormFieldSerializer.Serialize(workflow.EffectiveFormFields));
        }

        [TestMethod]
        public void MetadataJson_SerializesAndDeserializesPublishContract()
        {
            var metadata = new WorkflowDefinitionMetadataYaml
            {
                DisplayName = "Metadata Workflow",
                Description = "Metadata description",
                Start = new WorkflowStartOptionsYaml { Manual = false, OnCreated = true, OnUpdated = true },
                Initiation = new WorkflowInitiationMetadataYaml { FormFields = { new ParameterYaml { Name = "titleParam", Type = "Text", Default = "hello" } } }
            };

            var roundTripped = WorkflowDefinitionMetadataYaml.FromJson(metadata.ToJson());

            Assert.AreEqual("Metadata Workflow", roundTripped.DisplayName);
            Assert.IsFalse(roundTripped.Start.Manual.Value);
            Assert.IsTrue(roundTripped.Start.OnCreated.Value);
            Assert.AreEqual("titleParam", roundTripped.Initiation.FormFields.Single().Name);
            Assert.AreEqual("hello", roundTripped.Initiation.FormFields.Single().Default);
        }

        [TestMethod]
        [TestCategory("WebsiteCacheIntegration")]
        public void SerializeYamlWorkflow_WritesMetadataJsonSidecar()
        {
            var cacheFolder = GetConfiguredWebsiteCacheOrInconclusive();
            var directory = Path.Combine(Path.GetTempPath(), "spnet-metadata-json-sidecar-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var workflowPath = Path.Combine(directory, "workflow.yml");
            var xamlPath = Path.Combine(directory, "workflow.xaml");
            File.WriteAllText(workflowPath, @"schemaVersion: spnet.workflow/v1
name: MetadataSidecarWorkflow
parameters:
  requestTitle:
    type: Text
    displayName: Request title
stages:
  - name: Stage 1
    actions:
      - type: writeHistory
        message: Done
");

            try
            {
                WfActivityBuilderSerializer.SerializeYamlWorkflow(workflowPath, xamlPath, cacheFolder, new SpNetToolConfig());

                var metadataPath = xamlPath + ".metadata.json";
                Assert.IsTrue(File.Exists(metadataPath));
                var metadata = WorkflowDefinitionMetadataYaml.FromJson(File.ReadAllText(metadataPath));
                Assert.AreEqual("MetadataSidecarWorkflow", metadata.DisplayName);
                Assert.IsTrue(metadata.Initiation.RequiresForm.Value);
                Assert.AreEqual("requestTitle", metadata.Initiation.FormFields.Single().Name);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        [TestCategory("WebsiteCacheIntegration")]
        public void SerializeYamlWorkflow_EmitsDevOnlyMicrosoftActivitiesExpressions()
        {
            var cacheFolder = GetConfiguredWebsiteCacheOrInconclusive();
            var directory = Path.Combine(Path.GetTempPath(), "spnet-devonly-expressions-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var workflowPath = Path.Combine(directory, "workflow.yml");
            var xamlPath = Path.Combine(directory, "workflow.xaml");
            File.WriteAllText(workflowPath, @"schemaVersion: spnet.workflow/v1
name: DevOnlyExpressionsBuild
variables:
  - name: textValue
    type: String
  - name: lengthValue
    type: Int32
  - name: stringBoolValue
    type: Boolean
  - name: dateValue
    type: DateTime
  - name: guidValue
    type: Guid
  - name: boolValue
    type: Boolean
  - name: payload
    type: DynamicValue
stages:
  - name: Stage 1
    actions:
      - type: buildDynamicValue
        to: payload
        entries:
          - key: Title
            value: Example
      - type: assign
        to: textValue
        value:
          type: concatString
          values:
            -
              type: toLowerCase
              value: ABC
            -
              type: toUpperCase
              value: xyz
      - type: assign
        to: lengthValue
        value:
          type: stringLength
          value:
            variable: textValue
      - type: assign
        to: textValue
        value:
          type: replaceString
          value:
            variable: textValue
          oldValue: A
          newValue: B
      - type: assign
        to: textValue
        value:
          type: substring
          value:
            variable: textValue
          startIndex: 0
          length: 2
      - type: assign
        to: textValue
        value:
          type: trimString
          value: '  spaced  '
      - type: assign
        to: lengthValue
        value:
          type: indexOfString
          value:
            variable: textValue
          searchValue: B
      - type: assign
        to: stringBoolValue
        value:
          type: isEmptyString
          value:
            variable: textValue
      - type: assign
        to: stringBoolValue
        value:
          type: containsString
          value:
            variable: textValue
          searchValue: B
      - type: assign
        to: stringBoolValue
        value:
          type: startsWithString
          value:
            variable: textValue
          searchValue: B
      - type: assign
        to: stringBoolValue
        value:
          type: endsWithString
          value:
            variable: textValue
          searchValue: B
      - type: assign
        to: stringBoolValue
        value:
          type: parseBoolean
          value: true
      - type: assign
        to: dateValue
        value:
          type: currentDate
      - type: assign
        to: guidValue
        value:
          type: newGuid
      - type: assign
        to: boolValue
        value:
          type: containsDynamicValueProperty
          source:
            variable: payload
          propertyName: Title
      - type: assign
        to: boolValue
        value:
          type: isEmptyDynamicValue
          source:
            variable: payload
");

            try
            {
                WfActivityBuilderSerializer.SerializeYamlWorkflow(workflowPath, xamlPath, cacheFolder, new SpNetToolConfig());

                var xaml = File.ReadAllText(xamlPath);
                StringAssert.Contains(xaml, "ToLowerCase");
                StringAssert.Contains(xaml, "ToUpperCase");
                StringAssert.Contains(xaml, "StringLength");
                StringAssert.Contains(xaml, "ReplaceString");
                StringAssert.Contains(xaml, "Substring");
                StringAssert.Contains(xaml, "Trim");
                StringAssert.Contains(xaml, "IndexOfString");
                StringAssert.Contains(xaml, "IsEmptyString");
                StringAssert.Contains(xaml, "ContainsString");
                StringAssert.Contains(xaml, "StartsWithString");
                StringAssert.Contains(xaml, "EndsWithString");
                StringAssert.Contains(xaml, "ParseBoolean");
                StringAssert.Contains(xaml, "ConcatString");
                StringAssert.Contains(xaml, "CurrentDate");
                StringAssert.Contains(xaml, "NewGuid");
                StringAssert.Contains(xaml, "ContainsDynamicValueProperty");
                StringAssert.Contains(xaml, "IsEmptyDynamicValue");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string GetConfiguredWebsiteCacheOrInconclusive()
        {
            var cacheFolder = Environment.GetEnvironmentVariable("SPNET_SPD_CACHE");
            if (string.IsNullOrWhiteSpace(cacheFolder))
            {
                Assert.Inconclusive("SPNET_SPD_CACHE is not set. Skipping WebsiteCache integration coverage that requires SharePoint Designer proxy DLLs.");
            }

            if (!Directory.Exists(cacheFolder))
            {
                Assert.Inconclusive("SPNET_SPD_CACHE does not point to an existing WebsiteCache folder. Skipping WebsiteCache integration coverage.");
            }

            foreach (var dll in new[] { "Microsoft.SharePoint.WorkflowServices.Activities.Proxy.dll", "Microsoft.Activities.Proxy.dll" })
            {
                if (!File.Exists(Path.Combine(cacheFolder, dll)))
                {
                    Assert.Inconclusive("SPNET_SPD_CACHE is missing required SharePoint Designer proxy DLLs. Skipping WebsiteCache integration coverage.");
                }
            }

            return cacheFolder;
        }

        private static Exception AssertThrowsWorkflowException(Action action)
        {
            try
            {
                action();
            }
            catch (YamlException ex) when (ex.InnerException != null)
            {
                return ex.InnerException;
            }
            catch (Exception ex)
            {
                return ex;
            }

            Assert.Fail("Expected workflow YAML loading to throw.");
            throw new InvalidOperationException("Unreachable after Assert.Fail.");
        }

        private static string CreateWorkflowYaml(string actionsYaml) => @"schemaVersion: spnet.workflow/v1
name: TestWorkflow
variables:
  - name: currentWebUrl
    type: String
  - name: currentListId
    type: Guid
  - name: currentItemGuid
    type: Guid
  - name: readBackTitle
    type: String
  - name: statusCode
    type: String
  - name: responseContent
    type: String
  - name: taskId
    type: String
  - name: outcome
    type: Int32
  - name: duration
    type: TimeSpan
  - name: dueDate
    type: DateTime
  - name: approvedFlag
    type: Boolean
stages:
  - name: Stage 1
    actions:
" + actionsYaml + Environment.NewLine;

        private static string CreateActionYaml(string actionType)
        {
            switch (actionType)
            {
                case "assign":
                case "setVariable":
                    return "      - type: " + actionType + @"
        to: currentWebUrl
        value: hello";
                case "callHttpWebService":
                case "callHttp":
                case "http":
                    return "      - type: " + actionType + @"
        address: https://example.invalid
        requestType: GET
        responseStatusCodeTo: statusCode";
                case "sendEmail":
                case "email":
                    return "      - type: " + actionType + @"
        to: spnet-workflow-test@example.invalid
        subject: Test
        body: Test";
                case "singleTask":
                case "task":
                    return "      - type: " + actionType + @"
        assignedTo: spnet-workflow-test@example.invalid
        title: Test task
        taskIdTo: taskId
        outcomeTo: outcome";
                case "while":
                case "loop":
                    return "      - type: " + actionType + @"
        condition:
          type: isLessThan
          left: 1
          right: 2
        actions:
          - type: writeHistory
            message: inside loop";
                case "lookupRestPropertyName":
                case "lookupSPListItemPropertyNameInREST":
                    return "      - type: " + actionType + @"
        listId:
          type: getCurrentListId
        propertyName: Title
        to: readBackTitle";
                case "getDynamicValueProperty":
                case "getDictionaryItem":
                case "getDictionaryValue":
                case "getResponseProperty":
                    return "      - type: " + actionType + @"
        source: responseContent
        propertyName: Title
        to: readBackTitle";
                case "setDynamicValueProperty":
                case "setDictionaryItem":
                case "setDictionaryValue":
                case "setResponseProperty":
                    return "      - type: " + actionType + @"
        source: responseContent
        propertyName: Title
        value: Experimental title";
                case "buildDynamicValue":
                case "buildDictionary":
                case "createDictionary":
                    return "      - type: " + actionType + @"
        to: responseContent
        entries:
          - key: Title
            value: Test
          - key: Count
            value: 2
            valueType: Int32";
                case "createListItem":
                    return @"      - type: createListItem
        listId:
          type: getCurrentListId
        fields:
          Title: Created item";
                case "updateListItem":
                    return @"      - type: updateListItem
        listId:
          type: getCurrentListId
        itemId: 1
        fields:
          Title: Updated item";
                case "deleteListItem":
                    return @"      - type: deleteListItem
        listId:
          type: getCurrentListId
        itemId: 1";
                case "replaceString":
                case "stringReplace":
                    return "      - type: " + actionType + @"
        text:
          variable: currentWebUrl
        oldValue: old
        newValue: new
        to: readBackTitle";
                case "substring":
                case "substringString":
                    return "      - type: " + actionType + @"
        text:
          variable: currentWebUrl
        startIndex: 0
        length: 5
        to: readBackTitle";
                case "trimString":
                case "stringTrim":
                    return "      - type: " + actionType + @"
        text:
          variable: currentWebUrl
        to: readBackTitle";
                case "buildUri":
                    return @"      - type: buildUri
        scheme: https
        host: example.invalid
        path: /api/test
        to: readBackTitle";
                case "getConfigurationValue":
                    return @"      - type: getConfigurationValue
        name: Microsoft.SharePoint.ActivationProperties.CultureName
        defaultValue: en-US
        to: readBackTitle";
                case "getInstanceAddress":
                    return @"      - type: getInstanceAddress
        to: readBackTitle";
                case "setUserStatus":
                    return @"      - type: setUserStatus
        description: Test status";
                case "createTimeSpan":
                    return @"      - type: createTimeSpan
        days: 1
        hours: 2
        to: duration";
                case "getTimeSpanFields":
                    return @"      - type: getTimeSpanFields
        input:
          variable: duration
        daysTo: outcome";
                case "addToDate":
                    return @"      - type: addToDate
        input: 2026-05-10T00:00:00Z
        days: 1
        to: dueDate";
                case "subtractFromDate":
                    return @"      - type: subtractFromDate
        input: 2026-05-10T00:00:00Z
        days: 1
        to: dueDate";
                case "dateInRange":
                    return @"      - type: dateInRange
        input: 2026-05-10T00:00:00Z
        start: 2026-05-01T00:00:00Z
        end: 2026-05-31T00:00:00Z
        to: approvedFlag";
                case "lookupWorkflowContext":
                case "lookupContextProperty":
                    return "      - type: " + actionType + @"
        propertyName: CurrentWebUrl
        to: currentWebUrl";
                case "getCurrentListId":
                    return @"      - type: getCurrentListId
        to: currentListId";
                case "getCurrentItemGuid":
                    return @"      - type: getCurrentItemGuid
        to: currentItemGuid";
                case "lookupListItemStringProperty":
                case "lookupSPListItemStringProperty":
                case "lookupListItemIntProperty":
                case "lookupSPListItemIntProperty":
                    return "      - type: " + actionType + @"
        listId:
          type: getCurrentListId
        itemId: 1
        fieldName: Title
        to: readBackTitle";
                default:
                    throw new ArgumentOutOfRangeException(nameof(actionType), actionType, null);
            }
        }
    }
}

