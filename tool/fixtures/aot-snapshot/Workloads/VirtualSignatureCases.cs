using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace HybridCLR.Lab.VirtualSignatures
{
    public sealed class ReferenceValue
    {
        public int Value;
#if !VIRTUAL_SIGNATURE_BASE
        public long Extra;
#endif
    }

    public struct Packet
    {
        public int Count;
        public object Marker;
#if !VIRTUAL_SIGNATURE_BASE
        public long Extra;
        public ReferenceValue Anchor;
#endif
    }

    public interface IOperations
    {
        ReferenceValue CopyReference(ReferenceValue value);
        Packet CopyValue(Packet value);
        void ChangeValue(ref Packet value);
        T Identity<T>(T value);
        void Fail();
    }

    public interface IPacketOperation<T> { T Copy(T value); }

    public abstract class ProcessorRoot
    {
        public abstract ReferenceValue CopyReference(ReferenceValue value);
        public abstract Packet CopyValue(Packet value);
        public abstract void ChangeValue(ref Packet value);
        public abstract T Identity<T>(T value);
        public abstract void Fail();
    }

    public sealed class Processor : ProcessorRoot, IOperations, IPacketOperation<Packet>
    {
        public int Bias = 25;
#if !VIRTUAL_SIGNATURE_BASE
        public long Extra = 1000;
#endif
        private int Delta()
        {
#if VIRTUAL_SIGNATURE_BASE
            return Bias;
#else
            return Bias + checked((int)Extra);
#endif
        }
        public override ReferenceValue CopyReference(ReferenceValue value)
        {
            if (Delta() != Cases.ExpectedDelta || value.Value != 17) throw new InvalidOperationException("reference storage");
#if !VIRTUAL_SIGNATURE_BASE
            if (value.Extra != 4000000009L) throw new InvalidOperationException("reference extra storage");
#endif
            return value;
        }
        public override Packet CopyValue(Packet value)
        {
            value.Count += Delta();
#if !VIRTUAL_SIGNATURE_BASE
            value.Extra += 17;
#endif
            return value;
        }
        public override void ChangeValue(ref Packet value) { value = CopyValue(value); }
        public override T Identity<T>(T value)
        {
            if (Delta() != Cases.ExpectedDelta) throw new InvalidOperationException("generic receiver storage");
            return value;
        }
        Packet IPacketOperation<Packet>.Copy(Packet value) { return CopyValue(value); }
        public override void Fail()
        {
            if (Delta() != Cases.ExpectedDelta) throw new InvalidOperationException("exception receiver storage");
            throw new InvalidOperationException("virtual-signature-expected");
        }
    }

    public static class Cases
    {
#if VIRTUAL_SIGNATURE_BASE
        public const int ExpectedDelta = 25;
#else
        public const int ExpectedDelta = 1025;
#endif
        private const long PacketExtra = 90000000001L;
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-virtualSignatureProbe") >= 0) Run();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ReferenceValue InterfaceReference(IOperations op, ReferenceValue value) { return op.CopyReference(value); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ReferenceValue VirtualReference(ProcessorRoot op, ReferenceValue value) { return op.CopyReference(value); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Packet InterfaceValue(IOperations op, Packet value) { return op.CopyValue(value); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Packet VirtualValue(ProcessorRoot op, Packet value) { return op.CopyValue(value); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InterfaceRefValue(IOperations op, ref Packet value) { op.ChangeValue(ref value); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void VirtualRefValue(ProcessorRoot op, ref Packet value) { op.ChangeValue(ref value); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T InterfaceGeneric<T>(IOperations op, T value) { return op.Identity(value); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T VirtualGeneric<T>(ProcessorRoot op, T value) { return op.Identity(value); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Packet ClosedInterface(IPacketOperation<Packet> op, Packet value) { return op.Copy(value); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InterfaceFail(IOperations op) { op.Fail(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void VirtualFail(ProcessorRoot op) { op.Fail(); }

        private static ReferenceValue MakeReference()
        {
            var result = new ReferenceValue { Value = 17 };
#if !VIRTUAL_SIGNATURE_BASE
            result.Extra = 4000000009L;
#endif
            return result;
        }
        private static Packet MakePacket(ReferenceValue anchor, object marker)
        {
            var result = new Packet { Count = 17, Marker = marker };
#if !VIRTUAL_SIGNATURE_BASE
            result.Extra = PacketExtra; result.Anchor = anchor;
#endif
            return result;
        }
        private static bool IsPacket(Packet value, ReferenceValue anchor, object marker, bool changed)
        {
            bool valid = value.Count == 17 + (changed ? ExpectedDelta : 0) && ReferenceEquals(value.Marker, marker);
#if !VIRTUAL_SIGNATURE_BASE
            valid &= value.Extra == PacketExtra + (changed ? 17 : 0) && ReferenceEquals(value.Anchor, anchor);
#endif
            return valid;
        }
        private static bool ExpectedFailure(Action action, bool nullReceiver)
        {
            try { action(); return false; }
            catch (NullReferenceException) { return nullReceiver; }
            catch (InvalidOperationException error) { return !nullReceiver && error.Message == "virtual-signature-expected"; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string[] Run()
        {
            var passed = new List<string>(); int failures = 0;
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    passed.Add(name); Console.WriteLine("DHE virtual signature check: " + name);
                }
                catch (Exception error)
                {
                    ++failures; Console.WriteLine("DHE virtual signature failure: " + name + ": " + error);
                }
            }
            var processor = new Processor(); IOperations op = processor; ProcessorRoot root = processor;
            var reference = MakeReference(); object marker = new object(); var input = MakePacket(reference, marker);
            Check("reference-interface", () => ReferenceEquals(InterfaceReference(op, reference), reference));
            Check("reference-virtual", () => ReferenceEquals(VirtualReference(root, reference), reference));
            Check("value-interface", () => IsPacket(InterfaceValue(op, input), reference, marker, true));
            Check("value-virtual", () => IsPacket(VirtualValue(root, input), reference, marker, true));
            Check("byref-interface", () => { var value = input; InterfaceRefValue(op, ref value); return IsPacket(value, reference, marker, true); });
            Check("byref-virtual", () => { var value = input; VirtualRefValue(root, ref value); return IsPacket(value, reference, marker, true); });
            Check("generic-reference-interface", () => ReferenceEquals(InterfaceGeneric(op, reference), reference));
            Check("generic-value-interface", () => IsPacket(InterfaceGeneric(op, input), reference, marker, false));
            Check("generic-reference-virtual", () => ReferenceEquals(VirtualGeneric(root, reference), reference));
            Check("generic-value-virtual", () => IsPacket(VirtualGeneric(root, input), reference, marker, false));
            Check("closed-generic-interface-value", () => IsPacket(ClosedInterface(processor, input), reference, marker, true));
            Check("delegate-value", () => { Func<Packet, Packet> call = op.CopyValue; return IsPacket(call(input), reference, marker, true); });
            Check("delegate-reference", () => { Func<ReferenceValue, ReferenceValue> call = op.CopyReference; return ReferenceEquals(call(reference), reference); });
            Check("reflection-value", () => IsPacket((Packet)typeof(Processor).GetMethod("CopyValue").Invoke(processor, new object[] { input }), reference, marker, true));
            Check("reflection-byref", () => {
                object[] arguments = { input }; typeof(Processor).GetMethod("ChangeValue").Invoke(processor, arguments);
                return IsPacket((Packet)arguments[0], reference, marker, true);
            });
            Check("reflection-generic-value", () => IsPacket((Packet)typeof(Processor).GetMethod("Identity").MakeGenericMethod(typeof(Packet))
                .Invoke(processor, new object[] { input }), reference, marker, false));
            Check("interface-map-value", () => {
                var map = typeof(Processor).GetInterfaceMap(typeof(IOperations));
                int index = Array.FindIndex(map.InterfaceMethods, method => method.Name == "CopyValue");
                return index >= 0 && map.TargetMethods[index] != null &&
                    IsPacket((Packet)map.TargetMethods[index].Invoke(processor, new object[] { input }), reference, marker, true);
            });
            Check("interface-null", () => ExpectedFailure(() => InterfaceValue(null, input), true));
            Check("virtual-null", () => ExpectedFailure(() => VirtualValue(null, input), true));
            Check("interface-exception", () => ExpectedFailure(() => InterfaceFail(op), false));
            Check("virtual-exception", () => ExpectedFailure(() => VirtualFail(root), false));
            Check("repeated-interface-value", () => {
                for (int index = 0; index < 32; ++index) if (!IsPacket(InterfaceValue(op, input), reference, marker, true)) return false;
                return true;
            });
            Check("concurrent-interface-value", () => {
                var results = new bool[4]; var workers = new Thread[results.Length];
                for (int index = 0; index < workers.Length; ++index)
                {
                    int slot = index; workers[index] = new Thread(() => {
                        try
                        {
                            IOperations local = new Processor(); var anchor = MakeReference(); object token = new object(); var value = MakePacket(anchor, token);
                            bool valid = true;
                            for (int iteration = 0; iteration < 32; ++iteration)
                                valid &= IsPacket(InterfaceValue(local, value), anchor, token, true) &&
                                    IsPacket(InterfaceGeneric(local, value), anchor, token, false);
                            results[slot] = valid;
                        }
                        catch (Exception error) { Console.WriteLine("DHE virtual worker failure: " + error); }
                    }); workers[index].Start();
                }
                foreach (var worker in workers) if (!worker.Join(10000)) return false;
                return Array.TrueForAll(results, value => value);
            });
            Check("input-reference-and-value-preserved", () => IsPacket(input, reference, marker, false) && reference.Value == 17);
            Check("receiver-storage-fields", () => {
                bool valid = processor.Bias == 25;
#if !VIRTUAL_SIGNATURE_BASE
                valid &= processor.Extra == 1000;
#endif
                return valid;
            });
            if (failures != 0) throw new InvalidOperationException("Virtual signature failures: " + failures);
            Console.WriteLine("DHE virtual signature pass: " + passed.Count); return passed.ToArray();
        }
    }
}
