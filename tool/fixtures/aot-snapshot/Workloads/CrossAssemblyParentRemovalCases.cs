extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using Processor = model::HybridCLR.Lab.VirtualSignatures.Processor;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;
using IOperations = model::HybridCLR.Lab.VirtualSignatures.IOperations;
using ValueLayout = model::HybridCLR.Lab.ValueLayout;

namespace HybridCLR.Lab.CrossAssemblyParentRemoval
{
    public static class Cases
    {
        public static void RunIfRequested() { if (Array.IndexOf(Environment.GetCommandLineArgs(), "-virtualSignatureProbe") >= 0) Run(); }
        private static bool Absent(Action action) { try { action(); } catch (TypeLoadException) { return true; } return false; }
        public static string[] Run()
        {
            var passed = new List<string>(); int failures = 0;
            void Check(string name, Func<bool> assertion) { try { if (!assertion()) throw new InvalidOperationException("assertion returned false"); passed.Add(name); Console.WriteLine("DHE cross removal check: " + name); } catch (Exception error) { ++failures; Console.WriteLine("DHE cross removal failure: " + name + ": " + error); } }
            var type = typeof(Processor); var assembly = type.Assembly; var other = Assembly.Load("HybridCLR.ValueLayoutOther"); object boxed = new Processor(); var instance = (Processor)boxed;
            Check("current-parent-root", () => type.BaseType == typeof(ProcessorRoot));
            Check("other-parent-absent", () => other.GetType("HybridCLR.Lab.CrossAssemblyParents.CrossParent") == null);
            Check("other-marker-absent", () => other.GetType("HybridCLR.Lab.CrossAssemblyParents.CrossMarker") == null);
            Check("qualified-parent-absent", () => Absent(() => Type.GetType("HybridCLR.Lab.CrossAssemblyParents.CrossParent, HybridCLR.ValueLayoutOther", true)));
            Check("root-contract", () => typeof(ProcessorRoot).IsAssignableFrom(type) && typeof(IOperations).IsAssignableFrom(boxed.GetType()));
            Check("own-fields-retained", () => instance.Bias == 25 && instance.Extra == 1000);
            Check("own-reflection-retained", () => (int)type.GetField("Bias").GetValue(instance) == 25 && (long)type.GetField("Extra").GetValue(instance) == 1000);
            Check("former-members-absent", () => type.GetField("ParentExtra") == null && type.GetMethod("ReadParentExtra") == null && type.GetProperty("ParentProperty") == null && type.GetEvent("ParentEvent") == null);
            Check("root-virtual", () => { var v = ((ProcessorRoot)instance).CopyValue(new Packet { Count = 3, Extra = 90000000003L, Marker = boxed }); return v.Count == 1028 && v.Extra == 90000000020L && ReferenceEquals(v.Marker, boxed); });
            Check("interface-virtual", () => { var v = ((IOperations)instance).CopyValue(new Packet { Count = 7, Extra = 80000000001L, Marker = boxed }); return v.Count == 1032 && v.Extra == 80000000018L && ReferenceEquals(v.Marker, boxed); });
            Check("generic-root", () => ReferenceEquals(((ProcessorRoot)instance).Identity<object>(boxed), boxed));
            Check("root-definition", () => type.GetMethod("CopyValue").GetBaseDefinition().DeclaringType == typeof(ProcessorRoot));
            Check("reflection-construction", () => ((Processor)Activator.CreateInstance(type)).GetType() == type);
            Check("independent-objects", () => { var next = new Processor(); next.Bias = 31; return instance.Bias == 25 && next.Bias == 31; });
            Check("gc-own-data", () => { var weak = new WeakReference(boxed); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return weak.IsAlive && ((Processor)weak.Target).Extra == 1000; });
            Check("unchanged-aot", () => ValueLayout.Factory.UnchangedRevision() == 5 && !RuntimeApi.IsDifferentialMethodChanged(typeof(ValueLayout.Factory).GetMethod("UnchangedRevision")));
            if (failures != 0) throw new InvalidOperationException("Cross removal failures: " + failures);
            Console.WriteLine("DHE cross removal pass: " + passed.Count); return passed.ToArray();
        }
    }
}
