namespace HybridCLR.DheTool;

internal sealed class ResourceUpdateCompatibility
{
	public const string Policy = "dhe-proven-safe-subset-v1";
	public const string RuntimeProtocol = "dhe-runtime-protocol-v1";
    public const string CurrentNativeRuntimeContract = "dhe-runtime-v18";
    public static readonly string[] KnownRuntimeCapabilities =
    {
		"aot-guard-v1",
		"stable-method-identity-v1",
		"single-current-multibase-v1",
		"resource-update-plan-integrity-v1",
		"resource-update-aot-metadata-path-v1",
		"resource-update-aot-metadata-set-selection-v1",
		"atomic-multi-assembly-registration-v1",
		"supplemental-existing-type-instance-fields-v1",
        "supplemental-existing-type-static-fields-v1",
        "supplemental-existing-generic-type-fields-v1",
        "supplemental-instance-field-addresses-v1",
        "existing-interface-method-slots-v1",
        "cross-assembly-interface-declarations-v1",
        "inherited-interface-dispatch-v1",
		"supplemental-existing-type-methods-v1",
		"removed-existing-type-methods-v1",
		"existing-type-method-signature-replacement-v1",
		"removed-existing-type-fields-v1",
		"removed-types-v1",
		"logical-existing-type-properties-events-v1",
		"logical-existing-member-custom-attributes-v1",
        "supplemental-method-custom-attributes-v1",
        "assembly-reference-evolution-v1",
        "supplemental-type-base-references-v1",
        "supplemental-type-declarations-v1",
        "supplemental-method-generic-invocation-v1",
        "supplemental-generic-unresolved-stubs-v1",
        "homologous-attribute-constructors-v1",
        "logical-attribute-members-v1",
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
        MetaVersionSnapshot current, IEnumerable<string>? addressTakenFields = null,
        bool usesUnresolvedCallStubs = true, IEnumerable<MetaVersionSnapshot>? currentAssemblySet = null)
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
        if (!string.Equals(baseline.AssemblyNonReferenceMetadataVersion,
                current.AssemblyNonReferenceMetadataVersion, StringComparison.OrdinalIgnoreCase))
            unsupported.Add("assembly-or-module-metadata-change:" + baseline.AssemblyName);
        foreach (var reference in baseline.AssemblyReferences)
        {
            if (current.AssemblyReferences.TryGetValue(reference.Key, out string? currentIdentity) &&
                !string.Equals(reference.Value, currentIdentity, StringComparison.Ordinal))
                unsupported.Add("existing-assembly-reference-identity-change:" + reference.Key);
        }
        // Full type names alone do not distinguish two assemblies defining the
        // same type. Do not let reference evolution silently retarget old AOT IL.
        foreach (var type in baseline.TypeReferenceScopes)
        {
            if (current.TypeReferenceScopes.TryGetValue(type.Key, out string? currentScope) &&
                !string.Equals(type.Value, currentScope, StringComparison.Ordinal))
                unsupported.Add("existing-type-reference-scope-change:" + type.Key);
        }

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
			if (!field.IsStatic && !IsSupportedInstanceFieldAddition(field))
				unsupported.Add(UnsupportedInstanceFieldReason(field) + ":" + field.Identity);
            else if (field.IsStatic && (field.IsThreadStatic || field.HasRva))
                unsupported.Add("added-threadstatic-or-rva-field-on-existing-type:" +
                    field.Identity);
        }

        var interfaceImplementations = new HashSet<string>(current.InterfaceImplementationMethodIdentities,
            StringComparer.Ordinal);
        bool requiresInterfaceSlots = false;
        foreach (MetaVersionMethod method in added)
        {
            if (!baselineTypes.TryGetValue(method.DeclaringTypeStableId, out MetaVersionType? declaringType))
                continue;
            if (declaringType.IsInterface || method.DeclaringTypeIsInterface)
            {
                if (!method.IsStatic && !method.IsPInvoke && method.IsAbstract)
                    requiresInterfaceSlots = true;
                else
                    unsupported.Add("added-method-on-existing-interface:" + method.Identity);
            }
            else if (interfaceImplementations.Contains(method.Identity))
                requiresInterfaceSlots = true;
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
            "stable-method-identity-v1",
            "single-current-multibase-v1",
            "atomic-multi-assembly-registration-v1",
        };
        if (requiresInterfaceSlots)
            requiredCapabilities.Add("existing-interface-method-slots-v1");
        // A Current MemberRef declaration can name an interface method absent
        // from the Base definition table. Older slot-only runtimes cannot
        // resolve that declaration during atomic multi-image registration.
        var evolvedInterfaces = added.Where(method => method.DeclaringTypeIsInterface &&
            baselineTypes.ContainsKey(method.DeclaringTypeStableId)).Select(method => method.DeclaringType)
            .ToHashSet(StringComparer.Ordinal);
        if (evolvedInterfaces.Count != 0 && (currentAssemblySet == null || currentAssemblySet.Any(assembly =>
                assembly.AssemblyName != current.AssemblyName && evolvedInterfaces.Any(name =>
                    assembly.TypeReferenceScopes.TryGetValue(name, out string? scope) &&
                    scope.Split('\n').Any(identity => identity.Split(',')[0] == current.AssemblyName)))))
            requiredCapabilities.Add("cross-assembly-interface-declarations-v1");
        var interfaceOwners = current.Types.Where(type => !type.IsInterface &&
            type.LocalDeclarationReferencedTypeNames.Any(evolvedInterfaces.Contains))
            .Select(type => type.Identity).ToHashSet(StringComparer.Ordinal);
        if (evolvedInterfaces.Count != 0 && (currentAssemblySet == null || currentAssemblySet.Any(assembly =>
                assembly.TypeParents.Values.Any(parent => parent.AssemblyName == current.AssemblyName &&
                    interfaceOwners.Contains(parent.TypeName)))))
            requiredCapabilities.Add("inherited-interface-dispatch-v1");
        if (!new HashSet<string>(baseline.AssemblyReferences.Values, StringComparer.Ordinal)
                .SetEquals(current.AssemblyReferences.Values))
            requiredCapabilities.Add("assembly-reference-evolution-v1");
        if (added.Any(method => method.HasCustomAttributes &&
                baselineTypes.ContainsKey(method.DeclaringTypeStableId)))
            requiredCapabilities.Add("supplemental-method-custom-attributes-v1");
        if (added.Any(method => (method.GenericParameterCount != 0 || method.DeclaringTypeGenericParameterCount != 0) &&
                baselineTypes.ContainsKey(method.DeclaringTypeStableId)))
        {
            requiredCapabilities.Add("supplemental-method-generic-invocation-v1");
            if (usesUnresolvedCallStubs)
                requiredCapabilities.Add("supplemental-generic-unresolved-stubs-v1");
        }
        if (addedFields.Any(field => baselineTypes.ContainsKey(field.DeclaringTypeStableId) &&
                !field.IsStatic))
            requiredCapabilities.Add("supplemental-existing-type-instance-fields-v1");
        if (addedFields.Any(field => baselineTypes.ContainsKey(field.DeclaringTypeStableId) &&
                !field.IsStatic && allAddressTakenFields.Contains(field.Identity)))
            requiredCapabilities.Add("supplemental-instance-field-addresses-v1");
        if (addedFields.Any(field => baselineTypes.ContainsKey(field.DeclaringTypeStableId) &&
                field.IsStatic))
            requiredCapabilities.Add("supplemental-existing-type-static-fields-v1");
        if (addedFields.Any(field => field.DeclaringTypeIsGeneric &&
                baselineTypes.ContainsKey(field.DeclaringTypeStableId)) ||
            removedFields.Any(field => field.DeclaringTypeIsGeneric &&
                currentTypes.ContainsKey(field.DeclaringTypeStableId)))
            requiredCapabilities.Add("supplemental-existing-generic-type-fields-v1");
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
        var baseTypeNames = new HashSet<string>(baseline.Types.Select(type => type.Identity), StringComparer.Ordinal);
        if (current.LocalAttributeConstructorTypeNames.Any(baseTypeNames.Contains))
            requiredCapabilities.Add("homologous-attribute-constructors-v1");
        if (RequiresLogicalAttributeMetadata(baseline, currentAssemblySet ?? new[] { current }))
            requiredCapabilities.Add("logical-attribute-members-v1");
        if (addedTypes.Any(type => type.LocalReferencedTypeNames.Any(baseTypeNames.Contains)))
            requiredCapabilities.Add("supplemental-type-base-references-v1");
        if (addedTypes.Any(type => type.LocalDeclarationReferencedTypeNames.Any(baseTypeNames.Contains)))
            requiredCapabilities.Add("supplemental-type-declarations-v1");
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

    private static bool RequiresLogicalAttributeMetadata(MetaVersionSnapshot baseline,
        IEnumerable<MetaVersionSnapshot> currentAssemblySet)
    {
        var snapshots = currentAssemblySet.ToDictionary(snapshot => snapshot.AssemblyName, StringComparer.OrdinalIgnoreCase);
        var baseTypes = baseline.Types.Select(type => type.Identity).ToHashSet(StringComparer.Ordinal);
        var baseMethods = baseline.Methods.Select(method => method.Identity).ToHashSet(StringComparer.Ordinal);
        foreach (MetaVersionAttributeUse use in snapshots.Values.SelectMany(snapshot => snapshot.AttributeUses))
        {
            if (string.Equals(use.AssemblyName, baseline.AssemblyName, StringComparison.OrdinalIgnoreCase) &&
                baseTypes.Contains(use.TypeName) && !baseMethods.Contains(use.ConstructorIdentity))
                return true;
            if (!use.HasNamedProperties) continue;
            var type = new MetaVersionTypeReference(use.AssemblyName, use.TypeName);
            var visited = new HashSet<MetaVersionTypeReference>();
            while (visited.Add(type))
            {
                if (string.Equals(type.AssemblyName, baseline.AssemblyName, StringComparison.OrdinalIgnoreCase) &&
                    baseTypes.Contains(type.TypeName)) return true;
                if (!snapshots.TryGetValue(type.AssemblyName, out MetaVersionSnapshot? owner) ||
                    !owner.TypeParents.TryGetValue(type.TypeName, out MetaVersionTypeReference? parent)) break;
                type = parent;
            }
        }
        return false;
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
            field.IsStatic && !field.IsThreadStatic && !field.HasRva);
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
		!field.DeclaringTypeIsValueType && !field.HasRva &&
		!field.HasUnsupportedSidecarType;

	private static string UnsupportedInstanceFieldReason(MetaVersionField field)
	{
		if (field.DeclaringTypeIsValueType)
			return "added-instance-field-on-existing-value-type";
		if (field.HasUnsupportedSidecarType)
			return "added-instance-field-has-pointer-or-byref-type";
		return "unsupported-added-instance-field-on-existing-type";
	}
}
