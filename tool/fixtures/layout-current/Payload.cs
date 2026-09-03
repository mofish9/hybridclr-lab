namespace HybridCLR.Dhe.LayoutFixture
{
    public struct Payload
    {
        public long Second;
        public int First;
    }

    public static class SwitchCases
    {
        public static int SwitchProbe(int value)
        {
            switch (value)
            {
                case 0: return 10;
                case 1: return 20;
                case 2: return 30;
                case 3: return 40;
                case 4: return 50;
                default: return 60;
            }
        }
    }

	public sealed class ReferenceFieldRemoval
	{
		public int Kept;
		public object? Removed;
		public static object? RemovedStatic;
	}

	public struct ValueFieldRemoval
	{
		public int Kept;
		public int Removed;
	}

	public sealed class RemovedReferenceType
	{
		public int Value;
	}
}
