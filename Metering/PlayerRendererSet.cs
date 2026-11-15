using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ombarella
{
    /// <summary>
    /// Cached renderer list of one player + per-renderer optic classification + reusable state save/restore.
    /// Old code re-classified every renderer by name (string allocs) and allocated lists every sample; this rebuilds
    /// only when a renderer died, the held item changed, or the optic option flipped. Apply/Restore allocate nothing
    /// and only write properties whose value must change.
    /// </summary>
    public sealed class PlayerRendererSet
    {
        Player _owner;
        int _handsId;
        bool _excludeOptics;
        Renderer[] _all = Array.Empty<Renderer>();
        bool[] _hide = Array.Empty<bool>();
        SkinnedMeshRenderer[] _skinned = Array.Empty<SkinnedMeshRenderer>();

        // saved state (parallel arrays, reused)
        bool[] _enabled = Array.Empty<bool>();
        bool[] _forceOff = Array.Empty<bool>();
        ShadowCastingMode[] _shadow = Array.Empty<ShadowCastingMode>();
        bool[] _offscreen = Array.Empty<bool>();
        bool _applied;

        static readonly string[] OpticTokens =
        {
            "optic", "scope", "magnifier", "prism", "acog", "elcan", "hamr", "bravo", "valday", "razor",
            "vudu", "tac30", "march", "nightforce", "schmidt", "leupold"
        };

        /// <summary>Rebuild when stale. Returns false when player has no renderers.</summary>
        public bool Refresh(Player player, bool excludeOptics)
        {
            if (player == null) return false;
            GameObject hands = GetHands(player);
            int handsId = hands != null ? hands.GetInstanceID() : 0;

            if (_owner == player && _excludeOptics == excludeOptics && _handsId == handsId && !HasNull())
                return _all.Length > 0;

            _owner = player;
            _excludeOptics = excludeOptics;
            _handsId = handsId;
            _all = player.GetComponentsInChildren<Renderer>(true);
            int n = _all.Length;
            _hide = new bool[n];
            _skinned = new SkinnedMeshRenderer[n];
            _enabled = new bool[n];
            _forceOff = new bool[n];
            _shadow = new ShadowCastingMode[n];
            _offscreen = new bool[n];

            HashSet<Renderer> sightRenderers = excludeOptics ? CollectRangedOpticRenderers(hands) : null;
            for (int i = 0; i < n; i++)
            {
                Renderer r = _all[i];
                if (r == null) continue;
                _skinned[i] = r as SkinnedMeshRenderer;
                if (excludeOptics)
                    _hide[i] = (sightRenderers != null && sightRenderers.Contains(r)) || LooksLikeOptic(r);
            }
            return n > 0;
        }

        bool HasNull()
        {
            for (int i = 0; i < _all.Length; i++)
                if (_all[i] == null) return true;
            return false;
        }

        public void Apply(bool force)
        {
            if (_applied) Restore();
            for (int i = 0; i < _all.Length; i++)
            {
                Renderer r = _all[i];
                if (r == null) continue;
                bool hide = _hide[i];
                if (!hide && !force) continue;

                SkinnedMeshRenderer sk = _skinned[i];
                _enabled[i] = r.enabled;
                _forceOff[i] = r.forceRenderingOff;
                _shadow[i] = r.shadowCastingMode;
                if (sk != null) _offscreen[i] = sk.updateWhenOffscreen;

                if (hide)
                {
                    if (!_forceOff[i]) r.forceRenderingOff = true;
                    continue;
                }
                if (!_enabled[i]) r.enabled = true;
                if (_forceOff[i]) r.forceRenderingOff = false;
                if (_shadow[i] != ShadowCastingMode.On) r.shadowCastingMode = ShadowCastingMode.On;
                if (sk != null && !_offscreen[i]) sk.updateWhenOffscreen = true;
            }
            _applied = true;
            _lastForce = force;
        }

        bool _lastForce;

        public void Restore()
        {
            if (!_applied) return;
            for (int i = 0; i < _all.Length; i++)
            {
                Renderer r = _all[i];
                if (r == null) continue;
                if (!_hide[i] && !_lastForce) continue;

                if (r.enabled != _enabled[i]) r.enabled = _enabled[i];
                if (r.forceRenderingOff != _forceOff[i]) r.forceRenderingOff = _forceOff[i];
                if (r.shadowCastingMode != _shadow[i]) r.shadowCastingMode = _shadow[i];
                SkinnedMeshRenderer sk = _skinned[i];
                if (sk != null && sk.updateWhenOffscreen != _offscreen[i]) sk.updateWhenOffscreen = _offscreen[i];
            }
            _applied = false;
        }

        public void Clear()
        {
            Restore();
            _owner = null;
            _all = Array.Empty<Renderer>();
        }

        static GameObject GetHands(Player p)
        {
            return p.HandsController == null ? null : p.HandsController.ControllerGameObject;
        }

        static HashSet<Renderer> CollectRangedOpticRenderers(GameObject hands)
        {
            if (hands == null) return null;
            var result = new HashSet<Renderer>();
            SightModVisualControllers[] sights = hands.GetComponentsInChildren<SightModVisualControllers>(true);
            for (int i = 0; i < sights.Length; i++)
            {
                if (!IsRangedOptic(sights[i])) continue;
                Renderer[] rs = sights[i].GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < rs.Length; j++)
                    if (rs[j] != null) result.Add(rs[j]);
            }
            return result;
        }

        static bool IsRangedOptic(SightModVisualControllers s)
        {
            if (s == null || s.SightMod == null) return false;
            ScopeZoomHandler zoom;
            if (s.TryGetZoomHandler(out zoom) && zoom != null) return true;
            ScopePrefabCache cache = s.GetComponent<ScopePrefabCache>();
            return cache != null && cache.HasOptics;
        }

        // Name-based fallback. Runs once per renderer per rebuild (not per sample like before).
        static bool LooksLikeOptic(Renderer r)
        {
            Transform t = r.transform;
            for (int depth = 0; t != null && depth < 8; depth++, t = t.parent)
                if (HasToken(t.name)) return true;

            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;
                if (HasToken(m.name) || (m.shader != null && HasToken(m.shader.name))) return true;
            }
            return false;
        }

        static bool HasToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < OpticTokens.Length; i++)
                if (value.IndexOf(OpticTokens[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
