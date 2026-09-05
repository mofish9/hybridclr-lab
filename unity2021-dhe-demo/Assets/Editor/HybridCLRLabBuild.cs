using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;
using System.Linq;
using System.Xml.Linq;

namespace HybridCLR.Lab.Editor
{
    internal static class HybridCLRLabBuild
    {
        private const string ManagedAssemblyName = "HybridCLR.ManagedCases";
        private const string ManagedAssemblyFileName = ManagedAssemblyName + ".dll";
        private const string ManagedAssemblyRuntimeFileName = ManagedAssemblyFileName + ".bytes";
        private const string MetadataStressAssemblyName = "HybridCLR.MetadataStress";
        private const string MetadataStressAssemblyFileName = MetadataStressAssemblyName + ".dll";
        private const string MetadataStressAssemblyRuntimeFileName = MetadataStressAssemblyFileName + ".bytes";
        private const string CrossAssemblyDerivedName = "HybridCLR.CrossAssemblyDerived";
        private const string CrossAssemblyDerivedFileName = CrossAssemblyDerivedName + ".dll";
        private const string CrossAssemblyDerivedRuntimeFileName = CrossAssemblyDerivedFileName + ".bytes";
        private const string ManagedCasesAotName = "HybridCLR.ManagedCasesAot";
        private const string HybridclrFork = "https://github.com/mofish9/hybridclr.git";
        private const string Il2cppPlusFork = "https://github.com/mofish9/il2cpp_plus.git";
        private const string ScenePath = "Assets/Scenes/HybridCLRLab.unity";
        private const string ManifestContractsFileName = "test-manifest.contracts";
        private const string GoldenContractFileName = "test-golden.json";
        private const string BenchmarkGoldenFileName = "benchmark-golden.json";
        private const string BuildIdentityFileName = "build-identity.json";
        private const string AndroidApplicationIdentifier = "com.mofish.hybridclrlab";
        private static readonly string[] AotMetadataAssemblies =
        {
            "mscorlib",
            "System",
            "System.Core",
            "HybridCLR.BoundaryContracts",
        };

        [MenuItem("HybridCLR Lab/Install Runtime")]
        public static void InstallRuntime()
        {
            string source = GetArgument("-labRuntimeSource");
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new InvalidOperationException("Missing -labRuntimeSource argument.");
            }

            ConfigureSettings();
            source = Path.GetFullPath(source);
            if (!Directory.Exists(Path.Combine(source, "hybridclr")))
            {
                throw new DirectoryNotFoundException("Merged libil2cpp source does not contain hybridclr: " + source);
            }

            InstallerController installer = new InstallerController();
            installer.InstallFromLocal(source);
            if (!installer.HasInstalledHybridCLR())
            {
                throw new InvalidOperationException("HybridCLR installation did not create a local libil2cpp runtime.");
            }

            Debug.Log("[HybridCLR Lab] Clean local runtime installed from: " + source);
        }

        [MenuItem("HybridCLR Lab/Generate And Build")]
        public static void GenerateAndBuild()
        {
            GenerateProjectArtifacts();
            BuildPlayer();
        }

        [MenuItem("HybridCLR Lab/Generate Only")]
        public static void GenerateOnly()
        {
            GenerateProjectArtifacts();
        }

        [MenuItem("HybridCLR Lab/Build Player Only")]
        public static void BuildPlayerOnly()
        {
            // The deterministic workflow stages the runtime plan from an
            // external build host. Refresh before building so Unity
            // does not reuse an older imported StreamingAssets snapshot.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureSettings();
            EnsureBuildTarget();
            EnsureBuildScene();
            string managedDll = GetArgument("-labManagedDll");
            string[] dheAotAssemblies = GetDheAotAssemblyPaths(managedDll);
            if (dheAotAssemblies.Length > 0)
            {
                StageDheAotAssemblies(dheAotAssemblies);
            }
            string buildIdentity = GetArgument("-labBuildIdentity");
            // The scripts-only pass is intentionally performed before guard
            // injection. Native hashes do not exist yet, so the strict
            // identity is staged only for the final Player compilation.
            bool scriptsOnly = string.Equals(
                GetArgument("-labDheBuildScriptsOnly"), "true", StringComparison.OrdinalIgnoreCase);
            if (!scriptsOnly && !string.IsNullOrWhiteSpace(buildIdentity))
            {
                StageDheBuildIdentity(Path.GetFullPath(buildIdentity));
            }
            if (string.Equals(GetArgument("-labDheForceRegenerate"), "true", StringComparison.OrdinalIgnoreCase))
            {
                ForceRefreshDheArtifacts(GetBuildTarget());
            }
            // Staging above may have created or replaced files after the
            // initial project refresh. Force-import the runtime plan and its
            // payloads immediately before Unity snapshots StreamingAssets.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            const string dheStreamingPlanDirectory = "Assets/StreamingAssets/HybridCLRLab/DheDemo";
            if (AssetDatabase.IsValidFolder(dheStreamingPlanDirectory))
            {
                AssetDatabase.ImportAsset(
                    dheStreamingPlanDirectory,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ImportRecursive |
                    ImportAssetOptions.ForceSynchronousImport);
            }
            BuildPlayer();
        }

        private static void GenerateProjectArtifacts()
        {
            ConfigureSettings();
            EnsureBuildTarget();
            EnsureBuildScene();

            EditorUserBuildSettings.development = false;
            string managedDll = GetArgument("-labManagedDll");
            if (string.IsNullOrWhiteSpace(managedDll))
            {
                throw new InvalidOperationException("Missing -labManagedDll argument.");
            }
            managedDll = Path.GetFullPath(managedDll);
            string[] dheAotAssemblies = GetDheAotAssemblyPaths(managedDll);
            string[] inputAssemblies = GetInputAssemblyPaths(managedDll, dheAotAssemblies);
            if (string.Equals(GetArgument("-labDheForceRegenerate"), "true", StringComparison.OrdinalIgnoreCase))
            {
                ForceRefreshDheArtifacts(GetBuildTarget());
            }
            StageDheAotAssemblies(dheAotAssemblies);
            GenerateHybridClrArtifacts(GetBuildTarget(), inputAssemblies, dheAotAssemblies);

            string runtimeAssemblyPath = string.Empty;
            foreach (string inputAssembly in inputAssemblies)
            {
                string runtimeAssemblySource = inputAssembly;
                if (dheAotAssemblies.Any(path => string.Equals(path, inputAssembly, StringComparison.OrdinalIgnoreCase)))
                {
                    runtimeAssemblySource = Path.Combine(
                        ProjectRoot(),
                        "HybridCLRData",
                        "AssembliesPostIl2CppStrip",
                        GetBuildTarget().ToString(),
                        Path.GetFileName(inputAssembly));
                }
                string runtimePath = CopyAssemblyToStreamingAssets(
                    runtimeAssemblySource,
                    Path.GetFileName(inputAssembly) + ".bytes");
                if (string.IsNullOrEmpty(runtimeAssemblyPath)) runtimeAssemblyPath = runtimePath;
            }
            StageAotMetadata();
            CopyBuildIdentityToStreamingAssets();
            CopyTestManifestToStreamingAssets();
            CopyTestManifestIndexToStreamingAssets();
            CopyTestManifestContractsToStreamingAssets();
            CopyGoldenContractToStreamingAssets();
            CopyBenchmarkPolicyToStreamingAssets();
            CopyBenchmarkGoldenToStreamingAssets();
            CopyMetadataBenchmarkPolicyToStreamingAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[HybridCLR Lab] Runtime assembly staged at: " + runtimeAssemblyPath);
        }

        private static void ForceRefreshDheArtifacts(BuildTarget target)
        {
            string targetName = target.ToString();
            string[] generatedDirectories =
            {
                Path.Combine(ProjectRoot(), "Library", "Bee", "artifacts", "WinPlayerBuildProgram", "ManagedStripped"),
                Path.Combine(ProjectRoot(), "Library", "Bee", "artifacts", "WinPlayerBuildProgram", "il2cppOutput"),
                Path.Combine(ProjectRoot(), "HybridCLRData", "AssembliesPostIl2CppStrip", targetName),
                Path.Combine(ProjectRoot(), "HybridCLRData", "StrippedAOTDllsTempProj", targetName),
            };
            foreach (string directory in generatedDirectories)
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                    Debug.Log("[HybridCLR Lab] DHE artifact refresh removed: " + directory);
                }
            }
        }

        private static void StageDheAotAssemblies(string[] sourcePaths)
        {
            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "Plugins", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            var desiredFiles = new HashSet<string>(
                (sourcePaths ?? Array.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => Path.GetFileName(path)),
                StringComparer.OrdinalIgnoreCase);
            var compileOnlySources = new List<string>();
            string boundaryContractPath = Path.Combine(
                LabRoot(), "artifacts", "managed-cases-aot", "StandaloneWindows64", "HybridCLR.BoundaryContracts.dll");
            if (File.Exists(boundaryContractPath))
            {
                // BoundaryContracts is referenced by the demo's editor/player
                // scripts but is not a hot-update/DHE assembly. Keep it as a
                // normal AOT compile reference without adding it to the DHE
                // assembly plan or MV set.
                compileOnlySources.Add(boundaryContractPath);
                desiredFiles.Add(Path.GetFileName(boundaryContractPath));
            }

            // This directory is owned by the DHE demo staging step. Remove a
            // DLL that belonged to an earlier assembly plan before importing
            // the current set, otherwise Unity can silently compile a stale
            // assembly into the Player outside the project-plan contract.
            foreach (string existingPath in Directory.GetFiles(destinationDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                if (desiredFiles.Contains(Path.GetFileName(existingPath)))
                {
                    continue;
                }
                File.Delete(existingPath);
                string metaPath = existingPath + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }
                Debug.Log("[HybridCLR Lab] Removed stale DHE AOT assembly: " + existingPath);
            }

            foreach (string sourcePathValue in sourcePaths ?? Array.Empty<string>())
            {
                string sourcePath = Path.GetFullPath(sourcePathValue);
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("DHE AOT assembly was not found.", sourcePath);
                string destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)));
                File.Copy(sourcePath, destinationPath, true);
                string assetPath = "Assets/Plugins/HybridCLRLab/" + Path.GetFileName(sourcePath).Replace('\\', '/');
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[HybridCLR Lab] DHE AOT assembly staged: " + destinationPath +
                    " bytes=" + new FileInfo(destinationPath).Length);
            }

            foreach (string sourcePath in compileOnlySources)
            {
                string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destinationPath, true);
                string assetPath = "Assets/Plugins/HybridCLRLab/" + Path.GetFileName(sourcePath).Replace('\\', '/');
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[HybridCLR Lab] Compile-only AOT reference staged: " + destinationPath +
                    " bytes=" + new FileInfo(destinationPath).Length);
            }
        }

        private static void GenerateHybridClrArtifacts(BuildTarget target, string[] inputAssemblies, string[] dheAotAssemblies)
        {
            CompileDllCommand.CompileDll(target, false);
            string hotUpdateDirectory = Path.GetFullPath(Path.Combine(ProjectRoot(), HybridCLR.Editor.SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)));
            Directory.CreateDirectory(hotUpdateDirectory);
            foreach (string inputAssembly in inputAssemblies)
            {
                File.Copy(inputAssembly, Path.Combine(hotUpdateDirectory, Path.GetFileName(inputAssembly)), true);
            }

            Il2CppDefGeneratorCommand.GenerateIl2CppDef();
            LinkGeneratorCommand.GenerateLinkXml(target);
            PreserveDheAssemblyMetadata(dheAotAssemblies);
            StripAOTDllCommand.GenerateStripedAOTDlls(target);
            MethodBridgeGeneratorCommand.GenerateMethodBridgeAndReversePInvokeWrapper(target);
            AOTReferenceGeneratorCommand.GenerateAOTGenericReference(target);
        }

        private static void PreserveDheAssemblyMetadata(string[] managedDlls)
        {
            if (managedDlls == null || managedDlls.Length == 0)
            {
                return;
            }

            string linkPath = Path.Combine(ProjectRoot(), "Assets", "HybridCLRGenerate", "link.xml");
            if (!File.Exists(linkPath))
            {
                throw new FileNotFoundException("Generated linker configuration was not found.", linkPath);
            }

            XDocument document = XDocument.Load(linkPath, LoadOptions.PreserveWhitespace);
            XElement linker = document.Root;
            if (linker == null || !string.Equals(linker.Name.LocalName, "linker", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Generated linker configuration has no <linker> root.");
            }

            foreach (string managedDll in managedDlls)
            {
                string assemblyName = Path.GetFileNameWithoutExtension(Path.GetFileName(managedDll));
                XElement assembly = linker.Elements("assembly")
                    .FirstOrDefault(element => string.Equals(
                        (string)element.Attribute("fullname"), assemblyName, StringComparison.Ordinal));
                if (assembly == null)
                {
                    assembly = new XElement("assembly",
                        new XAttribute("fullname", assemblyName),
                        new XAttribute("preserve", "all"));
                    linker.Add(assembly);
                }
                else
                {
                    assembly.SetAttributeValue("preserve", "all");
                }
                Debug.Log("[HybridCLR Lab] DHE metadata preservation enabled for: " + assemblyName);
            }

            document.Save(linkPath, SaveOptions.DisableFormatting);
        }

        private static void BuildPlayer()
        {
            BuildTarget target = GetBuildTarget();
            string buildPath = GetArgument("-labBuildPath");
            if (string.IsNullOrWhiteSpace(buildPath))
            {
                string fileName = target == BuildTarget.Android ? "HybridCLRLab-arm64.apk" : "HybridCLRLab.exe";
                buildPath = Path.Combine(ProjectRoot(), "Builds", "Baseline-Clean", fileName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(buildPath))!);
            BuildOptions buildOptions = BuildOptions.CleanBuildCache;
            if (string.Equals(GetArgument("-labDheBuildScriptsOnly"), "true", StringComparison.OrdinalIgnoreCase))
            {
                buildOptions = BuildOptions.BuildScriptsOnly;
            }
            if (string.Equals(GetArgument("-labDheReuseGeneratedCpp"), "true", StringComparison.OrdinalIgnoreCase))
            {
                // The DHE workflow can inject guards after IL2CPP emits C++.
                // Reusing that source snapshot keeps the second compilation
                // deterministic and avoids a frontend race.
                buildOptions = BuildOptions.None;
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.GetFullPath(buildPath),
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = buildOptions,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[HybridCLR Lab] Player build result: {report.summary.result}, path: {options.locationPathName}");
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException("HybridCLR Lab Player build failed.");
            }
            if (!File.Exists(options.locationPathName))
            {
                throw new BuildFailedException("HybridCLR Lab Player build reported success without producing the requested file: " + options.locationPathName);
            }
            if (string.Equals(GetArgument("-labDhePreserveBeeInputs"), "true", StringComparison.OrdinalIgnoreCase))
            {
                PreserveBeeStagingInputs(options.locationPathName);
            }
        }

        private static void PreserveBeeStagingInputs(string buildPath)
        {
            string sourceRoot = Path.Combine(ProjectRoot(), "Temp", "StagingArea");
            if (!Directory.Exists(sourceRoot))
            {
                throw new DirectoryNotFoundException("DHE Bee staging inputs were not produced: " + sourceRoot);
            }
            string destinationRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(buildPath))!, "DHE-BeeStaging");
            if (Directory.Exists(destinationRoot))
            {
                Directory.Delete(destinationRoot, true);
            }
            foreach (string sourcePath in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relativePath = sourcePath.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destinationPath = Path.Combine(destinationRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, true);
            }
            Debug.Log("[HybridCLR Lab] Preserved DHE Bee staging inputs: " + destinationRoot);
        }

        private static void ConfigureSettings()
        {
            BuildTarget target = GetBuildTarget();
            HybridCLRSettings settings = HybridCLRSettings.LoadOrCreate();
            settings.enable = true;
            settings.useGlobalIl2cpp = false;
            string hybridclrRepo = GetArgument("-labHybridClrRepo");
            if (!string.IsNullOrWhiteSpace(hybridclrRepo))
            {
                settings.hybridclrRepoURL = hybridclrRepo;
            }
            else if (string.IsNullOrWhiteSpace(settings.hybridclrRepoURL))
            {
                settings.hybridclrRepoURL = HybridclrFork;
            }

            string il2cppPlusRepo = GetArgument("-labIl2CppPlusRepo");
            if (!string.IsNullOrWhiteSpace(il2cppPlusRepo))
            {
                settings.il2cppPlusRepoURL = il2cppPlusRepo;
            }
            else if (string.IsNullOrWhiteSpace(settings.il2cppPlusRepoURL))
            {
                settings.il2cppPlusRepoURL = Il2cppPlusFork;
            }

            string configuredHotUpdateAssemblies = GetArgument("-labHotUpdateAssemblies");
            if (!string.IsNullOrWhiteSpace(configuredHotUpdateAssemblies))
            {
                settings.hotUpdateAssemblies = SplitArgumentList(configuredHotUpdateAssemblies);
            }
            else if ((settings.hotUpdateAssemblies == null || settings.hotUpdateAssemblies.Length == 0) &&
                (settings.hotUpdateAssemblyDefinitions == null || settings.hotUpdateAssemblyDefinitions.Length == 0))
            {
                settings.hotUpdateAssemblies = new[] { ManagedAssemblyName, MetadataStressAssemblyName, CrossAssemblyDerivedName };
            }

            string configuredDheAotAssemblies = GetArgument("-labDheAotAssemblies");
            if (!string.IsNullOrWhiteSpace(configuredDheAotAssemblies))
            {
                settings.dheAotAssemblies = SplitArgumentList(configuredDheAotAssemblies)
                    .Select(item => Path.GetFileNameWithoutExtension(Path.GetFileName(item)))
                    .ToArray();
            }
            else if ((settings.dheAotAssemblies == null || settings.dheAotAssemblies.Length == 0) &&
                !string.IsNullOrWhiteSpace(GetArgument("-labDheAssemblyName")))
            {
                settings.dheAotAssemblies = new[] { GetArgument("-labDheAssemblyName") };
            }

            string managedDll = GetArgument("-labManagedDll");
            string managedAssemblyDirectory = string.IsNullOrWhiteSpace(managedDll)
                ? Path.Combine(LabRoot(), "artifacts", "managed-cases")
                : Path.GetDirectoryName(Path.GetFullPath(managedDll))!;
            string externalAssemblyDirectory = GetArgument("-labExternalAssemblyDir");
            if (!string.IsNullOrWhiteSpace(externalAssemblyDirectory))
            {
                settings.externalHotUpdateAssembliyDirs = SplitArgumentList(externalAssemblyDirectory);
            }
            else if (!string.IsNullOrWhiteSpace(managedDll))
            {
                // Each baseline/current generation must resolve references from
                // the exact input directory for that invocation. Do not retain
                // an absolute path from a previous Unity run.
                settings.externalHotUpdateAssembliyDirs = new[] { managedAssemblyDirectory };
            }
            else if (settings.externalHotUpdateAssembliyDirs == null || settings.externalHotUpdateAssembliyDirs.Length == 0)
            {
                settings.externalHotUpdateAssembliyDirs = new[] { managedAssemblyDirectory };
            }

            string patchAssemblies = GetArgument("-labPatchAotAssemblies");
            if (!string.IsNullOrWhiteSpace(patchAssemblies))
            {
                settings.patchAOTAssemblies = SplitArgumentList(patchAssemblies);
            }
            else if (settings.patchAOTAssemblies == null || settings.patchAOTAssemblies.Length == 0)
            {
                settings.patchAOTAssemblies = AotMetadataAssemblies;
            }

            if (string.IsNullOrWhiteSpace(settings.hotUpdateDllCompileOutputRootDir))
            {
                settings.hotUpdateDllCompileOutputRootDir = "HybridCLRData/HotUpdateDlls";
            }
            if (string.IsNullOrWhiteSpace(settings.strippedAOTDllOutputRootDir))
            {
                settings.strippedAOTDllOutputRootDir = "HybridCLRData/AssembliesPostIl2CppStrip";
            }
            if (string.IsNullOrWhiteSpace(settings.outputLinkFile))
            {
                settings.outputLinkFile = "HybridCLRGenerate/link.xml";
            }
            if (string.IsNullOrWhiteSpace(settings.outputAOTGenericReferenceFile))
            {
                settings.outputAOTGenericReferenceFile = "HybridCLRGenerate/AOTGenericReferences.cs";
            }
            HybridCLRSettings.Save();

            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            if (group == BuildTargetGroup.Standalone)
            {
                // This setting is stored in Library and can be left enabled by an interactive export.
                EditorUserBuildSettings.SetPlatformSettings("Standalone", "CreateSolution", "false");
            }
            else if (target == BuildTarget.Android)
            {
                PlayerSettings.SetApplicationIdentifier(group, AndroidApplicationIdentifier);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.Android.buildApkPerCpuArchitecture = false;
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            }

            string codeGeneration = GetArgument("-labIl2CppCodeGeneration");
            if (string.IsNullOrWhiteSpace(codeGeneration))
            {
                codeGeneration = nameof(Il2CppCodeGeneration.OptimizeSpeed);
            }

            if (!Enum.TryParse(codeGeneration, false, out Il2CppCodeGeneration parsedCodeGeneration))
            {
                throw new ArgumentException("-labIl2CppCodeGeneration must be OptimizeSpeed or OptimizeSize.");
            }

#if UNITY_2022_1_OR_NEWER
            NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(group);
            PlayerSettings.SetIl2CppCodeGeneration(namedBuildTarget, parsedCodeGeneration);
#else
            EditorUserBuildSettings.il2CppCodeGeneration = parsedCodeGeneration;
#endif
            Debug.Log("[HybridCLR Lab] IL2CPP code generation: " + parsedCodeGeneration);
        }

        private static string[] SplitArgumentList(string value)
        {
            string trimmed = value == null ? string.Empty : value.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                // The build host passes array-valued arguments as JSON so a valid
                // Windows path containing ';' remains a single item. Unity's
                // JsonUtility cannot deserialize a root array, so wrap it in
                // a tiny object before parsing.
                try
                {
                    StringArrayArgument parsed = JsonUtility.FromJson<StringArrayArgument>(
                        "{\"items\":" + trimmed + "}");
                    if (parsed != null && parsed.items != null)
                    {
                        return parsed.items
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Select(item => item.Trim())
                            .ToArray();
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Unable to parse JSON DHE argument list; using legacy delimiters: " + exception.Message);
                }
            }
            return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToArray();
        }

        [Serializable]
        private sealed class StringArrayArgument
        {
            public string[] items;
        }

        private static string[] GetDheAotAssemblyPaths(string managedDll)
        {
            string configured = GetArgument("-labDheAotAssemblies");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return SplitArgumentList(configured).Select(item => Path.GetFullPath(item)).ToArray();
            }
            if (!string.IsNullOrWhiteSpace(GetArgument("-labDheAssemblyName")) && !string.IsNullOrWhiteSpace(managedDll))
            {
                return new[] { Path.GetFullPath(managedDll) };
            }
            return Array.Empty<string>();
        }

        private static string[] GetInputAssemblyPaths(string managedDll, string[] dheAotAssemblies)
        {
            var paths = new List<string>();
            if (dheAotAssemblies != null) paths.AddRange(dheAotAssemblies);
            if (paths.Count == 0 && !string.IsNullOrWhiteSpace(managedDll))
            {
                paths.Add(Path.GetFullPath(managedDll));
                string directory = Path.GetDirectoryName(Path.GetFullPath(managedDll))!;
                foreach (string name in new[] { MetadataStressAssemblyFileName, CrossAssemblyDerivedFileName })
                {
                    string dependency = Path.Combine(directory, name);
                    if (File.Exists(dependency)) paths.Add(dependency);
                }
            }
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void EnsureBuildTarget()
        {
            BuildTarget target = GetBuildTarget();
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                {
                    throw new InvalidOperationException("Unable to switch Tuanjie active build target to " + target + ".");
                }
            }
        }

        private static void EnsureBuildScene()
        {
            string absolutePath = Path.Combine(ProjectRoot(), ScenePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, absolutePath))
                {
                    throw new IOException("Unable to create test scene: " + absolutePath);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static string CopyManagedAssemblyToStreamingAssets(string sourcePath)
        {
            return CopyAssemblyToStreamingAssets(sourcePath, ManagedAssemblyRuntimeFileName);
        }

        private static string CopyAssemblyToStreamingAssets(string sourcePath, string destinationFileName)
        {
            sourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Managed cases assembly was not found.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, destinationFileName);
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        private static string CopyTestManifestToStreamingAssets()
        {
            string sourcePath = Path.Combine(LabRoot(), "manifests", "test-manifest.json");
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Test manifest was not found.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, "test-manifest.json");
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        private static void CopyAotMetadataToStreamingAssets()
        {
            string sourceDirectory = Path.Combine(
                ProjectRoot(),
                "HybridCLRData",
                "AssembliesPostIl2CppStrip",
                GetBuildTarget().ToString());
            string destinationDirectory = Path.Combine(
                ProjectRoot(),
                "Assets",
                "StreamingAssets",
                "HybridCLRLab",
                "AotMetadata");
            Directory.CreateDirectory(destinationDirectory);

            foreach (string assemblyName in AotMetadataAssemblies)
            {
                string sourcePath = Path.Combine(sourceDirectory, assemblyName + ".dll");
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException("Stripped AOT metadata assembly was not found.", sourcePath);
                }

                string destinationPath = Path.Combine(destinationDirectory, assemblyName + ".dll.bytes");
                File.Copy(sourcePath, destinationPath, true);
            }

            File.WriteAllLines(
                Path.Combine(destinationDirectory, "aot-metadata.ids"),
                AotMetadataAssemblies);
        }

        private static void StageAotMetadata()
        {
            string packaging = GetArgument("-labAotMetadataPackaging");
            if (string.IsNullOrWhiteSpace(packaging))
            {
                packaging = "include";
            }

            if (packaging == "include")
            {
                CopyAotMetadataToStreamingAssets();
                return;
            }
            if (packaging != "exclude")
            {
                throw new ArgumentException("-labAotMetadataPackaging must be include or exclude.");
            }

            string destinationDirectory = Path.Combine(
                ProjectRoot(),
                "Assets",
                "StreamingAssets",
                "HybridCLRLab",
                "AotMetadata");
            if (Directory.Exists(destinationDirectory))
            {
                Directory.Delete(destinationDirectory, true);
            }
        }

        private static string CopyBuildIdentityToStreamingAssets()
        {
            string sourcePath = GetArgument("-labBuildIdentity");
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new InvalidOperationException("Missing -labBuildIdentity argument.");
            }
            sourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Build identity was not found.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, BuildIdentityFileName);
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        private static void StageDheBuildIdentity(string sourcePath)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("DHE build identity was not found.", sourcePath);
            }

            DheBuildIdentityData identity = JsonUtility.FromJson<DheBuildIdentityData>(File.ReadAllText(sourcePath));
            if (identity == null || identity.identityVersion != 1 ||
                identity.aotAssemblyNames == null || identity.aotAssemblyNames.Length == 0 ||
                string.IsNullOrWhiteSpace(identity.aotSnapshotSha256) ||
                !string.Equals(identity.aotSnapshotKind, "managed-assembly-plus-generated-cpp-v1", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(identity.nativeGuardSourceSha256) ||
                string.IsNullOrWhiteSpace(identity.nativeManifestSha256))
            {
                throw new InvalidDataException("DHE build identity is incomplete; native guard identity must be generated before Player build.");
            }
            NormalizeSha256(identity.aotSnapshotSha256, "aotSnapshotSha256");
            NormalizeSha256(identity.aotAssemblySetSha256, "aotAssemblySetSha256");
            NormalizeSha256(identity.nativeGuardSourceSha256, "nativeGuardSourceSha256");
            NormalizeSha256(identity.nativeManifestSha256, "nativeManifestSha256");
            string streamingDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(streamingDirectory);
            File.Copy(sourcePath, Path.Combine(streamingDirectory, BuildIdentityFileName), true);
        }

        private static string NormalizeSha256(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64 ||
                value.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("DHE build identity field '" + fieldName + "' must be a SHA-256 hex string.");
            }
            return value.ToLowerInvariant();
        }

        [Serializable]
        private sealed class DheBuildIdentityData
        {
            public int identityVersion;
            public string aotAssemblySetSha256;
            public string[] aotAssemblyNames;
            public string aotSnapshotSha256;
            public string aotSnapshotKind;
            public string nativeGuardSourceSha256;
            public string nativeManifestSha256;
        }

        private static string CopyTestManifestIndexToStreamingAssets()
        {
            string sourcePath = Path.Combine(LabRoot(), "manifests", "test-manifest.ids");
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Test manifest index was not found.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, "test-manifest.ids");
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        private static string CopyTestManifestContractsToStreamingAssets()
        {
            string sourcePath = Path.Combine(LabRoot(), "manifests", "test-manifest.contracts");
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Test manifest contracts were not found.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, ManifestContractsFileName);
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        private static string CopyGoldenContractToStreamingAssets()
        {
            string sourcePath = Path.Combine(LabRoot(), "manifests", GoldenContractFileName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Golden contract was not found.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, GoldenContractFileName);
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        private static string CopyBenchmarkPolicyToStreamingAssets()
        {
            string sourcePath = Path.Combine(LabRoot(), "manifests", "benchmark-policy.json");
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Benchmark policy was not found.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, "benchmark-policy.json");
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        private static string CopyBenchmarkGoldenToStreamingAssets()
        {
            string sourcePath = Path.Combine(LabRoot(), "manifests", BenchmarkGoldenFileName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Benchmark golden contract was not found.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, BenchmarkGoldenFileName);
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        private static string CopyMetadataBenchmarkPolicyToStreamingAssets()
        {
            string sourcePath = Path.Combine(LabRoot(), "manifests", "metadata-benchmark-policy.json");
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Metadata benchmark policy was not found.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, "metadata-benchmark-policy.json");
            File.Copy(sourcePath, destinationPath, true);
            return destinationPath;
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)!.FullName;
        }

        private static string LabRoot()
        {
            string value = GetArgument("-labRoot");
            return string.IsNullOrWhiteSpace(value)
                ? Directory.GetParent(ProjectRoot())!.FullName
                : Path.GetFullPath(value);
        }

        private static BuildTarget GetBuildTarget()
        {
            string value = GetArgument("-labTarget");
            if (string.IsNullOrWhiteSpace(value) || value == BuildTarget.StandaloneWindows64.ToString())
            {
                return BuildTarget.StandaloneWindows64;
            }
            if (value == BuildTarget.Android.ToString())
            {
                return BuildTarget.Android;
            }

            throw new ArgumentException("-labTarget must be StandaloneWindows64 or Android.");
        }

        private static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return string.Empty;
        }
    }
}
