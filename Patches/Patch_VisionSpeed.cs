using HarmonyLib;
using ombarella.Core;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace ombarella
{
    /// <summary>
    /// Scales bot visibility-change speed by the TARGET's light meter. Target = EnemyInfo.ProfileId, so in Fika each
    /// player uses its own synced value. Enemies without a tracked meter (bots, stale) stay vanilla.
    /// Also stamps "demand" while the bot is looking but target not yet detected (IsVisible false), which gates light sampling.
    /// </summary>
    public class Patch_VisionSpeed : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.GetVisibilityChangeSpeedK));
        }

        [PatchPostfix]
        public static void PatchPostfix(ref float __result, EnemyInfo __instance)
        {
            if (!OmbarellaConfig.Enabled || __instance == null) return;

            // Hot path: per bot, per enemy, on the process that owns the bots (host/headless). Cheapest test first:
            // one scan of the few tracked human ids rejects every bot-vs-bot pair before anything else runs.
            float raw; bool fresh;
            string id = __instance.ProfileId;
            float now = Meters.Now;
            if (!Meters.Registry.Observe(id, now, false, out raw, out fresh)) return;
            if (!__instance.IsVisible) Meters.Registry.MarkDemand(id, now); // not yet detected: light is worth measuring
            if (!fresh) return;

            __result *= MeterMath.VisionSpeed(raw, OmbarellaConfig.VisionCurveExponent.Value, OmbarellaConfig.Strength,
                __instance.HaveNightVision(), __instance.Distance, OmbarellaConfig.VisionRampNear.Value, OmbarellaConfig.VisionRampFar.Value);
        }
    }
}
