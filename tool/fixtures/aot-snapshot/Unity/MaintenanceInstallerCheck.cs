using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HybridCLR.Editor;
using HybridCLR.Editor.Installer;
using UnityEngine;

public static class MaintenanceInstallerCheck
{
    [Serializable] private class Result
    {
        public bool passed;
        public int runtimeFilesVerified;
        public string unityVersion;
        public string installedRoot;
    }

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        string Arg(string key) => args[Array.IndexOf(args, key) + 1];
        string expected = Path.GetFullPath(Arg("-expectedLibil2cpp"));
        string output = Path.GetFullPath(Arg("-installerResult"));
        var installer = new InstallerController();
        installer.InstallDefaultHybridCLR();
        string installed = Path.Combine(SettingsUtil.LocalIl2CppDir, "libil2cpp");
        if (!installer.HasInstalledHybridCLR()) throw new InvalidDataException("No installed runtime.");
        int count = 0;
        foreach (string file in Directory.GetFiles(expected, "*", SearchOption.AllDirectories))
        {
            string relative = file.Substring(expected.Length).TrimStart('/', '\\');
            string destination = Path.Combine(installed, relative);
            using (var hash = SHA256.Create())
            using (var left = File.OpenRead(file))
            using (var right = File.OpenRead(destination))
                if (!hash.ComputeHash(left).SequenceEqual(hash.ComputeHash(right)))
                    throw new InvalidDataException("Installed runtime differs: " + relative);
            ++count;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(new Result { passed = true,
            runtimeFilesVerified = count, unityVersion = Application.unityVersion,
            installedRoot = installed }, true));
        Debug.Log("Maintenance Installer verified " + count + " runtime files.");
    }
}
