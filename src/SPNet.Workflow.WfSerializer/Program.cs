using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SPNet.Workflow.WfSerializer
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var options = WfSerializerOptions.Parse(args);
                if (string.Equals(options.Mode, "build", StringComparison.OrdinalIgnoreCase))
                {
                    var config = SpNetToolConfig.Load(options.ConfigPath);
                    var cacheFolder = options.CacheFolder.OrIfEmpty(config.SpdCacheFolder).OrIfEmpty(Environment.GetEnvironmentVariable("SPNET_SPD_CACHE"));
                    WfSerializerOptions.ValidateCacheFolder(cacheFolder);
                    WfActivityBuilderSerializer.SerializeYamlWorkflow(options.WorkflowYamlPath, options.OutputXamlPath, cacheFolder, config);
                    Console.WriteLine("Saved XAML to " + options.OutputXamlPath);
                }
                else if (string.Equals(options.Mode, "export", StringComparison.OrdinalIgnoreCase))
                {
                    var config = SpNetToolConfig.Load(options.ConfigPath);
                    var cacheFolder = options.CacheFolder.OrIfEmpty(config.SpdCacheFolder).OrIfEmpty(Environment.GetEnvironmentVariable("SPNET_SPD_CACHE"));
                    WfActivityBuilderSerializer.ExportWorkflowYaml(options.InputXamlPath, options.OutputYamlPath, options.FormFieldXmlPath, cacheFolder);
                    Console.WriteLine("Saved YAML export to " + options.OutputYamlPath);
                }
                else if (string.Equals(options.Mode, "inspect", StringComparison.OrdinalIgnoreCase))
                {
                    var config = SpNetToolConfig.Load(options.ConfigPath);
                    options.SetCacheFolder(options.CacheFolder.OrIfEmpty(config.SpdCacheFolder).OrIfEmpty(Environment.GetEnvironmentVariable("SPNET_SPD_CACHE")));
                    WfSerializerOptions.ValidateCacheFolder(options.CacheFolder);
                    var report = WfActivityBuilderSerializer.InspectWorkflowXaml(options.InputXamlPath, options.CacheFolder);
                    if (string.IsNullOrWhiteSpace(options.OutputReportPath)) Console.WriteLine(report);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputReportPath)) ?? Environment.CurrentDirectory);
                        File.WriteAllText(options.OutputReportPath, report);
                        Console.WriteLine("Saved inspection report to " + options.OutputReportPath);
                    }
                }
                else
                {
                    var config = SpNetToolConfig.Load(options.ConfigPath);
                    options.SetCacheFolder(options.CacheFolder.OrIfEmpty(config.SpdCacheFolder).OrIfEmpty(Environment.GetEnvironmentVariable("SPNET_SPD_CACHE")));
                    WfSerializerOptions.ValidateCacheFolder(options.CacheFolder);
                    WfActivityBuilderSerializer.SerializeSampleWorkflow(options);
                    Console.WriteLine("Saved XAML to " + options.OutputXamlPath);
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }

    public sealed class WfSerializerOptions
    {
        public string CacheFolder { get; private set; } = string.Empty;
        public string OutputXamlPath { get; private set; } = string.Empty;
        public string InputXamlPath { get; private set; } = string.Empty;
        public string OutputReportPath { get; private set; } = string.Empty;
        public string OutputYamlPath { get; private set; } = string.Empty;
        public string FormFieldXmlPath { get; private set; } = string.Empty;
        public string WorkflowYamlPath { get; private set; } = string.Empty;
        public string ConfigPath { get; private set; } = string.Empty;
        public string WorkflowName { get; private set; } = "GeneratedWorkflow";
        public string Mode { get; private set; } = "generate";

        public void SetCacheFolder(string value) => CacheFolder = value ?? string.Empty;

        public static WfSerializerOptions Parse(IReadOnlyList<string> args)
        {
            if (args.Count == 0 || args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Usage: build --workflow <workflow.yml> --out <workflow.xaml> [--config <spnet.local.yml>] [--cache-folder <WebsiteCache>] OR export --xaml <workflow.xaml> --out <workflow.yml> [--form-field-xml <workflow.xaml.formfield.xml>] [--cache-folder <WebsiteCache>] OR inspect --in <workflow.xaml> [--cache-folder <WebsiteCache>]");
            }

            var options = new WfSerializerOptions();
            var first = args[0];
            var start = 0;
            if (!first.StartsWith("-", StringComparison.Ordinal))
            {
                options.Mode = first;
                start = 1;
            }
            for (var i = start; i < args.Count; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, "--cache-folder", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--cache", StringComparison.OrdinalIgnoreCase))
                {
                    options.CacheFolder = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--out", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--output-xaml", StringComparison.OrdinalIgnoreCase))
                {
                    options.OutputXamlPath = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--workflow", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--workflow-yaml", StringComparison.OrdinalIgnoreCase))
                {
                    options.WorkflowYamlPath = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--xaml", StringComparison.OrdinalIgnoreCase))
                {
                    options.InputXamlPath = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase))
                {
                    options.ConfigPath = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--in", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--input-xaml", StringComparison.OrdinalIgnoreCase))
                {
                    options.InputXamlPath = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--report", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--output-report", StringComparison.OrdinalIgnoreCase))
                {
                    options.OutputReportPath = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--output-yaml", StringComparison.OrdinalIgnoreCase))
                {
                    options.OutputYamlPath = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--form-field-xml", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--formfield", StringComparison.OrdinalIgnoreCase))
                {
                    options.FormFieldXmlPath = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--mode", StringComparison.OrdinalIgnoreCase))
                {
                    options.Mode = RequireValue(args, ref i, arg);
                }
                else if (string.Equals(arg, "--name", StringComparison.OrdinalIgnoreCase))
                {
                    options.WorkflowName = RequireValue(args, ref i, arg);
                }
                else
                {
                    throw new ArgumentException("Unknown option: " + arg);
                }
            }

            if (string.Equals(options.Mode, "build", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(options.WorkflowYamlPath)) throw new ArgumentException("Missing required --workflow value for build mode.");
                if (string.IsNullOrWhiteSpace(options.OutputXamlPath)) throw new ArgumentException("Missing required --out value for build mode.");
            }
            else if (string.Equals(options.Mode, "export", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(options.InputXamlPath)) throw new ArgumentException("Missing required --xaml value for export mode.");
                if (string.IsNullOrWhiteSpace(options.OutputYamlPath)) options.OutputYamlPath = options.OutputXamlPath;
                if (string.IsNullOrWhiteSpace(options.OutputYamlPath)) throw new ArgumentException("Missing required --out value for export mode.");
            }
            else if (string.Equals(options.Mode, "inspect", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(options.InputXamlPath)) throw new ArgumentException("Missing required --in value for inspect mode.");
            }
            else if (string.IsNullOrWhiteSpace(options.OutputXamlPath)) throw new ArgumentException("Missing required --out value.");
            return options;
        }

        public static void ValidateCacheFolder(string cacheFolder)
        {
            if (string.IsNullOrWhiteSpace(cacheFolder)) throw new ArgumentException("Missing SharePoint Designer WebsiteCache folder. Set --cache-folder, config spdCacheFolder, or SPNET_SPD_CACHE.");
            if (!Directory.Exists(cacheFolder)) throw new DirectoryNotFoundException("Cache folder not found: " + cacheFolder);
        }

        private static string RequireValue(IReadOnlyList<string> args, ref int index, string option)
        {
            if (++index >= args.Count) throw new ArgumentException("Missing value for " + option + ".");
            return args[index];
        }
    }

    internal static class StringExtensions
    {
        public static string OrIfEmpty(this string value, string fallback) => string.IsNullOrWhiteSpace(value) ? (fallback ?? string.Empty) : value;
    }
}
