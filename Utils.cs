using BepInEx.Logging;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ombarella
{
    public static class Utils
    {
        static float _logTimer;
        public static ManualLogSource Logger;

        public static Player GetMainPlayer()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            return world == null ? null : world.MainPlayer;
        }

        /// <summary>Live list owned by the game. Do not mutate. Empty static list when no world.</summary>
        static readonly List<Player> Empty = new List<Player>();
        public static List<Player> GetAllPlayers()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            return world == null ? Empty : (world.AllAlivePlayersList ?? Empty);
        }

        public static bool IsHeadlessPlayer(Player player)
        {
            if (player == null || player.Profile == null || player.Profile.Info == null) return false;
            string nick = player.Profile.Info.Nickname;
            if (!string.IsNullOrEmpty(nick) && nick.StartsWith("headless_", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(player.Profile.Info.GroupId, "HEADLESS", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsLightMeterUsablePlayer(Player player)
        {
            if (player == null) return false;
            if (player.HealthController == null || !player.HealthController.IsAlive) return false;
            if (player.PlayerBones == null || player.PlayerBones.Head == null || player.PlayerBones.Ribcage == null) return false;
            return IsFinite(player.Position) && IsFinite(player.PlayerBones.Head.position) && IsFinite(player.PlayerBones.Ribcage.position);
        }

        public static bool IsFinite(Vector3 v) { return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z); }
        public static bool IsFinite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }

        public static bool IsInRaid()
        {
            if (Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance != null && Singleton<AbstractGame>.Instance.InRaid)
                return true;
            GameWorld world = Singleton<GameWorld>.Instance;
            return world != null && world.MainPlayer != null;
        }

        /// <summary>Throttled debug log. Callers should pre-check IsDebug before building the string.</summary>
        public static void LogThrottled(string log)
        {
            if (!OmbarellaConfig.IsDebug.Value) return;
            _logTimer += Time.unscaledDeltaTime;
            if (_logTimer < 1f / OmbarellaConfig.DebugUpdateFreq.Value) return;
            _logTimer = 0f;
            Logger.LogInfo(log);
        }

        public static void LogError(string error) { Logger.LogError(error); }
    }
}
