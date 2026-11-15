using System.Collections.Generic;
using UnityEngine;

namespace ombarella
{
    /// <summary>
    /// Hides things that paint the empty background of the light camera while it renders, then restores them:
    /// 1) EFT's TOD sky dome renderers (6 flag writes),
    /// 2) oversized deferred decals. The probe found a ~1000 m "mark_solid" decal box: in deferred rendering it paints
    ///    every pixel whose depth falls inside the box, including empty background at the far plane, as a white solid.
    /// Big-decal list is cached and rescanned every ScanSeconds (one FindObjectsOfType, a one-off spike).
    /// </summary>
    public sealed class SkyHider
    {
        const float ScanSeconds = 60f;
        const float BigDecalSize = 100f;

        readonly Renderer[] _r = new Renderer[6];
        readonly bool[] _was = new bool[6];
        readonly List<Renderer> _decals = new List<Renderer>();
        readonly List<Renderer> _hiddenDecals = new List<Renderer>();
        float _nextScan;
        bool _applied;
        public string Status = "not run";

        public void Hide()
        {
            if (_applied) return;
            int tod = HideTod();
            int dec = HideBigDecals();
            _applied = true;
            Status = "hid " + tod + "/6 TOD renderers, " + dec + " big decals (cached " + _decals.Count + ")";
        }

        int HideTod()
        {
            TOD_Sky sky = TOD_Sky.Instance;
            if (sky == null || !sky.Initialized || sky.Components == null) return 0;
            TOD_Components c = sky.Components;
            _r[0] = c.SpaceRenderer; _r[1] = c.AtmosphereRenderer; _r[2] = c.ClearRenderer;
            _r[3] = c.CloudRenderer; _r[4] = c.SunRenderer; _r[5] = c.MoonRenderer;
            int n = 0;
            for (int i = 0; i < _r.Length; i++)
            {
                Renderer r = _r[i];
                if (r == null) continue; // Unity null: destroyed
                _was[i] = r.forceRenderingOff;
                r.forceRenderingOff = true;
                n++;
            }
            return n;
        }

        int HideBigDecals()
        {
            float t = Time.unscaledTime;
            if (t >= _nextScan) { _nextScan = t + ScanSeconds; ScanDecals(); }
            for (int i = _decals.Count - 1; i >= 0; i--)
            {
                Renderer r = _decals[i];
                if (r == null) { _decals.RemoveAt(i); continue; }
                if (r.forceRenderingOff) continue; // already off, not ours to restore
                r.forceRenderingOff = true;
                _hiddenDecals.Add(r);
            }
            return _hiddenDecals.Count;
        }

        void ScanDecals()
        {
            _decals.Clear();
            Renderer[] all = Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                Material m = r.sharedMaterial;
                if (m == null || m.shader == null) continue;
                if (m.shader.name.IndexOf("Decal", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float size = r.bounds.size.magnitude;
                if (float.IsInfinity(size) || size > BigDecalSize) _decals.Add(r);
            }
        }

        public void Restore()
        {
            if (!_applied) return;
            _applied = false;
            for (int i = 0; i < _r.Length; i++)
            {
                Renderer r = _r[i];
                if (r != null && r.forceRenderingOff != _was[i]) r.forceRenderingOff = _was[i];
                _r[i] = null;
            }
            for (int i = 0; i < _hiddenDecals.Count; i++)
                if (_hiddenDecals[i] != null) _hiddenDecals[i].forceRenderingOff = false;
            _hiddenDecals.Clear();
        }
    }
}
