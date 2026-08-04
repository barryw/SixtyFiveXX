using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// <c>REP</c> and <c>SEP</c> — 65816 width-flag instructions. <c>REP #$xx</c> clears the P
/// bits set in the operand; <c>SEP #$xx</c> sets them. Research document §9's "Immediate, and
/// REP/SEP" and §7's settled table: always 3 cycles, VPA low on the third, <c>m</c>/<c>x</c>
/// clamped to 1 whenever <c>e</c> is 1, and <c>XH</c>/<c>YH</c> forced to $00 the instant
/// <c>x</c> reads 1.
/// </summary>
public class RepSepTests
{
    /// <summary>
    /// The width-transition case that motivates a dedicated test: native mode, REP clears the
    /// m and x bits, so 8-bit index registers become 16-bit and stay whatever they already
    /// held. Also proves the plain bit-clear semantics against bits REP does not touch.
    /// </summary>
    [Fact]
    public void Rep_ClearsOnlyTheBitsSetInTheOperand()
    {
        var (cpu, _) = TestMachine.Flat<W65C816Variant>(0xC000, 0xC2, 0x30); // REP #$30 (m, x)
        cpu.State.E = false;
        cpu.State.P = (byte)(Flag.M | Flag.X | Flag.C | Flag.Z); // m, x, c, z all set

        cpu.Step();

        Assert.False(cpu.State.M);
        Assert.False(cpu.State.XFlag);
        Assert.True(cpu.State.C);    // untouched bit stays set
        Assert.True(cpu.State.Z);    // untouched bit stays set
    }

    [Fact]
    public void Sep_SetsOnlyTheBitsSetInTheOperand()
    {
        var (cpu, _) = TestMachine.Flat<W65C816Variant>(0xC000, 0xE2, 0x01); // SEP #$01 (c)
        cpu.State.E = false;
        cpu.State.P = 0;

        cpu.Step();

        Assert.True(cpu.State.C);
        Assert.False(cpu.State.M);
        Assert.False(cpu.State.XFlag);
        Assert.False(cpu.State.Z);
    }

    /// <summary>
    /// research document §7 / Clark §6.4.2, verbatim: "when the e flag is 1, the m and x flag
    /// are forced to 1, so after the REP or SEP, both flags will still be 1 no matter what the
    /// operand is." A REP that asks to clear both must fail silently in emulation mode.
    /// </summary>
    [Fact]
    public void Rep_InEmulationMode_CannotClearMOrX()
    {
        var (cpu, _) = TestMachine.Flat<W65C816Variant>(0xC000, 0xC2, 0x30); // REP #$30 (m, x)
        cpu.State.E = true;
        cpu.State.M = true;
        cpu.State.XFlag = true;

        cpu.Step();

        Assert.True(cpu.State.M);
        Assert.True(cpu.State.XFlag);
    }

    /// <summary>Emulation-mode SEP is a no-op on m/x — they are already forced to 1.</summary>
    [Fact]
    public void Sep_InEmulationMode_MAndXStayOne()
    {
        var (cpu, _) = TestMachine.Flat<W65C816Variant>(0xC000, 0xE2, 0x30); // SEP #$30 (m, x)
        cpu.State.E = true;
        cpu.State.M = true;
        cpu.State.XFlag = true;

        cpu.Step();

        Assert.True(cpu.State.M);
        Assert.True(cpu.State.XFlag);
    }

    /// <summary>
    /// research document §7: when x becomes 1, XH and YH are forced to $00 immediately — the
    /// same invariant XCE already enforces on an E transition. SEP #$10 sets x from a native,
    /// 16-bit-index start, so this is the moment the high bytes must be dropped, not merely
    /// initialised to zero because they always were.
    /// </summary>
    [Fact]
    public void Sep_SettingXFlag_ForcesXHAndYHToZeroImmediately()
    {
        var (cpu, _) = TestMachine.Flat<W65C816Variant>(0xC000, 0xE2, 0x10); // SEP #$10 (x)
        cpu.State.E = false;
        cpu.State.XFlag = false;
        cpu.State.X = 0x1234;
        cpu.State.Y = 0x5678;

        cpu.Step();

        Assert.True(cpu.State.XFlag);
        Assert.Equal(0x0034, cpu.State.X);
        Assert.Equal(0x0078, cpu.State.Y);
    }

    /// <summary>
    /// Leaving x set (REP that does not touch bit 4, or one whose operand excludes it) must
    /// not disturb X/Y's low byte — only entering 8-bit mode forces anything.
    /// </summary>
    [Fact]
    public void Rep_NotClearingXFlag_LeavesIndexRegistersAlone()
    {
        var (cpu, _) = TestMachine.Flat<W65C816Variant>(0xC000, 0xC2, 0x01); // REP #$01 (c only)
        cpu.State.E = false;
        cpu.State.XFlag = true;
        cpu.State.X = 0x0099;
        cpu.State.Y = 0x00AA;

        cpu.Step();

        Assert.True(cpu.State.XFlag);
        Assert.Equal(0x0099, cpu.State.X);
        Assert.Equal(0x00AA, cpu.State.Y);
    }

    /// <summary>
    /// With x already clear (native mode, 16-bit index registers), an operand that does not
    /// touch bit 4 must leave X/Y's full 16-bit values alone — the forcing in
    /// <c>Cpu.Exec.cs</c>'s <c>Op.Rep</c> case is conditional on <c>_s.XFlag</c>, not
    /// unconditional, and nothing in research document §7 zeros the high byte while x stays 0.
    /// Seeded with values whose high byte is non-zero (0x1234/0x5678) rather than values that
    /// already fit in 8 bits, so an accidental <c>_s.X &amp;= 0x00FF</c> — unconditional, or
    /// behind an inverted <c>XFlag</c> check — actually changes the result instead of being a
    /// no-op the assertion cannot see.
    /// </summary>
    [Fact]
    public void Rep_WithXFlagClear_LeavesFull16BitIndexRegistersAlone()
    {
        var (cpu, _) = TestMachine.Flat<W65C816Variant>(0xC000, 0xC2, 0x01); // REP #$01 (c only)
        cpu.State.E = false;
        cpu.State.XFlag = false;
        cpu.State.X = 0x1234;
        cpu.State.Y = 0x5678;

        cpu.Step();

        Assert.False(cpu.State.XFlag);
        Assert.Equal(0x1234, cpu.State.X);
        Assert.Equal(0x5678, cpu.State.Y);
    }

    /// <summary>
    /// Datasheet Note 1, verbatim: "REP, SEP are always 3 cycle instructions". Always, not
    /// "3-m" the way plain immediate instructions vary. <see cref="AddrMode.ImmediateByte"/>
    /// records that in the opcode table — the single source of truth for operand length and
    /// cycle count — but today <c>Emit816</c> branches on <c>info.Operation</c>, never on
    /// <c>info.Mode</c>, so nothing actually reads the mode yet and this test is what pins the
    /// 3-cycle count in the meantime. The mode is for Task 5: once <c>LDA</c>/<c>STA</c>'s
    /// immediate forms make the emitter distinguish it from the m-dependent
    /// <see cref="AddrMode.Immediate"/>, a wrong mode here would silently let REP/SEP's cycle
    /// count float with m too.
    /// </summary>
    [Theory]
    [InlineData(0xC2)]
    [InlineData(0xE2)]
    public void RepSep_AlwaysTakesThreeCycles(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat<W65C816Variant>(0xC000, opcode, 0x00);
        cpu.State.E = false;

        var cycles = cpu.Step();

        Assert.Equal(3, cycles);
    }

    /// <summary>
    /// Datasheet Note 1, verbatim: "VPA is low during the third cycle. The address bus is
    /// PC+1 during the third cycle." Cycle 1 is the opcode fetch; cycle 2 reads the operand at
    /// PC+1 (VDA=0 VPA=1); cycle 3 is the settle cycle this asserts. Per research document §9's
    /// own convention, every non-data, non-program-read cycle in the table is VDA=0 VPA=0 (an
    /// internal cycle, <see cref="IBus.Internal"/>) — there is no VDA=1 VPA=0 cycle without an
    /// actual data value and no VDA=0 VPA=1 cycle that is not a live program-stream read, so
    /// cycle 3 is internal, not a second real read of PC+1.
    /// </summary>
    [Theory]
    [InlineData(0xC2)]
    [InlineData(0xE2)]
    public void RepSep_ThirdCycle_IsInternalAtOperandAddress(byte opcode)
    {
        var (cpu, _, log) = TestMachine.Logged<W65C816Variant>(0xC000, opcode, 0x00);
        cpu.State.E = false;

        cpu.Tick(); // opcode fetch
        cpu.Tick(); // operand fetch at PC+1 = $C001
        Assert.Equal(0xC001, log[^1].Address);

        cpu.Tick(); // cycle 3: internal, same address per Note 1
        Assert.True(cpu.AtInstructionBoundary);
        // LoggingBus has no Internal override, so an internal cycle never reaches it and the
        // log does not grow on cycle 3 — this is the assertion that no bus access occurred.
        Assert.Equal(2, log.Count);
        // Note 1's other half: "The address bus is PC+1 during the third cycle" — the operand
        // byte's own address, $C001, not $C002 (a bare cpu.PC copy-pasted from
        // ImpliedExec816's PBR,PC, which would be wrong here since RepSepOperand already
        // advanced PC past the operand).
        Assert.Equal(0xC001, cpu.LastAddress);
    }
}
