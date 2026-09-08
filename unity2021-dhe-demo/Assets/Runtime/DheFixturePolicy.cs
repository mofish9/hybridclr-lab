using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HybridCLR.Lab
{
    public static class DheFixturePolicy
    {
        public const string Calculator = "HybridCLR.Lab.ManagedCasesAot.DheDemoCalculator";
        public const string StructuralType = "HybridCLR.Lab.ManagedCasesAot.DheAddedReferenceType";
        public const string StructuralEntry = Calculator + "::ExerciseCurrentMembers|System.Int32 (System.Int32)";

        public static readonly LegacyProbe[] LegacyProbes =
        {
            new LegacyProbe("structuralRemovedMethodGuardValidated", "DHE_PROBE_REMOVED_METHOD",
                Calculator + "::RemovedLegacyMethod|System.Int32 (System.Int32)"),
            new LegacyProbe("structuralRemovedFieldGuardValidated", "DHE_PROBE_REMOVED_FIELD",
                Calculator + "::ReadRemovedFields|System.Int32 ()"),
            new LegacyProbe("structuralRemovedPropertyGuardValidated", "DHE_PROBE_REMOVED_PROPERTY",
                Calculator + "::get_RemovedProperty|System.Int32 ()"),
            new LegacyProbe("structuralReplacedPropertyGuardValidated", "DHE_PROBE_REPLACED_PROPERTY",
                Calculator + "::get_EvolvedProperty|System.Int32 ()"),
            new LegacyProbe("structuralRemovedEventGuardValidated", "DHE_PROBE_REMOVED_EVENT",
                Calculator + "::add_RemovedEvent|System.Void (System.Action`1<System.Int32>)"),
            new LegacyProbe("structuralReplacedEventGuardValidated", "DHE_PROBE_REPLACED_EVENT",
                Calculator + "::add_EvolvedEvent|System.Void (System.Action`1<System.Int32>)"),
            new LegacyProbe("structuralRemovedTypeGuardValidated", "DHE_PROBE_REMOVED_TYPE",
                "HybridCLR.Lab.ManagedCasesAot.DheRemovedReferenceType::.ctor|System.Void (System.Int32)"),
            new LegacyProbe("structuralOldSignatureGuardValidated", "DHE_PROBE_OLD_SIGNATURE",
                Calculator + "::SignatureMigrated|System.Int32 (System.Int32)"),
        };

        public static string TypeId(string identity) => Hash("dhe-type-id\n" + identity);
        public static string MethodId(string identity) => Hash("dhe-method-id\n" + identity);

        public static bool HasStructuralFixture(DheFixtureMetaVersion current) =>
            current.types.ContainsKey(TypeId(StructuralType));

        public static bool MethodChanged(DheFixtureMetaVersion baseline,
            DheFixtureMetaVersion current, string identity)
        {
            string id = MethodId(identity);
            if (!current.methods.TryGetValue(id, out var version))
                throw new InvalidDataException("Expected fixture method is absent from Current MV: " + identity);
            return !baseline.methods.TryGetValue(id, out var original) ||
                !string.Equals(original, version, StringComparison.OrdinalIgnoreCase);
        }

        public static LegacyProbeResult[] RunLegacyProbes(DheFixtureMetaVersion baseline,
            DheFixtureMetaVersion current, IReadOnlyDictionary<string, Action> callbacks)
        {
            return LegacyProbes.Select(probe =>
            {
                bool presentInBase = baseline.methods.ContainsKey(probe.StableId);
                bool presentInCurrent = current.methods.ContainsKey(probe.StableId);
                var result = new LegacyProbeResult
                {
                    check = probe.Check,
                    methodStableId = probe.StableId,
                    applicable = presentInBase && !presentInCurrent,
                    reason = !presentInBase ? "absent-from-base" :
                        presentInCurrent ? "retained-in-current" : "removed-from-base",
                };
                if (!result.applicable) return result;
                if (!callbacks.TryGetValue(probe.Check, out var callback))
                {
                    result.error = "Applicable Base AOT probe was not compiled.";
                    return result;
                }
                result.executed = true;
                try
                {
                    callback();
                    result.error = "Removed Base method did not throw MissingMethodException.";
                }
                catch (MissingMethodException)
                {
                    result.passed = true;
                }
                catch (Exception exception)
                {
                    result.error = exception.ToString();
                }
                return result;
            }).ToArray();
        }

        public static EntryDispatchEvidence RecordEntryDispatch(DheFixtureMetaVersion baseline,
            DheFixtureMetaVersion current, string identity, bool nativeChanged, int interpreterEntries)
        {
            var result = new EntryDispatchEvidence
            {
                methodIdentity = identity,
                methodStableId = MethodId(identity),
                presentInBase = baseline.methods.ContainsKey(MethodId(identity)),
                expectedChanged = MethodChanged(baseline, current, identity),
                nativeChanged = nativeChanged,
                executed = true,
                interpreterEntries = interpreterEntries,
            };
            ValidateEntryDispatch(baseline, current, result);
            return result;
        }

        public static void ValidateEntryDispatch(DheFixtureMetaVersion baseline,
            DheFixtureMetaVersion current, EntryDispatchEvidence evidence)
        {
            if (evidence == null || evidence.methodIdentity != StructuralEntry ||
                evidence.methodStableId != MethodId(evidence.methodIdentity) || !evidence.executed ||
                evidence.interpreterEntries < 0 ||
                evidence.presentInBase != baseline.methods.ContainsKey(evidence.methodStableId) ||
                evidence.expectedChanged != MethodChanged(baseline, current, evidence.methodIdentity) ||
                (evidence.presentInBase && evidence.nativeChanged != evidence.expectedChanged) ||
                (evidence.presentInBase && evidence.expectedChanged && evidence.interpreterEntries == 0))
                throw new InvalidDataException("Executed structural entry disagrees with Base/Current MV or dispatch counters.");
        }

        [Serializable]
        public sealed class EntryDispatchEvidence
        {
            public string methodIdentity = string.Empty;
            public string methodStableId = string.Empty;
            public bool presentInBase;
            public bool expectedChanged;
            public bool nativeChanged;
            public bool executed;
            public int interpreterEntries;

            public bool ChangedBaseEntryExecuted => presentInBase && expectedChanged &&
                nativeChanged && executed && interpreterEntries > 0;
        }

        public static void ValidateLegacyEvidence(DheFixtureMetaVersion baseline,
            DheFixtureMetaVersion current, LegacyProbeResult[] evidence)
        {
            if (evidence == null || evidence.Length != LegacyProbes.Length ||
                evidence.Any(item => item == null) ||
                evidence.Select(item => item.check).Distinct(StringComparer.Ordinal).Count() != evidence.Length)
                throw new InvalidDataException("Legacy probe evidence is incomplete or duplicated.");
            foreach (LegacyProbe probe in LegacyProbes)
            {
                var item = evidence.SingleOrDefault(value => value.check == probe.Check);
                bool presentInBase = baseline.methods.ContainsKey(probe.StableId);
                bool presentInCurrent = current.methods.ContainsKey(probe.StableId);
                bool applicable = presentInBase && !presentInCurrent;
                string reason = !presentInBase ? "absent-from-base" :
                    presentInCurrent ? "retained-in-current" : "removed-from-base";
                if (item == null || !string.Equals(item.methodStableId, probe.StableId, StringComparison.OrdinalIgnoreCase) ||
                    item.applicable != applicable || item.executed != applicable || item.passed != applicable ||
                    item.reason != reason || !string.IsNullOrEmpty(item.error))
                    throw new InvalidDataException("Legacy probe evidence disagrees with Base/Current MV: " + probe.Check);
            }
        }

        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        public sealed class LegacyProbe
        {
            public readonly string Check;
            public readonly string Define;
            public readonly string Identity;
            public readonly string StableId;

            public LegacyProbe(string check, string define, string identity)
            {
                Check = check;
                Define = define;
                Identity = identity;
                StableId = MethodId(identity);
            }
        }

        [Serializable]
        public sealed class LegacyProbeResult
        {
            public string check = string.Empty;
            public string methodStableId = string.Empty;
            public bool applicable;
            public bool executed;
            public bool passed;
            public string reason = string.Empty;
            public string error = string.Empty;
        }
    }

    public sealed class DheFixtureMetaVersion
    {
        public byte[] assemblyHash = Array.Empty<byte>();
        public Dictionary<string, string> types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> methods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static DheFixtureMetaVersion Read(byte[] bytes, string expectedAssemblyName)
        {
            if (bytes == null || bytes.Length < 60 ||
                Encoding.ASCII.GetString(bytes, 0, 8) != "DHEMETA1" || BitConverter.ToUInt32(bytes, 8) != 1)
                throw new InvalidDataException("Invalid fixture MV header.");
            int nameSize = checked((int)BitConverter.ToUInt32(bytes, 16));
            int typeCount = checked((int)BitConverter.ToUInt32(bytes, 20));
            int methodCount = checked((int)BitConverter.ToUInt32(bytes, 24));
            if (nameSize <= 0 || 60L + nameSize + 72L * typeCount + 104L * methodCount != bytes.Length)
                throw new InvalidDataException("Invalid fixture MV size.");
            if (Encoding.UTF8.GetString(bytes, 60, nameSize) != expectedAssemblyName)
                throw new InvalidDataException("Fixture MV assembly identity mismatch.");
            string Digest(int offset) => BitConverter.ToString(bytes, offset, 32).Replace("-", string.Empty);
            var result = new DheFixtureMetaVersion
            {
                assemblyHash = bytes.Skip(28).Take(32).ToArray(),
                types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                methods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };
            for (int index = 0; index < typeCount; index++)
            {
                int offset = checked(60 + nameSize + 72 * index);
                result.types.Add(Digest(offset), Digest(offset + 32));
            }
            for (int index = 0; index < methodCount; index++)
            {
                int offset = checked(60 + nameSize + 72 * typeCount + 104 * index);
                result.methods.Add(Digest(offset), Digest(offset + 32));
            }
            return result;
        }
    }
}
