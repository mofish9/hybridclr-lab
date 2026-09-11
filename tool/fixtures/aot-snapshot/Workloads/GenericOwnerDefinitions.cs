extern alias model;
using System;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;
using ReferenceValue = model::HybridCLR.Lab.VirtualSignatures.ReferenceValue;

namespace HybridCLR.Lab.GenericPhysicalParents
{
    public abstract class GenericMiddle<T> : ProcessorRoot
    {
        public T MiddleValue;
        public T[] MiddleArray;
        public long MiddleStamp = 90000000079L;
        public object MiddleReference = new ParentMarker();
        public static int MiddleConstructors;
        protected GenericMiddle() { ++MiddleConstructors; }
        public T MiddleProperty { get => MiddleValue; set => MiddleValue = value; }
        public virtual T MiddleEcho(T value) => value;
        public U MiddleIdentity<U>(U value) => value;
    }

    public sealed class GenericChild<T> : GenericParent<T>
    {
        public T OwnValue;
        public override ReferenceValue CopyReference(ReferenceValue value) => value;
        public override Packet CopyValue(Packet value) => value;
        public override void ChangeValue(ref Packet value) { ++value.Count; }
        public override U Identity<U>(U value) => value;
        public override void Fail() { throw new InvalidOperationException("generic-child-expected"); }
    }
}
