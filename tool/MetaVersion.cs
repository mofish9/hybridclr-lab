using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace HybridCLR.DheTool;

internal sealed class MetaVersionSnapshot
{
    internal const string Magic = "DHEMETA1";
    internal const uint SchemaVersion = 1;
    internal const uint StrictFlag = 1;

    public string AssemblyName { get; private init; } = "";
    public string AssemblySha256 { get; private init; } = "";
    public string AssemblyMetadataVersion { get; private init; } = "";
    public MetaVersionType[] Types { get; private init; } = Array.Empty<MetaVersionType>();
    public MetaVersionField[] Fields { get; private init; } = Array.Empty<MetaVersionField>();
    public MetaVersionMethod[] Methods { get; private init; } = Array.Empty<MetaVersionMethod>();
    [JsonIgnore]
    public string[] AddressTakenFieldIdentities { get; private init; } = Array.Empty<string>();

    public static MetaVersionSnapshot Create(string assemblyPath)
    {
        using var module = ModuleDefMD.Load(assemblyPath);
        var types = module.Types.SelectMany(AllTypes).Where(type => type.Name != "<Module>")
            .Select(CreateType).OrderBy(type => type.StableId, StringComparer.Ordinal).ToArray();
        var typeIds = types.ToDictionary(type => type.Identity, type => type.StableId, StringComparer.Ordinal);
		var typeVersions = types.ToDictionary(type => type.Identity, type => type.Version,
			StringComparer.Ordinal);
		HashSet<string> addressTakenFields = FindAddressTakenFields(module);
        var fields = module.Types.SelectMany(AllTypes).Where(type => type.Name != "<Module>")
			.SelectMany(type => type.Fields.Select((field, index) => CreateField(field,
                typeIds[type.FullName], index, addressTakenFields.Contains(FieldIdentity(field)))))
            .OrderBy(field => field.StableId, StringComparer.Ordinal).ToArray();
		var fieldVersions = fields.ToDictionary(field => field.Identity, field => field.Version,
			StringComparer.Ordinal);
        var methods = module.Types.SelectMany(AllTypes).SelectMany(type => type.Methods)
			.Select(method => CreateMethod(method, typeIds[method.DeclaringType.FullName],
				typeVersions, fieldVersions))
            .OrderBy(method => method.StableId, StringComparer.Ordinal).ToArray();
        RequireUniqueIds(types.Select(type => (type.StableId, type.Identity)), "type");
        RequireUniqueIds(fields.Select(field => (field.StableId, field.Identity)), "field");
        RequireUniqueIds(methods.Select(method => (method.StableId, method.Identity)), "method");
        return new MetaVersionSnapshot
        {
            AssemblyName = module.Assembly?.Name.String ?? throw new InvalidDataException("Assembly has no name."),
            AssemblySha256 = FileSha256(assemblyPath),
            AssemblyMetadataVersion = Hash("dhe-assembly-metadata\n" +
                StableAssemblyShape(module)),
            Types = types,
            Fields = fields,
            Methods = methods,
            AddressTakenFieldIdentities = addressTakenFields.OrderBy(value => value,
                StringComparer.Ordinal).ToArray(),
        };
    }

    public object ToJson(string assemblyPath) => new
    {
        schemaVersion = SchemaVersion,
        format = "hybridclr.dhe-metaversion.json",
        generatedAtUtc = DateTimeOffset.UtcNow,
		algorithm = "sha256-canonical-managed-metadata",
        assemblyName = AssemblyName,
        assemblyMetadataVersion = AssemblyMetadataVersion,
        assembly = new { path = Path.GetFullPath(assemblyPath), sha256 = AssemblySha256 },
        types = Types,
        methods = Methods,
        summary = new { typeCount = Types.Length, methodCount = Methods.Length },
    };

    public void WriteBinary(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, ToBinary());
    }

    public byte[] ToBinary()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        byte[] name = Encoding.UTF8.GetBytes(AssemblyName);
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(SchemaVersion);
        writer.Write(StrictFlag);
        writer.Write(checked((uint)name.Length));
        writer.Write(checked((uint)Types.Length));
        writer.Write(checked((uint)Methods.Length));
        writer.Write(Convert.FromHexString(AssemblySha256));
        writer.Write(name);
        foreach (MetaVersionType type in Types)
        {
            writer.Write(Convert.FromHexString(type.StableId));
            writer.Write(Convert.FromHexString(type.Version));
            writer.Write(type.Token);
            writer.Write(type.Flags);
        }
        foreach (MetaVersionMethod method in Methods)
        {
            writer.Write(Convert.FromHexString(method.StableId));
            writer.Write(Convert.FromHexString(method.Version));
            writer.Write(Convert.FromHexString(method.DeclaringTypeStableId));
            writer.Write(method.Token);
            writer.Write(method.Flags);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static MetaVersionType CreateType(TypeDef type)
    {
        string identity = type.FullName;
        uint flags = type.IsValueType ? 1u : 0u;
        return new MetaVersionType(identity, Hash("dhe-type-id\n" + identity),
            Hash("dhe-type-version\n" + StableTypeShape(type)), type.MDToken.Raw, flags,
            Hash("dhe-type-layout\n" + StableTypeLayoutShape(type)),
            Hash("dhe-type-non-field-layout\n" + StableNonFieldTypeLayoutShape(type)),
            Hash("dhe-type-declarative\n" + StableTypeDeclarativeShape(type)),
            Hash("dhe-type-unsupported-declarative\n" +
				StableUnsupportedTypeDeclarativeShape(type)),
            Hash("dhe-type-noncustom-unsupported-declarative\n" +
				StableUnsupportedTypeDeclarativeWithoutTypeAttributes(type)),
            Hash("dhe-type-custom-attributes\n" + Attributes(type.CustomAttributes)),
            Hash("dhe-type-static-fields\n" + StableStaticFieldShape(type)),
            type.IsInterface, type.DeclaringType != null,
            string.Equals(type.Name.String, "<PrivateImplementationDetails>",
                StringComparison.Ordinal));
    }

	private static MetaVersionMethod CreateMethod(MethodDef method, string declaringTypeStableId,
		IReadOnlyDictionary<string, string> typeVersions,
		IReadOnlyDictionary<string, string> fieldVersions)
    {
        string identity = MethodIdentity(method);
        uint flags = 0;
        if (method.IsStatic) flags |= 1u;
        if (method.IsAbstract) flags |= 2u;
        if (method.IsPinvokeImpl) flags |= 4u;
        if (method.HasBody) flags |= 8u;
        if (method.MethodSig.GenParamCount > 0) flags |= 16u;
        if ((method.DeclaringType?.GenericParameters.Count ?? 0) > 0) flags |= 32u;
		string metadataShape = StableMethodMetadataShape(method);
		string bodyVersion = BodyHash(method);
		string dependencyVersion = StableMethodDependencyShape(method, typeVersions, fieldVersions);
		return new MetaVersionMethod(identity, Hash("dhe-method-id\n" + identity),
			Hash("dhe-method-version\n" + metadataShape + "|" + bodyVersion + "|" +
				dependencyVersion), declaringTypeStableId,
            method.MDToken.Raw, flags, method.Name.String, method.DeclaringType?.FullName ?? "",
            method.MethodSig.RetType.FullName, method.MethodSig.Params.Select(type => type.FullName).ToArray(),
            method.IsStatic, method.MethodSig.HasThis, method.IsAbstract, method.IsPinvokeImpl,
            method.DeclaringType?.IsValueType == true,
            checked((uint)method.GenericParameters.Count),
            checked((uint)(method.DeclaringType?.GenericParameters.Count ?? 0)),
            Hash("dhe-method-metadata\n" + metadataShape), bodyVersion,
			Hash("dhe-method-dependency\n" + dependencyVersion),
            Hash("dhe-method-noncustom-metadata\n" +
                StableMethodMetadataWithoutOwnAttributes(method)),
            Hash("dhe-method-custom-attributes\n" + Attributes(method.CustomAttributes)),
            method.CustomAttributes.Count != 0,
            method.IsVirtual, method.IsConstructor, method.DeclaringType?.IsInterface == true);
    }

	private static MetaVersionField CreateField(FieldDef field, string declaringTypeStableId,
		int declarationIndex, bool addressTaken)
    {
		string identity = FieldIdentity(field);
        bool isThreadStatic = field.CustomAttributes.Any(attribute =>
            string.Equals(attribute.TypeFullName, "System.ThreadStaticAttribute", StringComparison.Ordinal));
        return new MetaVersionField(identity, Hash("dhe-field-id\n" + identity),
            Hash("dhe-field-version\n" + StableFieldMetadataShape(field)), declaringTypeStableId,
            field.MDToken.Raw, (uint)field.Attributes, field.Name.String, field.FieldType.FullName,
            declarationIndex,
            field.IsStatic, field.IsLiteral, isThreadStatic,
            (field.DeclaringType?.GenericParameters.Count ?? 0) > 0,
			field.RVA != 0 || (field.InitialValue?.Length ?? 0) != 0,
			field.DeclaringType?.IsValueType == true, addressTaken,
			field.FieldType.ElementType is ElementType.ByRef or ElementType.Ptr or
				ElementType.FnPtr or ElementType.TypedByRef,
            Hash("dhe-field-noncustom-metadata\n" +
                StableFieldMetadataWithoutOwnAttributes(field)),
            Hash("dhe-field-custom-attributes\n" + Attributes(field.CustomAttributes)),
            field.CustomAttributes.Count != 0);
    }

    private static string StableTypeShape(TypeDef type)
    {
        var fields = type.Fields.Select((field, index) => string.Join(":", index, field.Name.String,
            field.FieldType.FullName, ((uint)field.Attributes).ToString("x8"), field.FieldOffset,
            ConstantShape(field.HasConstant ? field.Constant : null), field.RVA,
            BytesHash(field.InitialValue), field.MarshalType?.ToString() ?? "", Attributes(field.CustomAttributes)));
        var properties = type.Properties.Select(property => string.Join(":", property.Name.String,
            property.Type?.ToString() ?? "", ((uint)property.Attributes).ToString("x8"),
            property.GetMethod == null ? "" : MethodIdentity(property.GetMethod),
            property.SetMethod == null ? "" : MethodIdentity(property.SetMethod),
            string.Join(",", property.OtherMethods.Select(MethodIdentity)),
            ConstantShape(property.HasConstant ? property.Constant : null), Attributes(property.CustomAttributes)));
        var events = type.Events.Select(@event => string.Join(":", @event.Name.String,
            @event.EventType?.FullName ?? "", ((uint)@event.Attributes).ToString("x8"),
            @event.AddMethod == null ? "" : MethodIdentity(@event.AddMethod),
            @event.RemoveMethod == null ? "" : MethodIdentity(@event.RemoveMethod),
            @event.InvokeMethod == null ? "" : MethodIdentity(@event.InvokeMethod),
            string.Join(",", @event.OtherMethods.Select(MethodIdentity)), Attributes(@event.CustomAttributes)));
        return string.Join("|", type.FullName, type.BaseType?.FullName ?? "",
            ((uint)type.Attributes).ToString("x8"), type.IsValueType,
            type.ClassLayout?.PackingSize.ToString(CultureInfo.InvariantCulture) ?? "",
            type.ClassLayout?.ClassSize.ToString(CultureInfo.InvariantCulture) ?? "",
            string.Join(",", type.Interfaces.Select(item => item.Interface.FullName).OrderBy(value => value,
                StringComparer.Ordinal)),
            string.Join(",", type.GenericParameters.Select(StableGenericParameter)),
            string.Join(",", fields), string.Join(",", properties), string.Join(",", events),
            Attributes(type.CustomAttributes));
    }

    private static string StableTypeLayoutShape(TypeDef type)
    {
        var instanceFields = type.Fields.Where(field => !field.IsStatic)
            .Select((field, index) => StableFieldShape(field, index));
        return string.Join("|", type.FullName, type.BaseType?.FullName ?? "",
            ((uint)type.Attributes).ToString("x8"), type.IsValueType,
            type.ClassLayout?.PackingSize.ToString(CultureInfo.InvariantCulture) ?? "",
            type.ClassLayout?.ClassSize.ToString(CultureInfo.InvariantCulture) ?? "",
            string.Join(",", type.Interfaces.Select(item => item.Interface.FullName).OrderBy(value => value,
                StringComparer.Ordinal)),
            string.Join(",", type.GenericParameters.Select(StableGenericParameter)),
            string.Join(",", instanceFields));
    }

	private static string StableNonFieldTypeLayoutShape(TypeDef type) => string.Join("|",
		type.FullName, type.BaseType?.FullName ?? "", ((uint)type.Attributes).ToString("x8"),
		type.IsValueType, type.ClassLayout?.PackingSize.ToString(CultureInfo.InvariantCulture) ?? "",
		type.ClassLayout?.ClassSize.ToString(CultureInfo.InvariantCulture) ?? "",
		string.Join(",", type.Interfaces.Select(item => item.Interface.FullName).OrderBy(value => value,
			StringComparer.Ordinal)), string.Join(",", type.GenericParameters.Select(StableGenericParameter)));

    private static string StableTypeDeclarativeShape(TypeDef type)
    {
        var properties = type.Properties.Select(property => string.Join(":", property.Name.String,
            property.Type?.ToString() ?? "", ((uint)property.Attributes).ToString("x8"),
            property.GetMethod == null ? "" : MethodIdentity(property.GetMethod),
			property.SetMethod == null ? "" : MethodIdentity(property.SetMethod)));
        var events = type.Events.Select(@event => string.Join(":", @event.Name.String,
            @event.EventType?.FullName ?? "", ((uint)@event.Attributes).ToString("x8"),
            @event.AddMethod == null ? "" : MethodIdentity(@event.AddMethod),
            @event.RemoveMethod == null ? "" : MethodIdentity(@event.RemoveMethod),
			@event.InvokeMethod == null ? "" : MethodIdentity(@event.InvokeMethod)));
		return string.Join("|", type.FullName, string.Join(",", properties),
			string.Join(",", events));
    }

	private static string StableUnsupportedTypeDeclarativeShape(TypeDef type)
	{
		var properties = type.Properties.Where(property => property.OtherMethods.Count != 0 ||
			property.HasConstant || property.CustomAttributes.Count != 0).Select(property => string.Join(":",
			property.Name.String,
			string.Join(",", property.OtherMethods.Select(MethodIdentity)),
			ConstantShape(property.HasConstant ? property.Constant : null),
			Attributes(property.CustomAttributes)));
		var events = type.Events.Where(@event => @event.OtherMethods.Count != 0 ||
			@event.CustomAttributes.Count != 0).Select(@event => string.Join(":", @event.Name.String,
			string.Join(",", @event.OtherMethods.Select(MethodIdentity)),
			Attributes(@event.CustomAttributes)));
		return string.Join("|", type.FullName, string.Join(",", properties),
			string.Join(",", events), Attributes(type.CustomAttributes),
			DeclSecurities(type.DeclSecurities));
	}

	private static string StableUnsupportedTypeDeclarativeWithoutTypeAttributes(TypeDef type)
	{
		var properties = type.Properties.Where(property => property.OtherMethods.Count != 0 ||
			property.HasConstant || property.CustomAttributes.Count != 0).Select(property => string.Join(":",
			property.Name.String,
			string.Join(",", property.OtherMethods.Select(MethodIdentity)),
			ConstantShape(property.HasConstant ? property.Constant : null),
			Attributes(property.CustomAttributes)));
		var events = type.Events.Where(@event => @event.OtherMethods.Count != 0 ||
			@event.CustomAttributes.Count != 0).Select(@event => string.Join(":", @event.Name.String,
			string.Join(",", @event.OtherMethods.Select(MethodIdentity)),
			Attributes(@event.CustomAttributes)));
		return string.Join("|", type.FullName, string.Join(",", properties),
			string.Join(",", events), DeclSecurities(type.DeclSecurities));
	}

    private static string StableStaticFieldShape(TypeDef type) => string.Join(",",
        type.Fields.Where(field => field.IsStatic).Select((field, index) => StableFieldShape(field, index)));

    private static string StableFieldShape(FieldDef field, int index) => string.Join(":", index,
        field.Name.String, field.FieldType.FullName, ((uint)field.Attributes).ToString("x8"), field.FieldOffset,
        ConstantShape(field.HasConstant ? field.Constant : null), field.RVA, BytesHash(field.InitialValue),
        field.MarshalType?.ToString() ?? "", Attributes(field.CustomAttributes));

    private static string StableFieldMetadataShape(FieldDef field) => string.Join(":",
        field.DeclaringType?.FullName ?? "", field.Name.String, field.FieldType.FullName,
        ((uint)field.Attributes).ToString("x8"), field.FieldOffset,
        ConstantShape(field.HasConstant ? field.Constant : null), field.RVA, BytesHash(field.InitialValue),
        field.MarshalType?.ToString() ?? "", Attributes(field.CustomAttributes));

    private static string StableFieldMetadataWithoutOwnAttributes(FieldDef field) => string.Join(":",
        field.DeclaringType?.FullName ?? "", field.Name.String, field.FieldType.FullName,
        ((uint)field.Attributes).ToString("x8"), field.FieldOffset,
        ConstantShape(field.HasConstant ? field.Constant : null), field.RVA, BytesHash(field.InitialValue),
        field.MarshalType?.ToString() ?? "", RuntimeSemanticFieldAttributes(field.CustomAttributes));

	private static string FieldIdentity(FieldDef field) =>
		(field.DeclaringType?.FullName ?? "") + "::" + field.Name + "|" + field.FieldType.FullName;

	private static string FieldIdentity(IField field) =>
		(field.DeclaringType?.FullName ?? "") + "::" + field.Name + "|" +
		(field.FieldSig?.Type.FullName ?? "");

	private static HashSet<string> FindAddressTakenFields(ModuleDefMD module)
	{
		var fields = new HashSet<string>(StringComparer.Ordinal);
		foreach (MethodDef method in module.Types.SelectMany(AllTypes).SelectMany(type => type.Methods)
			.Where(method => method.HasBody))
		{
			foreach (Instruction instruction in method.Body!.Instructions.Where(instruction =>
				instruction.OpCode.Code == Code.Ldflda))
			{
				IField? field = instruction.Operand as IField;
				if (field != null)
					fields.Add(FieldIdentity(field));
			}
		}
		return fields;
	}

	private static string StableMethodDependencyShape(MethodDef method,
		IReadOnlyDictionary<string, string> typeVersions,
		IReadOnlyDictionary<string, string> fieldVersions)
	{
		var dependencies = new SortedSet<string>(StringComparer.Ordinal);
		AddTypeVersion(method.DeclaringType?.FullName, typeVersions, dependencies);
		AddTypeVersion(method.MethodSig.RetType.FullName, typeVersions, dependencies);
		foreach (TypeSig parameter in method.MethodSig.Params)
			AddTypeVersion(parameter.FullName, typeVersions, dependencies);
		if (!method.HasBody)
			return string.Join("|", dependencies);

		foreach (Local local in method.Body!.Variables)
			AddTypeVersion(local.Type.FullName, typeVersions, dependencies);
		foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
			AddTypeVersion(handler.CatchType?.FullName, typeVersions, dependencies);
		foreach (Instruction instruction in method.Body.Instructions)
		{
			switch (instruction.Operand)
			{
				case IField field:
					AddTypeVersion(field.DeclaringType?.FullName, typeVersions, dependencies);
					FieldDef? fieldDef = field.ResolveFieldDef();
					if (fieldDef != null && fieldVersions.TryGetValue(FieldIdentity(fieldDef),
							out string? fieldVersion))
						dependencies.Add("field:" + FieldIdentity(fieldDef) + ":" + fieldVersion);
					break;
				case IMethod calledMethod:
					AddTypeVersion(calledMethod.DeclaringType?.FullName, typeVersions, dependencies);
					break;
				case ITypeDefOrRef type:
					AddTypeVersion(type.FullName, typeVersions, dependencies);
					break;
				case TypeSig typeSignature:
					AddTypeVersion(typeSignature.FullName, typeVersions, dependencies);
					break;
			}
		}
		return string.Join("|", dependencies);
	}

	private static void AddTypeVersion(string? identity,
		IReadOnlyDictionary<string, string> typeVersions, ISet<string> dependencies)
	{
		if (!string.IsNullOrEmpty(identity) && typeVersions.TryGetValue(identity,
				out string? version))
			dependencies.Add("type:" + identity + ":" + version);
	}

    private static string StableMethodMetadataShape(MethodDef method)
    {
        var parameters = method.ParamDefs.OrderBy(parameter => parameter.Sequence).Select(parameter => string.Join("/",
            parameter.Sequence, parameter.Name.String, parameter.Attributes,
            ConstantShape(parameter.HasConstant ? parameter.Constant : null),
            parameter.MarshalType?.ToString() ?? "", Attributes(parameter.CustomAttributes)));
        return string.Join("|", MethodIdentity(method), method.MethodSig,
            ((uint)method.Attributes).ToString("x8"), ((uint)method.ImplAttributes).ToString("x8"),
            string.Join(",", method.GenericParameters.Select(StableGenericParameter)),
            string.Join(",", parameters), Attributes(method.CustomAttributes), method.ImplMap?.ToString() ?? "",
            string.Join(",", method.Overrides.Select(item => item.ToString()).OrderBy(value => value,
                StringComparer.Ordinal)), DeclSecurities(method.DeclSecurities));
    }

    private static string StableMethodMetadataWithoutOwnAttributes(MethodDef method)
    {
        var parameters = method.ParamDefs.OrderBy(parameter => parameter.Sequence).Select(parameter => string.Join("/",
            parameter.Sequence, parameter.Name.String, parameter.Attributes,
            ConstantShape(parameter.HasConstant ? parameter.Constant : null),
            parameter.MarshalType?.ToString() ?? "", Attributes(parameter.CustomAttributes)));
        return string.Join("|", MethodIdentity(method), method.MethodSig,
            ((uint)method.Attributes).ToString("x8"), ((uint)method.ImplAttributes).ToString("x8"),
            string.Join(",", method.GenericParameters.Select(StableGenericParameter)),
            string.Join(",", parameters), method.ImplMap?.ToString() ?? "",
            string.Join(",", method.Overrides.Select(item => item.ToString()).OrderBy(value => value,
                StringComparer.Ordinal)), DeclSecurities(method.DeclSecurities));
    }

    private static string RuntimeSemanticFieldAttributes(IEnumerable<CustomAttribute> attributes) =>
        Attributes(attributes.Where(attribute => string.Equals(attribute.TypeFullName,
            "System.ThreadStaticAttribute", StringComparison.Ordinal)));

    private static string BodyHash(MethodDef method)
    {
        if (!method.HasBody) return "";
        var instructions = method.Body!.Instructions;
        var indexes = instructions.Select((instruction, index) => (instruction, index))
            .ToDictionary(item => item.instruction, item => item.index);
        var text = new StringBuilder().Append(method.Body.MaxStack).Append('|')
            .Append(method.Body.InitLocals).Append('|').Append(method.Body.KeepOldMaxStack);
        foreach (Local local in method.Body.Variables)
            text.Append("|local:").Append(local.Type.FullName);
        foreach (Instruction instruction in instructions)
            text.Append('|').Append(instruction.OpCode.Code).Append(':').Append(Operand(instruction.Operand, indexes));
        foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
            text.Append("|eh:").Append(handler.HandlerType).Append(':').Append(handler.CatchType?.FullName)
                .Append(':').Append(InstructionIndex(handler.TryStart, indexes)).Append(':')
                .Append(InstructionIndex(handler.TryEnd, indexes)).Append(':')
                .Append(InstructionIndex(handler.HandlerStart, indexes)).Append(':')
                .Append(InstructionIndex(handler.HandlerEnd, indexes)).Append(':')
                .Append(InstructionIndex(handler.FilterStart, indexes));
        return Hash(text.ToString());
    }

    private static string Operand(object? operand, IReadOnlyDictionary<Instruction, int> indexes)
    {
        if (operand == null) return "";
        if (operand is Instruction instruction) return "target:" + InstructionIndex(instruction, indexes);
        if (operand is IList<Instruction> targets)
            return "targets:" + string.Join(",", targets.Select(target => InstructionIndex(target, indexes)));
        if (operand is Local local) return "local:" + local.Index + ":" + local.Type.FullName;
        if (operand is Parameter parameter) return "parameter:" + parameter.Index + ":" + parameter.Type.FullName;
        if (operand is UTF8String utf8) return "string:" + utf8.String;
        string? fullName = operand.GetType().GetProperty("FullName")?.GetValue(operand)?.ToString();
        return operand.GetType().FullName + ":" +
            (fullName ?? Convert.ToString(operand, CultureInfo.InvariantCulture) ?? "");
    }

    private static int InstructionIndex(Instruction? instruction,
        IReadOnlyDictionary<Instruction, int> indexes) => instruction == null ? -1 : indexes[instruction];

    private static string MethodIdentity(MethodDef method) =>
        (method.DeclaringType?.FullName ?? "") + "::" + method.Name + "|" + method.MethodSig;

    private static string StableGenericParameter(GenericParam parameter) => string.Join(":", parameter.Number,
        parameter.Name.String, ((uint)parameter.Flags).ToString("x8"),
        string.Join(",", parameter.GenericParamConstraints.Select(item => item.Constraint.FullName)
            .OrderBy(value => value, StringComparer.Ordinal)), Attributes(parameter.CustomAttributes));

    private static string Attributes(IEnumerable<CustomAttribute> attributes) => string.Join(",",
        attributes.Select(attribute => string.Join("", attribute.Constructor?.FullName ??
                attribute.TypeFullName, "(",
            string.Join(";", attribute.ConstructorArguments.Select(AttributeArgument)), ")",
            "{", string.Join(";", attribute.NamedArguments.Select(argument => string.Join(":",
                argument.IsField ? "field" : "property", argument.Name.String,
                argument.Type?.FullName ?? "", AttributeArgument(argument.Argument)))), "}"))
            .OrderBy(value => value, StringComparer.Ordinal));

    private static string AttributeArgument(CAArgument argument) =>
        (argument.Type?.FullName ?? "") + "=" + Value(argument.Value);

    private static string DeclSecurities(IEnumerable<DeclSecurity> securities) => string.Join(",",
        securities.Select(security => string.Join(":", ((uint)security.Action).ToString("x8"),
            BytesHash(security.GetBlob()), Attributes(security.CustomAttributes)))
            .OrderBy(value => value, StringComparer.Ordinal));

    private static string StableAssemblyShape(ModuleDef module)
    {
        AssemblyDef? assembly = module.Assembly;
        string assemblyShape = assembly == null ? "" : string.Join("|", assembly.FullName,
            ((uint)assembly.Attributes).ToString("x8"),
            ((uint)assembly.HashAlgorithm).ToString("x8"),
            assembly.PublicKey?.ToString() ?? "", Attributes(assembly.CustomAttributes),
            DeclSecurities(assembly.DeclSecurities));
        string references = string.Join(",", module.GetAssemblyRefs().Select(reference =>
            reference.FullName).OrderBy(value => value, StringComparer.Ordinal));
        string resources = string.Join(",", module.Resources.Select(resource => string.Join(":",
            resource.Name.String, ((uint)resource.Attributes).ToString("x8"), resource.ResourceType,
            resource is EmbeddedResource embedded ? BytesHash(embedded.CreateReader().ToArray()) :
                resource.ToString(), Attributes(resource.CustomAttributes)))
            .OrderBy(value => value, StringComparer.Ordinal));
        string exportedTypes = string.Join(",", module.ExportedTypes.Select(type => string.Join(":",
            type.FullName, ((uint)type.Attributes).ToString("x8"), type.TypeDefId,
            type.Implementation?.ToString() ?? "", Attributes(type.CustomAttributes)))
            .OrderBy(value => value, StringComparer.Ordinal));
        return string.Join("|", assemblyShape, references, module.Name.String, module.Kind,
            module.Characteristics, module.DllCharacteristics, module.RuntimeVersion, module.Machine,
            module.Cor20HeaderFlags, module.Cor20HeaderRuntimeVersion, module.TablesHeaderVersion,
            module.ManagedEntryPoint?.MDToken.Raw.ToString("x8") ?? "",
            Attributes(module.CustomAttributes), resources, exportedTypes);
    }

    private static string ConstantShape(Constant? constant) => constant == null ? "" :
        ((uint)constant.Type).ToString("x8") + ":" + Value(constant.Value);

    private static string Value(object? value)
    {
        if (value == null) return "null";
        if (value is UTF8String utf8) return utf8.String;
        if (value is IType type) return type.FullName;
        if (value is IList<CAArgument> arguments) return "[" + string.Join(",", arguments.Select(item =>
            (item.Type?.FullName ?? "") + "=" + Value(item.Value))) + "]";
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    private static string BytesHash(byte[]? bytes) => bytes == null || bytes.Length == 0 ? "" :
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string FileSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static IEnumerable<TypeDef> AllTypes(TypeDef type)
    {
        yield return type;
        foreach (TypeDef child in type.NestedTypes.SelectMany(AllTypes)) yield return child;
    }

    private static void RequireUniqueIds(IEnumerable<(string id, string identity)> records, string kind)
    {
        foreach (IGrouping<string, (string id, string identity)> group in records.GroupBy(record => record.id,
                     StringComparer.Ordinal))
        {
            if (group.Count() > 1)
                throw new InvalidDataException("MetaVersion " + kind + " stable-id collision: " +
                    string.Join(", ", group.Select(record => record.identity)));
        }
    }
}

internal sealed record MetaVersionType(string Identity, string StableId, string Version, uint Token, uint Flags,
    [property: JsonIgnore] string LayoutVersion,
	[property: JsonIgnore] string NonFieldLayoutVersion,
    [property: JsonIgnore] string DeclarativeVersion,
	[property: JsonIgnore] string UnsupportedDeclarativeVersion,
	[property: JsonIgnore] string NonCustomUnsupportedDeclarativeVersion,
	[property: JsonIgnore] string CustomAttributeVersion,
    [property: JsonIgnore] string StaticFieldVersion,
    [property: JsonIgnore] bool IsInterface,
    [property: JsonIgnore] bool IsNested,
    [property: JsonIgnore] bool IsPrivateImplementationDetails);

internal sealed record MetaVersionMethod(string Identity, string StableId, string Version,
    string DeclaringTypeStableId, uint Token, uint Flags, string Name, string DeclaringType,
    string ReturnType, string[] ParameterTypes, bool IsStatic, bool HasThis, bool IsAbstract,
    bool IsPInvoke, bool DeclaringTypeIsValueType, uint GenericParameterCount,
    uint DeclaringTypeGenericParameterCount,
    [property: JsonIgnore] string MetadataVersion,
    [property: JsonIgnore] string BodyVersion,
	[property: JsonIgnore] string DependencyVersion,
	[property: JsonIgnore] string NonCustomMetadataVersion,
	[property: JsonIgnore] string CustomAttributeVersion,
	[property: JsonIgnore] bool HasCustomAttributes,
    [property: JsonIgnore] bool IsVirtual,
    [property: JsonIgnore] bool IsConstructor,
    [property: JsonIgnore] bool DeclaringTypeIsInterface);

internal sealed record MetaVersionField(string Identity, string StableId, string Version,
    string DeclaringTypeStableId, uint Token, uint Flags, string Name, string FieldType,
    int DeclarationIndex, bool IsStatic, bool IsLiteral, bool IsThreadStatic,
    bool DeclaringTypeIsGeneric, bool HasRva,
	bool DeclaringTypeIsValueType, bool AddressTaken, bool HasUnsupportedSidecarType,
	[property: JsonIgnore] string NonCustomMetadataVersion,
	[property: JsonIgnore] string CustomAttributeVersion,
	[property: JsonIgnore] bool HasCustomAttributes);
