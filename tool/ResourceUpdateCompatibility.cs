namespace HybridCLR.DheTool;

internal sealed class ResourceUpdateCompatibility
{
	public const string Policy = "dhe-proven-safe-subset-v1";
	public const string RuntimeProtocol = "dhe-runtime-protocol-v1";
    public const string CurrentNativeRuntimeContract = "dhe-runtime-v1";
    public static readonly string[] KnownRuntimeCapabilities =
    {
		"aot-guard-v1",
		"single-current-multibase-v1",
		"resource-update-plan-integrity-v1",
		"resource-update-aot-metadata-path-v1",
		"atomic-multi-assembly-registration-v1",
		"supplemental-existing-type-instance-fields-v1",
        "supplemental-existing-type-static-fields-v1",
		"supplemental-existing-type-methods-v1",
		"removed-existing-type-methods-v1",
		"existing-type-method-signature-replacement-v1",
		"removed-existing-type-fields-v1",
		"removed-types-v1",
		"logical-existing-type-properties-events-v1",
		"logical-existing-member-custom-attributes-v1",
        "supplemental-nested-types-v1",
        "supplemental-top-level-types-v1",
    };

    public int UnchangedMethodCount { get; private init; }
    public int ChangedMethodCount { get; private init; }
    public int BodyOnlyChangedMethodCount { get; private init; }
	public int DependencyChangedMethodCount { get; private init; }
    public int RemovedMethodCount { get; private init; }
    public int AddedMethodCount { get; private init; }
	public int RemovedFieldCount { get; private init; }
	public int AddedFieldCount { get; private init; }
    public int ChangedExistingTypeCount { get; private init; }
    public int RemovedTypeCount { get; private init; }
    public int AddedTypeCount { get; private init; }
    public MetaVersionMethod[] GuardRequiredMethods { get; private init; } =
        Array.Empty<MetaVersionMethod>();
    public string[] RequiredRuntimeCapabilities { get; private init; } = Array.Empty<string>();
    public string[] UnsupportedChanges { get; private init; } = Array.Empty<string>();
    public bool Compatible => UnsupportedChanges.Length == 0;

    public static bool CanExecuteUpdate(string runtimeProtocol, string runtimeContract,
        IEnumerable<string> availableCapabilities,
        IEnumerable<string> requiredCapabilities)
    {
        string[] available = availableCapabilities.Where(value =>
            !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        string[] required = requiredCapabilities.Where(value =>
            !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        return string.Equals(runtimeProtocol, RuntimeProtocol, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(runtimeContract) &&
            required.Length != 0 &&
            new HashSet<string>(available, StringComparer.Ordinal).IsSupersetOf(required);
    }

    public static ResourceUpdateCompatibility Analyze(MetaVersionSnapshot baseline,
        MetaVersionSnapshot current, IEnumerable<string>? addressTakenFields = null)
    {
        var baselineMethods = baseline.Methods.ToDictionary(method => method.StableId,
            StringComparer.OrdinalIgnoreCase);
        var currentMethods = current.Methods.ToDictionary(method => method.StableId,
            StringComparer.OrdinalIgnoreCase);
        var baselineTypes = baseline.Types.ToDictionary(type => type.StableId,
            StringComparer.OrdinalIgnoreCase);
        var currentTypes = current.Types.ToDictionary(type => type.StableId,
            StringComparer.OrdinalIgnoreCase);
        var baselineFields = baseline.Fields.ToDictionary(field => field.StableId,
            StringComparer.OrdinalIgnoreCase);
        var currentFields = current.Fields.ToDictionary(field => field.StableId,
            StringComparer.OrdinalIgnoreCase);
        var unsupported = new List<string>();
        if (!string.Equals(baseline.AssemblyName, current.AssemblyName,
                StringComparison.Ordinal))
            unsupported.Add("assembly-name-change:" + baseline.AssemblyName + "->" +
                current.AssemblyName);
        if (!string.Equals(baseline.AssemblyMetadataVersion,
                current.AssemblyMetadataVersion, StringComparison.OrdinalIgnoreCase))
            unsupported.Add("assembly-or-module-metadata-change:" + baseline.AssemblyName);

        MetaVersionMethod[] changed = baseline.Methods.Where(method =>
            currentMethods.TryGetValue(method.StableId, out MetaVersionMethod? currentMethod) &&
            !string.Equals(method.Version, currentMethod.Version, StringComparison.OrdinalIgnoreCase)).ToArray();
        MetaVersionMethod[] removed = baseline.Methods.Where(method =>
            !currentMethods.ContainsKey(method.StableId)).ToArray();
        MetaVersionMethod[] added = current.Methods.Where(method =>
            !baselineMethods.ContainsKey(method.StableId)).ToArray();

        foreach (MetaVersionMethod method in changed)
        {
            MetaVersionMethod currentMethod = currentMethods[method.StableId];
            if (!string.Equals(method.NonCustomMetadataVersion,
                    currentMethod.NonCustomMetadataVersion,
                    StringComparison.OrdinalIgnoreCase))
                unsupported.Add("existing-method-metadata-change:" + method.Identity);
        }
		// A removed Base method keeps its native symbol for binary compatibility,
		// but its universal guard resolves to a MissingMethodException tombstone.
		// A same-name added method is therefore also a safe signature replacement.

        foreach (MetaVersionField field in baseline.Fields)
        {
            if (baselineTypes.TryGetValue(field.DeclaringTypeStableId,
                    out MetaVersionType? baselineDeclaringType) &&
                baselineDeclaringType.IsPrivateImplementationDetails)
                continue;
			if (!currentTypes.ContainsKey(field.DeclaringTypeStableId))
				continue;
            if (!currentFields.TryGetValue(field.StableId, out MetaVersionField? currentField))
			{
				if (!field.IsStatic && field.DeclaringTypeIsValueType)
					unsupported.Add("removed-instance-field-on-existing-value-type:" + field.Identity);
			}
			else if (!string.Equals(field.NonCustomMetadataVersion,
					 currentField.NonCustomMetadataVersion,
                         StringComparison.OrdinalIgnoreCase))
                unsupported.Add("existing-field-metadata-change:" + field.Identity);
        }
        var allAddressTakenFields = new HashSet<string>(addressTakenFields ??
            current.AddressTakenFieldIdentities, StringComparer.Ordinal);
        foreach (MetaVersionField field in current.Fields.Where(field =>
                     !baselineFields.ContainsKey(field.StableId) &&
                     baselineTypes.ContainsKey(field.DeclaringTypeStableId)))
        {
			if (baselineTypes[field.DeclaringTypeStableId].IsPrivateImplementationDetails)
                continue;
			if (!field.IsStatic && allAddressTakenFields.Contains(field.Identity))
				unsupported.Add("added-instance-field-address-taken:" + field.Identity);
			else if (!field.IsStatic && !IsSupportedInstanceFieldAddition(field))
				unsupported.Add(UnsupportedInstanceFieldReason(field) + ":" + field.Identity);
			else if (field.IsStatic && (field.IsThreadStatic || field.DeclaringTypeIsGeneric || field.HasRva))
                unsupported.Add("added-threadstatic-generic-or-rva-field-on-existing-type:" +
                    field.Identity);
        }

        foreach (MetaVersionMethod method in added)
        {
            if (!baselineTypes.TryGetValue(method.DeclaringTypeStableId, out MetaVersionType? declaringType))
                continue;
			if (method.HasCustomAttributes)
				unsupported.Add("added-method-custom-attributes-on-existing-type:" + method.Identity);
            if (declaringType.IsInterface || method.DeclaringTypeIsInterface)
                unsupported.Add("added-method-on-existing-interface:" + method.Identity);
            else if (method.IsVirtual || (method.Flags & (2u | 4u)) != 0)
                unsupported.Add("added-virtual-abstract-or-pinvoke-method-on-existing-type:" + method.Identity);
        }

        MetaVersionType[] changedTypes = baseline.Types.Where(type =>
            currentTypes.TryGetValue(type.StableId, out MetaVersionType? currentType) &&
            !string.Equals(type.Version, currentType.Version, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (MetaVersionType type in changedTypes)
        {
            MetaVersionType currentType = currentTypes[type.StableId];
			if (!string.Equals(type.LayoutVersion, currentType.LayoutVersion,
					StringComparison.OrdinalIgnoreCase) &&
				(!string.Equals(type.NonFieldLayoutVersion, currentType.NonFieldLayoutVersion,
					 StringComparison.OrdinalIgnoreCase) ||
				 !HasOnlySupportedInstanceFieldEvolution(type, baselineFields, currentFields)))
                unsupported.Add("existing-type-layout-or-vtable-change:" + type.Identity);
			if (!string.Equals(type.NonCustomUnsupportedDeclarativeVersion,
					currentType.NonCustomUnsupportedDeclarativeVersion,
					StringComparison.OrdinalIgnoreCase))
				unsupported.Add("existing-type-unsupported-declarative-metadata-change:" +
					type.Identity);
            if (!string.Equals(type.StaticFieldVersion, currentType.StaticFieldVersion,
                    StringComparison.OrdinalIgnoreCase) && !type.IsPrivateImplementationDetails &&
				!HasOnlySupportedStaticFieldEvolution(type, baselineFields, currentFields))
                unsupported.Add("existing-type-static-field-change:" + type.Identity);
        }

        MetaVersionType[] removedTypes = baseline.Types.Where(type =>
            !currentTypes.ContainsKey(type.StableId)).ToArray();

        MetaVersionType[] addedTypes = current.Types.Where(type =>
            !baselineTypes.ContainsKey(type.StableId)).ToArray();
		MetaVersionField[] removedFields = baseline.Fields.Where(field =>
			!currentFields.ContainsKey(field.StableId)).ToArray();
        MetaVersionField[] addedFields = current.Fields.Where(field =>
			!baselineFields.ContainsKey(field.StableId)).ToArray();

        var requiredCapabilities = new HashSet<string>(StringComparer.Ordinal)
        {
            "aot-guard-v1",
            "single-current-multibase-v1",
            "atomic-multi-assembly-registration-v1",
        };
        if (addedFields.Any(field => baselineTypes.ContainsKey(field.DeclaringTypeStableId) &&
                !field.IsStatic))
            requiredCapabilities.Add("supplemental-existing-type-instance-fields-v1");
        if (addedFields.Any(field => baselineTypes.ContainsKey(field.DeclaringTypeStableId) &&
                field.IsStatic))
            requiredCapabilities.Add("supplemental-existing-type-static-fields-v1");
        if (added.Any(method => baselineTypes.ContainsKey(method.DeclaringTypeStableId)))
            requiredCapabilities.Add("supplemental-existing-type-methods-v1");
        if (removed.Length != 0)
            requiredCapabilities.Add("removed-existing-type-methods-v1");
        if (removedFields.Any(field => currentTypes.ContainsKey(field.DeclaringTypeStableId)))
            requiredCapabilities.Add("removed-existing-type-fields-v1");
        if (removedTypes.Length != 0)
            requiredCapabilities.Add("removed-types-v1");
        if (addedTypes.Any(type => type.IsNested))
            requiredCapabilities.Add("supplemental-nested-types-v1");
        if (addedTypes.Any(type => !type.IsNested))
            requiredCapabilities.Add("supplemental-top-level-types-v1");
        if (HasSignatureReplacement(removed, added))
            requiredCapabilities.Add("existing-type-method-signature-replacement-v1");
        if (changedTypes.Any(type => currentTypes.TryGetValue(type.StableId,
                out MetaVersionType? currentType) &&
                !string.Equals(type.DeclarativeVersion, currentType.DeclarativeVersion,
                    StringComparison.OrdinalIgnoreCase)))
            requiredCapabilities.Add("logical-existing-type-properties-events-v1");
        if (changed.Any(method => !string.Equals(method.CustomAttributeVersion,
                    currentMethods[method.StableId].CustomAttributeVersion,
                    StringComparison.OrdinalIgnoreCase)) ||
            baseline.Fields.Any(field => currentFields.TryGetValue(field.StableId,
                    out MetaVersionField? currentField) &&
                !string.Equals(field.CustomAttributeVersion, currentField.CustomAttributeVersion,
                    StringComparison.OrdinalIgnoreCase)) ||
            changedTypes.Any(type => !string.Equals(type.CustomAttributeVersion,
                currentTypes[type.StableId].CustomAttributeVersion,
                StringComparison.OrdinalIgnoreCase)))
            requiredCapabilities.Add("logical-existing-member-custom-attributes-v1");

        return new ResourceUpdateCompatibility
        {
            UnchangedMethodCount = baseline.Methods.Length - changed.Length - removed.Length,
            ChangedMethodCount = changed.Length,
			BodyOnlyChangedMethodCount = changed.Count(method =>
				string.Equals(method.MetadataVersion, currentMethods[method.StableId].MetadataVersion,
					StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(method.BodyVersion, currentMethods[method.StableId].BodyVersion,
					StringComparison.OrdinalIgnoreCase) &&
				string.Equals(method.DependencyVersion, currentMethods[method.StableId].DependencyVersion,
					StringComparison.OrdinalIgnoreCase)),
			DependencyChangedMethodCount = changed.Count(method =>
				!string.Equals(method.DependencyVersion, currentMethods[method.StableId].DependencyVersion,
					StringComparison.OrdinalIgnoreCase)),
            RemovedMethodCount = removed.Length,
            AddedMethodCount = added.Length,
			RemovedFieldCount = removedFields.Length,
			AddedFieldCount = addedFields.Length,
            ChangedExistingTypeCount = changedTypes.Length,
            RemovedTypeCount = removedTypes.Length,
            AddedTypeCount = addedTypes.Length,
            GuardRequiredMethods = changed.Concat(removed).Where(MethodCanHaveAotEntry).ToArray(),
            RequiredRuntimeCapabilities = requiredCapabilities.OrderBy(value => value,
                StringComparer.Ordinal).ToArray(),
            UnsupportedChanges = unsupported.Distinct(StringComparer.Ordinal).OrderBy(value => value,
                StringComparer.Ordinal).ToArray(),
        };
    }

    private static bool HasSignatureReplacement(IEnumerable<MetaVersionMethod> removed,
        IEnumerable<MetaVersionMethod> added)
    {
        var removedNames = new HashSet<string>(removed.Select(method =>
            method.DeclaringTypeStableId + "\n" + method.Name), StringComparer.Ordinal);
        return added.Any(method => removedNames.Contains(method.DeclaringTypeStableId + "\n" +
            method.Name));
    }

    private static bool MethodCanHaveAotEntry(MetaVersionMethod method) =>
        (method.Flags & 8u) != 0 && (method.Flags & (2u | 4u)) == 0;

	private static bool HasOnlySupportedStaticFieldEvolution(MetaVersionType type,
        IReadOnlyDictionary<string, MetaVersionField> baselineFields,
        IReadOnlyDictionary<string, MetaVersionField> currentFields)
    {
        MetaVersionField[] before = baselineFields.Values.Where(field =>
            string.Equals(field.DeclaringTypeStableId, type.StableId,
				StringComparison.OrdinalIgnoreCase) && field.IsStatic).ToArray();
        MetaVersionField[] after = currentFields.Values.Where(field =>
            string.Equals(field.DeclaringTypeStableId, type.StableId,
				StringComparison.OrdinalIgnoreCase) && field.IsStatic).ToArray();
		if (before.Any(field => currentFields.TryGetValue(field.StableId,
				out MetaVersionField? current) &&
            !string.Equals(field.NonCustomMetadataVersion,
				current.NonCustomMetadataVersion, StringComparison.OrdinalIgnoreCase)))
            return false;
        return after.Where(field => !baselineFields.ContainsKey(field.StableId)).All(field =>
            field.IsStatic && !field.IsThreadStatic && !field.DeclaringTypeIsGeneric && !field.HasRva);
    }

	private static bool HasOnlySupportedInstanceFieldEvolution(MetaVersionType type,
		IReadOnlyDictionary<string, MetaVersionField> baselineFields,
		IReadOnlyDictionary<string, MetaVersionField> currentFields)
	{
		MetaVersionField[] before = baselineFields.Values.Where(field =>
			string.Equals(field.DeclaringTypeStableId, type.StableId,
				StringComparison.OrdinalIgnoreCase) && !field.IsStatic).ToArray();
		MetaVersionField[] after = currentFields.Values.Where(field =>
			string.Equals(field.DeclaringTypeStableId, type.StableId,
				StringComparison.OrdinalIgnoreCase) && !field.IsStatic).ToArray();
		string[] existingBefore = before.Where(field => currentFields.ContainsKey(field.StableId))
			.OrderBy(field => field.DeclarationIndex)
			.Select(field => field.StableId).ToArray();
		string[] existingAfter = after.Where(field => baselineFields.ContainsKey(field.StableId))
			.OrderBy(field => field.DeclarationIndex).Select(field => field.StableId).ToArray();
		if (!existingBefore.SequenceEqual(existingAfter, StringComparer.OrdinalIgnoreCase))
			return false;
		if (before.Any(field => currentFields.TryGetValue(field.StableId,
				out MetaVersionField? current) &&
			!string.Equals(field.NonCustomMetadataVersion,
				current.NonCustomMetadataVersion, StringComparison.OrdinalIgnoreCase)))
			return false;
		if (type.IsInterface || before.Any(field => field.DeclaringTypeIsValueType) &&
			(after.Length != before.Length || after.Any(field =>
				!baselineFields.ContainsKey(field.StableId))))
			return false;
		return after.Where(field => !baselineFields.ContainsKey(field.StableId))
			.All(IsSupportedInstanceFieldAddition);
	}

	private static bool IsSupportedInstanceFieldAddition(MetaVersionField field) =>
		!field.IsStatic && !field.IsLiteral && !field.IsThreadStatic &&
		!field.DeclaringTypeIsGeneric && !field.DeclaringTypeIsValueType && !field.HasRva &&
		!field.AddressTaken && !field.HasUnsupportedSidecarType;

	private static string UnsupportedInstanceFieldReason(MetaVersionField field)
	{
		if (field.DeclaringTypeIsValueType)
			return "added-instance-field-on-existing-value-type";
		if (field.DeclaringTypeIsGeneric)
			return "added-instance-field-on-existing-generic-type";
		if (field.AddressTaken)
			return "added-instance-field-address-taken";
		if (field.HasUnsupportedSidecarType)
			return "added-instance-field-has-pointer-or-byref-type";
		return "unsupported-added-instance-field-on-existing-type";
	}
}
