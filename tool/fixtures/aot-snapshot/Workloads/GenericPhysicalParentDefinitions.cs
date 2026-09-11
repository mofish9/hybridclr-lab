extern alias model;
using System;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;

namespace HybridCLR.Lab.GenericPhysicalParents
{
    public sealed class ParentMarker { public int Value = 1927; }
    public abstract class GenericParent<T> : ProcessorRoot
    {
        public T GenericValue;
        public long ParentExtra;
        public object ParentReference;
        public static int ConstructorCalls;
        public static int StaticMarker;
        public static bool ThrowConstruction;
        protected GenericParent()
        {
            if (ThrowConstruction) throw new InvalidOperationException("generic-parent-constructor-expected");
            ParentExtra = 70000000003L;
            ParentReference = new ParentMarker();
            ++ConstructorCalls;
        }
        public T ParentProperty { get => GenericValue; set => GenericValue = value; }
        public T ReadGenericValue() => GenericValue;
        public virtual T EchoParent(T value) => value;
        public event Action ParentEvent;
        public void RaiseParentEvent() => ParentEvent?.Invoke();
    }
}
