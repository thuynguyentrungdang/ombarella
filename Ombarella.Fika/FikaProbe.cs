using Fika.Core.Main.Utils;
using System.Runtime.CompilerServices;

namespace ombarella.FikaSupport
{
    /// <summary>
    /// ONLY place (with FikaNet) touching Fika types. Never call unless Fika plugin is loaded (FikaGate checks GUID),
    /// so a missing Fika.Core.dll can never fail the JIT of unrelated code. NoInlining keeps type loads inside.
    /// </summary>
    internal static class FikaProbe
    {
        [MethodImpl(MethodImplOptions.NoInlining)] internal static bool IsHeadless() { return FikaBackendUtils.IsHeadless; }
        [MethodImpl(MethodImplOptions.NoInlining)] internal static bool IsHost() { return FikaBackendUtils.IsServer; }
        [MethodImpl(MethodImplOptions.NoInlining)] internal static bool IsClient() { return FikaBackendUtils.IsClient; }
    }
}
