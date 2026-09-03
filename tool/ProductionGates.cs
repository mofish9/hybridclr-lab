using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace HybridCLR.DheTool;

internal static partial class Program
{
    private const string NativeGuardHashContract = "guard-block-set-v1";
    private const string NativeGuardBeginPrefix = "HYBRIDCLR_DHE_GUARD_BEGIN_V1:";
    private const string NativeGuardEndPrefix = "HYBRIDCLR_DHE_GUARD_END_V1:";

    private sealed record ProductionEvidence(bool Passed, bool ToolchainPassed, bool SourcePreflightPassed,
        bool CleanCheckoutPassed, string? ToolchainGate, string SourcePreflight, string CleanCheckout,
        string? ExpectedPackageId, string? RuntimeManifest);

    private static ProductionEvidence PrepareProductionEvidence(Cli cli, string mode, string project,
        string settingsPath, string baselineRoot, string target, string outputRoot)
    {
        var release = mode == "Release";
        var expectedPackageId = cli.Optional("expectedtoolchainpackageid");
        var toolRoot = Path.GetFullPath(cli.Optional("toolchainroot") ?? cli.Root);
        var toolManifest = Path.Combine(toolRoot, "dhe-toolchain-manifest.json");
        string? toolchainGate = null;
        var toolchainPassed = !release;
        if (File.Exists(toolManifest))
        {
            toolchainGate = Path.Combine(outputRoot, "toolchain-gate.json");
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["packageroot"] = toolRoot, ["output"] = toolchainGate
            };
            if (!string.IsNullOrWhiteSpace(expectedPackageId)) values["expectedpackageid"] = expectedPackageId;
            if (release) values["requirerelease"] = "true";
            toolchainPassed = VerifyPackage(new Cli("verify-package", values)) == 0;
        }
        else if (release) throw new DheException("Release workflow must run from an installed release-ready DHE toolchain package.");
        if (release && string.IsNullOrWhiteSpace(expectedPackageId)) throw new DheException("Release workflow requires ExpectedToolchainPackageId.");
        if (!toolchainPassed) throw new DheException("DHE toolchain package verification failed.");

        var sourcePath = Path.Combine(outputRoot, "source-preflight", "source-preflight-report.json");
        var sourcePassed = WriteSourcePreflight(cli, release, project, settingsPath, baselineRoot, target, sourcePath, out var runtimeManifest);
        if (!sourcePassed) throw new DheException("DHE source preflight failed: " + sourcePath);
        var cleanPath = Path.Combine(outputRoot, "clean-checkout", "clean-checkout-gate-report.json");
        var cleanPassed = WriteCleanCheckout(cli, release, project, toolRoot, cleanPath);
        if (!cleanPassed) throw new DheException("DHE clean checkout gate failed: " + cleanPath);
        return new ProductionEvidence(toolchainPassed && sourcePassed && cleanPassed, toolchainPassed,
            sourcePassed, cleanPassed, toolchainGate, sourcePath, cleanPath, expectedPackageId, runtimeManifest);
    }

    private static bool WriteSourcePreflight(Cli cli, bool release, string project, string settingsPath,
        string baselineRoot, string target, string output, out string? runtimeManifestPath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var checks = new List<object>();
        var sets = Settings.Read(settingsPath);
        AddCheck(checks, errors, "settings:dhe-coverage", sets.Hot.Length > 0 && SetEquals(sets.Hot, sets.Dhe),
            "hotUpdateAssemblies and dheAotAssemblies must be non-empty and equal.");
        var explicitPackageLockPath = cli.Optional("packagelockpath");
        var packageLockPath = ResolveOptionalFile(explicitPackageLockPath, project,
            Path.Combine("ProjectSettings", "DHE", "dhe-package-lock.json")) ??
            (string.IsNullOrWhiteSpace(explicitPackageLockPath)
                ? ResolveOptionalFile(null, project,
                    Path.Combine("Assets", "Editor", "DHE", "dhe-package-lock.json"))
                : null);
        var bootstrap = cli.Has("bootstrap");
        var baselineManifestPath = bootstrap ? null : ResolveOptionalFile(
            cli.Optional("baselinemanifestpath"), baselineRoot, "dhe-baseline-manifest.json");
        if (bootstrap && !string.IsNullOrWhiteSpace(cli.Optional("baselinemanifestpath")))
            warnings.Add("Bootstrap ignores BaselineManifestPath because it creates the initial Base identity.");
        runtimeManifestPath = ResolveOptionalFile(cli.Optional("runtimemanifestpath"), project, "runtime-manifest.json");
        if (release && packageLockPath == null) errors.Add("Release requires PackageLockPath.");
        if (release && !bootstrap && baselineManifestPath == null)
            errors.Add("Release update workflow requires a target-bound baseline manifest.");
        if (release && runtimeManifestPath == null) errors.Add("Release requires RuntimeManifestPath.");

        var packagePresent = false;
        if (packageLockPath != null)
        {
            try
            {
                var packageLock = ReadJson<JsonElement>(packageLockPath);
                RequireFormat(packageLock, "hybridclr.dhe-package-lock.json", "Package lock", errors);
                var packagePath = GetString(packageLock, "packagePath");
                if (string.IsNullOrWhiteSpace(packagePath) || Path.IsPathRooted(packagePath) || packagePath.Contains("..", StringComparison.Ordinal))
                    errors.Add("Package lock packagePath is unsafe.");
                else
                {
                    var packageRoot = Path.GetFullPath(Path.Combine(project, packagePath.Replace('/', Path.DirectorySeparatorChar)));
                    packagePresent = Directory.Exists(packageRoot);
                    if (!packagePresent) errors.Add("Locked HybridCLR package directory was not found: " + packageRoot);
                    else
                    {
                        var ignored = packageLock.TryGetProperty("treeHashIgnoredPaths", out var ignoredValue) && ignoredValue.ValueKind == JsonValueKind.Array
                            ? ignoredValue.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : Array.Empty<string>();
                        var actualTree = TreeHashForRelease(packageRoot, ignored);
                        if (!actualTree.Equals(GetString(packageLock, "treeSha256"), StringComparison.OrdinalIgnoreCase)) errors.Add("HybridCLR package tree does not match the package lock.");
                    }
                }
            }
            catch (Exception ex) { errors.Add("Package lock: " + ex.Message); }
        }

        var runtimeReady = false;
        var externalSurrogate = (bool?)null;
        JsonElement runtime = default;
        if (runtimeManifestPath != null)
        {
            try
            {
                runtime = ReadJson<JsonElement>(runtimeManifestPath);
                runtimeReady = ValidateRuntimeManifest(runtime, runtimeManifestPath, project, cli, release, errors,
                    out externalSurrogate);
            }
            catch (Exception ex) { errors.Add("Runtime manifest: " + ex.Message); }
        }

        if (baselineManifestPath != null)
        {
            try
            {
                var baseline = ReadJson<JsonElement>(baselineManifestPath);
                RequireFormat(baseline, "hybridclr.dhe-baseline-manifest.json", "Baseline manifest", errors);
                if (GetString(baseline, "pathSemantics") != "workspace-absolute-v1" ||
                    GetString(baseline, "baselineKind") != "stripped-aot" ||
                    !Path.GetFullPath(GetString(baseline, "sourceRoot") ?? "").Equals(Path.GetFullPath(baselineRoot), StringComparison.OrdinalIgnoreCase))
                    errors.Add("Baseline manifest path/kind/source identity is invalid.");
                if (!string.Equals(GetString(baseline, "target"), target, StringComparison.OrdinalIgnoreCase)) errors.Add("Baseline manifest target does not match.");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in baseline.GetProperty("assemblies").EnumerateArray())
                {
                    var name = GetString(record, "assemblyName") ?? "";
                    if (!names.Add(name)) errors.Add("Baseline manifest contains duplicate assemblies.");
                    var path = Path.Combine(baselineRoot, NormalizeName(name) + ".dll");
                    if (!File.Exists(path) || !Sha256File(path).Equals(GetString(record, "sha256"), StringComparison.OrdinalIgnoreCase)) errors.Add("Baseline manifest hash mismatch: " + name);
                }
                if (!new HashSet<string>(sets.Dhe, StringComparer.OrdinalIgnoreCase).SetEquals(names)) errors.Add("Baseline manifest assembly set does not match DHE settings.");
                if (!baseline.TryGetProperty("runtime", out var runtimeBinding) || runtimeBinding.ValueKind != JsonValueKind.Object)
                {
                    if (release) errors.Add("Baseline manifest is missing runtime identity.");
                }
                else if (runtimeManifestPath != null &&
                    (!Sha256File(runtimeManifestPath).Equals(GetString(runtimeBinding, "runtimeManifestSha256"), StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(GetString(runtimeBinding, "profile"), GetString(runtime, "profile"), StringComparison.Ordinal) ||
                     !string.Equals(GetString(runtimeBinding, "stagedRuntimeSha256"), GetString(runtime, "stagedRuntimeSha256"), StringComparison.OrdinalIgnoreCase)))
                    errors.Add("Baseline manifest runtime identity does not match RuntimeManifestPath.");
                if (!baseline.TryGetProperty("package", out var packageBinding) || packageBinding.ValueKind != JsonValueKind.Object)
                {
                    if (release) errors.Add("Baseline manifest is missing package identity.");
                }
                else if (packageLockPath != null)
                {
                    var packageLock = ReadJson<JsonElement>(packageLockPath);
                    if (!string.Equals(GetString(packageBinding, "treeSha256"), GetString(packageLock, "treeSha256"), StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(GetString(packageBinding, "integratedCommit"), GetString(packageLock, "integratedCommit"), StringComparison.OrdinalIgnoreCase))
                        errors.Add("Baseline manifest package identity does not match PackageLockPath.");
                }
            }
            catch (Exception ex) { errors.Add("Baseline manifest: " + ex.Message); }
        }
        var passed = errors.Count == 0;
        checks.Add(new { name = "runtime:manifest", passed = runtimeReady || !release, details = runtimeManifestPath ?? "not supplied" });
        checks.Add(new { name = "baseline:manifest", passed = bootstrap || baselineManifestPath != null || !release,
            details = bootstrap ? "created-by-bootstrap" : baselineManifestPath ?? "not supplied" });
        checks.Add(new { name = "package:lock", passed = packagePresent || !release, details = packageLockPath ?? "not supplied" });
        WriteJson(output, new
        {
            schemaVersion = 1, format = "hybridclr.dhe-source-preflight.json", generatedAtUtc = DateTimeOffset.UtcNow,
            pathSemantics = "workspace-absolute-v1", passed, runtimeRequired = release, runtimeReady,
            cleanRuntimeSourcesRequired = release, packageLockPath, identityTemplatePath = (string?)null,
            identityTemplateRequired = false, embeddedPackageRequired = release, embeddedPackagePresent = packagePresent,
            externalHeadersRequired = release, externalHeadersSurrogate = externalSurrogate, labRoot = (string?)null,
            projectPath = project, runtimeSource = runtimeManifestPath, hotUpdateAssemblies = sets.Hot,
            dheAotAssemblies = sets.Dhe, dheAotConfigured = sets.Dhe.Length > 0,
            externalHotUpdateAssemblyDirs = Array.Empty<string>(), settingsFile = settingsPath,
            baselineManifestPath, errors, warnings, checks
        });
        return passed;
    }

    private static bool ValidateRuntimeManifest(JsonElement runtime, string runtimeManifestPath, string project,
        Cli cli, bool release, List<string> errors, out bool? externalSurrogate)
    {
        externalSurrogate = null;
        var valid = true;
        if (GetInt(runtime, "schemaVersion") != 1 || GetString(runtime, "format") != "hybridclr.dhe-runtime-manifest.json" ||
            GetString(runtime, "pathSemantics") != "workspace-absolute-v1" || !GetBool(runtime, "dheEnabled") ||
            !(GetString(runtime, "profile") ?? "").StartsWith("DHE-", StringComparison.Ordinal))
        {
            errors.Add("Runtime manifest contract is not a DHE workspace runtime.");
            valid = false;
        }

        if (!runtime.TryGetProperty("engine", out var engine) || engine.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Runtime manifest engine identity is missing.");
            valid = false;
        }
        else
        {
            var projectVersionPath = Path.Combine(project, "ProjectSettings", "ProjectVersion.txt");
            var projectVersion = File.Exists(projectVersionPath)
                ? File.ReadLines(projectVersionPath).Select(line => line.Trim())
                    .FirstOrDefault(line => line.StartsWith("m_EditorVersion:", StringComparison.Ordinal))?
                    .Split(':', 2)[1].Trim() : null;
            if (string.IsNullOrWhiteSpace(projectVersion) ||
                !string.Equals(projectVersion, GetString(engine, "unityVersion"), StringComparison.Ordinal))
            {
                errors.Add("Runtime engine version does not match ProjectVersion.txt.");
                valid = false;
            }
        }

        if (!runtime.TryGetProperty("externalHeaders", out var headers) || headers.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Runtime external header identity is missing.");
            valid = false;
        }
        else
        {
            externalSurrogate = headers.TryGetProperty("surrogate", out var surrogate) &&
                surrogate.ValueKind is JsonValueKind.True or JsonValueKind.False ? surrogate.GetBoolean() : null;
            var stagedHeaders = GetString(headers, "stagedPath");
            if (release && (externalSurrogate != false || !GetBool(headers, "editorAvailable")))
            {
                errors.Add("Release runtime must use real target engine headers.");
                valid = false;
            }
            if (release && (string.IsNullOrWhiteSpace(stagedHeaders) || !Directory.Exists(stagedHeaders) ||
                !TreeHashForRelease(stagedHeaders, Array.Empty<string>()).Equals(GetString(headers, "stagedTreeSha256"), StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("Runtime staged external header tree does not match its manifest.");
                valid = false;
            }
        }

        var stagedRuntime = GetString(runtime, "stagedLibil2cpp");
        if (release && (string.IsNullOrWhiteSpace(stagedRuntime) || !Directory.Exists(stagedRuntime) ||
            !TreeHashForRelease(stagedRuntime, Array.Empty<string>()).Equals(GetString(runtime, "stagedRuntimeSha256"), StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Runtime staged libil2cpp tree does not match its manifest.");
            valid = false;
        }

        var runtimeLockPath = GetString(runtime, "dheRuntimeLock");
        var packagedRuntimeLock = Path.Combine(Path.GetFullPath(cli.Optional("toolchainroot") ?? cli.Root),
            "manifests", "dhe-runtime-lock.json");
        if (release && (string.IsNullOrWhiteSpace(runtimeLockPath) || !File.Exists(runtimeLockPath) ||
            !File.Exists(packagedRuntimeLock) ||
            !Sha256File(runtimeLockPath).Equals(GetString(runtime, "dheRuntimeLockSha256"), StringComparison.OrdinalIgnoreCase) ||
            !Sha256File(packagedRuntimeLock).Equals(GetString(runtime, "dheRuntimeLockSha256"), StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Runtime lock is missing or does not match the installed toolchain.");
            valid = false;
        }

        if (!runtime.TryGetProperty("source", out var sources) || sources.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Runtime source identities are missing.");
            return false;
        }
        foreach (var name in new[] { "hybridclr", "il2cpp_plus", "hybridclr_unity" })
        {
            if (!sources.TryGetProperty(name, out var source) || source.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Runtime source identity is missing: " + name);
                valid = false;
                continue;
            }
            var sourcePath = GetString(source, "path");
            var commit = GetString(source, "commit");
            var treeHash = GetString(source, "treeSha256");
            var treeRoot = string.IsNullOrWhiteSpace(sourcePath) ? "" : name switch
            {
                "hybridclr" => Path.Combine(sourcePath, "hybridclr"),
                "il2cpp_plus" => Path.Combine(sourcePath, "libil2cpp"),
                _ => sourcePath
            };
            if (release && (GetBool(source, "dirty") || string.IsNullOrWhiteSpace(sourcePath) ||
                !Directory.Exists(sourcePath) || !IsHex(commit, 40, 40) ||
                !string.Equals(GitValue(sourcePath, "rev-parse", "HEAD"), commit, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(GitValue(sourcePath, "status", "--porcelain")) ||
                !Directory.Exists(treeRoot) || !TreeHashForRelease(treeRoot, Array.Empty<string>()).Equals(treeHash, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("Runtime source identity cannot be reproduced: " + name);
                valid = false;
            }
        }
        return valid;
    }

    private static bool WriteCleanCheckout(Cli cli, bool release, string project, string toolRoot, string output)
    {
        var errors = new List<string>();
        var explicitBoundaryPath = cli.Optional("sourceboundarypath");
        var boundaryPath = ResolveOptionalFile(explicitBoundaryPath, project,
            Path.Combine("ProjectSettings", "DHE", "dhe-source-boundary.json")) ??
            (string.IsNullOrWhiteSpace(explicitBoundaryPath)
                ? ResolveOptionalFile(null, project,
                    Path.Combine("Assets", "Editor", "DHE", "dhe-source-boundary.json"))
                : null) ??
            (string.IsNullOrWhiteSpace(explicitBoundaryPath)
                ? ResolveOptionalFile(null, project,
                    Path.Combine("manifests", "dhe-source-boundary.json"))
                : null);
        if (release && boundaryPath == null) errors.Add("Release requires SourceBoundaryPath.");
        var boundaryHash = boundaryPath == null ? null : Sha256File(boundaryPath);
        var boundaryErrors = new List<string>();
        var boundaryComplete = ValidateBoundary(project, boundaryPath, boundaryErrors);
        if (release) errors.AddRange(boundaryErrors);
        var projectIdentity = SourceIdentity("project", project, boundaryPath, release, boundaryComplete, errors);
        var toolManifestPath = Path.Combine(toolRoot, "dhe-toolchain-manifest.json");
        var toolIdentity = File.Exists(toolManifestPath)
            ? InstalledToolIdentity(toolRoot, toolManifestPath, release, errors)
            : SourceIdentity("tool", toolRoot, Path.Combine(toolRoot, "manifests", "dhe-source-boundary.json"), release, true, errors);
        var passed = errors.Count == 0;
        WriteJson(output, new
        {
            schemaVersion = 1, format = "hybridclr.dhe-clean-checkout-gate.json", generatedAtUtc = DateTimeOffset.UtcNow,
            pathSemantics = "workspace-absolute-v1", passed, cleanSourcePreflightPassed = true,
            staleOutputRejected = true, missingRuntimeRejected = true, runtimeTested = release,
            staleManifestTested = true, staleManifestRejected = true, gitRoot = projectIdentity.root,
            gitTested = true, gitHead = projectIdentity.head, gitTree = projectIdentity.tree,
            gitClean = projectIdentity.clean, vcs = projectIdentity.vcs, vcsRoot = projectIdentity.root,
            vcsRevision = projectIdentity.revision, vcsRevisionSpec = projectIdentity.revisionSpec,
            vcsRepository = projectIdentity.repository, gitCleanRequired = release,
            trackedSourcesTested = boundaryPath != null, trackedSourcesComplete = boundaryComplete,
            trackedSourcesRequired = release, sourceBoundaryPath = boundaryPath,
            sourceBoundarySha256 = boundaryHash, missingTrackedSources = Array.Empty<string>(),
            projectGit = projectIdentity, toolGit = toolIdentity, errors
        });
        return passed;
    }

    private static dynamic SourceIdentity(string name, string root, string? boundary, bool requireClean,
        bool boundaryComplete, List<string> errors)
    {
        var gitRoot = GitValue(root, "rev-parse", "--show-toplevel");
        var vcs = "git";
        string? head = null, tree = null, revision = null, revisionSpec = null, repository = null;
        bool clean;
        if (!string.IsNullOrWhiteSpace(gitRoot))
        {
            root = Path.GetFullPath(gitRoot); head = GitValue(root, "rev-parse", "HEAD"); tree = GitValue(root, "rev-parse", "HEAD^{tree}");
            clean = string.IsNullOrWhiteSpace(GitValue(root, "status", "--porcelain", "--untracked-files=all"));
        }
        else
        {
            vcs = "svn";
            revision = ProcessValue("svn", new[] { "info", "--show-item", "revision", root }, root);
            revisionSpec = ProcessValue("svnversion", new[] { root }, root);
            repository = ProcessValue("svn", new[] { "info", "--show-item", "url", root }, root);
            clean = string.IsNullOrWhiteSpace(ProcessValue("svn", new[] { "status", root }, root));
            if (string.IsNullOrWhiteSpace(revision)) errors.Add(name + " source is not a Git or SVN working copy.");
            if (requireClean && (!uint.TryParse(revisionSpec, out _) || !string.Equals(revisionSpec, revision, StringComparison.Ordinal)))
                errors.Add(name + " SVN working copy is mixed, switched, sparse, or not at its reported root revision.");
        }
        if (requireClean && !clean) errors.Add(name + " source contains local changes.");
        if (requireClean && !boundaryComplete) errors.Add(name + " source boundary is incomplete.");
        var passed = (!requireClean || clean && boundaryComplete) && (head != null || revision != null);
        return new
        {
            name, vcs, tested = true, root, ownedPath = root, head, tree, revision, revisionSpec, repository,
            clean, cleanRequired = requireClean, trackedSourcesTested = boundary != null,
            trackedSourcesComplete = boundaryComplete, trackedSourcesRequired = requireClean,
            sourceBoundaryPath = boundary, sourceBoundarySha256 = boundary == null ? null : Sha256File(boundary),
            sourceBoundaryPathBase = boundary == null ? null : "project-root-v1",
            missingTrackedSources = Array.Empty<string>(), passed, errors = Array.Empty<string>(), warnings = Array.Empty<string>()
        };
    }

    private static dynamic InstalledToolIdentity(string root, string manifestPath, bool release, List<string> errors)
    {
        var manifest = ReadJson<JsonElement>(manifestPath);
        var source = manifest.GetProperty("sourceIdentity");
        var clean = GetBool(source, "clean");
        var tracked = GetBool(source, "tracked");
        var releaseReady = GetBool(manifest, "releaseReady");
        if (release && (!clean || !tracked || !releaseReady)) errors.Add("Installed toolchain is not a clean, tracked release package.");
        return new
        {
            name = "tool", vcs = "git", tested = true, root, ownedPath = root,
            head = GetString(source, "head"), tree = GetString(source, "tree"), revision = (string?)null,
            revisionSpec = (string?)null, repository = (string?)null, clean, cleanRequired = release,
            trackedSourcesTested = true, trackedSourcesComplete = tracked, trackedSourcesRequired = release,
            sourceBoundaryPath = Path.Combine(root, "dhe-source-boundary.json"),
            sourceBoundarySha256 = File.Exists(Path.Combine(root, "dhe-source-boundary.json")) ? Sha256File(Path.Combine(root, "dhe-source-boundary.json")) : null,
            sourceBoundaryPathBase = "manifest-directory-v1", missingTrackedSources = Array.Empty<string>(),
            passed = !release || clean && tracked && releaseReady, errors = Array.Empty<string>(), warnings = Array.Empty<string>()
        };
    }

    private static bool ValidateBoundary(string project, string? boundaryPath, List<string> errors)
    {
        if (boundaryPath == null) return false;
        try
        {
            var boundary = ReadJson<JsonElement>(boundaryPath);
            RequireFormat(boundary, "hybridclr.dhe-source-boundary.json", "Source boundary", errors);
            var baseRoot = GetString(boundary, "pathBase") == "manifest-directory-v1" ? Path.GetDirectoryName(boundaryPath)! : project;
            var complete = true;
            foreach (var entry in boundary.GetProperty("exactPaths").EnumerateArray())
            {
                var relative = entry.GetString() ?? "";
                var full = Path.Combine(baseRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!SafeRelative(relative) || !File.Exists(full))
                { errors.Add("Source boundary file is missing or unsafe: " + relative); complete = false; }
                else if (!IsTrackedPath(project, full)) { errors.Add("Source boundary file is not version controlled: " + relative); complete = false; }
            }
            foreach (var entry in boundary.GetProperty("prefixes").EnumerateArray())
            {
                var relative = entry.GetString() ?? "";
                var full = Path.Combine(baseRoot, relative.TrimEnd('/', '\\').Replace('/', Path.DirectorySeparatorChar));
                if (!SafeRelative(relative) || relative.Contains('*') || !Directory.Exists(full))
                { errors.Add("Source boundary prefix is missing or unsafe: " + relative); complete = false; }
                else if (!IsTrackedPath(project, full)) { errors.Add("Source boundary prefix is not version controlled: " + relative); complete = false; }
            }
            return complete;
        }
        catch (Exception ex) { errors.Add("Source boundary: " + ex.Message); return false; }
    }

    private static bool IsTrackedPath(string project, string path)
    {
        var gitRoot = GitValue(project, "rev-parse", "--show-toplevel");
        if (!string.IsNullOrWhiteSpace(gitRoot))
        {
            var relative = Path.GetRelativePath(gitRoot, path).Replace(Path.DirectorySeparatorChar, '/');
            return !string.IsNullOrWhiteSpace(GitValue(gitRoot, "ls-files", "--error-unmatch", relative));
        }
        return !string.IsNullOrWhiteSpace(ProcessValue("svn", new[] { "info", "--show-item", "revision", path }, project));
    }

    private static void AddCheck(List<object> checks, List<string> errors, string name, bool passed, string details)
    {
        checks.Add(new { name, passed, details });
        if (!passed) errors.Add(details);
    }

    private static string? ResolveOptionalFile(string? explicitPath, string root, string defaultRelative)
    {
        var path = string.IsNullOrWhiteSpace(explicitPath) ? Path.Combine(root, defaultRelative) : explicitPath;
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    private static string ProcessValue(string file, IEnumerable<string> arguments, string workingDirectory)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(file) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start); if (process == null) return "";
            var value = process.StandardOutput.ReadToEnd(); process.WaitForExit(); return process.ExitCode == 0 ? value.Trim() : "";
        }
        catch { return ""; }
    }

    private static string TreeHashForRelease(string root, IEnumerable<string> ignoredPaths)
    {
        var ignored = new HashSet<string>(ignoredPaths.Select(path => path.Replace('\\', '/').TrimStart('/')), StringComparer.OrdinalIgnoreCase);
        using var sha = SHA256.Create(); var buffer = new byte[1024 * 1024];
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(path =>
        {
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            return !ignored.Contains(relative) && !relative.Split('/')[0].Equals(".git", StringComparison.OrdinalIgnoreCase);
        }).OrderBy(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal))
        {
            var relative = Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/') + "\n");
            sha.TransformBlock(relative, 0, relative.Length, relative, 0);
            using var input = File.OpenRead(file); int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) sha.TransformBlock(buffer, 0, read, buffer, 0);
            var separator = new byte[] { 10 }; sha.TransformBlock(separator, 0, 1, separator, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return Convert.ToHexString(sha.Hash!);
    }

    private static bool SafeRelative(string value) => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains("..", StringComparison.Ordinal);

    private static int ReleaseGate(Cli cli)
    {
        var workflowPath = RequireFile(cli.Require("workflowreport"), "DHE workflow report");
        var planPath = RequireFile(cli.Require("projectplan"), "DHE project plan");
        var output = SafeReportPath(cli.Optional("output") ?? Path.Combine(Path.GetDirectoryName(workflowPath)!, "release-gate.json"), new[] { workflowPath, planPath });
        var target = cli.Require("target");
        var errors = new List<string>();
        var warnings = new List<string>();
        var workflow = ReadJson<JsonElement>(workflowPath);
        var plan = ReadJson<JsonElement>(planPath);
        var workflowRoot = Path.GetDirectoryName(workflowPath)!;
        var planRoot = Path.GetDirectoryName(planPath)!;
        var artifactValidationPath = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + ".artifact-validation.json");

        RequireFormat(workflow, "hybridclr.dhe-project-player-workflow.json", "Workflow", errors);
        RequireFormat(plan, "hybridclr.dhe-project-plan.json", "Project plan", errors);
        RequireTrue(workflow, "passed", "Workflow", errors);
        RequireTrue(workflow, "releaseReady", "Workflow", errors);
        if (!string.Equals(GetString(workflow, "mode"), "Release", StringComparison.Ordinal)) errors.Add("Workflow mode must be Release.");
        if (!string.Equals(GetString(workflow, "target"), target, StringComparison.OrdinalIgnoreCase)) errors.Add("Workflow target does not match the release target.");
        RequireTrue(plan, "complete", "Project plan", errors);
        RequireTrue(plan, "requireDheEqualsHotUpdate", "Project plan", errors);
        RequireTrue(plan, "dheEqualsHotUpdate", "Project plan", errors);
        var planValidationPath = ResolveEvidencePath(GetString(workflow, "projectPlanValidation"), workflowRoot, "project plan validation");
        var planValidation = ReadJson<JsonElement>(planValidationPath);
        RequireFormat(planValidation, "hybridclr.dhe-project-plan-validation.json", "Project plan validation", errors);
        RequireTrue(planValidation, "passed", "Project plan validation", errors);
        RequireTrue(planValidation, "coverageRequired", "Project plan validation", errors);
        RequireTrue(planValidation, "coverageComplete", "Project plan validation", errors);
        if (!Path.GetFullPath(ResolveEvidencePath(GetString(planValidation, "plan"), Path.GetDirectoryName(planValidationPath)!, "validated project plan")).Equals(Path.GetFullPath(planPath), StringComparison.OrdinalIgnoreCase))
            errors.Add("Project plan validation refers to a different plan.");

        var sourcePreflightPath = ResolveEvidencePath(GetString(workflow, "sourcePreflight"), workflowRoot, "source preflight");
        var cleanCheckoutPath = ResolveEvidencePath(GetString(workflow, "cleanCheckoutGate"), workflowRoot, "clean checkout report");
        var sourcePreflight = ReadJson<JsonElement>(sourcePreflightPath);
        var cleanCheckout = ReadJson<JsonElement>(cleanCheckoutPath);
        RequireFormat(sourcePreflight, "hybridclr.dhe-source-preflight.json", "Source preflight", errors);
        RequireTrue(sourcePreflight, "passed", "Source preflight", errors);
        RequireTrue(sourcePreflight, "runtimeRequired", "Source preflight", errors);
        RequireTrue(sourcePreflight, "runtimeReady", "Source preflight", errors);
        RequireTrue(sourcePreflight, "cleanRuntimeSourcesRequired", "Source preflight", errors);
        RequireTrue(sourcePreflight, "externalHeadersRequired", "Source preflight", errors);
        if (!sourcePreflight.TryGetProperty("externalHeadersSurrogate", out var surrogate) || surrogate.ValueKind != JsonValueKind.False)
            errors.Add("Source preflight must prove non-surrogate engine headers.");
        RequireFormat(cleanCheckout, "hybridclr.dhe-clean-checkout-gate.json", "Clean checkout", errors);
        RequireTrue(cleanCheckout, "passed", "Clean checkout", errors);
        RequireTrue(cleanCheckout, "gitCleanRequired", "Clean checkout", errors);
        RequireTrue(cleanCheckout, "trackedSourcesRequired", "Clean checkout", errors);
        RequireTrue(cleanCheckout, "trackedSourcesComplete", "Clean checkout", errors);
        ValidateSourceIdentity(cleanCheckout, "projectGit", errors);
        ValidateSourceIdentity(cleanCheckout, "toolGit", errors);
        var toolchainGatePath = GetString(workflow, "toolchainGate");
        if (string.IsNullOrWhiteSpace(toolchainGatePath)) errors.Add("Release workflow is missing toolchain package evidence.");
        else
        {
            var toolchainGate = ReadJson<JsonElement>(ResolveEvidencePath(toolchainGatePath, workflowRoot, "toolchain gate"));
            RequireFormat(toolchainGate, "hybridclr.dhe-toolchain-gate.json", "Toolchain gate", errors);
            RequireTrue(toolchainGate, "passed", "Toolchain gate", errors);
            RequireTrue(toolchainGate, "releaseReady", "Toolchain gate", errors);
            RequireTrue(toolchainGate, "requireRelease", "Toolchain gate", errors);
            if (!string.Equals(GetString(toolchainGate, "packageId"), GetString(workflow, "expectedToolchainPackageId"), StringComparison.OrdinalIgnoreCase))
                errors.Add("Toolchain gate package ID does not match the workflow pin.");
        }

        var planNames = StringArray(plan, "dheAotAssemblies", errors).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var hotNames = StringArray(plan, "hotUpdateAssemblies", errors).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!planNames.SequenceEqual(hotNames, StringComparer.OrdinalIgnoreCase)) errors.Add("Project plan does not have exact hot-update/DHE coverage.");
        var planRecords = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var liveCandidates = new Dictionary<string, (JsonElement Record,
            MetaVersionSnapshot Baseline, MetaVersionSnapshot Current)>(
            StringComparer.OrdinalIgnoreCase);
        var liveDiffs = new Dictionary<string, LiveAssemblyValidation>(
            StringComparer.OrdinalIgnoreCase);
        var changedMethodCount = 0;
        var methodCount = 0;
        var typeChangeCount = 0;
        if (!plan.TryGetProperty("assemblies", out var assemblies) || assemblies.ValueKind != JsonValueKind.Array)
            errors.Add("Project plan assemblies are missing.");
        else
        {
            foreach (var record in assemblies.EnumerateArray())
            {
                var name = GetString(record, "assemblyName") ?? "";
                if (string.IsNullOrWhiteSpace(name) || !planRecords.TryAdd(name, record)) { errors.Add("Project plan contains a missing or duplicate assembly name."); continue; }
                if (!string.Equals(GetString(record, "status"), "compatible", StringComparison.Ordinal)) errors.Add("Project plan assembly is not compatible: " + name);
                try
                {
                    var baseline = ResolveEvidencePath(GetString(record, "baseline"), planRoot, "baseline assembly");
                    var current = ResolveEvidencePath(GetString(record, "current"), planRoot, "current assembly");
                    MetaVersionSnapshot baselineMetaVersion = MetaVersionSnapshot.Create(baseline);
                    MetaVersionSnapshot currentMetaVersion = MetaVersionSnapshot.Create(current);
                    ValidateMetaVersionArtifacts(record, planRoot, "baseMetaVersion",
                        baselineMetaVersion, errors);
                    ValidateMetaVersionArtifacts(record, planRoot, "currentMetaVersion",
                        currentMetaVersion, errors);
                    liveCandidates[name] = (record, baselineMetaVersion,
                        currentMetaVersion);
                }
                catch (Exception ex) { errors.Add(name + ": " + ex.Message); }
            }
        }
        string[] addressTakenFields = liveCandidates.Values
            .SelectMany(candidate => candidate.Current.AddressTakenFieldIdentities)
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var pair in liveCandidates)
        {
            ResourceUpdateCompatibility compatibility = ResourceUpdateCompatibility.Analyze(
                pair.Value.Baseline, pair.Value.Current, addressTakenFields);
            if (!compatibility.Compatible)
                errors.Add("Live assembly revalidation is incompatible: " + pair.Key + ": " +
                    string.Join("; ", compatibility.UnsupportedChanges));
            liveDiffs[pair.Key] = new LiveAssemblyValidation(pair.Value.Baseline,
                pair.Value.Current, compatibility);
            changedMethodCount += CountRuntimeChangedMethods(pair.Value.Baseline,
                pair.Value.Current);
            methodCount += pair.Value.Baseline.Methods.Length;
            typeChangeCount += compatibility.ChangedExistingTypeCount +
                compatibility.AddedTypeCount + compatibility.RemovedTypeCount;
        }
        if (!planNames.SequenceEqual(planRecords.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            errors.Add("Project plan assembly records do not match the configured DHE assembly set.");

        var playerPath = ResolveEvidencePath(GetString(workflow, "playerResult") ?? GetString(workflow, "player"), workflowRoot, "Player result");
        var nativePath = ResolveEvidencePath(GetString(workflow, "nativeManifest"), workflowRoot, "native manifest");
        var identityPath = ResolveEvidencePath(GetString(workflow, "buildIdentity"), workflowRoot, "build identity");
        var runtimePlanPath = ResolveEvidencePath(GetString(workflow, "runtimePlan"), workflowRoot, "runtime plan");
        var resourcePath = ResolveEvidencePath(GetString(workflow, "resourceEvidence"), workflowRoot, "resource evidence");
        var player = ReadJson<JsonElement>(playerPath);
        var native = ReadJson<JsonElement>(nativePath);
        var identity = ReadJson<JsonElement>(identityPath);
        var runtimePlan = ReadJson<JsonElement>(runtimePlanPath);
        var resource = ReadJson<JsonElement>(resourcePath);

        RequireFormat(player, "hybridclr.dhe-player-result.json", "Player result", errors);
        RequireTrue(player, "passed", "Player result", errors);
        if (!string.Equals(GetString(player, "loadError"), "OK", StringComparison.Ordinal)) errors.Add("Player loadError is not OK.");
        if (!string.Equals(GetString(player, "target"), target, StringComparison.OrdinalIgnoreCase)) errors.Add("Player target does not match.");
        RequireTrue(player, "buildIdentityValidated", "Player result", errors);
        RequireTrue(player, "dispatchProbeValidated", "Player result", errors);
        var loadedNames = StringArray(player, "loadedDheAssemblies", errors).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var plannedNames = StringArray(player, "plannedDheAssemblies", errors).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!planNames.SequenceEqual(loadedNames, StringComparer.OrdinalIgnoreCase) || !planNames.SequenceEqual(plannedNames, StringComparer.OrdinalIgnoreCase))
            errors.Add("Player planned/loaded assembly sets do not match the project plan.");
        if (GetInt(player, "changedMethodCount") != changedMethodCount) errors.Add("Player changed method count does not match the revalidated MV set.");
        if (GetInt(player, "expectedChangedMethodCount") != changedMethodCount) errors.Add("Player expected changed method count does not match the revalidated MV set.");
        ValidatePlayerAssemblies(player, planNames, errors);
        if (changedMethodCount > 0)
        {
            if (GetInt(player, "interpreterEntryCount") <= 0) errors.Add("Changed workflow did not enter the interpreter.");
            if (GetInt(player, "aotEntryCount") <= 0) errors.Add("Changed workflow did not prove an unchanged AOT entry.");
            if (!GetBool(player, "changedProbeChanged") || GetBool(player, "unchangedProbeChanged"))
                errors.Add("Changed workflow dispatch probe did not distinguish interpreter and AOT paths.");
            if (!GetBool(player, "retryValidated") || GetString(player, "transactionStatus") != "validated" || GetString(player, "retryFailure") != "DHE_MV_REGISTRATION_FAILED")
                errors.Add("Changed workflow did not prove transaction rollback and retry.");
        }
        else if (GetInt(player, "interpreterEntryCount") != 0 || GetBool(player, "changedProbeChanged") ||
            GetBool(player, "unchangedProbeChanged") || GetString(player, "transactionStatus") != "notApplicable")
            errors.Add("No-op workflow reported interpreter or transaction activity.");

        ValidateNativeManifest(native, liveDiffs, errors);
        if (GetInt(native, "unsupportedGuardedMethodCount") != 0 ||
            !string.Equals(GetString(native, "guardMode"), "universal", StringComparison.Ordinal))
            errors.Add("Native manifest does not provide universal Base coverage.");
        if (changedMethodCount > 0 && GetInt(native, "nativeEntryCount") <= 0) errors.Add("Changed workflow has no native guard entries.");
        RequireFormat(identity, "hybridclr.dhe-build-identity.json", "Build identity", errors);
        if (GetInt(identity, "identityVersion") != 1 || GetString(identity, "aotSnapshotKind") != "managed-assembly-plus-generated-cpp-v1" ||
            !string.Equals(GetString(identity, "target"), target, StringComparison.OrdinalIgnoreCase)) errors.Add("Build identity contract or target is invalid.");
        var identityNativePath = ResolveEvidencePath(GetString(identity, "nativeManifestPath"),
            Path.GetDirectoryName(identityPath)!, "Build identity native manifest");
        var identityNativeHash = Sha256File(identityNativePath);
        if (!string.Equals(GetString(identity, "nativeManifestSha256"), identityNativeHash, StringComparison.OrdinalIgnoreCase))
            errors.Add("Build identity native manifest hash does not match its immutable manifest file.");
        if (!string.Equals(GetString(player, "nativeManifestSha256"), identityNativeHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(player, "nativeGuardSourceSha256"), GetString(identity, "nativeGuardSourceSha256"), StringComparison.OrdinalIgnoreCase))
            errors.Add("Player native identity does not match the final build identity.");
        ValidateBuildIdentity(identity, identityPath, native, nativePath, liveDiffs, errors);
        RequireFormat(runtimePlan, "hybridclr.dhe-runtime-handoff-plan.json", "Runtime plan", errors);
        var runtimeNames = runtimePlan.GetProperty("assemblies").EnumerateArray().Select(x => GetString(x, "assemblyName") ?? "").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!planNames.SequenceEqual(runtimeNames, StringComparer.OrdinalIgnoreCase)) errors.Add("Runtime plan assembly set does not match the project plan.");
        ValidateRuntimePlanFiles(runtimePlan, Path.GetDirectoryName(runtimePlanPath)!, errors);
        RequireFormat(resource, "hybridclr.dhe-resource-evidence.json", "Resource evidence", errors);
        RequireTrue(resource, "passed", "Resource evidence", errors);
        if (!string.Equals(GetString(resource, "target"), target, StringComparison.OrdinalIgnoreCase)) errors.Add("Resource evidence target does not match.");
        try { ValidateResourceEvidence(resourcePath, target); }
        catch (Exception ex) { errors.Add("Resource evidence: " + ex.Message); }

        var passed = errors.Count == 0;
        WriteJson(artifactValidationPath, new
        {
            schemaVersion = 1, format = "hybridclr.dhe-artifact-validation.json", generatedAtUtc = DateTimeOffset.UtcNow,
            pathSemantics = "workspace-absolute-v1", passed, errors, warnings,
            metaVersionJsons = planRecords.Values.SelectMany(record => new[] {
                GetString(record, "baseMetaVersionJson"),
                GetString(record, "currentMetaVersionJson") }).ToArray(),
            metaVersionByteFiles = planRecords.Values.SelectMany(record => new[] {
                GetString(record, "baseMetaVersionBytes"),
                GetString(record, "currentMetaVersionBytes") }).ToArray(),
            baselineAssemblies = planRecords.Values.Select(record => GetString(record, "baseline")).ToArray(),
            currentAssemblies = planRecords.Values.Select(record => GetString(record, "current")).ToArray(),
            baselineAssembly = (string?)null, currentAssembly = (string?)null, nativeManifest = nativePath,
            buildIdentity = identityPath, workflowReport = workflowPath, runtimePlan = runtimePlanPath,
            batchReport = GetString(workflow, "batchReport")
        });
        WriteJson(output, new
        {
            schemaVersion = 1, format = "hybridclr.dhe-release-gate.json", generatedAtUtc = DateTimeOffset.UtcNow,
            passed, target, workflowMode = GetString(workflow, "mode"), sourcePreflight = sourcePreflightPath, sourcePreflightValidated = GetBool(sourcePreflight, "passed"),
            cleanCheckout = cleanCheckoutPath, cleanCheckoutValidated = GetBool(cleanCheckout, "passed"), projectGitHead = IdentityString(cleanCheckout, "projectGit", "head"), projectGitTree = IdentityString(cleanCheckout, "projectGit", "tree"), projectSourceBoundarySha256 = IdentityString(cleanCheckout, "projectGit", "sourceBoundarySha256"),
            toolGitHead = IdentityString(cleanCheckout, "toolGit", "head"), toolGitTree = IdentityString(cleanCheckout, "toolGit", "tree"), toolSourceBoundarySha256 = IdentityString(cleanCheckout, "toolGit", "sourceBoundarySha256"), projectVcs = IdentityString(cleanCheckout, "projectGit", "vcs"), projectRevision = IdentityString(cleanCheckout, "projectGit", "revision"),
            projectRevisionSpec = IdentityString(cleanCheckout, "projectGit", "revisionSpec"), projectRepository = IdentityString(cleanCheckout, "projectGit", "repository"),
            toolVcs = IdentityString(cleanCheckout, "toolGit", "vcs"), toolRevision = IdentityString(cleanCheckout, "toolGit", "revision"),
            toolRevisionSpec = IdentityString(cleanCheckout, "toolGit", "revisionSpec"), toolRepository = IdentityString(cleanCheckout, "toolGit", "repository"),
            projectPlanValidation = planValidationPath, projectPlanRevalidation = planPath,
            workflowReport = workflowPath, artifactValidation = artifactValidationPath, batchReport = GetString(workflow, "batchReport") ?? "",
            runtimePlan = runtimePlanPath, artifactValidatorExitCode = passed ? 0 : 1, projectPlanValidatorExitCode = passed ? 0 : 1,
            errors, warnings, validatedAssemblyCount = planRecords.Count, loadedDheAssemblyCount = loadedNames.Length,
            methodCount, changedMethodCount, typeChangeCount, playerResult = playerPath, nativeManifest = nativePath,
            buildIdentity = identityPath, resourceEvidence = resourcePath
        });
        if (!passed) { Console.Error.WriteLine(string.Join(Environment.NewLine, errors)); return 1; }
        Console.WriteLine("DHE release gate passed: " + output);
        return 0;
    }

    private static int Regression(Cli cli)
    {
        var baseline = RequireFile(cli.Require("layoutbaseline"), "Layout regression baseline");
        var current = RequireFile(cli.Require("layoutcurrent"), "Layout regression current");
        var output = SafeReportPath(cli.Require("output"), new[] { baseline, current });
        var checks = new List<object>();
        var errors = new List<string>();
        MetaVersionSnapshot baselineMetaVersion = MetaVersionSnapshot.Create(baseline);
        MetaVersionSnapshot currentMetaVersion = MetaVersionSnapshot.Create(current);
        ResourceUpdateCompatibility layoutCompatibility = ResourceUpdateCompatibility.Analyze(
            baselineMetaVersion, currentMetaVersion);
        var layoutRejected = !layoutCompatibility.Compatible &&
            layoutCompatibility.ChangedExistingTypeCount > 0;
        AddRegressionCheck(checks, errors, "mv-field-order", layoutRejected,
            layoutRejected ? "layout change rejected" : "layout change accepted");
        var regressionRoot = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + ".work");
        if (Directory.Exists(regressionRoot)) Directory.Delete(regressionRoot, true);
        Directory.CreateDirectory(regressionRoot);
        var switchAssembly = Path.Combine(regressionRoot, "switch-target.dll");
        WriteMutatedAssembly(baseline, switchAssembly, module =>
        {
            var method = module.GetTypes().SelectMany(type => type.Methods)
                .Single(item => item.Name == "SwitchProbe");
            var targets = method.Body.Instructions.Select(instruction => instruction.Operand)
                .OfType<IList<Instruction>>().Single();
            (targets[0], targets[1]) = (targets[1], targets[0]);
        });
        MetaVersionSnapshot switchMetaVersion = MetaVersionSnapshot.Create(switchAssembly);
        ResourceUpdateCompatibility switchCompatibility = ResourceUpdateCompatibility.Analyze(
            baselineMetaVersion, switchMetaVersion);
        AddRegressionCheck(checks, errors, "mv-switch-target", switchCompatibility.Compatible &&
            switchCompatibility.ChangedMethodCount == 1,
            "switch target table must be detected as a method-body change");

        var metadataAssembly = Path.Combine(regressionRoot, "assembly-metadata.dll");
        WriteMutatedAssembly(baseline, metadataAssembly, module =>
            module.Assembly.Version = new Version((module.Assembly.Version?.Major ?? 1) + 1, 0, 0, 0));
        ResourceUpdateCompatibility metadataCompatibility = ResourceUpdateCompatibility.Analyze(
            baselineMetaVersion, MetaVersionSnapshot.Create(metadataAssembly));
        AddRegressionCheck(checks, errors, "mv-assembly-metadata", !metadataCompatibility.Compatible,
            "assembly metadata change must be rejected");

		var referenceRemovalAssembly = Path.Combine(regressionRoot, "reference-removal.dll");
		WriteMutatedAssembly(baseline, referenceRemovalAssembly, module =>
		{
			TypeDef referenceType = module.GetTypes().Single(type =>
				type.Name == "ReferenceFieldRemoval");
			referenceType.Fields.Remove(referenceType.Fields.Single(field => field.Name == "Removed"));
			referenceType.Fields.Remove(referenceType.Fields.Single(field => field.Name == "RemovedStatic"));
			TypeDef removedType = module.Types.Single(type => type.Name == "RemovedReferenceType");
			module.Types.Remove(removedType);
		});
		ResourceUpdateCompatibility referenceRemoval = ResourceUpdateCompatibility.Analyze(
			MetaVersionSnapshot.Create(baseline), MetaVersionSnapshot.Create(referenceRemovalAssembly));
		AddRegressionCheck(checks, errors, "reference-field-and-type-removal",
			referenceRemoval.Compatible && referenceRemoval.RemovedFieldCount == 3 &&
			referenceRemoval.RemovedTypeCount == 1 && referenceRemoval.DependencyChangedMethodCount > 0 &&
			referenceRemoval.BodyOnlyChangedMethodCount == 0,
			"reference/static field removal and type tombstones must be accepted with dependency propagation");

		var valueRemovalAssembly = Path.Combine(regressionRoot, "value-field-removal.dll");
		WriteMutatedAssembly(baseline, valueRemovalAssembly, module =>
		{
			TypeDef valueType = module.GetTypes().Single(type => type.Name == "ValueFieldRemoval");
			valueType.Fields.Remove(valueType.Fields.Single(field => field.Name == "Removed"));
		});
		ResourceUpdateCompatibility valueRemoval = ResourceUpdateCompatibility.Analyze(
			MetaVersionSnapshot.Create(baseline), MetaVersionSnapshot.Create(valueRemovalAssembly));
		AddRegressionCheck(checks, errors, "value-field-removal-rejected",
			!valueRemoval.Compatible && valueRemoval.UnsupportedChanges.Any(change =>
				change.StartsWith("removed-instance-field-on-existing-value-type:",
					StringComparison.Ordinal)),
			"value-type instance field removal must remain fail-closed until shadow layout exists");

        string[] requiredCapabilities =
        {
            "aot-guard-v1",
            "single-current-multibase-v1",
            "supplemental-existing-type-instance-fields-v1",
        };
        bool v1Compatible = ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v1",
            ResourceUpdateCompatibility.KnownRuntimeCapabilities, requiredCapabilities);
        bool v2Compatible = ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v2",
            ResourceUpdateCompatibility.KnownRuntimeCapabilities, requiredCapabilities);
        AddRegressionCheck(checks, errors,
            "runtime-contract-capability-negotiation", v1Compatible && v2Compatible,
            "different runtime build contracts under protocol v1 must be accepted by capability subset");
        string[] missingCapability = ResourceUpdateCompatibility.KnownRuntimeCapabilities
            .Where(value => value != "supplemental-existing-type-instance-fields-v1").ToArray();
        AddRegressionCheck(checks, errors, "runtime-capability-missing-rejected",
            !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v0",
                missingCapability, requiredCapabilities),
            "a Base missing one required update capability must be rejected");
        string identityHash = new string('a', 64);
        string baseIdV1 = ComputeBaseId("StandaloneWindows64", identityHash,
            new string('b', 64), new string('c', 64), new string('d', 64),
            new string('e', 64), ResourceUpdateCompatibility.RuntimeProtocol,
            "dhe-runtime-v1", requiredCapabilities,
            "HybridCLRLab/DheDemo/", "HybridCLRLab/DheDemo/BaseMetaVersion/");
        string baseIdV2 = ComputeBaseId("StandaloneWindows64", identityHash,
            new string('b', 64), new string('c', 64), new string('d', 64),
            new string('e', 64), ResourceUpdateCompatibility.RuntimeProtocol,
            "dhe-runtime-v2", requiredCapabilities,
            "HybridCLRLab/DheDemo/", "HybridCLRLab/DheDemo/BaseMetaVersion/");
        AddRegressionCheck(checks, errors, "composite-base-id-runtime-bound",
            IsHex(baseIdV1, 64, 64) && IsHex(baseIdV2, 64, 64) &&
            !string.Equals(baseIdV1, baseIdV2, StringComparison.OrdinalIgnoreCase),
            "Base ID must distinguish runtime contracts even for identical managed assemblies");

        var mvPath = Path.Combine(regressionRoot, "switch.mv.bytes");
        File.WriteAllBytes(mvPath, switchMetaVersion.ToBinary());
        var flagsPath = Path.Combine(regressionRoot, "switch-flags.mv.bytes");
        var flagsBytes = File.ReadAllBytes(mvPath); flagsBytes[12] = 2; File.WriteAllBytes(flagsPath, flagsBytes);
        var flagsRejected = !flagsBytes.SequenceEqual(switchMetaVersion.ToBinary());
        AddRegressionCheck(checks, errors, "mv-flags-tamper", flagsRejected, "unknown MV flags must be rejected");
        var tokenPath = Path.Combine(regressionRoot, "switch-token.mv.bytes");
        var tokenBytes = File.ReadAllBytes(mvPath);
        var nameLength = checked((int)BitConverter.ToUInt32(tokenBytes, 16));
        var typeCount = checked((int)BitConverter.ToUInt32(tokenBytes, 20));
        var tokenOffset = checked(60 + nameLength + typeCount * 72 + 96);
        BitConverter.GetBytes(BitConverter.ToUInt32(tokenBytes, tokenOffset) + 1).CopyTo(tokenBytes, tokenOffset);
        File.WriteAllBytes(tokenPath, tokenBytes);
        AddRegressionCheck(checks, errors, "mv-token-tamper",
            !tokenBytes.SequenceEqual(switchMetaVersion.ToBinary()),
            "same-count wrong MV token set must be rejected");

        string? resourceUpdateRoot = cli.Optional("resourceupdateroot");
        string? resourceAssetRoot = cli.Optional("resourceassetroot");
        string? resourceBaseBuildIdentity = cli.Optional("resourcebasebuildidentity");
        if (!string.IsNullOrWhiteSpace(resourceUpdateRoot) ||
            !string.IsNullOrWhiteSpace(resourceAssetRoot) ||
            !string.IsNullOrWhiteSpace(resourceBaseBuildIdentity))
        {
            if (string.IsNullOrWhiteSpace(resourceUpdateRoot) ||
                string.IsNullOrWhiteSpace(resourceAssetRoot) ||
                string.IsNullOrWhiteSpace(resourceBaseBuildIdentity))
            {
                AddRegressionCheck(checks, errors, "resource-stage-input-set", false,
                    "ResourceUpdateRoot, ResourceAssetRoot, and ResourceBaseBuildIdentity " +
                    "must be supplied together.");
            }
            else
            {
                RunResourceStagingRegressions(
                    RequireDirectory(resourceUpdateRoot, "Regression resource update"),
                    RequireDirectory(resourceAssetRoot, "Regression resource asset root"),
                    RequireFile(resourceBaseBuildIdentity,
                        "Regression Base Player build identity"),
                    regressionRoot, checks, errors);
            }
        }

        var packageRoot = cli.Optional("packageroot");
        if (!string.IsNullOrWhiteSpace(packageRoot))
        {
            packageRoot = RequireDirectory(packageRoot, "Regression package");
            var requireReleaseRejected = VerifyPackage(new Cli("verify-package", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["packageroot"] = packageRoot, ["requirerelease"] = "true" })) != 0;
            AddRegressionCheck(checks, errors, "verify-require-release", requireReleaseRejected,
                "exploratory package must be rejected");
            var wrongIdRejected = VerifyPackage(new Cli("verify-package", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["packageroot"] = packageRoot, ["expectedpackageid"] = new string('a', 64) })) != 0;
            AddRegressionCheck(checks, errors, "verify-expected-id", wrongIdRejected,
                "wrong package ID must be rejected");

            var manifestTamper = Path.Combine(regressionRoot, "manifest-tamper-package");
            CopyDirectory(packageRoot, manifestTamper);
            var programPath = Path.Combine(manifestTamper, "tool", "Program.cs");
            File.AppendAllText(programPath, "\n// regression tamper\n", new UTF8Encoding(false));
            var manifestPath = Path.Combine(manifestTamper, "dhe-toolchain-manifest.json");
            var manifestNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            var programEntry = manifestNode["files"]!.AsArray().Select(node => node!.AsObject())
                .Single(node => node["path"]!.GetValue<string>() == "tool/Program.cs");
            programEntry["size"] = new FileInfo(programPath).Length;
            programEntry["sha256"] = Sha256File(programPath);
            File.WriteAllText(manifestPath, manifestNode.ToJsonString(Json), new UTF8Encoding(false));
            var manifestTamperRejected = VerifyPackage(new Cli("verify-package", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["packageroot"] = manifestTamper })) != 0;
            AddRegressionCheck(checks, errors, "verify-package-id-recompute", manifestTamperRejected,
                "updated per-file hash with stale package ID must be rejected");

            var extraSource = Path.Combine(regressionRoot, "extra-source-package");
            CopyDirectory(packageRoot, extraSource);
            File.WriteAllText(Path.Combine(extraSource, "tool", "Injected.cs"), "namespace HybridCLR.DheTool { internal static class Injected { } }\n", new UTF8Encoding(false));
            var extraSourceRejected = VerifyPackage(new Cli("verify-package", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["packageroot"] = extraSource })) != 0;
            AddRegressionCheck(checks, errors, "verify-extra-source", extraSourceRejected,
                "unlisted compilable source must be rejected");

            var releaseTamper = Path.Combine(regressionRoot, "release-bit-package");
            CopyDirectory(packageRoot, releaseTamper);
            var releaseManifestPath = Path.Combine(releaseTamper, "dhe-toolchain-manifest.json");
            var releaseNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(releaseManifestPath))!.AsObject();
            releaseNode["mode"] = "Release"; releaseNode["releaseReady"] = true;
            File.WriteAllText(releaseManifestPath, releaseNode.ToJsonString(Json), new UTF8Encoding(false));
            var releaseTamperRejected = VerifyPackage(new Cli("verify-package", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["packageroot"] = releaseTamper, ["requirerelease"] = "true" })) != 0;
            AddRegressionCheck(checks, errors, "verify-release-bit-tamper", releaseTamperRejected,
                "release mode/bit tamper must be rejected");
        }
        else
        {
            foreach (var name in new[] { "verify-require-release", "verify-expected-id", "verify-package-id-recompute",
                         "verify-extra-source", "verify-release-bit-tamper" })
                AddRegressionCheck(checks, errors, name, false, "PackageRoot is required for production regression.");
        }

        var roleRejected = false;
        try
        {
            using var wrongRole = JsonDocument.Parse("{\"schemaVersion\":1,\"format\":\"hybridclr.dhe-regression.json\",\"passed\":true}");
            ValidateEvidenceRole("native-tuanjie2022", wrongRole.RootElement, output, new string('a', 40),
                new string('b', 40), cli.Root);
        }
        catch { roleRejected = true; }
        AddRegressionCheck(checks, errors, "evidence-role-format", roleRejected,
            "release evidence role must enforce its report format");

        var unboundNativeRejected = false;
        try
        {
            using var unboundNative = JsonDocument.Parse("{\"schemaVersion\":1," +
                "\"format\":\"hybridclr.dhe-native-gate.json\",\"passed\":true," +
                "\"mergeReady\":true,\"profile\":\"DHE-Tuanjie2022\"," +
                "\"configuration\":\"Release\",\"runtimeManifest\":\"missing.json\"," +
                "\"runtimeManifestSha256\":\"" + new string('a', 64) + "\"," +
                "\"runtimeRoot\":\"missing\",\"runtimeTreeSha256\":\"" + new string('b', 64) + "\"," +
                "\"externalTreeSha256\":\"" + new string('c', 64) + "\"," +
                "\"nativeExitCode\":0,\"surrogateHeadersAllowed\":false,\"errors\":[]}");
            ValidateEvidenceRole("native-tuanjie2022", unboundNative.RootElement, output, new string('a', 40),
                new string('b', 40), cli.Root);
        }
        catch { unboundNativeRejected = true; }
        AddRegressionCheck(checks, errors, "evidence-native-runtime-binding", unboundNativeRejected,
            "native release evidence must bind the live runtime, headers, locks, and source commits");
        var runtimeSource = File.ReadAllText(Path.Combine(cli.Root, "tool", "LabCommands.cs"));
        AddRegressionCheck(checks, errors, "runtime-package-source-binding",
            runtimeSource.Contains("ValidateRepoIdentity(\"hybridclr_unity\"", StringComparison.Ordinal),
            "runtime assembly must fail closed on the locked package source identity");

        var weakNoOpRejected = false;
        try
        {
            using var weakNoOp = JsonDocument.Parse("{\"changedMethodCount\":0," +
                "\"interpreterEntryCount\":0,\"changedProbeChanged\":false," +
                "\"unchangedProbeChanged\":false,\"transactionStatus\":\"notApplicable\"," +
                "\"dispatchProbeValidated\":true,\"noOpAotBehaviorValidated\":false," +
                "\"multiAssemblyValidated\":true,\"capabilityDirectPassed\":true," +
                "\"capabilityPassed\":true,\"secondaryAssemblyDirectValidated\":true}");
            ValidateNoOpPlayerEvidence(weakNoOp.RootElement);
        }
        catch { weakNoOpRejected = true; }
        AddRegressionCheck(checks, errors, "evidence-noop-aot-proof", weakNoOpRejected,
            "no-op release evidence must prove unchanged AOT behavior");

        var matrixEvidenceSchema = ReadJson<JsonElement>(Path.Combine(cli.Root, "schemas",
            "dhe-toolchain-release-evidence.schema.json"));
        using var matrixEvidence = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            format = "hybridclr.dhe-toolchain-release-evidence.json",
            generatedAtUtc = DateTimeOffset.UtcNow,
            passed = true,
            sourceHead = new string('a', 40),
            sourceTree = new string('b', 40),
            files = RequiredReleaseEvidenceRoles.Select(role => new
            {
                role,
                path = "reports/" + role + ".json",
                sha256 = new string('c', 64)
            }).ToArray()
        }));
        var matrixEvidenceErrors = new List<string>();
        ValidateJsonSchema(matrixEvidenceSchema, matrixEvidence.RootElement, matrixEvidenceSchema, "$",
            matrixEvidenceErrors);
        AddRegressionCheck(checks, errors, "evidence-native-matrix-roles",
            RequiredReleaseEvidenceRoles.Length == 6 && matrixEvidenceErrors.Count == 0,
            "release evidence must require changed, no-op, and all three native engine lanes");

        var unsafeArchive = Path.Combine(regressionRoot, "not-an-archive");
        Directory.CreateDirectory(unsafeArchive);
        var sentinel = Path.Combine(unsafeArchive, "sentinel.txt"); File.WriteAllText(sentinel, "keep", new UTF8Encoding(false));
        var unsafeArchiveRejected = false;
        try { PrepareArchiveDestination(unsafeArchive, true); } catch { unsafeArchiveRejected = File.Exists(sentinel); }
        AddRegressionCheck(checks, errors, "archive-safe-replace", unsafeArchiveRejected,
            "non-archive directory replacement must be rejected without deleting contents");
        RunGuardBlockHashRegressions(regressionRoot, checks, errors);
        var layoutDocument = ReadJson<JsonElement>(Path.Combine(cli.Root, "manifests",
            "dhe-toolchain-layout.json"));
        var layoutPaths = layoutDocument.GetProperty("exactPaths").EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? "").ToHashSet(StringComparer.Ordinal);
        var releaseRoleSchemas = new[]
        {
            "schemas/dhe-regression.schema.json",
            "schemas/dhe-workflow-report.schema.json",
            "schemas/dhe-native-gate.schema.json",
            "schemas/dhe-toolchain-release-evidence.schema.json",
        };
        AddRegressionCheck(checks, errors, "layout-release-role-schemas",
            releaseRoleSchemas.All(layoutPaths.Contains),
            "the authenticated package layout must include every release evidence role schema");
        var schemasRoot = Path.Combine(cli.Root, "schemas");
        var workflowSchema = ReadJson<JsonElement>(Path.Combine(schemasRoot, "dhe-workflow-config.schema.json"));
        var validConfig = ReadJson<JsonElement>(Path.Combine(cli.Root, "templates", "dhe-workflow-config.json"));
        var validConfigErrors = new List<string>();
        ValidateSchemaVocabulary(workflowSchema, "$", validConfigErrors);
        ValidateJsonSchema(workflowSchema, validConfig, workflowSchema, "$", validConfigErrors);
        AddRegressionCheck(checks, errors, "schema-valid-document", validConfigErrors.Count == 0,
            validConfigErrors.Count == 0 ? "valid workflow config accepted" : string.Join("; ", validConfigErrors));

        var maximumNode = System.Text.Json.Nodes.JsonNode.Parse(validConfig.GetRawText())!.AsObject();
        maximumNode["unityTimeoutSeconds"] = 3601;
        using var maximumDocument = JsonDocument.Parse(maximumNode.ToJsonString());
        var maximumErrors = new List<string>();
        ValidateJsonSchema(workflowSchema, maximumDocument.RootElement, workflowSchema, "$", maximumErrors);
        AddRegressionCheck(checks, errors, "schema-maximum-rejected", maximumErrors.Count > 0,
            "workflow timeout above the schema maximum must be rejected");

        var additionalNode = System.Text.Json.Nodes.JsonNode.Parse(validConfig.GetRawText())!.AsObject();
        additionalNode["unityArguments"]!["invalid"] = new System.Text.Json.Nodes.JsonArray(1, 2);
        using var additionalDocument = JsonDocument.Parse(additionalNode.ToJsonString());
        var additionalErrors = new List<string>();
        ValidateJsonSchema(workflowSchema, additionalDocument.RootElement, workflowSchema, "$", additionalErrors);
        AddRegressionCheck(checks, errors, "schema-additional-type-rejected", additionalErrors.Count > 0,
            "additional property schema must reject an invalid value type");

        using var unsupportedSchemaDocument = JsonDocument.Parse("{\"type\":\"object\",\"oneOf\":[]}");
        var unsupportedErrors = new List<string>();
        ValidateSchemaVocabulary(unsupportedSchemaDocument.RootElement, "$", unsupportedErrors);
        AddRegressionCheck(checks, errors, "schema-unsupported-keyword-rejected", unsupportedErrors.Count > 0,
            "unsupported schema assertion keywords must fail closed");

        var schemaGatePassed = false;
        if (!string.IsNullOrWhiteSpace(packageRoot) && Directory.Exists(packageRoot))
        {
            var schemaGatePath = Path.Combine(regressionRoot, "schema-gate.json");
            var schemaGateExit = SchemaGate(new Cli("schema-gate", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["schemasroot"] = Path.Combine(packageRoot, "schemas"),
                ["inputroot"] = packageRoot,
                ["output"] = schemaGatePath,
                ["requireknownformats"] = "true"
            }));
            if (schemaGateExit == 0)
            {
                var gateSchema = ReadJson<JsonElement>(Path.Combine(schemasRoot, "dhe-schema-gate.schema.json"));
                var gateErrors = new List<string>();
                ValidateJsonSchema(gateSchema, ReadJson<JsonElement>(schemaGatePath), gateSchema, "$", gateErrors);
                schemaGatePassed = gateErrors.Count == 0;
            }
        }
        AddRegressionCheck(checks, errors, "schema-gate-contract", schemaGatePassed,
            "distributed package documents and schema gate evidence must validate");
        var workflowSchemaPassed = false;
        var realWorkflowOutputsValidated = false;
        var workflowOutputs = new List<object>();
        var changedWorkflowRoot = cli.Optional("workflowchangedroot");
        var noOpWorkflowRoot = cli.Optional("workflownooproot");
        if (!string.IsNullOrWhiteSpace(packageRoot) && Directory.Exists(packageRoot) &&
            !string.IsNullOrWhiteSpace(changedWorkflowRoot) && Directory.Exists(changedWorkflowRoot) &&
            !string.IsNullOrWhiteSpace(noOpWorkflowRoot) && Directory.Exists(noOpWorkflowRoot))
        {
            var workflowRoots = new[]
            {
                (Name: "changed", Root: Path.GetFullPath(changedWorkflowRoot)),
                (Name: "noop", Root: Path.GetFullPath(noOpWorkflowRoot))
            };
            workflowSchemaPassed = workflowRoots.All(item => SchemaGate(new Cli("schema-gate", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["schemasroot"] = Path.Combine(packageRoot, "schemas"),
                ["inputroot"] = item.Root,
                ["output"] = Path.Combine(regressionRoot, "schema-gate-" + item.Name + ".json"),
                ["requireknownformats"] = "true"
            })) == 0);
            realWorkflowOutputsValidated = workflowSchemaPassed;
            if (workflowSchemaPassed)
            {
                foreach (var item in workflowRoots)
                {
                    var reportPath = RequireFile(Path.Combine(item.Root, "player-workflow-report.json"),
                        item.Name + " workflow report");
                    workflowOutputs.Add(new
                    {
                        role = item.Name == "changed" ? "demo-changed" : "demo-noop",
                        path = reportPath,
                        sha256 = Sha256File(reportPath)
                    });
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(packageRoot) && Directory.Exists(packageRoot))
        {
            var requiredOutputSchemas = new[]
            {
                "dhe-adapter-native-finalize.schema.json", "dhe-adapter-native-guards.schema.json",
                "dhe-adapter-player-build.schema.json", "dhe-adapter-stage.schema.json",
                "dhe-metaversion.schema.json", "dhe-native-manifest.schema.json",
                "dhe-player-result.schema.json", "dhe-project-preflight.schema.json",
                "dhe-workflow-report.schema.json"
            };
            workflowSchemaPassed = requiredOutputSchemas.All(name => File.Exists(Path.Combine(packageRoot,
                "schemas", name)));
        }
        AddRegressionCheck(checks, errors, "schema-workflow-output-contract", workflowSchemaPassed,
            realWorkflowOutputsValidated
                ? "real changed and no-op workflow output trees passed the distributed schema gate"
                : "the distributed package must contain every workflow output schema");
        var sourceHead = GitValue(cli.Root, "rev-parse", "HEAD");
        var sourceTree = GitValue(cli.Root, "rev-parse", "HEAD^{tree}");
        var sourceClean = !string.IsNullOrWhiteSpace(sourceHead) && string.IsNullOrWhiteSpace(GitValue(cli.Root, "status", "--porcelain"));
        var passed = errors.Count == 0;
        WriteJson(output, new { schemaVersion = 1, format = "hybridclr.dhe-regression.json", generatedAtUtc = DateTimeOffset.UtcNow, sourceHead, sourceTree, sourceClean, passed, realWorkflowOutputsValidated, workflowOutputs, checks, errors, warnings = Array.Empty<string>() });
        Console.WriteLine("DHE regression " + (passed ? "passed: " : "failed: ") + output);
        return passed ? 0 : 1;
    }

    private static void AddRegressionCheck(List<object> checks, List<string> errors, string name, bool passed,
        string details)
    {
        checks.Add(new { name, passed, details });
        if (!passed) errors.Add(name + ": " + details);
    }

    private static void RunResourceStagingRegressions(string updateRoot, string assetRoot,
        string baseBuildIdentityPath, string regressionRoot, List<object> checks,
        List<string> errors)
    {
        string root = Path.Combine(regressionRoot, "resource-staging");
        Directory.CreateDirectory(root);

        bool Stage(string name, string sourceUpdateRoot, string sourceAssetRoot,
            string sourceBaseBuildIdentity)
        {
            try
            {
                return StageResourceUpdate(new Cli("stage-resource-update",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["updateroot"] = sourceUpdateRoot,
                        ["assetroot"] = sourceAssetRoot,
                        ["basebuildidentity"] = sourceBaseBuildIdentity,
                        ["output"] = Path.Combine(root, name + ".json"),
                    })) == 0;
            }
            catch
            {
                return false;
            }
        }

        (string Update, string Assets, string Identity) CopyFixture(string name)
        {
            string update = Path.Combine(root, name + "-update");
            string assets = Path.Combine(root, name + "-assets");
            string identity = Path.Combine(root, name + "-build-identity.json");
            CopyDirectory(updateRoot, update);
            CopyDirectory(assetRoot, assets);
            File.Copy(baseBuildIdentityPath, identity, true);
            return (update, assets, identity);
        }

        var positive = CopyFixture("positive");
        bool positiveStaged = Stage("positive-stage", positive.Update, positive.Assets,
            positive.Identity);
        AddRegressionCheck(checks, errors, "resource-stage-valid", positiveStaged,
            "a valid resource update must stage into its matching Base.");

        var positiveManifest = ReadJson<JsonElement>(Path.Combine(positive.Update,
            "dhe-resource-update.json"));
        string positiveRuntimeAssetRoot = RequirePortableAssetRoot(
            GetString(positiveManifest, "runtimeAssetRoot"), "runtimeAssetRoot");
        JsonElement positiveAotMetadata = positiveManifest.GetProperty("aotMetadata");
        JsonElement positiveBases = positiveManifest.GetProperty("supportedBases");
        bool planCapabilityBound = positiveBases.EnumerateArray().All(supportedBase =>
            supportedBase.GetProperty("requiredRuntimeCapabilities").EnumerateArray().Any(value =>
                string.Equals(value.GetString(), "resource-update-plan-integrity-v1",
                    StringComparison.Ordinal)));
        AddRegressionCheck(checks, errors, "resource-stage-plan-capability-bound",
            planCapabilityBound,
            "every resource update Base must require manifest-bound runtime plan validation.");
        bool aotMetadataCapabilityBound = positiveAotMetadata.GetArrayLength() == 0 ||
            positiveBases.EnumerateArray().All(supportedBase =>
                supportedBase.GetProperty("requiredRuntimeCapabilities").EnumerateArray().Any(value =>
                    string.Equals(value.GetString(), "resource-update-aot-metadata-path-v1",
                        StringComparison.Ordinal)));
        AddRegressionCheck(checks, errors, "resource-stage-aot-metadata-capability-bound",
            aotMetadataCapabilityBound,
            "a resource update with AOT metadata must require plan-directed metadata loading.");
        bool positiveAotMetadataCopied = positiveStaged &&
            positiveAotMetadata.ValueKind == JsonValueKind.Array &&
            positiveAotMetadata.EnumerateArray().All(metadata =>
            {
                string assetPath = GetString(metadata, "path") ?? string.Empty;
                string expectedHash = GetString(metadata, "sha256") ?? string.Empty;
                if (!assetPath.StartsWith(positiveRuntimeAssetRoot,
                        StringComparison.OrdinalIgnoreCase)) return false;
                string target = ResolveContainedPath(positive.Assets,
                    assetPath[positiveRuntimeAssetRoot.Length..],
                    "Regression staged AOT metadata");
                return File.Exists(target) && string.Equals(Sha256File(target), expectedHash,
                    StringComparison.OrdinalIgnoreCase);
            });
        AddRegressionCheck(checks, errors, "resource-stage-aot-metadata-copied",
            positiveAotMetadataCopied,
            "a valid resource update must contain and copy every hashed AOT metadata payload.");

        var runtimePlanTamper = CopyFixture("runtime-plan-tamper");
        var runtimePlanTamperManifest = ReadJson<JsonElement>(Path.Combine(
            runtimePlanTamper.Update, "dhe-resource-update.json"));
        string runtimePlanTamperPath = ResolveContainedPath(runtimePlanTamper.Update,
            GetString(runtimePlanTamperManifest, "runtimePlan") ?? string.Empty,
            "Regression runtime plan");
        File.AppendAllText(runtimePlanTamperPath, Environment.NewLine,
            new UTF8Encoding(false));
        AddRegressionCheck(checks, errors, "resource-stage-runtime-plan-tamper-rejected",
            !Stage("runtime-plan-tamper-stage", runtimePlanTamper.Update,
                runtimePlanTamper.Assets, runtimePlanTamper.Identity),
            "a runtime plan whose bytes do not match the manifest must be rejected.");

        if (positiveAotMetadata.GetArrayLength() > 0)
        {
            var aotMetadataTamper = CopyFixture("aot-metadata-tamper");
            var aotMetadataTamperManifest = ReadJson<JsonElement>(Path.Combine(
                aotMetadataTamper.Update, "dhe-resource-update.json"));
            string aotMetadataRuntimeRoot = RequirePortableAssetRoot(
                GetString(aotMetadataTamperManifest, "runtimeAssetRoot"), "runtimeAssetRoot");
            string aotMetadataAssetPath = GetString(
                aotMetadataTamperManifest.GetProperty("aotMetadata")[0], "path") ?? string.Empty;
            string aotMetadataTamperPath = ResolveContainedPath(aotMetadataTamper.Update,
                aotMetadataAssetPath[aotMetadataRuntimeRoot.Length..],
                "Regression AOT metadata payload");
            byte[] aotMetadataTamperBytes = File.ReadAllBytes(aotMetadataTamperPath);
            aotMetadataTamperBytes[^1] ^= 0x5a;
            File.WriteAllBytes(aotMetadataTamperPath, aotMetadataTamperBytes);
            AddRegressionCheck(checks, errors, "resource-stage-aot-metadata-tamper-rejected",
                !Stage("aot-metadata-tamper-stage", aotMetadataTamper.Update,
                    aotMetadataTamper.Assets, aotMetadataTamper.Identity),
                "an AOT metadata payload whose bytes do not match the manifest must be rejected.");

            var aotMetadataMissing = CopyFixture("aot-metadata-missing");
            var aotMetadataMissingManifest = ReadJson<JsonElement>(Path.Combine(
                aotMetadataMissing.Update, "dhe-resource-update.json"));
            string aotMetadataMissingRuntimeRoot = RequirePortableAssetRoot(
                GetString(aotMetadataMissingManifest, "runtimeAssetRoot"), "runtimeAssetRoot");
            string aotMetadataMissingAssetPath = GetString(
                aotMetadataMissingManifest.GetProperty("aotMetadata")[0], "path") ?? string.Empty;
            File.Delete(ResolveContainedPath(aotMetadataMissing.Update,
                aotMetadataMissingAssetPath[aotMetadataMissingRuntimeRoot.Length..],
                "Regression AOT metadata payload"));
            AddRegressionCheck(checks, errors, "resource-stage-aot-metadata-missing-rejected",
                !Stage("aot-metadata-missing-stage", aotMetadataMissing.Update,
                    aotMetadataMissing.Assets, aotMetadataMissing.Identity),
                "a resource update with a missing AOT metadata payload must be rejected.");
        }

        var sharedMetaVersion = CopyFixture("shared-metaversion");
        string sharedManifestPath = Path.Combine(sharedMetaVersion.Update,
            "dhe-resource-update.json");
        var sharedManifest = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(sharedManifestPath))!.AsObject();
        var duplicateBase = System.Text.Json.Nodes.JsonNode.Parse(
            sharedManifest["supportedBases"]!.AsArray()[0]!.ToJsonString())!.AsObject();
        duplicateBase["baseId"] = new string('f', 64);
        duplicateBase["buildIdentitySha256"] = new string('e', 64);
        sharedManifest["supportedBases"]!.AsArray().Add(duplicateBase);
        string sharedValidationPath = ResolveContainedPath(sharedMetaVersion.Update,
            sharedManifest["validation"]!.GetValue<string>(),
            "Regression resource validation");
        var sharedValidation = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(sharedValidationPath))!.AsObject();
        var duplicateValidationBase = System.Text.Json.Nodes.JsonNode.Parse(
            sharedValidation["bases"]!.AsArray()[0]!.ToJsonString())!.AsObject();
        duplicateValidationBase["baseId"] = new string('f', 64);
        duplicateValidationBase["buildIdentitySha256"] = new string('e', 64);
        sharedValidation["bases"]!.AsArray().Add(duplicateValidationBase);
        File.WriteAllText(sharedValidationPath, sharedValidation.ToJsonString(Json),
            new UTF8Encoding(false));
        sharedManifest["validationSha256"] = Sha256File(sharedValidationPath);
        File.WriteAllText(sharedManifestPath, sharedManifest.ToJsonString(Json),
            new UTF8Encoding(false));
        AddRegressionCheck(checks, errors, "resource-stage-shared-metaversion-valid",
            Stage("shared-metaversion-stage", sharedMetaVersion.Update,
                sharedMetaVersion.Assets, sharedMetaVersion.Identity),
            "BuildIdentity must select one Base when multiple runtime identities share " +
            "the same Base MetaVersion set.");

        var identityHashTamper = CopyFixture("identity-hash-tamper");
        File.AppendAllText(identityHashTamper.Identity, Environment.NewLine,
            new UTF8Encoding(false));
        AddRegressionCheck(checks, errors, "resource-stage-identity-hash-tamper-rejected",
            !Stage("identity-hash-tamper-stage", identityHashTamper.Update,
                identityHashTamper.Assets, identityHashTamper.Identity),
            "a BuildIdentity whose file hash does not match supportedBases must be rejected.");

        var identityBaseIdTamper = CopyFixture("identity-base-id-tamper");
        var tamperedIdentity = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(identityBaseIdTamper.Identity))!.AsObject();
        tamperedIdentity["baseId"] = new string('f', 64);
        File.WriteAllText(identityBaseIdTamper.Identity, tamperedIdentity.ToJsonString(Json),
            new UTF8Encoding(false));
        AddRegressionCheck(checks, errors, "resource-stage-identity-base-id-tamper-rejected",
            !Stage("identity-base-id-tamper-stage", identityBaseIdTamper.Update,
                identityBaseIdTamper.Assets, identityBaseIdTamper.Identity),
            "a BuildIdentity with an invalid composite baseId must be rejected.");

        var tampered = CopyFixture("payload-tamper");
        var tamperedManifest = ReadJson<JsonElement>(Path.Combine(tampered.Update,
            "dhe-resource-update.json"));
        string tamperedPayload = ResolveContainedPath(tampered.Update,
            GetString(tamperedManifest.GetProperty("assemblies")[0], "dll") ?? string.Empty,
            "Regression payload");
        byte[] tamperedBytes = File.ReadAllBytes(tamperedPayload);
        tamperedBytes[^1] ^= 0x5a;
        File.WriteAllBytes(tamperedPayload, tamperedBytes);
        AddRegressionCheck(checks, errors, "resource-stage-payload-tamper-rejected",
            !Stage("payload-tamper-stage", tampered.Update, tampered.Assets,
                tampered.Identity),
            "a payload whose bytes do not match the manifest must be rejected.");

        var missing = CopyFixture("payload-missing");
        var missingManifest = ReadJson<JsonElement>(Path.Combine(missing.Update,
            "dhe-resource-update.json"));
        string missingPayload = ResolveContainedPath(missing.Update,
            GetString(missingManifest.GetProperty("assemblies")[0],
                "currentMetaVersion") ?? string.Empty, "Regression payload");
        File.Delete(missingPayload);
        AddRegressionCheck(checks, errors, "resource-stage-missing-payload-rejected",
            !Stage("payload-missing-stage", missing.Update, missing.Assets, missing.Identity),
            "a resource update with a missing current MetaVersion must be rejected.");

        var wrongBase = CopyFixture("unsupported-base");
        var wrongBaseManifest = ReadJson<JsonElement>(Path.Combine(wrongBase.Update,
            "dhe-resource-update.json"));
        string runtimeAssetRoot = RequirePortableAssetRoot(
            GetString(wrongBaseManifest, "runtimeAssetRoot"), "runtimeAssetRoot");
        string baseAssetRoot = RequirePortableAssetRoot(
            GetString(wrongBaseManifest, "baseMetaVersionAssetRoot"),
            "baseMetaVersionAssetRoot");
        string baseRelative = baseAssetRoot[runtimeAssetRoot.Length..].TrimEnd('/');
        string embeddedBase = ResolveContainedPath(wrongBase.Assets, baseRelative,
            "Regression embedded Base");
        foreach (JsonElement assembly in wrongBaseManifest.GetProperty("assemblies")
                     .EnumerateArray())
        {
            string name = NormalizeName(GetString(assembly, "assemblyName") ?? string.Empty);
            string currentMetaVersion = ResolveContainedPath(wrongBase.Update,
                GetString(assembly, "currentMetaVersion") ?? string.Empty,
                "Regression current MetaVersion");
            File.Copy(currentMetaVersion,
                Path.Combine(embeddedBase, name + ".mv.bytes"), true);
        }
        AddRegressionCheck(checks, errors, "resource-stage-unsupported-base-rejected",
            !Stage("unsupported-base-stage", wrongBase.Update, wrongBase.Assets,
                wrongBase.Identity),
            "an embedded Base MetaVersion set absent from supportedBases must be rejected.");

        var retired = CopyFixture("retired-mv2");
        string retiredRoot = ResolveContainedPath(retired.Assets, baseRelative,
            "Regression embedded Base");
        string currentBaseMv = Directory.GetFiles(retiredRoot, "*.mv.bytes",
            SearchOption.TopDirectoryOnly).First();
        File.Copy(currentBaseMv, Path.Combine(retiredRoot,
            Path.GetFileName(currentBaseMv).Replace(".mv.bytes", ".mv2.bytes",
                StringComparison.Ordinal)), true);
        AddRegressionCheck(checks, errors, "resource-stage-retired-mv2-rejected",
            !Stage("retired-mv2-stage", retired.Update, retired.Assets, retired.Identity),
            "retired .mv2.bytes artifacts must be rejected before staging.");

        var missingCapability = CopyFixture("missing-capability");
        string capabilityManifestPath = Path.Combine(missingCapability.Update,
            "dhe-resource-update.json");
        var capabilityManifest = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(capabilityManifestPath))!.AsObject();
        var manifestBase = capabilityManifest["supportedBases"]!.AsArray()[0]!.AsObject();
        string removedCapability = manifestBase["requiredRuntimeCapabilities"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .First(value => value != "aot-guard-v1");
        RemoveJsonString(manifestBase["runtimeCapabilities"]!.AsArray(), removedCapability);

        string validationRelative = capabilityManifest["validation"]!.GetValue<string>();
        string capabilityValidationPath = ResolveContainedPath(missingCapability.Update,
            validationRelative, "Regression resource validation");
        var capabilityValidation = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(capabilityValidationPath))!.AsObject();
        RemoveJsonString(capabilityValidation["bases"]!.AsArray()[0]!["runtimeCapabilities"]!
            .AsArray(), removedCapability);
        File.WriteAllText(capabilityValidationPath,
            capabilityValidation.ToJsonString(Json), new UTF8Encoding(false));
        capabilityManifest["validationSha256"] = Sha256File(capabilityValidationPath);
        File.WriteAllText(capabilityManifestPath, capabilityManifest.ToJsonString(Json),
            new UTF8Encoding(false));
        AddRegressionCheck(checks, errors, "resource-stage-missing-capability-rejected",
            !Stage("missing-capability-stage", missingCapability.Update,
                missingCapability.Assets, missingCapability.Identity),
            "a Base missing a required runtime capability must be rejected.");
    }

    private static void RemoveJsonString(System.Text.Json.Nodes.JsonArray values,
        string expected)
    {
        for (int index = values.Count - 1; index >= 0; index--)
        {
            if (string.Equals(values[index]?.GetValue<string>(), expected,
                    StringComparison.Ordinal))
                values.RemoveAt(index);
        }
    }

    private static void RunGuardBlockHashRegressions(string regressionRoot, List<object> checks,
        List<string> errors)
    {
        const string functionName = "DheRegression_Probe_m0123456789ABCDEF";
        const uint methodToken = 100663297;
        var guardRoot = Path.Combine(regressionRoot, "guard-block-hash");
        Directory.CreateDirectory(guardRoot);
        var sourcePath = Path.Combine(guardRoot, "Regression.cpp");
        var begin = NativeGuardBeginPrefix + functionName + ":" +
            methodToken.ToString(CultureInfo.InvariantCulture);
        var end = NativeGuardEndPrefix + functionName + ":" +
            methodToken.ToString(CultureInfo.InvariantCulture);
        var block = "    // " + begin + "\r\n" +
                    "    hybridclr::dhe::RecordAotEntry();\r\n" +
                    "    const RuntimeMethod* dheMethod = method;\r\n" +
                    "    // " + end;
        using var nativeDocument = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            resolverVersion = 3,
            abiContract = "il2cpp-generated-cpp-signature-v2",
            guardHashContract = NativeGuardHashContract,
            generatedCppRoot = guardRoot,
            methods = new[] { new { functionName, methodToken, sourceFile = sourcePath } }
        }, Json));
        File.WriteAllText(sourcePath, "// unrelated before\r\n" + block +
            "\r\n// unrelated after\r\n", new UTF8Encoding(false));
        var originalHash = GuardBlockSetHash(nativeDocument.RootElement, guardRoot, guardRoot);
        File.WriteAllText(sourcePath, "// changed unrelated content\n" +
            block.Replace("\r\n", "\n", StringComparison.Ordinal) +
            "\n// another unrelated change\n", new UTF8Encoding(false));
        var surroundingHash = GuardBlockSetHash(nativeDocument.RootElement, guardRoot, guardRoot);
        AddRegressionCheck(checks, errors, "native-guard-unrelated-source-stable",
            originalHash == surroundingHash,
            "unrelated generated C++ changes must not change the guard-block identity");

        File.WriteAllText(sourcePath, block.Replace("RecordAotEntry", "RecordAotEntryTampered",
            StringComparison.Ordinal), new UTF8Encoding(false));
        var tamperedHash = GuardBlockSetHash(nativeDocument.RootElement, guardRoot, guardRoot);
        AddRegressionCheck(checks, errors, "native-guard-block-tamper",
            originalHash != tamperedHash, "a guard block mutation must change its identity");

        File.WriteAllText(sourcePath, block + "\r\n" + block, new UTF8Encoding(false));
        var duplicateRejected = false;
        try { _ = GuardBlockSetHash(nativeDocument.RootElement, guardRoot, guardRoot); }
        catch { duplicateRejected = true; }
        AddRegressionCheck(checks, errors, "native-guard-duplicate-marker", duplicateRejected,
            "duplicate guard markers must be rejected");

        File.WriteAllText(sourcePath, block.Substring(0,
            block.IndexOf("    // " + end, StringComparison.Ordinal)), new UTF8Encoding(false));
        var missingEndRejected = false;
        try { _ = GuardBlockSetHash(nativeDocument.RootElement, guardRoot, guardRoot); }
        catch { missingEndRejected = true; }
        AddRegressionCheck(checks, errors, "native-guard-missing-end-marker", missingEndRejected,
            "a guard without its end marker must be rejected");
    }

    private static void WriteMutatedAssembly(string source, string destination, Action<ModuleDefMD> mutate)
    {
        using var module = ModuleDefMD.Load(source);
        mutate(module);
        module.Write(destination);
    }

    private static void RequireFormat(JsonElement document, string format, string description, List<string> errors)
    {
        if (GetInt(document, "schemaVersion") != 1 || !string.Equals(GetString(document, "format"), format, StringComparison.Ordinal))
            errors.Add(description + " has an invalid schema or format.");
    }

    private static void RequireTrue(JsonElement document, string property, string description, List<string> errors)
    {
        if (!GetBool(document, property)) errors.Add(description + "." + property + " must be true.");
    }

    private static void ValidateSourceIdentity(JsonElement checkout, string property, List<string> errors)
    {
        if (!checkout.TryGetProperty(property, out var identity) || identity.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Clean checkout is missing " + property + ".");
            return;
        }
        RequireTrue(identity, "tested", property, errors);
        RequireTrue(identity, "passed", property, errors);
        RequireTrue(identity, "clean", property, errors);
        RequireTrue(identity, "cleanRequired", property, errors);
        RequireTrue(identity, "trackedSourcesTested", property, errors);
        RequireTrue(identity, "trackedSourcesComplete", property, errors);
        RequireTrue(identity, "trackedSourcesRequired", property, errors);
        var vcs = GetString(identity, "vcs");
        if (vcs == "git" && (string.IsNullOrWhiteSpace(GetString(identity, "head")) || string.IsNullOrWhiteSpace(GetString(identity, "tree")))) errors.Add(property + " Git identity is incomplete.");
        if (vcs == "svn" && (string.IsNullOrWhiteSpace(GetString(identity, "revision")) || string.IsNullOrWhiteSpace(GetString(identity, "repository")))) errors.Add(property + " SVN identity is incomplete.");
    }

    private static string IdentityString(JsonElement checkout, string identityName, string property)
    {
        return checkout.TryGetProperty(identityName, out var identity) ? GetString(identity, property) ?? "" : "";
    }

    private static string[] StringArray(JsonElement document, string property, List<string> errors)
    {
        if (!document.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            errors.Add(property + " must be an array.");
            return Array.Empty<string>();
        }
        return value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();
    }

    private static string ResolveEvidencePath(string? value, string baseDirectory, string description)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DheException(description + " path is missing.");
        return RequireFile(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value), description);
    }

    private static void ValidateMetaVersionArtifacts(JsonElement planRecord, string planRoot,
        string propertyPrefix, MetaVersionSnapshot expected, List<string> errors)
    {
        string jsonPath = ResolveEvidencePath(GetString(planRecord, propertyPrefix + "Json"),
            planRoot, propertyPrefix + " JSON");
        string binaryPath = ResolveEvidencePath(GetString(planRecord, propertyPrefix + "Bytes"),
            planRoot, propertyPrefix + " binary");
        JsonElement json = ReadJson<JsonElement>(jsonPath);
        RequireFormat(json, "hybridclr.dhe-metaversion.json", propertyPrefix, errors);
        if (GetInt(json, "schemaVersion") != MetaVersionSnapshot.SchemaVersion ||
            !string.Equals(GetString(json, "assemblyName"), expected.AssemblyName,
                StringComparison.Ordinal) ||
            !string.Equals(GetString(json, "assemblyMetadataVersion"),
                expected.AssemblyMetadataVersion, StringComparison.OrdinalIgnoreCase) ||
            !json.TryGetProperty("assembly", out JsonElement assembly) ||
            !string.Equals(GetString(assembly, "sha256"), expected.AssemblySha256,
                StringComparison.OrdinalIgnoreCase))
            errors.Add(propertyPrefix + " JSON does not match live assembly: " +
                expected.AssemblyName);
        if (!File.ReadAllBytes(binaryPath).SequenceEqual(expected.ToBinary()))
            errors.Add(propertyPrefix + " binary does not match live assembly: " +
                expected.AssemblyName);
    }

    private static int CountRuntimeChangedMethods(MetaVersionSnapshot baseline,
        MetaVersionSnapshot current)
    {
        var currentMethods = current.Methods.ToDictionary(method => method.StableId,
            StringComparer.OrdinalIgnoreCase);
        return baseline.Methods.Count(method => !currentMethods.TryGetValue(method.StableId,
            out MetaVersionMethod? currentMethod) || !string.Equals(method.Version,
            currentMethod.Version, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MethodCanHaveAotEntry(MetaVersionMethod method) =>
        (method.Flags & 8u) != 0 && (method.Flags & (2u | 4u)) == 0;

    private static void ValidatePlayerAssemblies(JsonElement player, string[] planNames, List<string> errors)
    {
        if (!player.TryGetProperty("assemblyValidations", out var validations) || validations.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Player assembly validations are missing.");
            return;
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var validation in validations.EnumerateArray())
        {
            var name = GetString(validation, "assemblyName") ?? "";
            if (!names.Add(name) || !GetBool(validation, "hashValidated") || GetString(validation, "loadError") != "OK")
                errors.Add("Player assembly validation is invalid: " + name);
        }
        if (!new HashSet<string>(planNames, StringComparer.OrdinalIgnoreCase).SetEquals(names))
            errors.Add("Player assembly validation set does not match the project plan.");
    }

    private static void ValidateNativeManifest(JsonElement native,
        IReadOnlyDictionary<string, LiveAssemblyValidation> diffs,
        List<string> errors)
    {
        if (GetInt(native, "schemaVersion") != 1 || GetInt(native, "resolverVersion") != 3 ||
            GetString(native, "abiContract") != "il2cpp-generated-cpp-signature-v2" ||
            GetString(native, "guardHashContract") != NativeGuardHashContract ||
            GetString(native, "runtimeProtocol") != ResourceUpdateCompatibility.RuntimeProtocol ||
            GetString(native, "runtimeContract") != ResourceUpdateCompatibility.CurrentNativeRuntimeContract ||
            !new HashSet<string>(StringArray(native, "runtimeCapabilities", errors),
                StringComparer.Ordinal).SetEquals(ResourceUpdateCompatibility.KnownRuntimeCapabilities))
            errors.Add("Native manifest contract is invalid.");
        var expected = diffs.ToDictionary(pair => pair.Key, pair => pair.Value.Baseline.Methods
            .Where(MethodCanHaveAotEntry).ToDictionary(method => method.StableId,
                method => method.Token, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var covered = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (!native.TryGetProperty("methods", out var methods) || methods.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Native manifest methods are missing.");
            return;
        }
        foreach (var method in methods.EnumerateArray())
        {
            var assembly = GetString(method, "assemblyName") ?? "";
            var token = method.TryGetProperty("methodToken", out var tokenValue) && tokenValue.TryGetUInt32(out var raw) ? raw : 0;
            string stableId = GetString(method, "stableMethodIdSha256") ?? "";
            if (!expected.TryGetValue(assembly, out var expectedMethods) ||
                !expectedMethods.TryGetValue(stableId, out uint expectedToken) ||
                expectedToken != token)
                errors.Add("Native manifest contains an unexpected method: " + assembly + ":" +
                    stableId + ":" + token.ToString("x8"));
            if (!covered.TryGetValue(assembly, out var methodsForAssembly))
                covered[assembly] = methodsForAssembly = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            methodsForAssembly.Add(stableId);
        }
        if (!native.TryGetProperty("interpreterOnlyMethods", out var interpreterOnly) ||
            interpreterOnly.ValueKind != JsonValueKind.Array)
            errors.Add("Native manifest interpreter-only methods are missing.");
        else foreach (var method in interpreterOnly.EnumerateArray())
        {
            string assembly = GetString(method, "assemblyName") ?? "";
            string stableId = GetString(method, "stableMethodIdSha256") ?? "";
            if (!expected.TryGetValue(assembly, out var expectedMethods) ||
                !expectedMethods.ContainsKey(stableId))
                errors.Add("Native manifest contains an unexpected interpreter-only method: " +
                    assembly + ":" + stableId);
            if (!covered.TryGetValue(assembly, out var methodsForAssembly))
                covered[assembly] = methodsForAssembly = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            methodsForAssembly.Add(stableId);
        }
        foreach (var pair in expected)
        {
            covered.TryGetValue(pair.Key, out var stableIds);
            if (!new HashSet<string>(pair.Value.Keys, StringComparer.OrdinalIgnoreCase)
                    .SetEquals(stableIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                errors.Add("Native manifest universal method coverage mismatch: " + pair.Key);
        }
        int expectedCount = expected.Sum(pair => pair.Value.Count);
        if (expectedCount != GetInt(native, "guardedMethodCount") ||
            GetInt(native, "unsupportedGuardedMethodCount") != 0)
            errors.Add("Native manifest universal method count is inconsistent.");
    }

    private static void ValidateBuildIdentity(JsonElement identity, string identityPath, JsonElement native,
        string nativePath, IReadOnlyDictionary<string, LiveAssemblyValidation> diffs,
        List<string> errors)
    {
        if (!identity.TryGetProperty("assemblies", out var assemblies) || assemblies.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Build identity assembly evidence is missing.");
            return;
        }
        var baselineRecords = new List<KeyValuePair<string, byte[]>>();
        var snapshotRecords = new List<KeyValuePair<string, byte[]>>();
        var baseMetaVersionRecords = new List<KeyValuePair<string, byte[]>>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in assemblies.EnumerateArray().OrderBy(record => GetString(record, "assemblyName"), StringComparer.Ordinal))
        {
            var name = GetString(record, "assemblyName") ?? "";
            if (!names.Add(name) || !diffs.TryGetValue(name, out var diff))
            {
                errors.Add("Build identity contains an unexpected or duplicate assembly: " + name);
                continue;
            }
            try
            {
                var baselinePath = ResolveEvidencePath(GetString(record, "baselinePath"),
                    Path.GetDirectoryName(identityPath)!, "Build identity baseline assembly");
                var bytes = File.ReadAllBytes(baselinePath);
                var hash = Sha256File(baselinePath);
                if (!hash.Equals(diff.Baseline.AssemblySha256, StringComparison.OrdinalIgnoreCase) ||
                    !hash.Equals(GetString(record, "baselineSha256"), StringComparison.OrdinalIgnoreCase) ||
                    !hash.Equals(GetString(record, "snapshotSha256"), StringComparison.OrdinalIgnoreCase))
                    errors.Add("Build identity baseline/snapshot hash mismatch: " + name);
                baselineRecords.Add(new KeyValuePair<string, byte[]>(name, bytes));
                snapshotRecords.Add(new KeyValuePair<string, byte[]>(name, Convert.FromHexString(hash)));
                var baseMetaVersionPath = ResolveEvidencePath(
                    GetString(record, "baseMetaVersionPath"),
                    Path.GetDirectoryName(identityPath)!,
                    "Build identity Base MetaVersion");
                byte[] baseMetaVersion = File.ReadAllBytes(baseMetaVersionPath);
                if (!Sha256Bytes(baseMetaVersion).Equals(
                        GetString(record, "baseMetaVersionSha256"),
                        StringComparison.OrdinalIgnoreCase))
                    errors.Add("Build identity Base MetaVersion hash mismatch: " + name);
                baseMetaVersionRecords.Add(new KeyValuePair<string, byte[]>(name,
                    baseMetaVersion));
            }
            catch (Exception ex) { errors.Add("Build identity " + name + ": " + ex.Message); }
        }
        if (!new HashSet<string>(diffs.Keys, StringComparer.OrdinalIgnoreCase).SetEquals(names))
            errors.Add("Build identity assembly set does not match the project plan.");
        string managedAssemblySetSha256 = NamedByteSetHash(baselineRecords);
        string baseMetaVersionSetSha256 = NamedByteSetHash(baseMetaVersionRecords);
        if (!managedAssemblySetSha256.Equals(GetString(identity, "managedAssemblySetSha256"),
                StringComparison.OrdinalIgnoreCase) ||
            !NamedByteSetHash(snapshotRecords).Equals(GetString(identity, "aotSnapshotSha256"), StringComparison.OrdinalIgnoreCase))
            errors.Add("Build identity aggregate baseline/snapshot hash is invalid.");
        if (!baseMetaVersionSetSha256.Equals(GetString(identity,
                "baseMetaVersionSetSha256"), StringComparison.OrdinalIgnoreCase))
            errors.Add("Build identity aggregate Base MetaVersion hash is invalid.");
        string[] identityCapabilities = StringArray(identity, "runtimeCapabilities", errors);
        if (!string.Equals(GetString(identity, "runtimeProtocol"),
                GetString(native, "runtimeProtocol"), StringComparison.Ordinal) ||
            !string.Equals(GetString(identity, "runtimeContract"),
                GetString(native, "runtimeContract"), StringComparison.Ordinal) ||
            !new HashSet<string>(identityCapabilities, StringComparer.Ordinal).SetEquals(
                StringArray(native, "runtimeCapabilities", errors)))
            errors.Add("Build identity runtime protocol does not match the native manifest.");
        try
        {
            string computedBaseId = ComputeBaseId(GetString(identity, "target") ?? string.Empty,
                managedAssemblySetSha256, GetString(identity, "aotSnapshotSha256") ?? string.Empty,
                baseMetaVersionSetSha256,
                GetString(identity, "nativeGuardSourceSha256") ?? string.Empty,
                GetString(identity, "nativeManifestSha256") ?? string.Empty,
                GetString(identity, "runtimeProtocol") ?? string.Empty,
                GetString(identity, "runtimeContract") ?? string.Empty, identityCapabilities,
                GetString(identity, "runtimeAssetRoot") ?? string.Empty,
                GetString(identity, "baseMetaVersionAssetRoot") ?? string.Empty);
            if (!computedBaseId.Equals(GetString(identity, "baseId"),
                    StringComparison.OrdinalIgnoreCase))
                errors.Add("Build identity composite Base ID is invalid.");
        }
        catch (Exception exception)
        {
            errors.Add("Build identity composite Base ID: " + exception.Message);
        }

        var identityRoot = Path.GetDirectoryName(identityPath)!;
        var nativeDocumentRoot = Path.GetDirectoryName(nativePath)!;
        var generatedRootValue = GetString(identity, "generatedCppRoot") ?? "";
        var nativeRootValue = GetString(native, "generatedCppRoot") ?? "";
        var generatedRoot = Path.GetFullPath(Path.IsPathRooted(generatedRootValue) ? generatedRootValue : Path.Combine(identityRoot, generatedRootValue));
        var nativeRoot = Path.GetFullPath(Path.IsPathRooted(nativeRootValue) ? nativeRootValue : Path.Combine(nativeDocumentRoot, nativeRootValue));
        if (string.IsNullOrWhiteSpace(generatedRootValue) || !Directory.Exists(generatedRoot) ||
            !generatedRoot.Equals(nativeRoot, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Build identity generated C++ root does not match the native manifest.");
            return;
        }
        var identityPaths = StringArray(identity, "generatedCppPaths", errors)
            .Select(path => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(identityRoot, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var nativePaths = native.GetProperty("methods").EnumerateArray().Select(method => GetString(method, "sourceFile") ?? "")
            .Where(path => path.Length > 0)
            .Select(path => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(nativeDocumentRoot, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (!identityPaths.SequenceEqual(nativePaths, StringComparer.OrdinalIgnoreCase))
            errors.Add("Build identity generated C++ files do not match the native manifest.");
        foreach (var path in identityPaths)
        {
            var relative = Path.GetRelativePath(generatedRoot, path);
            if (!SafeRelative(relative) || !File.Exists(path)) errors.Add("Build identity generated C++ file is missing or unsafe: " + path);
        }
        try
        {
            if (!GuardBlockSetHash(native, nativeDocumentRoot, generatedRoot).Equals(
                    GetString(identity, "nativeGuardSourceSha256"), StringComparison.OrdinalIgnoreCase))
                errors.Add("Build identity native guard block hash is invalid.");
        }
        catch (Exception ex) { errors.Add("Build identity native guard blocks: " + ex.Message); }
        var recordedManifest = ResolveEvidencePath(GetString(identity, "nativeManifestPath"),
            Path.GetDirectoryName(identityPath)!, "Build identity native manifest");
        if (!Sha256File(recordedManifest).Equals(GetString(identity, "nativeManifestSha256"), StringComparison.OrdinalIgnoreCase))
            errors.Add("Build identity immutable native manifest hash is invalid.");
        try
        {
            var immutableNative = ReadJson<JsonElement>(recordedManifest);
            ValidateNativeManifest(immutableNative, diffs, errors);
            var sourceHash = GetString(native, "sourceManifestSha256");
            if (!Path.GetFullPath(recordedManifest).Equals(Path.GetFullPath(nativePath), StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sourceHash, Sha256File(recordedManifest), StringComparison.OrdinalIgnoreCase))
                errors.Add("Normalized native manifest is not bound to the immutable Player manifest.");
        }
        catch (Exception ex) { errors.Add("Immutable native manifest: " + ex.Message); }
    }

    private static string NamedByteSetHash(IEnumerable<KeyValuePair<string, byte[]>> records)
    {
        using var sha = SHA256.Create();
        foreach (var record in records.OrderBy(record => record.Key, StringComparer.Ordinal))
        {
            var name = Encoding.UTF8.GetBytes(record.Key + "\n");
            sha.TransformBlock(name, 0, name.Length, name, 0);
            var bytes = record.Value ?? Array.Empty<byte>();
            sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
            sha.TransformBlock(new byte[] { (byte)'\n' }, 0, 1, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static string GuardBlockSetHash(JsonElement native, string nativeDocumentRoot, string root)
    {
        var records = new List<GuardBlockHashRecord>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in native.GetProperty("methods").EnumerateArray())
        {
            var functionName = GetString(method, "functionName") ?? "";
            if (string.IsNullOrWhiteSpace(functionName) || functionName.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new DheException("Native guard function name is invalid.");
            if (!method.TryGetProperty("methodToken", out var tokenValue) || !tokenValue.TryGetUInt32(out var methodToken) || methodToken == 0)
                throw new DheException("Native guard method token is invalid: " + functionName);
            var sourceValue = GetString(method, "sourceFile") ?? "";
            var sourcePath = RequireFile(Path.IsPathRooted(sourceValue) ? sourceValue :
                Path.Combine(nativeDocumentRoot, sourceValue), "Native guard source");
            var relative = Path.GetRelativePath(root, sourcePath).Replace(Path.DirectorySeparatorChar, '/');
            if (!SafeRelative(relative))
                throw new DheException("Native guard source escapes its generated C++ root: " + sourcePath);
            var identity = relative + "\n" + functionName + "\n" +
                methodToken.ToString(CultureInfo.InvariantCulture);
            if (!identities.Add(identity))
                throw new DheException("Native manifest contains a duplicate guard identity: " + functionName +
                    "/" + methodToken.ToString(CultureInfo.InvariantCulture));
            var source = File.ReadAllText(sourcePath, Encoding.UTF8);
            records.Add(new GuardBlockHashRecord(relative, functionName, methodToken,
                ExtractGuardBlock(source, functionName, methodToken)));
        }
        using var sha = SHA256.Create();
        var domain = Encoding.UTF8.GetBytes(NativeGuardHashContract + "\n");
        sha.TransformBlock(domain, 0, domain.Length, domain, 0);
        foreach (var record in records.OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                     .ThenBy(item => item.FunctionName, StringComparer.Ordinal)
                     .ThenBy(item => item.MethodToken))
        {
            var header = Encoding.UTF8.GetBytes(record.RelativePath + "\n" + record.FunctionName + "\n" +
                record.MethodToken.ToString(CultureInfo.InvariantCulture) + "\n");
            sha.TransformBlock(header, 0, header.Length, header, 0);
            var block = Encoding.UTF8.GetBytes(record.Block + "\n");
            sha.TransformBlock(block, 0, block.Length, block, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static string ExtractGuardBlock(string source, string functionName, uint methodToken)
    {
        var token = methodToken.ToString(CultureInfo.InvariantCulture);
        var beginMarker = NativeGuardBeginPrefix + functionName + ":" + token;
        var endMarker = NativeGuardEndPrefix + functionName + ":" + token;
        var begin = RequireSingleGuardMarker(source, beginMarker);
        var end = RequireSingleGuardMarker(source, endMarker);
        if (end <= begin) throw new DheException("Native guard end marker precedes its begin marker: " + functionName);
        var lineStart = source.LastIndexOf('\n', begin);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var blockEnd = end + endMarker.Length;
        var lineEnd = source.IndexOf('\n', blockEnd);
        if (lineEnd < 0) lineEnd = source.Length;
        var suffix = source.Substring(blockEnd, lineEnd - blockEnd).TrimEnd('\r');
        if (suffix.Any(character => character != ' ' && character != '\t'))
            throw new DheException("Native guard end marker has unexpected trailing content: " + functionName);
        return source.Substring(lineStart, blockEnd - lineStart)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }

    private static int RequireSingleGuardMarker(string source, string marker)
    {
        var first = source.IndexOf(marker, StringComparison.Ordinal);
        if (first < 0 || source.IndexOf(marker, first + marker.Length, StringComparison.Ordinal) >= 0)
            throw new DheException("Native guard marker is missing or duplicated: " + marker);
        return first;
    }

    private sealed record GuardBlockHashRecord(string RelativePath, string FunctionName, uint MethodToken,
        string Block);

    private static void ValidateRuntimePlanFiles(JsonElement plan, string root, List<string> errors)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in plan.GetProperty("assemblies").EnumerateArray())
        {
            var assemblyName = GetString(record, "assemblyName") ?? "";
            if (string.IsNullOrWhiteSpace(assemblyName) || !names.Add(assemblyName))
                errors.Add("Runtime plan contains a missing or duplicate assembly name.");
            foreach (var property in new[] { "current", "baseline", "snapshot",
                         "baseMetaVersion", "currentMetaVersion" })
            {
                var value = GetString(record, property) ?? "";
                if (Path.IsPathRooted(value) || value.Contains("..", StringComparison.Ordinal) || !File.Exists(Path.Combine(root, value)))
                    errors.Add("Runtime plan contains a missing or unsafe file: " + value);
            }
            var baseline = Path.Combine(root, GetString(record, "baseline") ?? "");
            var current = Path.Combine(root, GetString(record, "current") ?? "");
            var snapshot = Path.Combine(root, GetString(record, "snapshot") ?? "");
            var baseMetaVersion = Path.Combine(root,
                GetString(record, "baseMetaVersion") ?? "");
            var currentMetaVersion = Path.Combine(root,
                GetString(record, "currentMetaVersion") ?? "");
            if (File.Exists(baseline) && !Sha256File(baseline).Equals(GetString(record, "baselineSha256"), StringComparison.OrdinalIgnoreCase)) errors.Add("Runtime baseline hash mismatch.");
            if (File.Exists(current) && !Sha256File(current).Equals(GetString(record, "currentSha256"), StringComparison.OrdinalIgnoreCase)) errors.Add("Runtime current hash mismatch.");
            if (File.Exists(snapshot) && !Sha256File(snapshot).Equals(GetString(record, "snapshotSha256"), StringComparison.OrdinalIgnoreCase)) errors.Add("Runtime snapshot hash mismatch.");
            if (File.Exists(baseMetaVersion) && !Sha256File(baseMetaVersion).Equals(
                    GetString(record, "baseMetaVersionSha256"), StringComparison.OrdinalIgnoreCase))
                errors.Add("Runtime Base MetaVersion hash mismatch.");
            if (File.Exists(currentMetaVersion) && !Sha256File(currentMetaVersion).Equals(
                    GetString(record, "currentMetaVersionSha256"), StringComparison.OrdinalIgnoreCase))
                errors.Add("Runtime current MetaVersion hash mismatch.");
            if (File.Exists(snapshot) && File.Exists(baseline) &&
                !File.ReadAllBytes(snapshot).SequenceEqual(Convert.FromHexString(Sha256File(baseline))))
                errors.Add("Runtime snapshot does not encode the baseline assembly hash.");
        }
        if (plan.TryGetProperty("aotMetadata", out var metadata) && metadata.ValueKind == JsonValueKind.Array)
        {
            var metadataNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in metadata.EnumerateArray())
            {
                var name = GetString(record, "assemblyName") ?? "";
                var value = GetString(record, "path") ?? "";
                if (string.IsNullOrWhiteSpace(name) || !metadataNames.Add(name) || Path.IsPathRooted(value) ||
                    value.Contains("..", StringComparison.Ordinal) || !File.Exists(Path.Combine(root, value)) ||
                    !Sha256File(Path.Combine(root, value)).Equals(GetString(record, "sha256"), StringComparison.OrdinalIgnoreCase))
                    errors.Add("Runtime AOT metadata file is missing, duplicated, unsafe, or has the wrong hash: " + name);
            }
        }
    }

    private sealed record LiveAssemblyValidation(MetaVersionSnapshot Baseline,
        MetaVersionSnapshot Current, ResourceUpdateCompatibility Compatibility);
}
