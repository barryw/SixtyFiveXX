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

    [Fact]
    public void NativeModeFlagAliases_ShareBitsWithBreakAndUnused()
    {
        // One bit, two meanings — bit 4 is b in emulation mode and x in native mode. This
        // asserts the sharing deliberately, so that "fixing" it to distinct bits fails here
        // rather than thousands of vectors later. See research doc §3.1.
        Assert.Equal(Flag.B, Flag.X);
        Assert.Equal(Flag.U, Flag.M);
        Assert.Equal(0x10, Flag.X);
        Assert.Equal(0x20, Flag.M);
    }

    [Fact]
    public void WidthProperties_ReadAndWriteTheirBits()
    {
        var state = new CpuState { P = 0x00 };

        state.M = true;
        Assert.Equal(0x20, state.P);
        Assert.True(state.M);
        Assert.False(state.XFlag);

        state.XFlag = true;
        Assert.Equal(0x30, state.P);

        state.M = false;
        Assert.Equal(0x10, state.P);
        Assert.True(state.XFlag);
    }

    [Fact]
    public void NewRegisters_DefaultToZero()
    {
        var state = new CpuState();

        Assert.Equal(0, state.DP);
        Assert.Equal(0, state.DBR);
        Assert.Equal(0, state.PBR);
        Assert.False(state.E);
    }

    [Fact]
    public void Registers_HoldSixteenBitValues()
    {
        var state = new CpuState { A = 0x1234, X = 0x5678, Y = 0x9ABC, S = 0x01FF, DP = 0xDEF0 };

        Assert.Equal(0x1234, state.A);
        Assert.Equal(0x5678, state.X);
        Assert.Equal(0x9ABC, state.Y);
        Assert.Equal(0x01FF, state.S);
        Assert.Equal(0xDEF0, state.DP);
    }

    [Fact]
    public void ToString_OmitsTheSixteenBitTailWhenNothingInItIsSet()
    {
        var state = new CpuState { PC = 0xC000, A = 0x42, X = 0x01, Y = 0x02, S = 0xFD, P = 0x24 };

        Assert.Equal("PC:C000 A:0042 X:0001 Y:0002 S:00FD P:24", state.ToString());
    }

    [Fact]
    public void ToString_ShowsTheSixteenBitTailWhenAnyOfItIsSet()
    {
        var state = new CpuState
        {
            PC = 0xC000, A = 0x1234, X = 0x01, Y = 0x02, S = 0x01FD, P = 0x24,
            DBR = 0x7E, PBR = 0x01, DP = 0x2000, E = true,
        };

        Assert.Equal(
            "PC:C000 A:1234 X:0001 Y:0002 S:01FD P:24 DBR:7E PBR:01 DP:2000 E:1",
            state.ToString());
    }
}
