namespace HybridCLR.Lab.ManagedCases
{
    public interface ICrossAssemblyLazyVTableContract
    {
#if DHE_CROSS_INTERFACE_CURRENT
        int Added(int value);
        T EchoAdded<T>(T value);
#endif
        int Compute(int value);
    }

    public class CrossAssemblyLazyVTableBase : ICrossAssemblyLazyVTableContract
    {
#if DHE_CROSS_INTERFACE_CURRENT
        public int Added(int value) => Compute(value) + 1000;
        public T EchoAdded<T>(T value) => value;
#endif
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
