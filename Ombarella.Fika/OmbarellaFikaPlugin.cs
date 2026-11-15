using BepInEx;
using BepInEx.Logging;
using System;

namespace ombarella.FikaSupport
{
    /// <summary>Fika sync add-on. Hard-depends on Ombarella core + Fika; wires core's OmbarellaApi hooks.</summary>
    [BepInPlugin(ModGuid, "Ombarella Fika Sync", ModVersion.Value)]
    [BepInDependency(Plugin.ModGuid)]
    [BepInDependency("com.fika.core")]
    public class OmbarellaFikaPlugin : BaseUnityPlugin
    {
        public const string ModGuid = "Ombarella.Fika";
        internal static ManualLogSource Log;

        void Awake()
        {
            Log = Logger;
            try
            {
                FikaNet.Register();
                OmbarellaApi.RoleResolver = FikaSync.ResolveRole;
                OmbarellaApi.ClientShouldSample = FikaSync.ClientShouldSample;
                OmbarellaApi.ClientPublish = FikaSync.ClientPublish;
                OmbarellaApi.HostTick = FikaSync.HostTick;
                OmbarellaApi.RaidReset = FikaSync.ResetRaid;
                Log.LogInfo("Fika sync hooked into Ombarella");
            }
            catch (Exception ex)
            {
                Log.LogError("init failed, running without sync: " + ex);
            }
        }
    }
}
