using ombarella.Core;
using Xunit;

public class CoreTests
{
    static ScoreSettings S(bool ignoreClear = true) => new ScoreSettings
    {
        LumaCoef = 1f, RedLumaMulti = 1f, GreenLumaMulti = 1f, BlueLumaMulti = 1f,
        RedColorDepthMulti = 1f, GreenColorDepthMulti = 1f, BlueColorDepthMulti = 1f,
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
        // one white opaque + one black clear. Luma = (255*3)/(255*1) = 3 -> clamp 1. depth = 0. score = 0.5
        Assert.True(ScoreCalculator.TryCalculate(new byte[] { 255, 255, 255, 255, 0, 0, 0, 0 }, S(), out float score, out float luma, out float cd));
        Assert.Equal(1f, luma);
        Assert.Equal(0f, cd);
        Assert.Equal(0.5f, score, 3);
    }

    [Fact]
    public void Score_Black_ClampsToMin()
    {
        Assert.True(ScoreCalculator.TryCalculate(new byte[] { 0, 0, 0, 255 }, S(), out float score, out _, out _));
        Assert.Equal(0.01f, score);
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
    public void Math_AttenuateAndAim()
    {
        Assert.Equal(0.4f, MeterMath.Attenuate(0.4f, 1f), 4);
        Assert.Equal(1f, MeterMath.Attenuate(0.4f, 0f), 4);
        Assert.Equal(0.7f, MeterMath.Attenuate(0.4f, 0.5f), 4);
        Assert.Equal(0f, MeterMath.AimOffsetScale(1f, 0.03f), 4);   // preserved quirk: full light = zero offset
        Assert.Equal(0f, MeterMath.AimOffsetScale(0.2f, 0f), 4);    // preserved quirk: AimNerf 0 = zero offset
    }
}
