using ombarella.Core;
using Xunit;

public class CoreTests
{
    static ScoreSettings S(bool ignoreClear = true) => new ScoreSettings
    {
        LumaCoef = 1f, RedLumaMulti = 1f, GreenLumaMulti = 1f, BlueLumaMulti = 1f,
        RedColorDepthMulti = 1f, GreenColorDepthMulti = 1f, BlueColorDepthMulti = 1f,
        DepthWeight = 0.5f,
        IgnoreTransparentPixels = ignoreClear
    };

    [Fact]
    public void Score_AllClear_IsRejected()
    {
        Assert.False(ScoreCalculator.TryCalculate(new byte[] { 9, 9, 9, 0, 9, 9, 9, 0 }, S(), out _, out _, out _));
    }

    [Fact]
    public void Score_ClearPixelsIgnored_OnlyOpaqueCount()
    {
        // one white opaque + one black clear. Luma = (255*3)/(255*1) = 3 -> clamp 1. depth = 0. whiteout reaches 1.0
        Assert.True(ScoreCalculator.TryCalculate(new byte[] { 255, 255, 255, 255, 0, 0, 0, 0 }, S(), out float score, out float luma, out float cd));
        Assert.Equal(1f, luma);
        Assert.Equal(0f, cd);
        Assert.Equal(1f, score, 3);
    }

    [Fact]
    public void Score_DepthIsAdditiveBonus()
    {
        // opaque black + opaque (60,60,60): mean luma = 3*60/(255*2) = 0.353; depth = 60*3/(255*3) = 0.235; score = luma + 0.5*depth
        var px = new byte[] { 0, 0, 0, 255, 60, 60, 60, 255 };
        Assert.True(ScoreCalculator.TryCalculate(px, S(), out float score, out float luma, out float cd));
        Assert.Equal(0.353f, luma, 3);
        Assert.Equal(0.235f, cd, 3);
        Assert.Equal(0.4706f, score, 3);
        var s0 = S(); s0.DepthWeight = 0f;
        Assert.True(ScoreCalculator.TryCalculate(px, s0, out score, out _, out _));
        Assert.Equal(0.353f, score, 3);
    }

    [Fact]
    public void Score_Black_ClampsToMin()
    {
        Assert.True(ScoreCalculator.TryCalculate(new byte[] { 0, 0, 0, 255 }, S(), out float score, out _, out _));
        Assert.Equal(0.01f, score);
    }

    [Fact]
    public void Exposure_ScalesRgbClipsKeepsAlpha()
    {
        var src = new byte[] { 10, 20, 200, 77 };
        var dst = new byte[4];
        Exposure.Apply(src, dst, Exposure.GainFromEv(2f));          // x4
        Assert.Equal(new byte[] { 40, 80, 255, 77 }, dst);
        Exposure.Apply(src, dst, Exposure.GainFromEv(0f));
        Assert.Equal(src, dst);
        Exposure.Apply(src, dst, Exposure.GainFromEv(-1f));         // x0.5
        Assert.Equal(new byte[] { 5, 10, 100, 77 }, dst);
    }

    [Fact]
    public void Average_RollsAndClamps()
    {
        var a = new MeterAverager();
        a.Add(1f, 2, out _);
        a.Add(0.5f, 2, out float m);
        Assert.Equal(0.75f, m, 3);
        a.Add(0.1f, 2, out m);            // oldest (1.0) dropped
        Assert.Equal(0.3f, m, 3);
        Assert.False(a.Add(float.NaN, 2, out _));
        a.Add(0f, 1, out m);              // window shrink + clamp floor
        Assert.Equal(0.01f, m);
    }

    [Fact]
    public void Registry_UntrackedNeverStored()
    {
        var r = new VisibilityRegistry();
        Assert.False(r.SetMeter("bot", 0.5f, 0f));
        Assert.False(r.TryGetRaw("bot", 0f, out _));
        r.MarkDemand("bot", 0f);
        Assert.False(r.HasDemand("bot", 0f, 5f));
    }

    [Fact]
    public void Registry_ClampsRejectsNanAndGoesStale()
    {
        var r = new VisibilityRegistry();
        r.Track("p");
        Assert.False(r.TryGetRaw("p", 0f, out _));            // no sample yet
        Assert.False(r.SetMeter("p", float.NaN, 0f));
        Assert.True(r.SetMeter("p", 5f, 10f));
        Assert.True(r.TryGetRaw("p", 11f, out float v));
        Assert.Equal(1f, v);
        Assert.False(r.TryGetRaw("p", 10f + VisibilityRegistry.StaleSeconds + 1f, out _));
    }

    [Fact]
    public void Registry_Demand_Window()
    {
        var r = new VisibilityRegistry();
        r.Track("p");
        r.MarkDemand("p", 10f);
        Assert.True(r.HasDemand("p", 11.9f, 2f));
        Assert.False(r.HasDemand("p", 12.1f, 2f));
    }

    [Fact]
    public void Math_VisionSpeed_CurveRampNvg()
    {
        Assert.Equal(1f, MeterMath.VisionSpeed(1f, 0.5f, 1f, false, 100f, 5f, 30f), 4);          // full light
        Assert.Equal(0.3162f, MeterMath.VisionSpeed(0.1f, 0.5f, 1f, false, 30f, 5f, 30f), 3);    // sqrt curve, full range
        Assert.Equal(0.1f, MeterMath.VisionSpeed(0.1f, 1f, 1f, false, 100f, 5f, 30f), 4);        // exponent 1 = old linear
        Assert.Equal(1f, MeterMath.VisionSpeed(0.1f, 0.5f, 1f, false, 5f, 5f, 30f), 4);          // close = no effect
        Assert.Equal(0.6581f, MeterMath.VisionSpeed(0.1f, 0.5f, 1f, false, 17.5f, 5f, 30f), 3);  // halfway ramp
        Assert.Equal(0.7948f, MeterMath.VisionSpeed(0.1f, 0.5f, 1f, true, 50f, 5f, 30f), 3);     // NVG after curve
        Assert.Equal(1f, MeterMath.VisionSpeed(0.1f, 0.5f, 0f, false, 50f, 5f, 30f), 4);         // strength 0 = off
    }

    [Fact]
    public void Math_AttenuateAndAim()
    {
        Assert.Equal(0.4f, MeterMath.Attenuate(0.4f, 1f), 4);
        Assert.Equal(1f, MeterMath.Attenuate(0.4f, 0f), 4);
        Assert.Equal(0.7f, MeterMath.Attenuate(0.4f, 0.5f), 4);
        Assert.Equal(0f, MeterMath.AimOffsetScale(1f, 0.03f), 4);   // full light = no extra penalty
        Assert.Equal(0f, MeterMath.AimOffsetScale(0.2f, 0f), 4);    // AimNerf 0 = no extra penalty
    }

    [Fact]
    public void Registry_IrFlag_RoundTripsAndClears()
    {
        var r = new VisibilityRegistry();
        r.Track("p");
        Assert.False(r.IrLit("p"));
        r.SetMeter("p", 0.2f, 1f, true);
        Assert.True(r.IrLit("p"));
        r.SetMeter("p", 0.2f, 2f);          // next report without IR clears it
        Assert.False(r.IrLit("p"));
        Assert.False(r.IrLit("bot"));       // untracked
    }

    [Fact]
    public void Registry_Observe_Untrack_Grow()
    {
        var r = new VisibilityRegistry();
        Assert.False(r.Observe("bot", 1f, true, out _, out _));      // untracked: rejected, nothing stamped
        for (int i = 0; i < 20; i++) r.Track("p" + i);               // forces array growth
        Assert.True(r.Observe("p7", 1f, true, out _, out bool fresh));
        Assert.False(fresh);                                          // tracked but never metered
        Assert.True(r.HasDemand("p7", 2f, 2f));
        r.SetMeter("p7", 0.5f, 1f);
        Assert.True(r.Observe("p7", 2f, false, out float raw, out fresh));
        Assert.True(fresh); Assert.Equal(0.5f, raw, 3);
        r.Untrack("p3");
        Assert.False(r.IsTracked("p3")); Assert.True(r.IsTracked("p19")); Assert.True(r.IsTracked("p7"));
    }
}
