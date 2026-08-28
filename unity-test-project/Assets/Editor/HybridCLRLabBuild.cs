using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;

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
        private const string MetadataStressPrewarmManifestFileName = "metadata-stress-prewarm.json";
        private const string CrossAssemblyDerivedName = "HybridCLR.CrossAssemblyDerived";
        private const string CrossAssemblyDerivedFileName = CrossAssemblyDerivedName + ".dll";
        private const string CrossAssemblyDerivedRuntimeFileName = CrossAssemblyDerivedFileName + ".bytes";
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
            ConfigureSettings();
            EnsureBuildTarget();
            EnsureBuildScene();
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
            string metadataStressDll = Path.Combine(Path.GetDirectoryName(managedDll)!, MetadataStressAssemblyFileName);
            string crossAssemblyDerivedDll = Path.Combine(Path.GetDirectoryName(managedDll)!, CrossAssemblyDerivedFileName);
            GenerateHybridClrArtifacts(GetBuildTarget(), managedDll, metadataStressDll, crossAssemblyDerivedDll);

            string runtimeAssemblyPath = CopyManagedAssemblyToStreamingAssets(managedDll);
            CopyAssemblyToStreamingAssets(metadataStressDll, MetadataStressAssemblyRuntimeFileName);
            CopyMetadataStressPrewarmManifestToStreamingAssets(GetArgument("-labPrewarmManifest"));
            CopyAssemblyToStreamingAssets(crossAssemblyDerivedDll, CrossAssemblyDerivedRuntimeFileName);
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

        private static void GenerateHybridClrArtifacts(BuildTarget target, string managedDll, string metadataStressDll, string crossAssemblyDerivedDll)
        {
            CompileDllCommand.CompileDll(target, false);
            string hotUpdateDirectory = Path.GetFullPath(Path.Combine(ProjectRoot(), HybridCLR.Editor.SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)));
            Directory.CreateDirectory(hotUpdateDirectory);
            File.Copy(managedDll, Path.Combine(hotUpdateDirectory, ManagedAssemblyFileName), true);
            File.Copy(metadataStressDll, Path.Combine(hotUpdateDirectory, MetadataStressAssemblyFileName), true);
            File.Copy(crossAssemblyDerivedDll, Path.Combine(hotUpdateDirectory, CrossAssemblyDerivedFileName), true);

            Il2CppDefGeneratorCommand.GenerateIl2CppDef();
            LinkGeneratorCommand.GenerateLinkXml(target);
            StripAOTDllCommand.GenerateStripedAOTDlls(target);
            MethodBridgeGeneratorCommand.GenerateMethodBridgeAndReversePInvokeWrapper(target);
            AOTReferenceGeneratorCommand.GenerateAOTGenericReference(target);
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
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.GetFullPath(buildPath),
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.CleanBuildCache,
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
        }

        private static void ConfigureSettings()
        {
            BuildTarget target = GetBuildTarget();
            HybridCLRSettings settings = HybridCLRSettings.LoadOrCreate();
            settings.enable = true;
            settings.useGlobalIl2cpp = false;
            settings.hybridclrRepoURL = HybridclrFork;
            settings.il2cppPlusRepoURL = Il2cppPlusFork;
            settings.hotUpdateAssemblyDefinitions = null;
            settings.hotUpdateAssemblies = new[] { ManagedAssemblyName, MetadataStressAssemblyName, CrossAssemblyDerivedName };
            string managedDll = GetArgument("-labManagedDll");
            string managedAssemblyDirectory = string.IsNullOrWhiteSpace(managedDll)
                ? Path.Combine(LabRoot(), "artifacts", "managed-cases")
                : Path.GetDirectoryName(Path.GetFullPath(managedDll))!;
            settings.externalHotUpdateAssembliyDirs = new[] { managedAssemblyDirectory };
            settings.patchAOTAssemblies = AotMetadataAssemblies;
            settings.hotUpdateDllCompileOutputRootDir = "HybridCLRData/HotUpdateDlls";
            settings.strippedAOTDllOutputRootDir = "HybridCLRData/AssembliesPostIl2CppStrip";
            settings.outputLinkFile = "HybridCLRGenerate/link.xml";
            settings.outputAOTGenericReferenceFile = "HybridCLRGenerate/AOTGenericReferences.cs";
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

        private static string CopyMetadataStressPrewarmManifestToStreamingAssets(string requestedSourcePath)
        {
            string sourcePath = string.IsNullOrWhiteSpace(requestedSourcePath)
                ? Path.Combine(LabRoot(), "reports", "prewarm-manifest-stress.json")
                : Path.GetFullPath(requestedSourcePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Metadata stress prewarm manifest was not found. Generate it before building the Player.", sourcePath);
            }

            string destinationDirectory = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = Path.Combine(destinationDirectory, MetadataStressPrewarmManifestFileName);
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
