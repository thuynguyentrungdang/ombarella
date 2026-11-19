using System.Runtime.CompilerServices;
using UnityEngine;

namespace ombarella
{
    /// <summary>
    /// The only class that names a TraceSix type. Touch it only after the "com.matsix.tracesix" plugin is known to be loaded
    /// (LightMatch checks), and call it inside try/catch: the JIT resolves TraceSix here when a method is first called, so a
    /// missing or older TraceSix surfaces as an exception at the call, never as Ombarella failing to load.
    /// Built with TRACESIX_API (ombarella.csproj, when TraceSix.dll is found); without it these are inert stubs.
    /// </summary>
    internal static class TraceSixBridge
    {
        public const string PluginGuid = "com.matsix.tracesix";
#if TRACESIX_API
        public const bool Compiled = true;
        [MethodImpl(MethodImplOptions.NoInlining)] public static int ApiVersion() { return TraceSix.TraceSixApi.Version; }
        [MethodImpl(MethodImplOptions.NoInlining)] public static bool Attach(Camera cam) { return TraceSix.TraceSixApi.AttachTo(cam); }
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Detach(Camera cam) { TraceSix.TraceSixApi.Detach(cam); }
        [MethodImpl(MethodImplOptions.NoInlining)] public static bool IsActive() { return TraceSix.TraceSixApi.IsActive; }
#else
        public const bool Compiled = false;
        public static int ApiVersion() { return -1; }
        public static bool Attach(Camera cam) { return false; }
        public static void Detach(Camera cam) { }
        public static bool IsActive() { return false; }
#endif
    }
}
