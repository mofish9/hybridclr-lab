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
#if DHE_FIELD_ADDRESSES_CURRENT
        // Insert before the original backing field to exercise Current/Base token collisions.
        public T AddedValue = default!;
        public T[] AddedItems = null!;
        public static T AddedShared = default!;
        public static int AddedCount;
        public int AddressPadding00;
        public int AddressPadding01;
        public int AddressPadding02;
        public int AddressPadding03;
        public int AddressPadding04;
        public int AddressPadding05;
        public int AddressPadding06;
        public int AddressPadding07;
        public int AddressPadding08;
        public int AddressPadding09;
        public int AddressPadding10;
        public int AddressPadding11;
        public int AddressPadding12;
        public int AddressPadding13;
        public int AddressPadding14;
        public int AddressPadding15;
        public int AddressPadding16;
        public int AddressPadding17;
        public int AddressPadding18;
        public int AddressPadding19;
        public int AddressPadding20;
        public int AddressPadding21;
        public int AddressPadding22;
        public int AddressPadding23;
        public int AddressPadding24;
        public int AddressPadding25;
        public int AddressPadding26;
        public int AddressPadding27;
        public int AddressPadding28;
        public int AddressPadding29;
        public int AddressPadding30;
        public int AddressPadding31;
#endif

        public DheAddedGenericType(T value)
        {
            Value = value;
        }

        public T Value { get; }

#if DHE_GENERIC_FIELDS_CURRENT && !DHE_FIELD_ADDRESSES_CURRENT
        public T AddedValue = default!;
        public T[] AddedItems = null!;
        public static T AddedShared = default!;
        public static int AddedCount;
#endif
    }
#endif
}
