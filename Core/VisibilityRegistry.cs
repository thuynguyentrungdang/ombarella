using System;
using System.Collections.Generic;

namespace ombarella.Core
{
    /// <summary>
    /// Per-player raw light meter (0.01..1) plus "a bot is evaluating this player" demand stamp.
    /// Pure: time passed in. Only tracked ids stored, so bot-vs-bot checks never allocate.
    /// </summary>
    public sealed class VisibilityRegistry
    {
        public const float StaleSeconds = 20f;

        sealed class Entry
        {
            public float Raw = 1f;
            public float UpdatedAt = float.NegativeInfinity;
            public float DemandAt = float.NegativeInfinity;
        }

        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public void Clear() { _entries.Clear(); }
        public bool IsTracked(string id) { return id != null && _entries.ContainsKey(id); }

        public void Track(string id)
        {
            if (string.IsNullOrEmpty(id) || _entries.ContainsKey(id)) return;
            _entries[id] = new Entry();
        }

        public void Untrack(string id) { if (id != null) _entries.Remove(id); }

        /// <summary>Validated write. False for untracked id or non-finite value.</summary>
        public bool SetMeter(string id, float raw, float now)
        {
            if (float.IsNaN(raw) || float.IsInfinity(raw)) return false;
            Entry e;
            if (id == null || !_entries.TryGetValue(id, out e)) return false;
            e.Raw = raw < 0.01f ? 0.01f : (raw > 1f ? 1f : raw);
            e.UpdatedAt = now;
            return true;
        }

        /// <summary>Hot path (bot patches). False = no fresh meter, caller keeps vanilla.</summary>
        public bool TryGetRaw(string id, float now, out float raw)
        {
            raw = 1f;
            Entry e;
            if (id == null || !_entries.TryGetValue(id, out e)) return false;
            if (now - e.UpdatedAt > StaleSeconds) return false;
            raw = e.Raw;
            return true;
        }

        public void MarkDemand(string id, float now)
        {
            Entry e;
            if (id != null && _entries.TryGetValue(id, out e)) e.DemandAt = now;
        }

        public bool HasDemand(string id, float now, float window)
        {
            Entry e;
            return id != null && _entries.TryGetValue(id, out e) && now - e.DemandAt <= window;
        }
    }
}
