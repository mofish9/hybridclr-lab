using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HybridCLR.Editor.Commands;
using UnityEditor.Build;
using UnityEngine;

namespace HybridCLR.Lab.Editor
{
    public static class HybridCLRDheCppResolverRegression
    {
        public static void Run()
        {
            string outputRoot = Path.GetFullPath(Argument("-dheResolverOutput"));
            string engineWorkflow = Argument("-dheResolverEngineWorkflow");
            if (engineWorkflow != "Unity2021Standard" && engineWorkflow != "Unity2022Fgs" &&
                engineWorkflow != "Tuanjie2022Fgs")
                throw new ArgumentException("Unsupported DHE resolver engine workflow: " + engineWorkflow + ".");
            string resolverSource = Path.GetFullPath(
                "Packages/com.code-philosophy.hybridclr/Editor/Commands/DheBuildPipeline.cs");
            if (!File.Exists(resolverSource))
                throw new FileNotFoundException("DHE resolver source is missing.", resolverSource);
            Directory.CreateDirectory(outputRoot);
            List<ResolverCheck> checks = new List<ResolverCheck>();
            List<string> errors = new List<string>();
            RunCheck(checks, errors, "methoddef-token-overload-no-comments",
                () => ValidateMethodDefTokenOverload(Path.Combine(outputRoot, "methoddef-overload")));
            RunCheck(checks, errors, "generic-method-table-overload-no-comments",
                () => ValidateGenericMethodTableOverload(Path.Combine(outputRoot, "generic-overload")));
            RunCheck(checks, errors, "managed-signature-conflict-rejected",
                () => ValidateManagedSignatureConflict(Path.Combine(outputRoot, "signature-conflict")));
            RunCheck(checks, errors, "managed-signature-complex-parameters",
                () => ValidateManagedSignatureComplexParameters(
                    Path.Combine(outputRoot, "signature-complex-parameters")));
            RunCheck(checks, errors, "pointer-count-tamper-rejected",
                () => ValidatePointerCountTamper(Path.Combine(outputRoot, "pointer-count-tamper")));
            RunCheck(checks, errors, "generic-native-owner-conflict-rejected",
                () => ValidateGenericNativeOwnerConflict(Path.Combine(outputRoot, "generic-owner-conflict")));
            RunCheck(checks, errors, "bee-graph-regeneration-reapplies-guard",
                ValidateBeeGraphRegeneration);
            RunCheck(checks, errors, "bee-attempt-limit-rejected",
                ValidateBeeAttemptLimit);
            RunCheck(checks, errors, "bee-missing-reapply-rejected",
                ValidateBeeMissingReapply);
            RunCheck(checks, errors, "android-gradle-root-dag-bound",
                () => ValidateAndroidGradleRoot(Path.Combine(outputRoot, "android-gradle-root")));
            RunCheck(checks, errors, "android-native-hash-mismatch-rejected",
                () => ValidateAndroidNativeHashMismatch(
                    Path.Combine(outputRoot, "android-native-hash-mismatch")));

            ResolverReport report = new ResolverReport
            {
                schemaVersion = 1,
                format = "hybridclr.dhe-cpp-resolver-regression.json",
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                engineWorkflow = engineWorkflow,
                unityVersion = Application.unityVersion,
                resolverSourceSha256 = Sha256Hex(resolverSource),
                passed = errors.Count == 0,
                checks = checks.ToArray(),
                errors = errors.ToArray(),
            };
            File.WriteAllText(Path.Combine(outputRoot, "dhe-cpp-resolver-regression.json"),
                JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            if (!report.passed)
                throw new BuildFailedException("DHE generated C++ resolver regression failed: " +
                    string.Join("; ", errors));
        }

        private static void ValidateMethodDefTokenOverload(string root)
        {
            string cpp = PrepareRoot(root);
            WriteCodeGenModule(cpp, new[]
            {
                "Fixture_Resolve_mAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "Fixture_Resolve_mBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            });
            string sourcePath = Path.Combine(cpp, "Fixture.cpp");
            File.WriteAllText(sourcePath,
                Definition("Fixture_Resolve_mAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 1) +
                Definition("Fixture_Resolve_mBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", 2),
                new UTF8Encoding(false));
            string mv = WriteMv(root, new[]
            {
                Method(1, "Fixture.Type::Resolve|System.Int32 (System.Int32)", "Resolve", 0, 'a'),
                Method(2, "Fixture.Type::Resolve|System.Int32 (System.String)", "Resolve", 0, 'b'),
            });
            DheNativeGuardResult result = Inject(root, cpp, mv);
            Require(result.NativeEntryCount == 2 && result.TransformedMethodCount == 2,
                "MethodDef token resolver did not guard both overloads.");
            string transformed = File.ReadAllText(sourcePath, Encoding.UTF8);
            Require(transformed.Contains("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:100663297"),
                "First overload was not bound to MethodDef RID 1.");
            Require(transformed.Contains("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB:100663298"),
                "Second overload was not bound to MethodDef RID 2.");
        }

        private static void ValidateGenericMethodTableOverload(string root)
        {
            string cpp = PrepareRoot(root);
            WriteCodeGenModule(cpp, new string[] { null, null });
            string first = "Fixture_Generic_TisInt32_mCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC_gshared";
            string second = "Fixture_Generic_TisInt32_mDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD_gshared";
            string sourcePath = Path.Combine(cpp, "GenericMethods.cpp");
            File.WriteAllText(sourcePath, Definition(first, 3) + Definition(second, 4),
                new UTF8Encoding(false));
            WriteGenericTables(cpp, first, second, false);
            WriteGlobalMetadata(root, 2);
            string mv = WriteMv(root, new[]
            {
                Method(1, "Fixture.Type::Generic|T <!!0>(T)", "Generic", 1, 'c'),
                Method(2, "Fixture.Type::Generic|T <!!0>(System.Int32)", "Generic", 1, 'd'),
            });
            DheNativeGuardResult result = Inject(root, cpp, mv);
            Require(result.NativeEntryCount == 2 && result.TransformedMethodCount == 2,
                "Generic method table did not guard both same-name overloads.");
            string transformed = File.ReadAllText(sourcePath, Encoding.UTF8);
            Require(transformed.Contains("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC_gshared:100663297"),
                "First generic overload was not bound through its MethodSpec.");
            Require(transformed.Contains("DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD_gshared:100663298"),
                "Second generic overload was not bound through its MethodSpec.");
        }

        private static void ValidateManagedSignatureConflict(string root)
        {
            string cpp = PrepareRoot(root);
            string function = "Fixture_Resolve_mEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE";
            WriteCodeGenModule(cpp, new[] { function });
            File.WriteAllText(Path.Combine(cpp, "Fixture.cpp"),
                "// System.Int32 Other.Type::Wrong(System.Int32)\n" + Definition(function, 5),
                new UTF8Encoding(false));
            string mv = WriteMv(root, new[]
            {
                Method(1, "Fixture.Type::Resolve|System.Int32 (System.Int32)", "Resolve", 0, 'e'),
            });
            ExpectFailure(() => Inject(root, cpp, mv), "conflicts with the managed signature comment");
        }

        private static void ValidateManagedSignatureComplexParameters(string root)
        {
            string cpp = PrepareRoot(root);
            string function = "Fixture_Resolve_m3333333333333333333333333333333333333333";
            WriteCodeGenModule(cpp, new[] { function });
            string sourcePath = Path.Combine(cpp, "Fixture.cpp");
            File.WriteAllText(sourcePath,
                "// System.Int32 Fixture.Type::Resolve(System.Byte[],System.Int32[,]" +
                ",System.Collections.Generic.Dictionary<System.String,System.Int32[]>)\n" +
                "IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR int32_t " + function +
                " (RuntimeArray* ___0_bytes, RuntimeArray* ___1_matrix, RuntimeObject* ___2_map, " +
                "const RuntimeMethod* method)\n{\n    return 8;\n}\n",
                new UTF8Encoding(false));
            string mv = WriteMv(root, new[]
            {
                Method(1,
                    "Fixture.Type::Resolve|System.Int32 (System.Byte[],System.Int32[,]" +
                    ",System.Collections.Generic.Dictionary<System.String,System.Int32[]>)",
                    "Resolve", 0, '3', new[]
                    {
                        "System.Byte[]",
                        "System.Int32[,]",
                        "System.Collections.Generic.Dictionary<System.String,System.Int32[]>",
                    }),
            });
            DheNativeGuardResult result = Inject(root, cpp, mv);
            Require(result.NativeEntryCount == 1 && result.TransformedMethodCount == 1,
                "Managed signature parsing did not preserve array and nested generic parameters.");
            Require(File.ReadAllText(sourcePath, Encoding.UTF8).Contains(
                    "3333333333333333333333333333333333333333:100663297"),
                "Complex managed signature was not bound to MethodDef RID 1.");
        }

        private static void ValidatePointerCountTamper(string root)
        {
            string cpp = PrepareRoot(root);
            File.WriteAllText(Path.Combine(cpp, "Fixture_CodeGen.c"),
                "static Il2CppMethodPointer s_methodPointers[2] =\n{\n" +
                "    Fixture_Resolve_mFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF,\n};\n" +
                ModuleInitializer(2, "s_methodPointers"), new UTF8Encoding(false));
            string mv = WriteMv(root, new[]
            {
                Method(1, "Fixture.Type::Resolve|System.Int32 (System.Int32)", "Resolve", 0, 'f'),
            });
            ExpectFailure(() => Inject(root, cpp, mv), "initializer count is wrong");
        }

        private static void ValidateGenericNativeOwnerConflict(string root)
        {
            string cpp = PrepareRoot(root);
            WriteCodeGenModule(cpp, new string[] { null, null });
            string first = "Fixture_Generic_TisInt32_m1111111111111111111111111111111111111111_gshared";
            string second = "Fixture_Generic_TisInt32_m2222222222222222222222222222222222222222_gshared";
            File.WriteAllText(Path.Combine(cpp, "GenericMethods.cpp"),
                Definition(first, 6) + Definition(second, 7), new UTF8Encoding(false));
            WriteGenericTables(cpp, first, second, true);
            WriteGlobalMetadata(root, 2);
            string mv = WriteMv(root, new[]
            {
                Method(1, "Fixture.Type::Generic|T <!!0>(T)", "Generic", 1, '1'),
                Method(2, "Fixture.Type::Generic|T <!!0>(System.Int32)", "Generic", 1, '2'),
            });
            ExpectFailure(() => Inject(root, cpp, mv), "maps to multiple managed methods");
        }

        private static void ValidateBeeGraphRegeneration()
        {
            int[] exits = { 4, 4, 0, 0 };
            int backendCalls = 0;
            int graphCalls = 0;
            int reapplyCalls = 0;
            List<bool> jsonDagUses = new List<bool>();
            DheBeeRebuildResult result = RunBeeStateMachine(8,
                (attempt, useJsonDag, phase) =>
                {
                    jsonDagUses.Add(useJsonDag);
                    return exits[backendCalls++];
                },
                regeneration =>
                {
                    graphCalls++;
                    return "build-program-" + regeneration.ToString(CultureInfo.InvariantCulture);
                },
                () => reapplyCalls++);
            Require(result.ExitCode == 0 && result.Attempts == 4 &&
                result.GraphRegenerations == 2 && result.GuardReapplications == 1 &&
                result.BuildProgramPath == "build-program-2" && backendCalls == 4 &&
                graphCalls == 2 && reapplyCalls == 1 &&
                jsonDagUses.Count == 4 && !jsonDagUses[0] && jsonDagUses[1] &&
                jsonDagUses[2] && !jsonDagUses[3],
                "Bee graph regeneration did not stabilize and rebuild reapplied guards exactly once.");
        }

        private static void ValidateBeeAttemptLimit()
        {
            int[] exits = { 4, 0 };
            int backendCalls = 0;
            DheBeeRebuildResult result = RunBeeStateMachine(2,
                (attempt, useJsonDag, phase) => exits[backendCalls++],
                regeneration => "build-program", () => { });
            Require(result.ExitCode != 0 && result.Attempts == 2 &&
                result.GraphRegenerations == 1 && result.GuardReapplications == 1 &&
                backendCalls == 2,
                "Bee attempt limit accepted a stabilized graph before reapplied guards were rebuilt.");
        }

        private static void ValidateBeeMissingReapply()
        {
            DheBeeRebuildResult result = RunBeeStateMachine(8,
                (attempt, useJsonDag, phase) => 4,
                regeneration => "unexpected-build-program", null);
            Require(result.ExitCode != 0 && result.Attempts == 1 &&
                result.GraphRegenerations == 0 && result.GuardReapplications == 0,
                "Bee graph regeneration proceeded without a generated-C++ reapply callback.");
        }

        private static DheBeeRebuildResult RunBeeStateMachine(int maxAttempts,
            Func<int, bool, string, int> runBackend, Func<int, string> regenerateGraph,
            Action reapply)
        {
            object value = InvokePipelineMethod("RunBeeRebuildStateMachine", new object[]
            {
                maxAttempts, runBackend, regenerateGraph, reapply, new Action<string>(_ => { })
            });
            DheBeeRebuildResult result = value as DheBeeRebuildResult;
            Require(result != null, "Bee rebuild state machine returned no result.");
            return result;
        }

        private static void ValidateAndroidGradleRoot(string root)
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string projectRoot = Path.Combine(root, "project");
            string beeRoot = Path.Combine(projectRoot, "Library", "Bee");
            Directory.CreateDirectory(beeRoot);
            string dagPath = Path.Combine(beeRoot, "Player123.dag");
            File.WriteAllText(dagPath, string.Empty, new UTF8Encoding(false));
            string boundRoot = Path.Combine(beeRoot, "Android", "Prj", "IL2CPP", "Gradle");
            string staleRoot = Path.Combine(beeRoot, "stale", "Gradle");
            string generatedCpp = Path.Combine(beeRoot, "artifacts", "Android", "il2cppOutput", "cpp");
            Directory.CreateDirectory(generatedCpp);
            PrepareGradleRoot(boundRoot);
            PrepareGradleRoot(staleRoot);
            File.SetLastWriteTimeUtc(Path.Combine(staleRoot, "settings.gradle"),
                DateTime.UtcNow.AddMinutes(1));
            string input = "{\"PlayerBuildConfig\":{\"DestinationPath\":\"" +
                EscapeJson(boundRoot) + "\"}}";
            File.WriteAllText(Path.Combine(beeRoot, "Player123-inputdata.json"), input,
                new UTF8Encoding(false));
            File.WriteAllText(dagPath + ".json", "{\"Path\":\"" +
                EscapeJson(generatedCpp.Replace('\\', '/')) + "\"}", new UTF8Encoding(false));
            string staleDag = Path.Combine(beeRoot, "Player999.dag");
            File.WriteAllText(staleDag, string.Empty, new UTF8Encoding(false));
            File.WriteAllText(staleDag + ".json", "{\"Path\":\"" +
                EscapeJson(Path.Combine(staleRoot, "cpp").Replace('\\', '/')) + "\"}",
                new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(staleDag, DateTime.UtcNow.AddMinutes(1));
            string selectedDag = (string)InvokePipelineMethod("ResolveBeePlayerDag",
                new object[] { beeRoot, generatedCpp });
            string resolved = (string)InvokePipelineMethod(
                "ResolveAndroidGradleRootFromBeeDag",
                new object[] { projectRoot, beeRoot, dagPath });
            Require(Path.GetFullPath(selectedDag).Equals(Path.GetFullPath(dagPath),
                    StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(resolved).Equals(Path.GetFullPath(boundRoot),
                    StringComparison.OrdinalIgnoreCase),
                "Player DAG or Android Gradle root was selected by recency instead of generated-C++ identity.");
        }

        private static void PrepareGradleRoot(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "launcher"));
            string library = Path.Combine(root, "unityLibrary");
            Directory.CreateDirectory(Path.Combine(library, "src", "main", "jniLibs"));
            File.WriteAllText(Path.Combine(root, "settings.gradle"), string.Empty,
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(library, "build.gradle"), string.Empty,
                new UTF8Encoding(false));
        }

        private static void ValidateAndroidNativeHashMismatch(string root)
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string nativeRoot = Path.Combine(root, "jniLibs", "arm64-v8a");
            Directory.CreateDirectory(nativeRoot);
            string nativePath = Path.Combine(nativeRoot, "libil2cpp.so");
            File.WriteAllBytes(nativePath, new byte[] { 1, 2, 3, 4 });
            string artifactPath = Path.Combine(root, "player.apk");
            using (ZipArchive archive = ZipFile.Open(artifactPath, ZipArchiveMode.Create))
            using (Stream stream = archive.CreateEntry("lib/arm64-v8a/libil2cpp.so").Open())
                stream.Write(new byte[] { 4, 3, 2, 1 }, 0, 4);
            try
            {
                InvokePipelineMethod("ValidateAndroidPlayerNativeLibraries", new object[]
                {
                    artifactPath, new[] { nativePath }, new List<string>(), new List<string>()
                });
            }
            catch (BuildFailedException exception)
            {
                Require(exception.Message.IndexOf("does not match", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Unexpected Android native hash failure: " + exception.Message);
                return;
            }
            throw new InvalidOperationException(
                "Android Player accepted a libil2cpp.so that differs from Bee staging.");
        }

        private static object InvokePipelineMethod(string name, object[] arguments)
        {
            MethodInfo method = typeof(DheBuildPipeline).GetMethod(name,
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(method != null, "DHE package method is missing: " + name + ".");
            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null) throw exception.InnerException;
                throw;
            }
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static DheNativeGuardResult Inject(string root, string cpp, string mv)
        {
            return DheBuildPipeline.InjectGeneratedGuards(new DheNativeGuardOptions
            {
                MvJsonPaths = new[] { mv },
                GeneratedCppRoot = cpp,
                OutputManifestPath = Path.Combine(root, "dhe-native-manifest.json"),
                RequireCompleteCoverage = true,
                GuardAllMethods = true,
            });
        }

        private static string PrepareRoot(string root)
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string cpp = Path.Combine(root, "cpp");
            Directory.CreateDirectory(cpp);
            return cpp;
        }

        private static void WriteCodeGenModule(string cpp, string[] pointers)
        {
            StringBuilder source = new StringBuilder();
            source.Append("static Il2CppMethodPointer s_methodPointers[")
                .Append(pointers.Length.ToString(CultureInfo.InvariantCulture)).Append("] =\n{\n");
            foreach (string pointer in pointers)
                source.Append("    ").Append(pointer ?? "NULL").Append(",\n");
            source.Append("};\n").Append(ModuleInitializer(pointers.Length, "s_methodPointers"));
            File.WriteAllText(Path.Combine(cpp, "Fixture_CodeGen.c"), source.ToString(),
                new UTF8Encoding(false));
        }

        private static string ModuleInitializer(int count, string pointers)
        {
            return "const Il2CppCodeGenModule g_Fixture_CodeGenModule =\n{\n" +
                "    \"Fixture.dll\",\n    " + count.ToString(CultureInfo.InvariantCulture) +
                ",\n    " + pointers + ",\n};\n";
        }

        private static string Definition(string function, int value)
        {
            return "IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR int32_t " + function +
                " (int32_t ___0_value, const RuntimeMethod* method)\n{\n    return " +
                value.ToString(CultureInfo.InvariantCulture) + ";\n}\n";
        }

        private static string WriteMv(string root, IEnumerable<string> methods)
        {
            string path = Path.Combine(root, "fixture.mv.json");
            File.WriteAllText(path, "{\n  \"assemblyName\": \"Fixture\",\n  \"methods\": [\n" +
                string.Join(",\n", methods) + "\n  ]\n}\n", new UTF8Encoding(false));
            return path;
        }

        private static string Method(int rid, string identity, string name, int genericCount, char hash,
            string[] parameterTypes = null)
        {
            string stableId = new string(hash, 64);
            if (parameterTypes == null) parameterTypes = new[] { "System.Int32" };
            return "    {\"identity\":\"" + identity + "\",\"stableId\":\"" + stableId +
                "\",\"token\":" + (0x06000000 + rid).ToString(CultureInfo.InvariantCulture) +
                ",\"flags\":8,\"name\":\"" + name +
                "\",\"declaringType\":\"Fixture.Type\",\"returnType\":\"System.Int32\"," +
                "\"parameterTypes\":[\"" + string.Join("\",\"", parameterTypes) +
                "\"],\"isStatic\":true,\"hasThis\":false," +
                "\"isAbstract\":false,\"isPInvoke\":false,\"declaringTypeIsValueType\":false," +
                "\"genericParameterCount\":" + genericCount.ToString(CultureInfo.InvariantCulture) +
                ",\"declaringTypeGenericParameterCount\":0}";
        }

        private static void WriteGenericTables(string cpp, string first, string second, bool sharePointer)
        {
            File.WriteAllText(Path.Combine(cpp, "Il2CppGenericMethodDefinitions.c"),
                "const Il2CppMethodSpec g_Il2CppMethodSpecTable[2] =\n{\n" +
                "    { 0, -1, 0 },\n    { 1, -1, 1 },\n};\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(cpp, "Il2CppGenericMethodPointerTable.c"),
                "const Il2CppMethodPointer g_Il2CppGenericMethodPointers[2] =\n{\n" +
                "    (Il2CppMethodPointer)&" + first + ",\n    (Il2CppMethodPointer)&" + second + ",\n};\n",
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(cpp, "Il2CppGenericMethodTable.c"),
                "const Il2CppGenericMethodFunctionsDefinitions g_Il2CppGenericMethodFunctions[2] =\n{\n" +
                "    { 0, 0, 0, -1 },\n    { 1, " + (sharePointer ? "0" : "1") + ", 0, -1 },\n};\n",
                new UTF8Encoding(false));
        }

        private static void WriteGlobalMetadata(string root, int methodCount)
        {
            byte[] strings = Encoding.UTF8.GetBytes("Fixture.dll\0");
            const int headerSize = 176;
            const int methodRecordSize = 36;
            const int imageRecordSize = 40;
            int methodsOffset = headerSize + strings.Length;
            int methodsSize = methodCount * methodRecordSize;
            int imagesOffset = methodsOffset + methodsSize;
            byte[] metadata = new byte[imagesOffset + imageRecordSize];
            WriteInt32(metadata, 0, unchecked((int)0xfab11baf));
            WriteInt32(metadata, 4, 31);
            WriteInt32(metadata, 24, headerSize);
            WriteInt32(metadata, 28, strings.Length);
            WriteInt32(metadata, 48, methodsOffset);
            WriteInt32(metadata, 52, methodsSize);
            WriteInt32(metadata, 168, imagesOffset);
            WriteInt32(metadata, 172, imageRecordSize);
            Buffer.BlockCopy(strings, 0, metadata, headerSize, strings.Length);
            for (int index = 0; index < methodCount; index++)
            {
                int method = methodsOffset + index * methodRecordSize;
                WriteInt32(metadata, method + 4, 0);
                WriteInt32(metadata, method + 24, 0x06000000 + index + 1);
            }
            WriteInt32(metadata, imagesOffset, 0);
            WriteInt32(metadata, imagesOffset + 8, 0);
            WriteInt32(metadata, imagesOffset + 12, 1);
            string directory = Path.Combine(root, "data", "Metadata");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "global-metadata.dat"), metadata);
        }

        private static void WriteInt32(byte[] bytes, int offset, int value)
        {
            byte[] encoded = BitConverter.GetBytes(value);
            Buffer.BlockCopy(encoded, 0, bytes, offset, encoded.Length);
        }

        private static void ExpectFailure(Action action, string expected)
        {
            try
            {
                action();
            }
            catch (BuildFailedException exception)
            {
                Require(exception.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0,
                    "Unexpected failure: " + exception.Message);
                return;
            }
            throw new InvalidOperationException("Expected BuildFailedException containing '" + expected + "'.");
        }

        private static void RunCheck(List<ResolverCheck> checks, List<string> errors, string name, Action action)
        {
            try
            {
                action();
                checks.Add(new ResolverCheck { name = name, passed = true });
            }
            catch (Exception exception)
            {
                string error = name + ": " + exception.Message;
                checks.Add(new ResolverCheck { name = name, passed = false, error = exception.ToString() });
                errors.Add(error);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string Argument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1];
            }
            throw new ArgumentException("Missing argument " + name + ".");
        }

        private static string Sha256Hex(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        [Serializable]
        private sealed class ResolverCheck
        {
            public string name;
            public bool passed;
            public string error;
        }

        [Serializable]
        private sealed class ResolverReport
        {
            public int schemaVersion;
            public string format;
            public string generatedAtUtc;
            public string engineWorkflow;
            public string unityVersion;
            public string resolverSourceSha256;
            public bool passed;
            public ResolverCheck[] checks;
            public string[] errors;
        }
    }
}
