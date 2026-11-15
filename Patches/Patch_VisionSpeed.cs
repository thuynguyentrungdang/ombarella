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

            string id = __instance.ProfileId;
            float now = Time.time;
            VisibilityRegistry reg = Meters.Registry;

            if (!__instance.IsVisible) reg.MarkDemand(id, now);

            float raw;
            if (!reg.TryGetRaw(id, now, out raw)) return;

            float v = MeterMath.Attenuate(raw, OmbarellaConfig.Strength);
            if (__instance.HaveNightVision()) v = MeterMath.NightVisionBlend(v);
            __result *= v;
        }
    }
}
