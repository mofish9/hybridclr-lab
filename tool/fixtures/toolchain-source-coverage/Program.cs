using System.Text.Json;
using HybridCLR.DheTool;

if (args.Length != 1 || Directory.Exists(args[0])) throw new ArgumentException("Pass a new output directory.");
string root = Path.GetFullPath(args[0]);
string[] inputs = { "tool/Program.cs", "tool/ResourceExecutionPlan.cs", "tool/HybridCLR.DheTool.csproj",
    "tool/dnlib.dll", "Directory.Build.props", "Directory.Build.targets",
    "templates/DheWorkflowBuild.cs", "templates/DheBuildIdentity.cs" };
foreach (string path in inputs)
{
    string file = Path.Combine(root, path);
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, "fixture");
}
JsonElement Layout(IEnumerable<string> exact, params string[] prefixes) =>
    JsonSerializer.SerializeToElement(new { exactPaths = exact, prefixes });
var checks = new Dictionary<string, bool>();
bool Reject(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
ToolchainSourceCoverage.Validate(root, Layout(inputs)); checks["complete-inputs"] = true;
checks["missing-execution-plan-rejected"] = Reject(() => ToolchainSourceCoverage.Validate(root,
    Layout(inputs.Where(path => path != "tool/ResourceExecutionPlan.cs"))));
checks["similar-prefix-rejected"] = Reject(() => ToolchainSourceCoverage.Validate(root,
    Layout(inputs.Where(path => !path.StartsWith("tool/")), "tools/")));
ToolchainSourceCoverage.Validate(root, Layout(inputs.Where(path => !path.StartsWith("tool/")), "tool/"));
checks["directory-prefix-accepted"] = true;
File.Delete(Path.Combine(root, "templates/DheBuildIdentity.cs"));
checks["missing-declared-template-rejected"] = Reject(() => ToolchainSourceCoverage.Validate(root, Layout(inputs)));
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Toolchain build-input checks: {checks.Count}, passed={passed}");
return passed ? 0 : 1;
