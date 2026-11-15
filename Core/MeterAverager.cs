namespace ombarella.Core
{
    /// <summary>Rolling mean over last N samples. Ring buffer, no alloc after construction.</summary>
    public sealed class MeterAverager
    {
        public const int MaxSamples = 600;
        readonly float[] _buf = new float[MaxSamples];
        int _head, _count;
        double _sum;

        public int Count { get { return _count; } }

        public void Clear() { _head = 0; _count = 0; _sum = 0; }

        /// <summary>Mean clamped to [0.01,1]. Ignores NaN/inf (returns false).</summary>
        public bool Add(float value, int window, out float mean)
        {
            mean = 1f;
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            if (window < 1) window = 1;
            if (window > MaxSamples) window = MaxSamples;

            // keep room for the new sample; also handles window shrink
            while (_count >= window)
            {
                int oldest = (_head - _count + MaxSamples) % MaxSamples;
                _sum -= _buf[oldest];
                _count--;
            }
            _buf[_head] = value;
            _head = (_head + 1) % MaxSamples;
            _sum += value;
            _count++;

            float m = (float)(_sum / _count);
            mean = m < 0.01f ? 0.01f : (m > 1f ? 1f : m);
            return true;
        }
    }
}
