using System;

namespace ombarella.Core
{
    /// <summary>
    /// Per-player raw light meter (0.01..1) plus "a bot is evaluating this player" demand stamp.
    /// Pure: time passed in. Only tracked ids stored (humans: a handful), looked up by linear scan: the bot-side patches
    /// run per bot per enemy, mostly for bot-vs-bot pairs that must exit cheaply, and a scan of a few entries is
    /// cheaper than hashing a 24-char id (and allocation free).
    /// </summary>
    public sealed class VisibilityRegistry
    {
        public const float StaleSeconds = 20f;

        sealed class Entry
        {
            public string Id;
            public float Raw = 1f;
            public float UpdatedAt = float.NegativeInfinity;
            public float DemandAt = float.NegativeInfinity;
            public bool Ir; // target's IR light is on: only NVG bots see it
        }

        Entry[] _entries = new Entry[8];
        int _count;

        Entry Find(string id)
        {
            if (id == null) return null;
            Entry[] arr = _entries;
            for (int i = 0, n = _count; i < n; i++)
            {
                string k = arr[i].Id;
                if ((object)k == (object)id || string.Equals(k, id, StringComparison.Ordinal)) return arr[i];
            }
            return null;
        }

        public void Clear()
        {
            Array.Clear(_entries, 0, _count);
            _count = 0;
        }

        public bool IsTracked(string id) { return Find(id) != null; }

        public void Track(string id)
        {
            if (string.IsNullOrEmpty(id) || Find(id) != null) return;
            if (_count == _entries.Length) Array.Resize(ref _entries, _count * 2);
            _entries[_count++] = new Entry { Id = id };
        }

        public void Untrack(string id)
        {
            if (id == null) return;
            for (int i = 0; i < _count; i++)
            {
                if (!string.Equals(_entries[i].Id, id, StringComparison.Ordinal)) continue;
                _entries[i] = _entries[--_count];
                _entries[_count] = null;
                return;
            }
        }

        /// <summary>Validated write. False for untracked id or non-finite value.</summary>
        public bool SetMeter(string id, float raw, float now, bool ir = false)
        {
            if (float.IsNaN(raw) || float.IsInfinity(raw)) return false;
            Entry e = Find(id);
            if (e == null) return false;
            e.Raw = raw < 0.01f ? 0.01f : (raw > 1f ? 1f : raw);
            e.Ir = ir;
            e.UpdatedAt = now;
            return true;
        }

        /// <summary>True when the id's last meter report had an IR light on. Call only after Observe said fresh.</summary>
        public bool IrLit(string id)
        {
            Entry e = Find(id);
            return e != null && e.Ir;
        }

        /// <summary>Hot path (bot patches). False = no fresh meter, caller keeps vanilla.</summary>
        public bool TryGetRaw(string id, float now, out float raw)
        {
            raw = 1f;
            Entry e = Find(id);
            if (e == null || now - e.UpdatedAt > StaleSeconds) return false;
            raw = e.Raw;
            return true;
        }

        /// <summary>
        /// Hot path, ONE lookup: false when id is not a tracked player (bot-vs-bot: caller exits, nothing else done).
        /// Otherwise stamps demand if asked; raw/fresh report the meter.
        /// </summary>
        public bool Observe(string id, float now, bool markDemand, out float raw, out bool fresh)
        {
            raw = 1f; fresh = false;
            Entry e = Find(id);
            if (e == null) return false;
            if (markDemand) e.DemandAt = now;
            if (now - e.UpdatedAt <= StaleSeconds) { raw = e.Raw; fresh = true; }
            return true;
        }

        public void MarkDemand(string id, float now)
        {
            Entry e = Find(id);
            if (e != null) e.DemandAt = now;
        }

        public bool HasDemand(string id, float now, float window)
        {
            Entry e = Find(id);
            return e != null && now - e.DemandAt <= window;
        }
    }
}
