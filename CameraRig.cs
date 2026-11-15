using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace ombarella
{
    public static class CameraRig
    {
        static Camera _lightCam;

        public static void Initialize(Camera camera) { _lightCam = camera; }

        /// <summary>Camera placed on the line from the nearest other player toward the target, clamped to horizontal offset.</summary>
        public static bool TryRepositionCamera(Player player, List<Player> observers, out Vector3 focusPoint)
        {
            focusPoint = Vector3.zero;
            if (!Utils.IsLightMeterUsablePlayer(player) || observers == null || observers.Count == 0 || _lightCam == null)
                return false;

            float best = float.MaxValue;
            Player closest = null;
            for (int i = 0; i < observers.Count; i++)
            {
                Player o = observers[i];
                if (o == player || !Utils.IsLightMeterUsablePlayer(o)) continue;
                float d = (o.Position - player.Position).sqrMagnitude;
                if (d < best) { best = d; closest = o; }
            }
            if (closest == null) return false;

            focusPoint = GetPlayerFocusPoint(player);
            Vector3 toFocus = focusPoint - closest.PlayerBones.Head.position;
            if (toFocus.sqrMagnitude <= 0.0001f) return false;

            toFocus = Vector3.ClampMagnitude(toFocus, OmbarellaConfig.CamHorizontalOffset.Value);
            Vector3 pos = focusPoint - toFocus;
            _lightCam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(focusPoint - pos));
            return true;
        }

        public static bool TryRepositionCameraOnOrbit(Player player, float radius, float heightOffset, float angleDegrees, out Vector3 focusPoint)
        {
            focusPoint = Vector3.zero;
            if (!Utils.IsLightMeterUsablePlayer(player) || _lightCam == null) return false;

            focusPoint = GetPlayerFocusPoint(player);
            if (!Utils.IsFinite(focusPoint)) return false;

            radius = Mathf.Max(0.1f, radius);
            Vector3 pos = focusPoint + Quaternion.Euler(0f, angleDegrees, 0f) * (Vector3.forward * radius) + Vector3.up * heightOffset;
            Vector3 toPlayer = focusPoint - pos;
            if (toPlayer.sqrMagnitude <= 0.0001f) return false;

            _lightCam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toPlayer));
            return true;
        }

        public static Vector3 GetPlayerFocusPoint(Player player)
        {
            return player.PlayerBones.Ribcage.position + Vector3.up * OmbarellaConfig.CameraFocusHeightOffset.Value;
        }
    }
}
