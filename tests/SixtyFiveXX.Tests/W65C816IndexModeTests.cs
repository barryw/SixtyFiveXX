using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Direct-page indexed by Y — the one addressing mode phase 7c adds, used by <c>LDX</c> and
/// <c>STX</c> and by nothing else on the part. Its wrapping and bank-confinement rules are the
/// same ones <c>dp,X</c> obeys, and the same ones phase 7b's reviews twice found wired the wrong
/// way round for a stack mode with no vector coverage; each test below was verified to fail
/// against a deliberately broken version of the corresponding production line.
/// </summary>
public class W65C816IndexModeTests
{
    /// <summary>
    /// Emulation mode with <c>DL == $00</c>: the index add wraps within the direct page and keeps
    /// <c>DH</c>. Clark's appendix is explicit that DH need not be zero, which is why DP is
    /// $FF00 here rather than $0000. Fails against an implementation that wraps at 16 bits
    /// instead: the read would land on $000001, the decoy.
    /// </summary>
    [Fact]
    public void LdxDirectPageY_EmulationMode_WrapsWithinThePage()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xB6;       // LDX dp,Y
        ram[0xC001] = 0xFF;       // DO -> D + DO = $FFFF
        ram[0x00FF01] = 0x42;     // wrapped: (DP & $FF00) | (($FFFF + 2) & $FF)
        ram[0x000001] = 0x99;     // decoy: where a 16-bit wrap would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;       // emulation: x is forced to 1, so this is an 8-bit load
        cpu.State.DP = 0xFF00;    // DL == $00, DH == $FF
        cpu.State.Y = 0x0002;

        cpu.Step();

        Assert.Equal(0x0042, cpu.State.X);
    }

    /// <summary>
    /// The same addresses in native mode, where the page wrap does not apply: the add is a plain
    /// 16-bit one and lands on $000001. The mirror of the test above — together they pin both
    /// arms of the condition, so neither can be deleted unnoticed.
    /// </summary>
    [Fact]
    public void LdxDirectPageY_NativeMode_DoesNotWrapWithinThePage()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xB6;       // LDX dp,Y
        ram[0xC001] = 0xFF;       // DO -> D + DO = $FFFF
        ram[0x000001] = 0x42;     // ($FFFF + 2) & $FFFF
        ram[0x00FF01] = 0x99;     // decoy: where the emulation-mode wrap would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = true;   // 8-bit index, so only one byte is read
        cpu.State.DP = 0xFF00;
        cpu.State.Y = 0x0002;

        cpu.Step();

        Assert.Equal(0x0042, cpu.State.X);
    }

    /// <summary>
    /// dp,Y is bank-0 confined, so a 16-bit load whose low byte sits at $00FFFF takes its high
    /// byte from $000000 — not $010000. Zero vector coverage: it needs x = 0 with D + DO landing
    /// exactly on $FFFF. Fails if <c>DirectPageY</c> is left out of
    /// <c>MicroOpTable.EmitAddressed816</c>'s bank-0 exclusion set, which is the mistake phase
    /// 7b's review found in the opposite direction for <c>(sr,S),Y</c>.
    /// <para>
    /// <c>m</c> and <c>x</c> are set deliberately opposed, the same way
    /// <see cref="Stz_TakesItsWidthFromTheAccumulatorFlag"/> does, so this also discriminates
    /// <c>LDX</c>'s <see cref="Width"/> source. Measured, not assumed: with both flags left at
    /// their default this test passes against all five <c>LDX</c> entries mutated to
    /// <see cref="Width.M"/>, because <c>CpuState.P</c> starts at $00 — the core's constructor
    /// does not <c>Reset()</c> — so <c>m</c> and <c>x</c> agree and the two widths resolve
    /// <c>_wide</c> identically. That is the same uniform-slip blind spot task 4 measured in
    /// <c>W65C816WidthTests</c>, which only requires opcodes to agree with each other.
    /// </para>
    /// </summary>
    [Fact]
    public void LdxDirectPageY_SixteenBit_WrapsTheHighByteWithinBankZero()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xB6;       // LDX dp,Y
        ram[0xC001] = 0x00;       // DO -> D + DO + Y = $FFFF
        ram[0x00FFFF] = 0x34;     // data low
        ram[0x000000] = 0x12;     // data high, wrapped within bank 0
        ram[0x010000] = 0x99;     // decoy: where a wrongly-carrying read would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = false;  // 16-bit index
        cpu.State.M = true;       // 8-bit accumulator — opposed, so Width.M would read one byte
        cpu.State.DP = 0xFFF0;    // DL != $00, so no page wrap
        cpu.State.Y = 0x000F;     // $FFF0 + $00 + $0F = $FFFF

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.X);
    }

    /// <summary>
    /// A 16-bit index store writes two bytes, low first. <c>m</c> and <c>x</c> are opposed for
    /// the reason
    /// <see cref="LdxDirectPageY_SixteenBit_WrapsTheHighByteWithinBankZero"/> gives: without
    /// that, a <c>STX</c> uniformly mis-declared <see cref="Width.M"/> passes this.
    /// </summary>
    [Fact]
    public void Stx_SixteenBitIndex_WritesBothBytes()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x86;       // STX dp
        ram[0xC001] = 0x10;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.XFlag = false;  // 16-bit index
        cpu.State.M = true;       // 8-bit accumulator — opposed, so Width.M would write one byte
        cpu.State.DP = 0x0000;
        cpu.State.X = 0x1234;

        cpu.Step();

        Assert.Equal(0x34, ram[0x000010]);
        Assert.Equal(0x12, ram[0x000011]);
    }

    /// <summary>
    /// STZ stores an accumulator-width zero, so its width comes from m even though it names no
    /// register. Set up with m and x deliberately opposed: a Width.X mis-declaration would write
    /// one byte and leave the decoy at $11 intact.
    /// </summary>
    [Fact]
    public void Stz_TakesItsWidthFromTheAccumulatorFlag()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x64;       // STZ dp
        ram[0xC001] = 0x10;
        ram[0x000010] = 0xAA;
        ram[0x000011] = 0xBB;     // decoy: untouched if STZ were sized by x

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.XFlag = true;   // 8-bit index — the flag a wrong declaration would read
        cpu.State.DP = 0x0000;

        cpu.Step();

        Assert.Equal(0x00, ram[0x000010]);
        Assert.Equal(0x00, ram[0x000011]);
    }
}
