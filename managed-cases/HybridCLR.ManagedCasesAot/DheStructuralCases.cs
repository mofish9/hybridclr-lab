namespace HybridCLR.Lab.ManagedCasesAot
{
#if !DHE_STRUCTURE_CURRENT
	public sealed class DheRemovedReferenceType
	{
		public int Value;

		public DheRemovedReferenceType(int value)
		{
			Value = value;
		}

		public int Read()
		{
			return Value;
		}
	}
#else
    public sealed class DheAddedReferenceType
    {
        private readonly int offset;

        public DheAddedReferenceType(int offset)
        {
            this.offset = offset;
        }

        public int Apply(int value)
        {
            return value + offset;
        }
    }

    public sealed class DheAddedGenericType<T>
    {
        public DheAddedGenericType(T value)
        {
            Value = value;
        }

        public T Value { get; }
    }
#endif
}
