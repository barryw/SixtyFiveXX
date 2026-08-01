using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class UnstableOpTests
{
    [Fact]
    public void Ane_MixesTheMagicConstantIntoTheAccumulator()
    {
        // (A | $EE) & X & imm — verified against Harte vector "8b 1".
        var (cpu, _) = TestMachine.Flat(0x0200, 0x8B, 0x23);
        cpu.State.A = 0xE4;
        cpu.State.X = 0xE2;

        var cycles = cpu.Step();

        Assert.Equal(0x22, cpu.State.A);
        Assert.Equal(0xE2, cpu.State.X);   // X is unchanged
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Lxa_LoadsBothRegistersThroughTheMagicConstant()
    {
        // (A | $EE) & imm — verified against Harte vector "ab 1".
        var (cpu, _) = TestMachine.Flat(0x0200, 0xAB, 0xE4);
        cpu.State.A = 0xAE;
        cpu.State.X = 0x8D;

        var cycles = cpu.Step();

        Assert.Equal(0xE4, cpu.State.A);
        Assert.Equal(0xE4, cpu.State.X);
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void Las_AndsMemoryWithTheStackPointerIntoAllThreeRegisters()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xBB, 0x00, 0x30);   // LAS $3000,Y
        cpu.State.Y = 0x10;
        cpu.State.S = 0xF0;
        ram[0x3010] = 0x3C;

        var cycles = cpu.Step();

        var expected = (byte)(0x3C & 0xF0);
        Assert.Equal(expected, cpu.State.A);
        Assert.Equal(expected, cpu.State.X);
        Assert.Equal(expected, cpu.State.S);
        Assert.Equal(4, cycles);
    }

    [Fact]
    public void Sha_StoresAccumulatorAndXAndTheAddressHighBytePlusOne()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x9F, 0x00, 0x30);   // SHA $3000,Y
        cpu.State.Y = 0x10;
        cpu.State.A = 0xFF;
        cpu.State.X = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x31, ram[0x3010]);   // $FF & $FF & ($30 + 1)
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Shx_StoresXAndTheAddressHighBytePlusOne()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x9E, 0x00, 0x30);   // SHX $3000,Y
        cpu.State.Y = 0x10;
        cpu.State.X = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x31, ram[0x3010]);   // $FF & ($30 + 1)
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Shy_StoresYAndTheAddressHighBytePlusOne()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x9C, 0x00, 0x30);   // SHY $3000,X
        cpu.State.X = 0x10;
        cpu.State.Y = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x31, ram[0x3010]);
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Tas_SetsStackPointerToAAndXThenStoresWithTheHighByte()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x9B, 0x00, 0x30);   // TAS $3000,Y
        cpu.State.Y = 0x10;
        cpu.State.A = 0xFF;
        cpu.State.X = 0xF0;

        var cycles = cpu.Step();

        Assert.Equal(0xF0, cpu.State.S);   // S = A & X
        Assert.Equal(0x30, ram[0x3010]);   // S & ($30 + 1)
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Shx_PageCross_FoldsTheStoredValueIntoTheHighByte()
    {
        // SHX $30FF,Y with Y=1 crosses a page: indexing wraps the nominal address to
        // $3000, and UnstableStoreFixup (the only new engine code path in Phase 2a)
        // folds the ANDed value into the target's high byte on a page cross, so the
        // write actually lands at $3100, not $3000. Pinned from the vector-certified
        // implementation — this is a regression guard, not a new correctness claim.
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x9E, 0xFF, 0x30);   // SHX $30FF,Y
        cpu.State.Y = 0x01;
        cpu.State.X = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x31, ram[0x3100]);   // $FF & ($30 + 1), written to the folded address
        Assert.Equal(0x00, ram[0x3000]);   // the nominal (unindexed-wrap) address is untouched
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void Sha_IndirectIndexed_StoresAtThePointerTargetWithoutACross()
    {
        // $93 is the only IndirectIndexed unstable store and has its own emission
        // prefix in MicroOpTable (FetchAddrLo, PtrReadLo, PtrReadHiY). Non-page-crossing
        // case: pointer at zp $10/$11 resolves to $3000, +Y ($10) stays in the page.
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x93, 0x10);   // SHA ($10),Y
        ram[0x10] = 0x00;
        ram[0x11] = 0x30;
        cpu.State.Y = 0x10;
        cpu.State.A = 0xFF;
        cpu.State.X = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x31, ram[0x3010]);   // $FF & $FF & ($30 + 1)
        Assert.Equal(6, cycles);
    }

    [Fact]
    public void UnstableStores_DoNotAffectFlags()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x9E, 0x00, 0x30);
        cpu.State.Y = 0x10;
        cpu.State.X = 0x00;
        cpu.State.P = Flag.U;

        cpu.Step();

        Assert.Equal(Flag.U, cpu.State.P);
    }
}
