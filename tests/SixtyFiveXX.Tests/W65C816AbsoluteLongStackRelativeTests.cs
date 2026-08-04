using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Discrimination tests for task 6's absolute, long and stack-relative family — rules the
/// SingleStepTests/65816 vectors do not exercise (or exercise too rarely for a 10,000-vector
/// fuzz run to land on), named explicitly in the task brief: bank carry at <c>$xxFFFF</c> for
/// the DBR-relative modes, the stack-relative modes' lack of the direct-page penalty (<c>w</c>),
/// and <c>long,X</c> paying no page-cross penalty. Each test was verified to fail against a
/// deliberately broken version of the corresponding production code before being restored — see
/// the task 6 report for the failure output.
/// </summary>
public class W65C816AbsoluteLongStackRelativeTests
{
    /// <summary>
    /// Bruce Clark, "65C816 Opcodes" §5.2, Example 2, verbatim: "If the DBR is $12 and the m
    /// flag is 0, then LDA $FFFF loads the low byte of the data from address $12FFFF, and the
    /// high byte from address $130000." Task 5's review found and fixed the identical bug for
    /// <c>(dp)</c>; this is the same rule measured against <c>$AD</c> (plain absolute) instead,
    /// which is the mode Clark's own example describes. A 64 KB bus could not tell this test
    /// apart from a wrongly-wrapping-within-bank-$12 implementation — both formulas alias to the
    /// same masked address once the bank is discarded — which is exactly why <c>BankedBus</c>
    /// (a full 24-bit address space) is required here.
    /// </summary>
    [Fact]
    public void LdaAbsolute_SixteenBit_CarriesTheHighByteIntoTheNextBank()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xAD;       // LDA abs
        ram[0xC001] = 0xFF;       // AAL
        ram[0xC002] = 0xFF;       // AAH -> AA = $FFFF
        ram[0x12FFFF] = 0x34;     // data low, DBR,AA
        ram[0x130000] = 0x12;     // data high, carried into bank $13
        ram[0x120000] = 0x99;     // decoy: where a wrongly-wrapping-within-bank-$12 read would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.DBR = 0x12;

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.A);
    }

    /// <summary>
    /// The same rule as <see cref="LdaAbsolute_SixteenBit_CarriesTheHighByteIntoTheNextBank"/>,
    /// for <c>(sr,S),Y</c> instead — a self-review finding during this task: the mode's own
    /// bank-0 pointer fetch (<c>0,S+SO</c>) made it easy to mistake for bank-0-confined like
    /// plain <c>sr,S</c>, but <c>(sr,S),Y</c>'s <em>final</em> access goes through <c>DBR</c> —
    /// <c>DBR,AA+Y</c> (research document §9, "(Stack Relative),Y — row 24", cycles 7/7a) — the
    /// same shape as <c>(dp),Y</c>, and must carry into the next bank exactly as that mode does.
    /// The reviewing agent reproduced the bug this test pins: with <c>M=0</c> and the indexed
    /// pointer landing on <c>$xxFFFF</c>, the high byte wrongly wrapped to <c>$120000</c> instead
    /// of carrying to <c>$130000</c>, loading <c>$9934</c> (the decoy) instead of <c>$1234</c>.
    /// No SingleStepTests vector catches it: it needs both <c>M=0</c> and the indexed pointer to
    /// land exactly on a bank boundary, which 10,000 random vectors for <c>$B3</c>/<c>$93</c>
    /// never happen to hit.
    /// </summary>
    [Fact]
    public void LdaStackRelativeIndirectY_SixteenBit_CarriesTheHighByteIntoTheNextBank()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xB3;       // LDA (sr,S),Y
        ram[0xC001] = 0x01;       // SO -> pointer at 0,S+SO = 0,$01FF+$01 = $000200
        ram[0x000200] = 0xFE;     // pointer low (AAL)
        ram[0x000201] = 0xFF;     // pointer high (AAH) -> AA = $FFFE
        ram[0x12FFFF] = 0x34;     // data low, DBR,AA+Y = $12,$FFFE+$01 = $12FFFF
        ram[0x130000] = 0x12;     // data high, carried into bank $13
        ram[0x120000] = 0x99;     // decoy: where a wrongly-wrapping-within-bank-$12 read would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit accumulator
        cpu.State.DBR = 0x12;
        cpu.State.Y = 0x0001;

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.A);
    }

    /// <summary>
    /// Research document §5: "<c>w</c> appears only on direct-page modes" — <c>sr,S</c> is flat
    /// <c>5-m</c> in Clark, with no <c>w</c> term at all, unlike <c>dp</c>'s <c>4-m+w</c>. This
    /// sets <c>DL = $00</c> deliberately: that is the exact condition
    /// <see cref="MicroOp.FetchDpOffset"/> tests to decide whether to skip
    /// <see cref="MicroOp.DirectPagePenalty"/>, so it is the value that would expose
    /// <c>sr,S</c>'s cycle count wrongly dropping to 3 if its own penalty cycle were ever wired
    /// through that same DL-gated skip by mistake. Checks both the cycle count (<c>5-m</c> with
    /// <c>m=1</c> is 4) and the loaded value, so a broken version that skips the cycle but still
    /// happens to read the right byte cannot pass by accident.
    /// </summary>
    [Fact]
    public void LdaStackRelative_PaysItsPenaltyCycle_EvenWhenDirectPageLowByteIsZero()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xA3;       // LDA sr,S
        ram[0xC001] = 0x05;       // SO
        ram[0x000204] = 0x42;     // data at 0,S+SO = 0,$01FF+$05 = $000204 (S set by Make)

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator: 5-m = 4 total cycles
        cpu.State.DP = 0x0000;    // DL == $00 — the direct-page skip condition, deliberately

        var cycles = cpu.Step();

        Assert.Equal(4, cycles);
        Assert.Equal(0x42, cpu.State.A & 0xFF);
    }

    /// <summary>
    /// Research document §5: <c>long,X</c> is flat <c>6-m</c>, with no <c>p</c> term — "no
    /// indexing cycle exists for this mode" (§9, "Absolute Long,X — row 5"), unlike <c>abs,X</c>
    /// (<c>6-m-x+x*p</c>), whose 24-bit add needs no fixup either way. <c>X</c> is chosen so that
    /// <c>AAL+X</c> overflows the low byte — the exact condition that would cost <c>abs,X</c> an
    /// extra cycle — to prove <c>long,X</c> does not pay it. Checks both the cycle count and the
    /// loaded value together, the same discipline as the stack-relative test above.
    /// </summary>
    [Fact]
    public void LdaAbsoluteLongX_IndexingAcrossAPageBoundary_PaysNoExtraCycle()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xBF;       // LDA long,X
        ram[0xC001] = 0xFF;       // AAL
        ram[0xC002] = 0x00;       // AAH -> AA = $00FF
        ram[0xC003] = 0x12;       // AAB -> $12
        ram[0x120100] = 0x77;     // data at AAB,AA+X = $12,$00FF+$01 = $120100 (crosses the page)

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator: 6-m = 5 total cycles
        cpu.State.XFlag = true;   // 8-bit index
        cpu.State.X = 0x01;

        var cycles = cpu.Step();

        Assert.Equal(5, cycles);
        Assert.Equal(0x77, cpu.State.A & 0xFF);
    }
}
