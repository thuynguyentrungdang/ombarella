using EFT;
using HarmonyLib;
using ombarella.Core;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace ombarella
{
    /// <summary>
    /// 4.1 target: BotAimingData.UpdateEndTargetPoint (was BotAimingClass.method_13 in 4.0).
    /// Dark-only penalty: end += offsetStandart * timeCoef * scale(meter), added to the vanilla end point (keeps bad-shoot,
    /// recoil and SAIN's changes). Bright light or AimNerf 0 = scale 0 = untouched.
    /// Typed field access, no reflection or boxing. Only for tracked human targets.
    /// </summary>
    public class Patch_AimOffset : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotAimingData), nameof(BotAimingData.UpdateEndTargetPoint));
        }

        [PatchPostfix]
        public static void PatchPostfix(BotAimingData __instance)
        {
            if (!OmbarellaConfig.Enabled || __instance == null) return;

            BotOwner owner = __instance._owner;
            if (owner == null || owner.Memory == null) return;
            EnemyInfo goal = owner.Memory.GoalEnemy; // ghosted bots have none
            if (goal == null) return;

            float raw;
            if (!Meters.Registry.TryGetRaw(goal.ProfileId, Meters.Now, out raw)) return;
            if (goal.HaveNightVision() && Meters.Registry.IrLit(goal.ProfileId)) return; // target's IR light: NVG bot sees it lit

            float final = MeterMath.Attenuate(OmbarellaConfig.Shape(raw), OmbarellaConfig.Strength);
            float scale = MeterMath.AimOffsetScale(final, OmbarellaConfig.AimNerfValue, OmbarellaConfig.AimRampStartValue);
            if (scale <= 0f) return; // bright, above ramp start (or AimNerf 0): vanilla/SAIN end point untouched
            // add the dark penalty on top of whatever vanilla (and SAIN's own patches) produced
            __instance.EndTargetPoint += __instance._offsetStandart * (__instance._timeCoef * scale);
        }
    }
}
