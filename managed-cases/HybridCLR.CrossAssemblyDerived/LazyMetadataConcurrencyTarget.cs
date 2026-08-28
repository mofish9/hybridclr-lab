using System;

namespace HybridCLR.Lab.CrossAssemblyDerived
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    public sealed class LazyMetadataMarkerAttribute : Attribute
    {
        public LazyMetadataMarkerAttribute(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    public interface ILazyMetadataConcurrencyTarget
    {
        int Compute(int value);
    }

    public abstract class LazyMetadataConcurrencyBase
    {
        public virtual int Compute(int value)
        {
            return value * 2;
        }
    }

    [LazyMetadataMarker(17)]
    public sealed class LazyMetadataConcurrencyTarget : LazyMetadataConcurrencyBase, ILazyMetadataConcurrencyTarget
    {
        private readonly long _offset = 3;

        [LazyMetadataMarker(19)]
        public static int StaticValue;

        [ThreadStatic]
        [LazyMetadataMarker(23)]
        public static int ThreadValue;

        [LazyMetadataMarker(29)]
        public int Value { get; set; }

        [LazyMetadataMarker(31)]
        public event Action? Changed;

        [LazyMetadataMarker(37)]
        public override int Compute([LazyMetadataMarker(41)] int value)
        {
            return base.Compute(value) + (int)_offset;
        }

        public static int SetAndGetThreadValue(int value)
        {
            ThreadValue = value;
            return ThreadValue;
        }

        public void Raise()
        {
            Changed?.Invoke();
        }
    }
}
