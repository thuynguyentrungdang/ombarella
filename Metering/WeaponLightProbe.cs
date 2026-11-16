using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ombarella
{
    public enum WeaponLight { None, White, IR }

    /// <summary>
    /// Which weapon light mode is on for the local player. Lasers are ignored (SAIN handles those).
    /// A tactical device activates one "mode_NNN" child; the emitter inside is named by kind (same scheme SAIN's FlashLightClass reads):
    /// light_0* = white light, il_0* = IR light (vis_0* / ir_0* = lasers, never matched). Combo devices count as long as the active mode is a light.
    /// Transform lists cached per firearm, refreshed once a second; the per-poll check is a few activeInHierarchy reads, no allocation.
    /// </summary>
    public sealed class WeaponLightProbe
    {
        readonly List<Transform> _white = new List<Transform>();
        readonly List<Transform> _ir = new List<Transform>();
        readonly List<TacticalComboVisualController> _controllers = new List<TacticalComboVisualController>();
        readonly List<Transform> _scratch = new List<Transform>();
        Player.FirearmController _cached;
        float _nextRefresh;

        /// <summary>White wins when both kinds are on (several devices).</summary>
        public WeaponLight Read(Player player, float now)
        {
            Player.FirearmController firearm = player != null ? player.HandsController as Player.FirearmController : null;
            if (firearm == null) { Clear(); return WeaponLight.None; }
            if (!ReferenceEquals(_cached, firearm) || now >= _nextRefresh) Refresh(player, firearm, now);

            if (AnyActive(_white)) return WeaponLight.White;
            if (AnyActive(_ir)) return WeaponLight.IR;
            return WeaponLight.None;
        }

        static bool AnyActive(List<Transform> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Transform t = list[i];
                if (t != null && t.gameObject.activeInHierarchy) return true;
            }
            return false;
        }

        public void Clear()
        {
            _cached = null;
            _nextRefresh = 0f;
            _white.Clear();
            _ir.Clear();
        }

        void Refresh(Player player, Player.FirearmController firearm, float now)
        {
            _cached = firearm;
            _nextRefresh = now + 1f;
            _white.Clear();
            _ir.Clear();
            Transform root = player.PlayerBones != null ? player.PlayerBones.WeaponRoot.Original : null;
            if (root == null) return;

            root.GetComponentsInChildren(true, _controllers);
            for (int c = 0; c < _controllers.Count; c++)
            {
                TacticalComboVisualController tc = _controllers[c];
                if (tc == null || tc.LightMod == null) continue;
                tc.GetComponentsInChildren(true, _scratch);
                for (int i = 0; i < _scratch.Count; i++)
                {
                    Transform t = _scratch[i];
                    Transform mode = t != null ? t.parent : null;
                    if (mode == null || !mode.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)) continue;
                    string n = t.name;
                    if (n.StartsWith("light_0", StringComparison.OrdinalIgnoreCase)) _white.Add(t);
                    else if (n.StartsWith("il_0", StringComparison.OrdinalIgnoreCase)) _ir.Add(t);
                }
            }
            _controllers.Clear();
            _scratch.Clear();
        }
    }
}
