using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HybridCLR.Editor;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;
using UnityEngine;

// Run only in the disposable installer fixture. Uses local mirrors supplied as
// project settings and restores the in-memory settings after the test.
public static class InstallerRepositorySelectionCheck
{
    [Serializable] private sealed class Result
    {
        public bool passed;
        public int runtimeFilesVerified;
        public bool configuredRepositoriesUsed;
        public bool missingConfiguredRepositoryRejected;
        public bool manifestSelectsRefOnly;
        public string hybridclrCommit;
        public string il2cppCommit;
    }

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        string Arg(string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name);
            return args[index + 1];
        }
        var settings = HybridCLRSettings.Instance;
        string originalHybridclr = settings.hybridclrRepoURL, originalIl2cpp = settings.il2cppPlusRepoURL;
        string hybridclr = Arg("-testHybridclrRepository"), il2cpp = Arg("-testIl2cppRepository");
        string expected = Path.GetFullPath(Arg("-expectedLibil2cpp"));
        string reportPath = Path.GetFullPath(Arg("-repositoryResult"));
        var result = new Result();
        try
        {
            settings.hybridclrRepoURL = hybridclr;
            settings.il2cppPlusRepoURL = il2cpp;
            Type descriptor = typeof(InstallerController).GetNestedType("VersionDesc", BindingFlags.NonPublic);
            result.manifestSelectsRefOnly = descriptor.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(f => f.Name).SequenceEqual(new[] { "branch" });
            if (!result.manifestSelectsRefOnly) throw new InvalidOperationException("Version manifest must only select a ref.");
            var installer = new InstallerController();
            MethodInfo prepare = typeof(InstallerController).GetMethod("PrepareLibil2cppWithHybridclrFromGitRepo",
                BindingFlags.Instance | BindingFlags.NonPublic);
            string assembled = (string)prepare.Invoke(installer, null);
            string Git(string directory, params string[] arguments)
            {
                var command = BashUtil.RunCommand2(directory, "git", arguments, false);
                if (command.ExitCode != 0) throw new InvalidOperationException(command.StdOut);
                return command.StdOut.Trim();
            }
            string hybridclrClone = Path.Combine(SettingsUtil.HybridCLRDataDir, "hybridclr_repo");
            string il2cppClone = Path.Combine(SettingsUtil.HybridCLRDataDir, "il2cpp_plus_repo");
            result.configuredRepositoriesUsed = Git(hybridclrClone, "remote", "get-url", "origin") == hybridclr &&
                Git(il2cppClone, "remote", "get-url", "origin") == il2cpp;
            if (!result.configuredRepositoriesUsed) throw new InvalidOperationException("Project repository settings were overridden.");
            result.hybridclrCommit = Git(hybridclrClone, "rev-parse", "HEAD");
            result.il2cppCommit = Git(il2cppClone, "rev-parse", "HEAD");
            if (result.hybridclrCommit != Arg("-expectedHybridclrCommit") || result.il2cppCommit != Arg("-expectedIl2cppCommit"))
                throw new InvalidOperationException("Selected runtime tag does not match the release lock.");
            foreach (string source in Directory.GetFiles(expected, "*", SearchOption.AllDirectories))
            {
                string relative = source.Substring(expected.Length).TrimStart('/', '\\');
                using (var sha = SHA256.Create())
                using (var left = File.OpenRead(source))
                using (var right = File.OpenRead(Path.Combine(assembled, relative)))
                    if (!sha.ComputeHash(left).SequenceEqual(sha.ComputeHash(right)))
                        throw new InvalidDataException("Runtime differs: " + relative);
                ++result.runtimeFilesVerified;
            }
            settings.hybridclrRepoURL = Path.Combine(Path.GetDirectoryName(reportPath), "missing-configured-repository");
            try { prepare.Invoke(installer, null); }
            catch (TargetInvocationException error) when (error.InnerException != null &&
                error.InnerException.Message.Contains("clone repository fail"))
            {
                result.missingConfiguredRepositoryRejected = true;
            }
            if (!result.missingConfiguredRepositoryRejected)
                throw new InvalidOperationException("Missing project repository must fail without fallback.");
            result.passed = true;
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, JsonUtility.ToJson(result, true));
            Debug.Log("Installer project repository checks passed: " + result.runtimeFilesVerified + " runtime files.");
        }
        finally
        {
            settings.hybridclrRepoURL = originalHybridclr;
            settings.il2cppPlusRepoURL = originalIl2cpp;
        }
    }
}
