extern alias model;
using System;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;

namespace HybridCLR.Lab.CrossAssemblyParents
{
    public sealed class CrossMarker { public int Value = 1237; }
    public abstract class CrossParent : ProcessorRoot
    {
        public static int ConstructorCalls;
        public static bool ThrowConstruction;
        public int CrossVersion;
        public long ParentExtra;
        public object ParentReference;
        protected CrossParent()
        {
            if (ThrowConstruction) throw new InvalidOperationException("cross-parent-constructor-expected");
            CrossVersion = 311; ParentExtra = 70000000003L; ParentReference = new CrossMarker(); ++ConstructorCalls;
        }
        public long ReadParentExtra() => ParentExtra;
        public long ParentProperty { get => ParentExtra; set => ParentExtra = value; }
        public event Action ParentEvent;
        public void RaiseParentEvent() => ParentEvent?.Invoke();
        public static int ParentStaticProperty => 311;
        private int PrivateParentProperty => -1;
    }
}
