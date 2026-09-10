using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    internal sealed class UnityReferenceCachePlayer
    {
        private Type cachedType;
        private FieldInfo cachedField;
        private int cachedHash, baseDelta;
        private Dictionary<Type, int> keyed;
        private GameObject oldOwner;
        private Component oldObject;

        internal static UnityReferenceCachePlayer Capture(Type type, int delta)
        {
            var state = new UnityReferenceCachePlayer { cachedType = type, cachedField = type.GetField("Value"),
                cachedHash = type.GetHashCode(), baseDelta = delta, keyed = new Dictionary<Type, int> { [type] = 19 } };
            state.oldOwner = new GameObject("DHE pre-selection reference"); state.oldOwner.SetActive(false);
            state.oldObject = state.oldOwner.AddComponent(type); state.cachedField.SetValue(state.oldObject, 17);
            return state;
        }

        internal string[] Verify(out string failure)
        {
            var checks = new List<string>(); var errors = new List<string>();
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    checks.Add(name); Console.WriteLine("DHE reference cache check: " + name);
                }
                catch (Exception error)
                {
                    errors.Add(name + ": " + error); Console.WriteLine("DHE reference cache failure: " + name + ": " + error);
                }
            }
            GameObject freshOwner = null;
            try
            {
                Type freshType = typeof(ValueLayout.Factory).Assembly.GetType(cachedType.FullName, true);
                Check("cached-type-identity-stable", () => ReferenceEquals(cachedType, freshType));
                Check("cached-type-hash-stable", () => cachedType.GetHashCode() == cachedHash && freshType.GetHashCode() == cachedHash);
                Check("cached-type-dictionary-lookup", () => keyed.TryGetValue(freshType, out int value) && value == 19);
                Check("old-object-public-type-stable", () => ReferenceEquals(oldObject.GetType(), cachedType));
                freshOwner = new GameObject("DHE post-selection reference"); freshOwner.SetActive(false);
                Component fresh = freshOwner.AddComponent(cachedType);
                FieldInfo currentField = freshType.GetField("Value"), added = freshType.GetField("Extra");
                Check("cached-type-allocation-has-current-storage", () => {
                    added.SetValue(fresh, 91000000019L); return (long)added.GetValue(fresh) == 91000000019L;
                });
                currentField.SetValue(fresh, 37);
                Check("cached-field-reads-current-object", () => (int)cachedField.GetValue(fresh) == 37);
                Check("cached-field-writes-current-object", () => {
                    cachedField.SetValue(fresh, 41); return (int)currentField.GetValue(fresh) == 41;
                });
                Check("cached-field-retains-old-object-storage", () => (int)cachedField.GetValue(oldObject) == 17);
                Check("current-field-validates-physical-receiver", () => {
                    if (baseDelta == 2) return (int)currentField.GetValue(oldObject) == 17;
                    try { currentField.GetValue(oldObject); return false; }
                    catch (ArgumentException error) { return error.Message.Contains("physical"); }
                });
                Check("rejected-access-preserves-both-objects", () =>
                    (int)cachedField.GetValue(oldObject) == 17 && (int)currentField.GetValue(fresh) == 41);
                Check("current-code-casts-respect-physical-layout", () => {
                    var probe = typeof(ValueLayout.Factory).Assembly.GetType("HybridCLR.Lab.UnityReference.GenericReferenceCases", true);
                    return (bool)probe.GetMethod("CheckPhysicalReceivers").Invoke(null, new object[] { oldObject, fresh, baseDelta == 2 });
                });
            }
            catch (Exception error) { errors.Add(error.ToString()); }
            finally
            {
                if (freshOwner != null) UnityEngine.Object.DestroyImmediate(freshOwner);
                if (oldOwner != null) UnityEngine.Object.DestroyImmediate(oldOwner);
            }
            failure = errors.Count == 0 ? null : string.Join("\n", errors);
            if (failure == null) Console.WriteLine("DHE reference cache pass: " + checks.Count);
            return checks.ToArray();
        }
    }
}
