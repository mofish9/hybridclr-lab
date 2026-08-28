using HybridCLR.Lab.BoundaryContracts;

namespace HybridCLR.Lab
{
    public static class HybridCLRLabRuntimeMarker
    {
        public const string Version = "hybridclr-lab-v1";

        public static int BoundaryContractAnchor()
        {
            return AotBoundaryHost.Add(1, 1);
        }
    }
}
