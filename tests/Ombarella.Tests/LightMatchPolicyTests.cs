using ombarella.Core;
using Xunit;

public class LightMatchPolicyTests
{
    [Theory]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    [InlineData(true, false, -1)]
    public void Off_is_always_off(bool loaded, bool compiled, int api)
        => Assert.Equal(LightMatchState.Off, LightMatchPolicy.Resolve(LightMatchMode.Off, loaded, compiled, api));

    [Fact] public void Attach_without_plugin() => Assert.Equal(LightMatchState.NoTraceSix, LightMatchPolicy.Resolve(LightMatchMode.Attach, false, true, 1));
    [Fact] public void Attach_without_bridge() => Assert.Equal(LightMatchState.NoBridge, LightMatchPolicy.Resolve(LightMatchMode.Attach, true, false, -1));
    [Fact] public void Attach_old_api() => Assert.Equal(LightMatchState.ApiTooOld, LightMatchPolicy.Resolve(LightMatchMode.Attach, true, true, -1));
    [Fact] public void Attach_ok() => Assert.Equal(LightMatchState.Attached, LightMatchPolicy.Resolve(LightMatchMode.Attach, true, true, LightMatchPolicy.MinApiVersion));
}
