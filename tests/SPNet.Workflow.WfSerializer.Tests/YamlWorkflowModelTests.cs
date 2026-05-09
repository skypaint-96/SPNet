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
        [DataRow("replaceString", typeof(StringReplaceActionYaml))]
        [DataRow("stringReplace", typeof(StringReplaceActionYaml))]
        [DataRow("substring", typeof(StringSubstringActionYaml))]
        [DataRow("substringString", typeof(StringSubstringActionYaml))]
        [DataRow("trimString", typeof(StringTrimActionYaml))]
        [DataRow("stringTrim", typeof(StringTrimActionYaml))]
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
        [DataRow("replaceString", "replaceString action requires 'to'.")]
        [DataRow("substring", "substring action requires 'to'.")]
        [DataRow("trimString", "trimString action requires 'to'.")]
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

        [TestMethod]
        public void ExportWorkflowYaml_EmitsBuildablePlaceholdersForTestWFAutoStartListActions()
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

                Assert.IsTrue(create.Fields.ContainsKey("TestWFAutoStart"), "Expected exported createListItem fields to include TestWFAutoStart.");
                Assert.AreEqual("getCurrentListId", create.ListId.Type, "Expected createListItem listId placeholder to build as current-list lookup.");
                Assert.IsTrue(targeted.Count >= 1, "Expected at least one exported targeted list-item action.");
                Assert.IsTrue(targeted.All(a => string.Equals("getCurrentListId", a.ListId.Type, StringComparison.OrdinalIgnoreCase)), "Expected targeted list actions to use buildable current-list placeholders.");
                Assert.IsTrue(targeted.All(a => Convert.ToInt32(a.ItemId.Literal) == 1), "Expected targeted list actions to use buildable integer itemId placeholders.");
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
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
