using dnlib.DotNet;

namespace HybridCLR.DheTool;

// Planning only: an impact result does not enable value-layout loading. The
// runtime/storage implementation must satisfy these obligations before release.
public sealed record DheValueLayoutMethodImpact(string AssemblyName, string MethodIdentity,
    string Decision, string[] ChangedValueTypes);
public sealed record DheValueLayoutTypeImpact(string TypeIdentity, string[] ChangedValueTypes);
public sealed record DheValueLayoutImpactResult(string[] ChangedValueTypes,
    DheValueLayoutTypeImpact[] Layouts, DheValueLayoutMethodImpact[] Methods);

public static class DheValueLayoutImpact
{
    public static DheValueLayoutImpactResult Analyze(IEnumerable<string> baselinePaths,
        IEnumerable<string> currentPaths)
    {
        var baseline = baselinePaths.Select(MetaVersionSnapshot.Create).ToDictionary(
            value => value.AssemblyName, StringComparer.Ordinal);
        using var analysis = new Analysis(currentPaths);
        return analysis.Run(baseline);
    }

    private sealed record Context(Bound[] Types, Bound[] Methods)
    {
        public static readonly Context Empty = new(Array.Empty<Bound>(), Array.Empty<Bound>());
    }
    private sealed record Bound(TypeSig Signature, Context Context);
    private sealed record Use(TypeDef Definition, Context Context, string Key);

    private sealed class Analysis : IDisposable
    {
        private readonly Dictionary<string, ModuleDefMD> modules = new(StringComparer.Ordinal);
        private readonly HashSet<string> changed = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> layouts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> genericLayouts = new(StringComparer.Ordinal);
        private readonly HashSet<string> visiting = new(StringComparer.Ordinal);

        public Analysis(IEnumerable<string> paths)
        {
            try
            {
                foreach (string path in paths)
                {
                    var module = ModuleDefMD.Load(path);
                    string name = module.Assembly?.Name.String ?? throw new InvalidDataException("Missing assembly.");
                    if (!modules.TryAdd(name, module))
                    {
                        module.Dispose();
                        throw new InvalidDataException("Duplicate current assembly: " + name);
                    }
                }
            }
            catch { Dispose(); throw; }
        }

        public DheValueLayoutImpactResult Run(Dictionary<string, MetaVersionSnapshot> baseline)
        {
            foreach (var entry in modules)
            {
                if (!baseline.TryGetValue(entry.Key, out var before)) continue;
                var after = MetaVersionSnapshot.Create(entry.Value.Location).Types.ToDictionary(type => type.StableId);
                foreach (var type in before.Types.Where(type => (type.Flags & 1u) != 0))
                    if (after.TryGetValue(type.StableId, out var next) &&
                        !string.Equals(type.LayoutVersion, next.LayoutVersion, StringComparison.Ordinal))
                        changed.Add(entry.Key + "|" + type.Identity);
            }
            if (changed.Count == 0)
                return new(Array.Empty<string>(), Array.Empty<DheValueLayoutTypeImpact>(),
                    Array.Empty<DheValueLayoutMethodImpact>());

            var methods = new List<DheValueLayoutMethodImpact>();
            foreach (var entry in modules.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                if (!baseline.TryGetValue(entry.Key, out var before)) continue;
                var oldMethods = before.Methods.Select(method => method.Identity).ToHashSet(StringComparer.Ordinal);
                foreach (TypeDef type in entry.Value.GetTypes().Where(type => type.Name != "<Module>"))
                {
                    bool generic = false;
                    Layout(new Use(type, Context.Empty, TypeKey(type)), ref generic);
                    foreach (MethodDef method in type.Methods)
                    {
                        string identity = MetaVersionSnapshot.MethodIdentity(method);
                        if (!oldMethods.Contains(identity)) continue; // New methods are already interpreted.
                        var dependencies = new HashSet<string>(StringComparer.Ordinal);
                        bool openContext = false;
                        Signature(type.ToTypeSig(), Context.Empty, dependencies, ref openContext);
                        MethodSignature(method.MethodSig, Context.Empty, dependencies, ref openContext);
                        if (method.HasBody)
                        {
                            foreach (var local in method.Body.Variables)
                                Signature(local.Type, Context.Empty, dependencies, ref openContext);
                            foreach (var handler in method.Body.ExceptionHandlers)
                                Signature(handler.CatchType?.ToTypeSig(), Context.Empty, dependencies, ref openContext);
                            foreach (var instruction in method.Body.Instructions)
                            {
                                if (instruction.Operand is IMethod call)
                                {
                                    Signature(call.DeclaringType.ToTypeSig(), Context.Empty, dependencies, ref openContext);
                                    Context owner = GenericContext(call.DeclaringType.ToTypeSig(), Context.Empty);
                                    Bound[] arguments = call is MethodSpec spec
                                        ? spec.GenericInstMethodSig.GenericArguments.Select(arg => new Bound(arg, Context.Empty)).ToArray()
                                        : Array.Empty<Bound>();
                                    var context = new Context(owner.Types, arguments);
                                    MethodSignature(call.MethodSig, context, dependencies, ref openContext);
                                    foreach (Bound argument in arguments)
                                        Signature(argument.Signature, argument.Context, dependencies, ref openContext);
                                }
                                else if (instruction.Operand is IField field)
                                {
                                    Signature(field.DeclaringType.ToTypeSig(), Context.Empty, dependencies, ref openContext);
                                    Context owner = GenericContext(field.DeclaringType.ToTypeSig(), Context.Empty);
                                    Signature(field.FieldSig?.Type, owner, dependencies, ref openContext);
                                }
                                else if (instruction.Operand is ITypeDefOrRef reference)
                                    Signature(reference.ToTypeSig(), Context.Empty, dependencies, ref openContext);
                                else if (instruction.Operand is TypeSig signature)
                                    Signature(signature, Context.Empty, dependencies, ref openContext);
                                else if (instruction.Operand is MethodSig indirect)
                                    MethodSignature(indirect, Context.Empty, dependencies, ref openContext);
                            }
                        }
                        if (dependencies.Count != 0 || openContext)
                            methods.Add(new(entry.Key, identity,
                                method.IsPinvokeImpl ? "native-abi-bridge" :
                                dependencies.Count != 0 ? "interpret" : "inspect-generic-context",
                                dependencies.OrderBy(value => value, StringComparer.Ordinal).ToArray()));
                    }
                }
            }
            return new(changed.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                layouts.Where(entry => entry.Value.Count != 0).OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new DheValueLayoutTypeImpact(entry.Key,
                        entry.Value.OrderBy(value => value, StringComparer.Ordinal).ToArray())).ToArray(),
                methods.OrderBy(method => method.AssemblyName, StringComparer.Ordinal)
                    .ThenBy(method => method.MethodIdentity, StringComparer.Ordinal).ToArray());
        }

        private void MethodSignature(MethodSig? signature, Context context,
            HashSet<string> dependencies, ref bool open)
        {
            if (signature == null) return;
            Signature(signature.RetType, context, dependencies, ref open);
            foreach (TypeSig parameter in signature.Params)
                Signature(parameter, context, dependencies, ref open);
            if (signature.ParamsAfterSentinel != null)
                foreach (TypeSig parameter in signature.ParamsAfterSentinel)
                    Signature(parameter, context, dependencies, ref open);
        }

        private void Signature(TypeSig? signature, Context context, HashSet<string> dependencies, ref bool open)
        {
            if (signature == null) return;
            if (signature is GenericSig variable)
            {
                Bound[] arguments = variable is GenericVar ? context.Types : context.Methods;
                if (variable.Number >= arguments.Length) { open = true; return; }
                Bound bound = arguments[variable.Number];
                Signature(bound.Signature, bound.Context, dependencies, ref open);
                return;
            }
            Use? use = ResolveUse(signature, context);
            if (use != null) dependencies.UnionWith(Layout(use, ref open));
            if (signature is GenericInstSig instance)
                foreach (TypeSig argument in instance.GenericArguments)
                    Signature(argument, context, dependencies, ref open);
            if (signature.Next != null) Signature(signature.Next, context, dependencies, ref open);
            if (signature is FnPtrSig pointer) MethodSignature(pointer.MethodSig, context, dependencies, ref open);
        }

        private HashSet<string> Layout(Use use, ref bool open)
        {
            if (layouts.TryGetValue(use.Key, out var cached))
            {
                open |= genericLayouts[use.Key];
                return cached;
            }
            if (!visiting.Add(use.Key))
                throw new InvalidDataException("Recursive inline value layout: " + use.Key);
            var result = new HashSet<string>(StringComparer.Ordinal);
            bool generic = false;
            if (changed.Contains(TypeKey(use.Definition))) result.Add(TypeKey(use.Definition));
            if (!use.Definition.IsValueType && use.Definition.BaseType != null)
            {
                Use? parent = ResolveUse(use.Definition.BaseType.ToTypeSig(), use.Context);
                if (parent != null) result.UnionWith(Layout(parent, ref generic));
            }
            foreach (FieldDef field in use.Definition.Fields.Where(field => !field.IsStatic))
                Inline(field.FieldType, use.Context, result, ref generic);
            visiting.Remove(use.Key);
            layouts.Add(use.Key, result);
            genericLayouts.Add(use.Key, generic);
            open |= generic;
            return result;
        }

        private void Inline(TypeSig signature, Context context, HashSet<string> result, ref bool open)
        {
            if (signature is GenericSig variable)
            {
                Bound[] arguments = variable is GenericVar ? context.Types : context.Methods;
                if (variable.Number >= arguments.Length) { open = true; return; }
                Bound bound = arguments[variable.Number];
                Inline(bound.Signature, bound.Context, result, ref open);
                return;
            }
            if (signature is ModifierSig || signature is PinnedSig)
            {
                Inline(signature.Next, context, result, ref open);
                return;
            }
            // References, arrays, pointers and byrefs have fixed pointer storage.
            if (!signature.IsValueType) return;
            Use? use = ResolveUse(signature, context);
            if (use != null) result.UnionWith(Layout(use, ref open));
            // External value-generic storage (e.g. Nullable<T>) may embed T.
            else if (signature is GenericInstSig generic)
                foreach (TypeSig argument in generic.GenericArguments)
                    Inline(argument, context, result, ref open);
        }

        private Use? ResolveUse(TypeSig signature, Context context)
        {
            ITypeDefOrRef? reference = signature is GenericInstSig instance
                ? instance.GenericType.TypeDefOrRef : (signature as TypeDefOrRefSig)?.TypeDefOrRef;
            if (reference == null) return null;
            string name = reference.DefinitionAssembly?.Name.String ?? "";
            if (!modules.TryGetValue(name, out var module)) return null; // Ordinary AOT domain.
            if (reference.DefinitionAssembly!.FullName != module.Assembly.FullName)
                throw new InvalidDataException("Assembly identity mismatch: " + reference.FullName);
            TypeDef definition = module.Find(reference.FullName, false) ??
                throw new InvalidDataException("Missing current hotfix type: " + name + "|" + reference.FullName);
            return new(definition, GenericContext(signature, context), Key(signature, context));
        }

        private static Context GenericContext(TypeSig signature, Context context) => new(
            signature is GenericInstSig closed
                ? closed.GenericArguments.Select(argument => new Bound(argument, context)).ToArray()
                : Array.Empty<Bound>(), Array.Empty<Bound>());

        private string Key(TypeSig signature, Context context)
        {
            if (signature is GenericSig variable)
            {
                Bound[] arguments = variable is GenericVar ? context.Types : context.Methods;
                if (variable.Number >= arguments.Length) return signature.FullName;
                Bound bound = arguments[variable.Number];
                return Key(bound.Signature, bound.Context);
            }
            if (signature is GenericInstSig generic)
                return TypeKey(generic.GenericType.TypeDefOrRef) + "<" +
                    string.Join(",", generic.GenericArguments.Select(argument => Key(argument, context))) + ">";
            if (signature is TypeDefOrRefSig reference) return TypeKey(reference.TypeDefOrRef);
            return signature.Next == null ? signature.FullName : signature.ElementType + "(" + Key(signature.Next, context) + ")";
        }

        private static string TypeKey(ITypeDefOrRef type) =>
            (type.DefinitionAssembly?.Name.String ?? "") + "|" + type.FullName;
        public void Dispose() { foreach (var module in modules.Values) module.Dispose(); }
    }
}
