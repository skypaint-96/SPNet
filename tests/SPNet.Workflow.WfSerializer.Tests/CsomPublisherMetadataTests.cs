using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SPNet.Workflow.Publisher.Csom;

namespace SPNet.Workflow.WfSerializer.Tests
{
    [TestClass]
    public sealed class CsomPublisherMetadataTests
    {
        [TestMethod]
        public void DiscoverFormFieldSidecarPath_AppendsSidecarSuffix()
        {
            Assert.AreEqual(@"artifacts\workflow.xaml.formfield.xml", Program.DiscoverFormFieldSidecarPath(@"artifacts\workflow.xaml"));
        }

        [TestMethod]
        public void ComputeInitiationUrl_UsesCompactDefinitionIdShape()
        {
            var definitionId = Guid.Parse("064d6913-9616-415e-bbbd-492dc28e6add");

            Assert.AreEqual("wfsvc/064d69139616415ebbbd492dc28e6add/WFInitForm.aspx", Program.ComputeInitiationUrl(definitionId));
        }

        [TestMethod]
        public void NormalizeFormFieldXml_ValidatesFieldsRootAndPreservesMetadata()
        {
            var xml = "<Fields><Field Name=\"ExampleStringParam\" FormType=\"Initiation\" DisplayName=\"Example\" Direction=\"None\" Type=\"Text\"><Default>abc</Default></Field></Fields>";

            var normalized = Program.NormalizeFormFieldXml(xml);

            StringAssert.StartsWith(normalized, "<Fields><Field Name=\"ExampleStringParam\"");
            StringAssert.Contains(normalized, "<Default>abc</Default>");
        }

        [TestMethod]
        public void NormalizeFormFieldXml_RejectsNonFieldsRoot()
        {
            Assert.ThrowsException<ArgumentException>(() => Program.NormalizeFormFieldXml("<Field />"));
        }

        [TestMethod]
        public void PublishOptionsParse_AcceptsOptionalFormFieldXmlArgument()
        {
            var directory = Path.Combine(Path.GetTempPath(), "SPNetPublisherTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var xamlPath = Path.Combine(directory, "workflow.xaml");
            var formFieldPath = xamlPath + ".formfield.xml";
            File.WriteAllText(xamlPath, "<Activity />");
            File.WriteAllText(formFieldPath, "<Fields />");

            try
            {
                var options = PublishOptions.Parse(new[]
                {
                    "--site-url", "https://sharepoint.example/sites/test",
                    "--workflow-name", "Parameterized Workflow",
                    "--xaml", xamlPath,
                    "--target-type", "Site",
                    "--form-field-xml", formFieldPath
                });

                Assert.AreEqual(formFieldPath, options.FormFieldXmlPath);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
