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
    /// Preserved behaviour: end point = real + offsetStandart * timeCoef * scale(meter); bad-shoot and recoil offsets
    /// are dropped (verified against 4.0 decompile: Float_13 = _timeCoef, Vector3_4 = _offsetStandart).
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
            EnemyInfo goal = owner.Memory.GoalEnemy;
            if (goal == null) return;

            float raw;
            if (!Meters.Registry.TryGetRaw(goal.ProfileId, Time.time, out raw)) return;

            float final = MeterMath.Attenuate(raw, OmbarellaConfig.Strength);
            float scale = MeterMath.AimOffsetScale(final, OmbarellaConfig.AimNerfValue);
            float aimOffset = __instance._timeCoef * scale;
            __instance.EndTargetPoint = __instance.RealTargetPoint + __instance._offsetStandart * aimOffset;
        }
    }
}
