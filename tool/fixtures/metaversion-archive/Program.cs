using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

if (args.Length < 2) throw new ArgumentException("Pass a new output directory and archived Base roots.");
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
var checks = new List<object>();
bool passed = true;
foreach (string root in args.Skip(1).Select(Path.GetFullPath))
    foreach (string dll in Directory.GetFiles(Path.Combine(root, "baseline"), "*.dll").OrderBy(path => path, StringComparer.Ordinal))
    {
        string mv = Path.Combine(root, "project-preflight", "batch", Path.GetFileNameWithoutExtension(dll) + ".base.mv.bytes");
        byte[] archived = File.ReadAllBytes(mv);
        byte[] generated = MetaVersionSnapshot.Create(dll).ToBinary();
        bool equal = archived.SequenceEqual(generated);
        checks.Add(new { root, dll, dllSha256 = Hash(File.ReadAllBytes(dll)), mv,
            archivedSha256 = Hash(archived), regeneratedSha256 = Hash(generated), passed = equal });
        passed &= equal;
        Console.WriteLine(Path.GetFileName(root) + "/" + Path.GetFileName(dll) + ": " + equal);
    }
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed, scope = "Exact historical MV byte compatibility; no archive is modified", checks,
}, new JsonSerializerOptions { WriteIndented = true }));
return passed ? 0 : 1;

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
