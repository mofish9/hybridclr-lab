extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;
using Processor = model::HybridCLR.Lab.VirtualSignatures.Processor;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;

namespace HybridCLR.Lab.GenericPhysicalParents
{
    public static class OwnerCases
    {
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-virtualSignatureProbe") >= 0) Run();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference InstallMarker(GenericMiddle<Packet> value)
        {
            var marker = new ParentMarker();
            value.MiddleValue = new Packet { Count = 83, Extra = 90000000083L, Marker = marker };
            value.MiddleArray = new[] { value.MiddleValue };
            return new WeakReference(marker);
        }
        public static string[] Run()
        {
            var checks = new List<string>();
            void Check(string name, bool value)
            {
                if (!value) throw new InvalidOperationException("Generic owner failed: " + name);
                checks.Add(name); Console.WriteLine("DHE generic owner check: " + name);
            }
            var open = typeof(GenericParent<>).BaseType;
            Check("open-owner-parent", open.GetGenericTypeDefinition() == typeof(GenericMiddle<>) &&
                open.GetGenericArguments()[0].IsGenericParameter && open.GetGenericArguments()[0].GenericParameterPosition == 0);
            void Instance<T>(string name, T first, T second)
            {
                int before = GenericMiddle<T>.MiddleConstructors;
                var child = new GenericChild<T>(); var parent = (GenericParent<T>)child;
                var middle = (GenericMiddle<T>)(object)child;
                var equal = EqualityComparer<T>.Default;
                Check(name + ":parent", typeof(GenericParent<T>).BaseType == typeof(GenericMiddle<T>) &&
                    typeof(GenericMiddle<T>).IsAssignableFrom(child.GetType()));
                Check(name + ":constructor", GenericMiddle<T>.MiddleConstructors == before + 1 &&
                    middle.MiddleStamp == 90000000079L && ((ParentMarker)middle.MiddleReference).Value == 1927);
                middle.MiddleValue = first; parent.GenericValue = second; child.OwnValue = first;
                middle.MiddleArray = new[] { first, second };
                Check(name + ":three-level-fields", equal.Equals(middle.MiddleValue, first) &&
                    equal.Equals(parent.GenericValue, second) && equal.Equals(child.OwnValue, first) && equal.Equals(middle.MiddleArray[1], second));
                Check(name + ":generic-methods", equal.Equals(middle.MiddleEcho(first), first) &&
                    equal.Equals(middle.MiddleIdentity<T>(second), second) && equal.Equals(parent.ReadGenericValue(), second));
                var field = child.GetType().GetField("MiddleValue");
                field.SetValue(child, second);
                Check(name + ":field-reflection", field.DeclaringType == typeof(GenericMiddle<T>) && field.FieldType == typeof(T) &&
                    equal.Equals((T)field.GetValue(child), second) && equal.Equals(middle.MiddleValue, second));
                var property = child.GetType().GetProperty("MiddleProperty"); property.SetValue(child, first, null);
                Check(name + ":property-reflection", property.DeclaringType == typeof(GenericMiddle<T>) && equal.Equals((T)property.GetValue(child, null), first));
                var method = child.GetType().GetMethod("MiddleEcho");
                Check(name + ":method-reflection", method.DeclaringType == typeof(GenericMiddle<T>) && equal.Equals((T)method.Invoke(child, new object[] { second }), second));
                Check(name + ":root-dispatch", equal.Equals(((ProcessorRoot)child).Identity<T>(first), first));
                bool threw = false; try { ((ProcessorRoot)child).Fail(); } catch (InvalidOperationException error) { threw = error.Message == "generic-child-expected"; }
                Check(name + ":exception", threw);
            }
            var marker = new object();
            Instance("value", new Packet { Count = 3, Extra = 90000000003L, Marker = marker }, new Packet { Count = 7, Extra = 90000000007L, Marker = marker });
            Instance("long", 90000000031L, 90000000037L);
            Instance("reference", new object(), new object());
            Check("static-isolation", GenericMiddle<Packet>.MiddleConstructors > 0 && GenericMiddle<long>.MiddleConstructors == 1 &&
                GenericMiddle<object>.MiddleConstructors == 1);
            var physical = new Processor(); var physicalMiddle = (GenericMiddle<Packet>)(object)physical;
            var weak = InstallMarker(physicalMiddle);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check("physical-descendant-gc", weak.IsAlive && ReferenceEquals(weak.Target, physicalMiddle.MiddleValue.Marker) &&
                ReferenceEquals(weak.Target, physicalMiddle.MiddleArray[0].Marker) && physicalMiddle.MiddleValue.Count == 83 &&
                physicalMiddle.MiddleValue.Extra == 90000000083L && physical.Bias == 25 && physical.Extra == 1000L);
            Check("physical-descendant-parent-chain", physical.GetType().BaseType == typeof(GenericParent<Packet>) &&
                physical.GetType().BaseType.BaseType == typeof(GenericMiddle<Packet>) && typeof(GenericMiddle<Packet>).BaseType == typeof(ProcessorRoot));
            Console.WriteLine("DHE generic owner pass: " + checks.Count); return checks.ToArray();
        }
    }
}
