using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace HybridCLR.Lab.Editor
{
    /// <summary>
    /// Minimal C# project adapter consumed by HybridCLR.DheTool. The demo
    /// deliberately keeps resource/device assertions out of the package and
    /// exposes the same phase-bound methods as a production project.
    /// </summary>
    public static class HybridCLRDheWorkflowBuild
    {
        public static void Prepare()
        {
            BuildTarget target = ParseTarget(Argument("-dheTarget"));
            string outputRoot = Path.GetFullPath(Argument("-dheOutputRoot"));
            string baselineRoot = Path.GetFullPath(Argument("-dheBaselineRoot"));
            string currentRoot = Path.GetFullPath(Argument("-dheCurrentRoot"));
            string mode = OptionalArgument("-dheMode") ?? "Exploratory";
            DheBuildPipeline.ValidateAssemblyScope(true, out string[] hotfix, out string[] dhe);
            DheBuildPipeline.GenerateCurrentArtifacts(target);
            string strippedRoot = Path.GetFullPath(SettingsUtil.GetAssembliesPostIl2CppStripDir(target));
            CopyAssemblies(strippedRoot, currentRoot, dhe);
            string configuredBaseline = Environment.GetEnvironmentVariable("DHE_BASELINE_ROOT");
            if (!string.IsNullOrWhiteSpace(configuredBaseline) && Directory.Exists(configuredBaseline))
            {
                CopyAssemblies(Path.GetFullPath(configuredBaseline), baselineRoot, dhe);
            }
            if (string.Equals(mode, "Release", StringComparison.OrdinalIgnoreCase) &&
                !Directory.Exists(baselineRoot))
            {
                throw new BuildFailedException("Release Prepare requires a previous baseline root.");
            }
            if (!Directory.Exists(baselineRoot))
            {
                CopyAssemblies(strippedRoot, baselineRoot, dhe);
            }
            var report = new PrepareReport
            {
                schemaVersion = 1,
                format = "hybridclr.dhe-project-adapter-prepare.json",
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                passed = true,
                toolchainContractVersion = 1,
                target = target.ToString(),
                mode = mode,
                pathSemantics = "workspace-absolute-v1",
                projectPath = Directory.GetParent(Application.dataPath).FullName,
                settingsFile = Path.GetFullPath("ProjectSettings/HybridCLRSettings.asset"),
                baselineRoot = baselineRoot,
                currentRoot = currentRoot,
                baselineSourceRoot = baselineRoot,
                currentSourceRoot = strippedRoot,
                runtimeAssemblySourceRoot = strippedRoot,
                baselineGeneratedFromCurrent = !string.Equals(baselineRoot, strippedRoot, StringComparison.OrdinalIgnoreCase),
                aotAssemblies = dhe,
                hotUpdateAssemblies = hotfix,
            };
            WriteJson(Path.Combine(outputRoot, "adapter", "prepare.json"), report);
        }

        public static void StageRuntimePlan()
        {
            throw new BuildFailedException("The demo adapter requires a validated project plan; use the C# host preflight before staging.");
        }

        public static void BuildScriptsOnly() => throw new BuildFailedException("Demo Player build requires a project-owned smoke configuration.");

        public static void BuildFinalPlayer() => throw new BuildFailedException("Demo Player build requires a project-owned smoke configuration.");

        private static void CopyAssemblies(string sourceRoot, string destinationRoot, IEnumerable<string> names)
        {
            Directory.CreateDirectory(destinationRoot);
            foreach (string name in names)
            {
                string source = Path.Combine(sourceRoot, name + ".dll");
                if (!File.Exists(source)) throw new FileNotFoundException("DHE stripped assembly was not found", source);
                File.Copy(source, Path.Combine(destinationRoot, name + ".dll"), true);
            }
        }

        private static string Argument(string name) => OptionalArgument(name) ?? throw new InvalidOperationException("Missing Unity argument: " + name);

        private static string OptionalArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static BuildTarget ParseTarget(string value)
        {
            if (Enum.TryParse(value, true, out BuildTarget target)) return target;
            throw new InvalidOperationException("Unsupported DHE target: " + value);
        }

        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(value, true));
        }

        [Serializable]
        private sealed class PrepareReport
        {
            public int schemaVersion;
            public string format;
            public string generatedAtUtc;
            public bool passed;
            public int toolchainContractVersion;
            public string target;
            public string mode;
            public string pathSemantics;
            public string projectPath;
            public string settingsFile;
            public string baselineRoot;
            public string currentRoot;
            public string baselineSourceRoot;
            public string currentSourceRoot;
            public string runtimeAssemblySourceRoot;
            public bool baselineGeneratedFromCurrent;
            public string[] aotAssemblies;
            public string[] hotUpdateAssemblies;
        }
    }
}
