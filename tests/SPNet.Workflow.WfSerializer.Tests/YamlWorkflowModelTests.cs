using System;
using System.IO;
using System.Linq;
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
        [DataRow("createListItem", typeof(CreateListItemActionYaml))]
        [DataRow("updateListItem", typeof(UpdateListItemActionYaml))]
        [DataRow("deleteListItem", typeof(DeleteListItemActionYaml))]
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

        [DataTestMethod]
        [DataRow("assign", "assign action requires 'to'.")]
        [DataRow("calc", "calc action requires 'to'.")]
        [DataRow("setField", "setField action requires 'fieldName'.")]
        [DataRow("callHttpWebService", "requires at least one response target")]
        [DataRow("sendEmail", "sendEmail action requires 'to'.")]
        [DataRow("singleTask", "singleTask action requires 'assignedTo'.")]
        [DataRow("getDynamicValueProperty", "getDynamicValueProperty action requires 'source'.")]
        [DataRow("lookupRestPropertyName", "lookupRestPropertyName action requires 'to'.")]
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
