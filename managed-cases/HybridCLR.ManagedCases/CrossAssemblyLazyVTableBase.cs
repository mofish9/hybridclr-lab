namespace HybridCLR.Lab.ManagedCases
{
    public interface ICrossAssemblyLazyVTableContract
    {
        int Compute(int value);
    }

    public class CrossAssemblyLazyVTableBase : ICrossAssemblyLazyVTableContract
    {
        public virtual int Compute(int value)
        {
            return value + 10;
        }

        public virtual string Describe()
        {
            return "base";
        }
    }
}
