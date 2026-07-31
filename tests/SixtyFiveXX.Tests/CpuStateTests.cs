using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class CpuStateTests
{
    [Fact]
    public void FlagBits_MatchTheSixFiveOhTwoLayout()
    {
        Assert.Equal(0x01, Flag.C);
        Assert.Equal(0x02, Flag.Z);
        Assert.Equal(0x04, Flag.I);
        Assert.Equal(0x08, Flag.D);
        Assert.Equal(0x10, Flag.B);
        Assert.Equal(0x20, Flag.U);
        Assert.Equal(0x40, Flag.V);
        Assert.Equal(0x80, Flag.N);
    }

    [Theory]
    [InlineData(0x01, true, false, false, false, false, false)]
    [InlineData(0x02, false, true, false, false, false, false)]
    [InlineData(0x04, false, false, true, false, false, false)]
    [InlineData(0x08, false, false, false, true, false, false)]
    [InlineData(0x40, false, false, false, false, true, false)]
    [InlineData(0x80, false, false, false, false, false, true)]
    public void Properties_ReadTheCorrespondingBit(byte p, bool c, bool z, bool i, bool d, bool v, bool n)
    {
        var state = new CpuState { P = p };

        Assert.Equal(c, state.C);
        Assert.Equal(z, state.Z);
        Assert.Equal(i, state.I);
        Assert.Equal(d, state.D);
        Assert.Equal(v, state.V);
        Assert.Equal(n, state.N);
    }

    [Fact]
    public void Properties_SetAndClearWithoutDisturbingOtherBits()
    {
        var state = new CpuState { P = 0x00 };

        state.C = true;
        state.N = true;
        Assert.Equal(0x81, state.P);

        state.C = false;
        Assert.Equal(0x80, state.P);
    }
}
