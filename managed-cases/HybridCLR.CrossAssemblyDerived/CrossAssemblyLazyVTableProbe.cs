using HybridCLR.Lab.ManagedCases;

namespace HybridCLR.Lab.CrossAssemblyDerived
{
    public sealed class CrossAssemblyLazyVTableDerived : CrossAssemblyLazyVTableBase
    {
        public override int Compute(int value)
        {
            return base.Compute(value) * 2;
        }

        public override string Describe()
        {
            return "derived";
        }
    }

    public static class CrossAssemblyLazyVTableProbe
    {
        public static string Run()
        {
            CrossAssemblyLazyVTableBase instance = new CrossAssemblyLazyVTableDerived();
            ICrossAssemblyLazyVTableContract contract = instance;
            return instance.Describe() + ":" + instance.Compute(3) + ":" + contract.Compute(7);
        }
    }
}
