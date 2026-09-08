using System.Runtime.CompilerServices;
using HybridCLR.Lab.ValueLayout;

namespace HybridCLR.Lab.ValueLayoutNative
{
    // This assembly is ordinary AOT, never included in the hotfix input set.
    public class NativeInlineOwner { public Payload Value; public int Neighbor; }
    public static class NativeBoundary
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Payload Echo(Payload value) => value;
    }
}
