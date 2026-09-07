using System.Text.Json;
using System.Xml.Linq;
using dnlib.DotNet;
using HybridCLR.Editor.Link;

if (args.Length != 1) throw new ArgumentException("Pass a new test output directory.");
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output)) throw new IOException("Test output already exists.");
Directory.CreateDirectory(output);
string input = Path.Combine(output, "inputs");
Directory.CreateDirectory(input);
ModuleDefUser Assembly(string name)
{
    var module = new ModuleDefUser(name + ".dll") { Kind = ModuleKind.Dll };
    new AssemblyDefUser(name, new Version(1, 0, 0, 0)).Modules.Add(module);
    return module;
}
var implementation = Assembly("Implementation");
var implementationType = new TypeDefUser("Example", "Forwarded");
implementation.Types.Add(implementationType);
implementation.Write(Path.Combine(input, "Implementation.dll"));
var facade = Assembly("Facade");
facade.ExportedTypes.Add(new ExportedTypeUser(facade, 0, "Example", "Forwarded",
    TypeAttributes.Public | TypeAttributes.Forwarder, new AssemblyRefUser(implementation.Assembly)));
facade.Write(Path.Combine(input, "Facade.dll"));
var root = Assembly("Hotfix");
var rootType = new TypeDefUser("Example", "Root");
root.Types.Add(rootType);
rootType.Fields.Add(new FieldDefUser("Value", new FieldSig(new ClassSig(
    new TypeRefUser(root, "Example", "Forwarded", new AssemblyRefUser(facade.Assembly))))));
root.Write(Path.Combine(input, "Hotfix.dll"));

string linkPath = Path.Combine(output, "link.xml");
DheLinkerPreservation.Write(input, new[] { "Hotfix" }, linkPath);
XElement linker = XDocument.Load(linkPath).Root;
var checks = new Dictionary<string, bool>
{
    ["preserves-entire-dhe-root"] = linker.Elements("assembly").Any(element =>
        (string)element.Attribute("fullname") == "Hotfix" && (string)element.Attribute("preserve") == "all"),
    ["preserves-forwarded-implementation"] = linker.Elements("assembly").Any(element =>
        (string)element.Attribute("fullname") == "Implementation" && element.Elements("type").Any(type =>
            (string)type.Attribute("fullname") == "Example.Forwarded" && (string)type.Attribute("preserve") == "all")),
    ["does-not-preserve-empty-facade"] = !linker.Elements("assembly").Any(element =>
        (string)element.Attribute("fullname") == "Facade"),
};
string first = File.ReadAllText(linkPath);
DheLinkerPreservation.Write(input, new[] { "Hotfix", "Hotfix" }, linkPath);
checks["deterministic-and-deduplicated"] = first == File.ReadAllText(linkPath);
string exactInputPath = Path.Combine(output, "exact-inputs.xml");
string[] exactInputs = Directory.GetFiles(input, "*.dll");
string graphImplementation = Path.Combine(output, "graph-inputs", "Implementation.dll");
Directory.CreateDirectory(Path.GetDirectoryName(graphImplementation));
File.Copy(Path.Combine(input, "Implementation.dll"), graphImplementation);
string[] graphInputs = exactInputs.Where(path => Path.GetFileName(path) != "Implementation.dll")
    .Append(graphImplementation).ToArray();
DheLinkerPreservation.Write(graphInputs.Concat(graphInputs), new[] { "Hotfix" }, exactInputPath);
checks["explicit-inputs-without-staging-directory"] = first == File.ReadAllText(exactInputPath);
string missingInputPath = Path.Combine(output, "missing-input.xml");
try
{
    DheLinkerPreservation.Write(exactInputs.Append(Path.Combine(input, "Absent.dll")), new[] { "Hotfix" }, missingInputPath);
    checks["explicit-missing-input-rejected"] = false;
}
catch (FileNotFoundException)
{
    checks["explicit-missing-input-rejected"] = !File.Exists(missingInputPath);
}
string fullPath = Path.Combine(output, "future-api.xml");
DheLinkerPreservation.Write(input, new[] { "Hotfix" }, fullPath, new[] { "Implementation" });
checks["future-aot-assembly-retained-in-full"] = XDocument.Load(fullPath).Root.Elements("assembly")
    .Any(element => (string)element.Attribute("fullname") == "Implementation" &&
        (string)element.Attribute("preserve") == "all");
try
{
    DheLinkerPreservation.Write(input, new[] { "Hotfix" }, Path.Combine(output, "missing-future.xml"),
        new[] { "MissingFuture" });
    checks["missing-future-aot-assembly-rejected"] = false;
}
catch (FileNotFoundException)
{
    checks["missing-future-aot-assembly-rejected"] = !File.Exists(Path.Combine(output, "missing-future.xml"));
}
bool Rejects(string[] names, string destination)
{
    try { DheLinkerPreservation.Write(input, names, destination); return false; }
    catch (Exception) { return !File.Exists(destination); }
}
checks["missing-root-rejected-before-output"] = Rejects(new[] { "Missing" }, Path.Combine(output, "missing.xml"));
checks["empty-root-rejected-before-output"] = Rejects(Array.Empty<string>(), Path.Combine(output, "empty.xml"));
File.Move(Path.Combine(input, "Implementation.dll"), Path.Combine(output, "Implementation.dll"));
checks["missing-forwarder-target-rejected-before-output"] = Rejects(new[] { "Hotfix" }, Path.Combine(output, "unresolved.xml"));
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { passed, checks },
    new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
