using System.Runtime.CompilerServices;

namespace HybridCLR.Lab
{
    internal static class Instrumentation
    {
#if UNITY_EDITOR
        public static void Reset()
        {
        }

        public static string Snapshot()
        {
            return null;
        }

        public static void ResetFullGenericSharing()
        {
        }

        public static long GetFullGenericSharingDispatchCount()
        {
            return 0;
        }

        public static long GetFullGenericSharingInterpreterInvokerCount()
        {
            return 0;
        }

        public static void FlushMetadataProfile()
        {
        }
#else
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Reset();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern string Snapshot();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void ResetFullGenericSharing();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern long GetFullGenericSharingDispatchCount();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern long GetFullGenericSharingInterpreterInvokerCount();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void FlushMetadataProfile();
#endif
    }
}
