using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("Pass the unchanged ordinary AOT test DLL, Base or Current support root, and a new report path.");
string input = Path.GetFullPath(args[0]), support = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
if (File.Exists(output)) throw new IOException("Report already exists.");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(support, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(input);
var probe = assembly.GetType("HybridCLR.Lab.NativeDescendants.NativeClassVirtualProbe", true)!;
var records = (string[])probe.GetMethod("Run")!.Invoke(null, null)!;
bool passed = records.Length == 9 && records.All(record => record.EndsWith("\tpassed", StringComparison.Ordinal));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    passed, scope = "CLR reference; the ordinary test assembly is unchanged between Base and Current",
    input, inputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))), support, records,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (string record in records) Console.WriteLine(record);
return passed ? 0 : 1;
