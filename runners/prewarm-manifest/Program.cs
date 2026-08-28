using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using dnlib.DotNet;

internal static class Program
{
    private sealed class Manifest
    {
        public int schemaVersion { get; set; }
        public string assemblyName { get; set; } = string.Empty;
        public string assemblySha256 { get; set; } = string.Empty;
        public string rootType { get; set; } = string.Empty;
        public string rootMethod { get; set; } = string.Empty;
        public int rootParameterCount { get; set; }
        public int maxDepth { get; set; }
        public int maxMethods { get; set; }
        public int reachableMethodCount { get; set; }
        public int methodCount { get; set; }
        public string graphCoverage { get; set; } = "static-il-candidates";
        public bool dynamicEdgesIncluded { get; set; }
        public MethodDescriptor[] methods { get; set; } = Array.Empty<MethodDescriptor>();
        public string[] types { get; set; } = Array.Empty<string>();
    }

    private sealed class MethodDescriptor
    {
        public string declaringType { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public int parameterCount { get; set; }
        public int genericParameterCount { get; set; }
        public int metadataToken { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? parameterTypes { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? returnType { get; set; }

        [JsonIgnore]
        public string Key
        {
            get
            {
                if (metadataToken > 0)
                    return declaringType + "|token|" + metadataToken;
                return declaringType + "|signature|" + name + "|" + genericParameterCount + "|" +
                    string.Join(",", parameterTypes ?? Array.Empty<string>()) + "|" + (returnType ?? string.Empty);
            }
        }
    }

    private sealed class MethodWork
    {
        public MethodDef Method { get; }
        public int Depth { get; }

        public MethodWork(MethodDef method, int depth)
        {
            Method = method;
            Depth = depth;
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            Generate(options);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            string key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException("Expected --key value arguments.");
            options[key.Substring(2)] = args[++index];
        }

        foreach (string required in new[] { "assembly", "root-type", "root-method", "output-json", "output-cs" })
        {
            if (!options.ContainsKey(required) || string.IsNullOrWhiteSpace(options[required]))
                throw new ArgumentException("Missing required option --" + required + ".");
        }
        return options;
    }

    private static void Generate(Dictionary<string, string> options)
    {
        string assemblyPath = Path.GetFullPath(options["assembly"]);
        string rootTypeName = options["root-type"];
        string rootMethodName = options["root-method"];
        int rootParameterCount = -1;
        if (options.TryGetValue("root-parameter-count", out string? parameterCountText) &&
            !int.TryParse(parameterCountText, out rootParameterCount))
            throw new ArgumentException("--root-parameter-count must be an integer.");
        int maxDepth = ParsePositiveOption(options, "max-depth", 64);
        int maxMethods = ParsePositiveOption(options, "max-methods", 16384);
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException("Hot-update assembly was not found.", assemblyPath);

        ModuleDefMD module = ModuleDefMD.Load(assemblyPath);
        TypeDef? rootType = module.GetTypes().FirstOrDefault(type =>
            type.FullName == rootTypeName || NormalizeTypeName(type.FullName) == rootTypeName);
        if (rootType == null)
            throw new InvalidOperationException("Root type was not found: " + rootTypeName);

        MethodDef[] rootMethods = rootType.Methods.Where(method => method.Name == rootMethodName &&
            (rootParameterCount < 0 || method.MethodSig?.Params.Count == rootParameterCount)).ToArray();
        if (rootMethods.Length == 0)
            throw new InvalidOperationException("Root method was not found: " + rootTypeName + "." + rootMethodName);
        if (rootMethods.Length > 1)
            throw new InvalidOperationException("Root method is ambiguous; pass --root-parameter-count: " + rootTypeName + "." + rootMethodName);
        MethodDef rootMethod = rootMethods[0];

        var types = new SortedSet<string>(StringComparer.Ordinal);
        var methodDescriptors = new Dictionary<string, MethodDescriptor>(StringComparer.Ordinal);
        var methods = new Queue<MethodWork>();
        var visitedMethods = new HashSet<uint>();
        AddType(rootType, module, types);
        AddMethodDescriptor(rootMethod, module, methodDescriptors, rootMethod.MDToken.Raw);
        methods.Enqueue(new MethodWork(rootMethod, 0));

        while (methods.Count > 0)
        {
            MethodWork work = methods.Dequeue();
            if (work.Depth > maxDepth)
                throw new InvalidOperationException("Prewarm call graph exceeds --max-depth " + maxDepth + "; raise the limit and review the manifest.");
            uint methodToken = work.Method.MDToken.Raw;
            if (!visitedMethods.Add(methodToken))
                continue;
            if (visitedMethods.Count > maxMethods)
                throw new InvalidOperationException("Prewarm call graph exceeds --max-methods " + maxMethods + "; raise the limit and review the manifest.");

            AddMethodSignature(work.Method.MethodSig, module, types);
            if (work.Method.Body == null)
                continue;

            foreach (dnlib.DotNet.Emit.Instruction instruction in work.Method.Body.Instructions)
            {
                if (instruction.Operand is IMethod methodReference)
                {
                    AddType(methodReference.DeclaringType, module, types);
                    AddMethodSignature(methodReference.MethodSig, module, types);
                    MethodDef? resolved = ResolveLocalMethod(methodReference, module);
                    AddMethodDescriptor(methodReference, module, methodDescriptors, resolved?.MDToken.Raw ?? 0);
                    if (resolved != null && resolved.Module == module)
                        methods.Enqueue(new MethodWork(resolved, work.Depth + 1));
                }
                else if (instruction.Operand is IField fieldReference)
                {
                    AddType(fieldReference.DeclaringType, module, types);
                    AddType(fieldReference.FieldSig?.Type, module, types);
                }
                else if (instruction.Operand is ITypeDefOrRef typeReference)
                {
                    AddType(typeReference, module, types);
                }
            }
        }

        var manifest = new Manifest
        {
            schemaVersion = 1,
            assemblyName = module.Assembly?.Name ?? Path.GetFileNameWithoutExtension(assemblyPath),
            assemblySha256 = ComputeSha256(assemblyPath),
            rootType = NormalizeTypeName(rootType.FullName),
            rootMethod = rootMethodName,
            rootParameterCount = rootMethod.MethodSig?.Params.Count ?? 0,
            maxDepth = maxDepth,
            maxMethods = maxMethods,
            reachableMethodCount = visitedMethods.Count,
            methodCount = methodDescriptors.Count,
            graphCoverage = "static-il-candidates",
            dynamicEdgesIncluded = false,
            methods = methodDescriptors.Values.OrderBy(method => method.Key, StringComparer.Ordinal).ToArray(),
            types = types.ToArray(),
        };
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        WriteFile(options["output-json"], json + Environment.NewLine);
        WriteFile(options["output-cs"], GenerateCSharp(manifest));
        Console.WriteLine("Generated prewarm manifest: " + manifest.types.Length + " type(s), " + manifest.reachableMethodCount + " reachable method(s), " + manifest.methodCount + " explicit method(s).");
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static int ParsePositiveOption(Dictionary<string, string> options, string name, int defaultValue)
    {
        if (!options.TryGetValue(name, out string? value))
            return defaultValue;
        if (!int.TryParse(value, out int result) || result < 1)
            throw new ArgumentException("--" + name + " must be a positive integer.");
        return result;
    }

    private static void AddMethodSignature(MethodSig? signature, ModuleDef module, SortedSet<string> types)
    {
        if (signature == null)
            return;
        AddType(signature.RetType, module, types);
        foreach (TypeSig parameter in signature.Params)
            AddType(parameter, module, types);
    }

    private static void AddMethodDescriptor(IMethod methodReference, ModuleDef module,
        Dictionary<string, MethodDescriptor> descriptors, uint metadataToken = 0)
    {
        if (methodReference == null || methodReference.MethodSig == null ||
            methodReference.DeclaringType == null || methodReference.Module != module)
            return;

        TypeDef? declaringType = methodReference.DeclaringType.ResolveTypeDef();
        if (declaringType == null || declaringType.Module != module)
            return;

        // Static constructors are not returned by Type.GetConstructors. Instance
        // constructors are emitted and resolved through RuntimeApi.PrewarmMethodBase.
        if (methodReference.Name == ".cctor" ||
            methodReference.MethodSig.GenParamCount != 0)
            return;

        string declaringTypeName = FormatDeclaringTypeName(methodReference.DeclaringType);
        if (declaringTypeName.IndexOf('!') >= 0 || declaringTypeName.IndexOf("!!", StringComparison.Ordinal) >= 0)
            return;

        var descriptor = new MethodDescriptor
        {
            declaringType = declaringTypeName,
            name = methodReference.Name,
            parameterCount = methodReference.MethodSig.Params.Count,
            genericParameterCount = (int)methodReference.MethodSig.GenParamCount,
            metadataToken = checked((int)metadataToken),
            parameterTypes = FormatClosedSignatureTypes(methodReference.MethodSig.Params),
            returnType = FormatClosedSignatureType(methodReference.MethodSig.RetType),
        };
        MethodDescriptor? existing;
        if (descriptors.TryGetValue(descriptor.Key, out existing))
        {
            if (!MethodDescriptorsEqual(existing, descriptor))
                throw new InvalidOperationException("Conflicting prewarm descriptors share the same identity: " + descriptor.Key);
            return;
        }
        descriptors.Add(descriptor.Key, descriptor);
    }

    private static void AddMethodDescriptor(MethodDef method, ModuleDef module,
        Dictionary<string, MethodDescriptor> descriptors)
    {
        AddMethodDescriptor((IMethod)method, module, descriptors, method.MDToken.Raw);
    }

    private static string[]? FormatClosedSignatureTypes(IList<TypeSig> signatures)
    {
        var typeNames = new string[signatures.Count];
        for (int index = 0; index < signatures.Count; index++)
        {
            string? typeName = FormatClosedSignatureType(signatures[index]);
            if (typeName == null)
                return null;
            typeNames[index] = typeName;
        }
        return typeNames;
    }

    private static string? FormatClosedSignatureType(TypeSig signature)
    {
        if (signature.ContainsGenericParameter)
            return null;
        string typeName = FormatTypeName(signature);
        return typeName.IndexOf('!') >= 0 ? null : typeName;
    }

    private static bool MethodDescriptorsEqual(MethodDescriptor left, MethodDescriptor right)
    {
        return string.Equals(left.declaringType, right.declaringType, StringComparison.Ordinal) &&
            string.Equals(left.name, right.name, StringComparison.Ordinal) &&
            left.parameterCount == right.parameterCount &&
            left.genericParameterCount == right.genericParameterCount &&
            left.metadataToken == right.metadataToken &&
            Enumerable.SequenceEqual(left.parameterTypes ?? Array.Empty<string>(), right.parameterTypes ?? Array.Empty<string>()) &&
            string.Equals(left.returnType, right.returnType, StringComparison.Ordinal);
    }

    private static string FormatDeclaringTypeName(ITypeDefOrRef declaringType)
    {
        if (declaringType is TypeSpec typeSpec && typeSpec.TypeSig != null)
            return FormatTypeName(typeSpec.TypeSig);
        return NormalizeTypeName(declaringType.FullName);
    }

    private static MethodDef? ResolveLocalMethod(IMethod methodReference, ModuleDef module)
    {
        MethodDef? resolved = methodReference.ResolveMethodDef();
        if (resolved != null)
            return resolved;

        // dnlib cannot resolve a MemberRef whose declaring type is a closed
        // generic TypeSpec without a full assembly resolver. For local hot-update
        // code, the declaring type and parameter count are sufficient to find the
        // definition while preserving the generic instantiation in the type set.
        TypeDef? declaringType = methodReference.DeclaringType?.ResolveTypeDef();
        if (declaringType == null || declaringType.Module != module)
            return null;
        int parameterCount = methodReference.MethodSig?.Params.Count ?? -1;
        return declaringType.Methods.FirstOrDefault(candidate =>
            candidate.Name == methodReference.Name &&
            (parameterCount < 0 || MethodSignaturesMatch(candidate.MethodSig, methodReference.MethodSig)));
    }

    private static bool MethodSignaturesMatch(MethodSig? candidate, MethodSig? reference)
    {
        if (candidate == null || reference == null)
            return candidate == reference;
        if (candidate.Params.Count != reference.Params.Count || candidate.GenParamCount != reference.GenParamCount)
            return false;
        for (int index = 0; index < candidate.Params.Count; index++)
        {
            if (!string.Equals(candidate.Params[index].FullName, reference.Params[index].FullName, StringComparison.Ordinal))
                return false;
        }
        return string.Equals(candidate.RetType.FullName, reference.RetType.FullName, StringComparison.Ordinal);
    }

    private static void AddType(TypeSig? signature, ModuleDef module, SortedSet<string> types)
    {
        if (signature == null)
            return;
        if (signature is ByRefSig byRef)
        {
            AddType(byRef.Next, module, types);
            return;
        }
        if (signature is PtrSig pointer)
        {
            AddType(pointer.Next, module, types);
            return;
        }
        if (signature is SZArraySig szArray)
        {
            AddType(szArray.Next, module, types);
            return;
        }
        if (signature is ArraySig array)
        {
            AddType(array.Next, module, types);
            return;
        }
        if (signature is GenericInstSig genericInstance)
        {
            AddGenericInstance(genericInstance, module, types);
            foreach (TypeSig argument in genericInstance.GenericArguments)
                AddType(argument, module, types);
        }
        else
        {
            ITypeDefOrRef? typeReference = signature.ToTypeDefOrRef();
            if (typeReference is TypeSpec typeSpec)
                AddType(typeSpec.ScopeType, module, types);
            else
                AddType(typeReference, module, types);
        }
    }

    private static void AddType(ITypeDefOrRef? typeReference, ModuleDef module, SortedSet<string> types)
    {
        if (typeReference is TypeSpec typeSpec)
        {
            if (typeSpec.TypeSig is GenericInstSig genericInstance)
                AddGenericInstance(genericInstance, module, types);
            else
                AddType(typeSpec.ScopeType, module, types);
            return;
        }
        TypeDef? type = typeReference?.ResolveTypeDef();
        if (type != null && type.Module == module)
            AddType(type, module, types);
    }

    private static void AddType(TypeDef type, ModuleDef module, SortedSet<string> types)
    {
        if (type.Module != module)
            return;
        types.Add(NormalizeTypeName(type.FullName));
        if (type.DeclaringType != null)
            AddType(type.DeclaringType, module, types);
    }

    private static void AddGenericInstance(GenericInstSig genericInstance, ModuleDef module, SortedSet<string> types)
    {
        if (genericInstance.ContainsGenericParameter)
            return;
        TypeDef? genericType = genericInstance.GenericType.ToTypeDefOrRef()?.ResolveTypeDef();
        if (genericType != null && genericType.Module == module)
            types.Add(NormalizeTypeName(genericType.FullName) + "<" + string.Join(",", genericInstance.GenericArguments.Select(FormatTypeName)) + ">");
    }

    private static string NormalizeTypeName(string fullName)
    {
        return fullName.Replace('/', '+');
    }

    private static string FormatTypeName(TypeSig signature)
    {
        TypeSig normalized = signature.RemovePinnedAndModifiers();
        if (normalized is SZArraySig szArray)
            return FormatTypeName(szArray.Next) + "[]";
        if (normalized is ArraySig array)
        {
            string rank = array.Rank == 1
                ? "*"
                : new string(',', checked((int)array.Rank - 1));
            return FormatTypeName(array.Next) + "[" + rank + "]";
        }
        if (normalized is ByRefSig byRef)
            return FormatTypeName(byRef.Next) + "&";
        if (normalized is PtrSig pointer)
            return FormatTypeName(pointer.Next) + "*";
        if (normalized is GenericInstSig genericInstance)
        {
            string genericTypeName = NormalizeTypeName(genericInstance.GenericType.ToTypeDefOrRef()?.FullName ?? genericInstance.GenericType.FullName);
            return genericTypeName + "<" + string.Join(",", genericInstance.GenericArguments.Select(FormatTypeName)) + ">";
        }

        TypeDefOrRefSig? typeSig = normalized as TypeDefOrRefSig;
        if (typeSig != null)
            return NormalizeTypeName(typeSig.TypeDefOrRef.FullName);

        switch (normalized.ElementType)
        {
            case ElementType.Boolean: return "System.Boolean";
            case ElementType.Char: return "System.Char";
            case ElementType.I1: return "System.SByte";
            case ElementType.U1: return "System.Byte";
            case ElementType.I2: return "System.Int16";
            case ElementType.U2: return "System.UInt16";
            case ElementType.I4: return "System.Int32";
            case ElementType.U4: return "System.UInt32";
            case ElementType.I8: return "System.Int64";
            case ElementType.U8: return "System.UInt64";
            case ElementType.R4: return "System.Single";
            case ElementType.R8: return "System.Double";
            case ElementType.String: return "System.String";
            case ElementType.Object: return "System.Object";
            case ElementType.I: return "System.IntPtr";
            case ElementType.U: return "System.UIntPtr";
            default: return normalized.FullName;
        }
    }

    private static string GenerateCSharp(Manifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Generated by HybridCLR prewarm-manifest. Do not edit.");
        builder.AppendLine("namespace HybridCLR.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    public static class " + SanitizeIdentifier(manifest.assemblyName) + "PrewarmManifest");
        builder.AppendLine("    {");
        builder.AppendLine("        public const string AssemblyName = " + Quote(manifest.assemblyName) + ";");
        builder.AppendLine("        public const string AssemblySha256 = " + Quote(manifest.assemblySha256) + ";");
        builder.AppendLine("        public const string GraphCoverage = " + Quote(manifest.graphCoverage) + ";");
        builder.AppendLine("        public const bool DynamicEdgesIncluded = false;");
        builder.AppendLine("        public sealed class MethodDescriptor");
        builder.AppendLine("        {");
        builder.AppendLine("            public string DeclaringTypeName;");
        builder.AppendLine("            public string MethodName;");
        builder.AppendLine("            public int ParameterCount;");
        builder.AppendLine("            public int GenericParameterCount;");
        builder.AppendLine("            public int MetadataToken;");
        builder.AppendLine("            public string[] ParameterTypeNames;");
        builder.AppendLine("            public string ReturnTypeName;");
        builder.AppendLine("        }");
        builder.AppendLine("        public static readonly MethodDescriptor[] Methods = new MethodDescriptor[]");
        builder.AppendLine("        {");
        foreach (MethodDescriptor method in manifest.methods)
        {
            builder.AppendLine("            new MethodDescriptor");
            builder.AppendLine("            {");
            builder.AppendLine("                DeclaringTypeName = " + Quote(method.declaringType) + ",");
            builder.AppendLine("                MethodName = " + Quote(method.name) + ",");
            builder.AppendLine("                ParameterCount = " + method.parameterCount + ",");
            builder.AppendLine("                GenericParameterCount = " + method.genericParameterCount + ",");
            builder.AppendLine("                MetadataToken = " + method.metadataToken + ",");
            if (method.parameterTypes != null)
                builder.AppendLine("                ParameterTypeNames = new string[] { " + string.Join(", ", method.parameterTypes.Select(Quote)) + " },");
            if (method.returnType != null)
                builder.AppendLine("                ReturnTypeName = " + Quote(method.returnType) + ",");
            builder.AppendLine("            },");
        }
        builder.AppendLine("        };");
        builder.AppendLine("        public static readonly string[] Types = new string[]");
        builder.AppendLine("        {");
        foreach (string type in manifest.types)
            builder.AppendLine("            " + Quote(type) + ",");
        builder.AppendLine("        };");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length + 1);
        foreach (char character in value)
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        if (builder.Length == 0 || char.IsDigit(builder[0]))
            builder.Insert(0, '_');
        return builder.ToString();
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static void WriteFile(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
    }
}
