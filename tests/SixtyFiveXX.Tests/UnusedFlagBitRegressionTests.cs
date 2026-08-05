using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Code-review regression: Task 5 made <c>Op.Lda</c>/<c>Op.Sta</c>'s <c>Exec()</c> cases branch
/// on <c>_s.M</c> to pick an 8- or 16-bit path. <c>Flag.M</c> is <c>0x20</c> — the same bit as
/// <c>Flag.U</c>, the 6502/6510/65C02 "unused" flag, which is publicly writable through
/// <see cref="Cpu{TBus,TVariant}.State"/> and normally reads as set. Clearing bit 5 of P on an
/// 8-bit core must not change which path <c>LDA</c>/<c>STA</c> take: the 16-bit path reads from
/// and writes to <c>_data16</c>, a field no 8-bit-core micro-op sequence ever touches, so taking
/// it silently reads whatever <c>_data16</c> last held (always <c>0</c>, since nothing on these
/// cores ever writes it) instead of the byte the addressing mode actually fetched.
/// <para>
/// <b>Which mutation these defend against changed in phase 7c, task 2.</b> The width test moved
/// out of these <c>Exec()</c> arms and into <c>_wide</c>, and an 8-bit core is now kept off the
/// 16-bit path by three independent things: (1) every 8-bit opcode table leaves
/// <c>OpcodeInfo.Width</c> at <c>Width.None</c>, which resolves to <see langword="false"/>
/// whatever P holds; (2) <c>FetchOpcode</c> assigns <c>_wide</c> only inside
/// <c>if (TVariant.Variant == CpuVariant.W65C816)</c>; (3) <c>Op.Lda</c>/<c>Op.Sta</c> read it
/// only behind <c>TVariant.Variant != CpuVariant.W65C816 ||</c>. Measured, by mutating the source
/// and re-running these two tests: deleting (2) alone still passes, because (1) holds <c>_wide</c>
/// false regardless; replacing the whole resolution with a bare <c>_wide = !_s.M</c> — deleting
/// (1) and (2) together, which is exactly the pre-task-2 live-flag read — <i>also</i> still
/// passes, because (3) catches it. Both tests fail only when (3) goes too, at which point
/// <c>LDA</c> leaves <c>A</c> at <c>$0000</c> with <c>Z</c> set and <c>STA</c> writes a stale
/// <c>_data</c>. So (3) is the load-bearing one these tests pin, and it is not the redundant
/// belt-and-braces it looks like.
/// </para>
/// <para>
/// Not caught by conformance: 0 of 10,000 <c>6502/a5</c> vectors and 0 of 10,000
/// <c>wdc65c02/a5</c> vectors have bit 5 of P clear, so the suite never exercises this branch.
/// </para>
/// </summary>
public class UnusedFlagBitRegressionTests
{
    [Fact]
    public void Lda_OnA6502WithUnusedFlagClear_StillTakesTheEightBitPath()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xA5, 0x10); // LDA $10
        ram[0x0010] = 0x42;
        cpu.State.P &= unchecked((byte)~Flag.U);               // clear bit 5
        cpu.State.A = 0x1234;                                  // junk in a high byte a 6502 does not have

        cpu.Step();

        // Buggy path: _wide is set, _data16 has never been written on this core and reads 0, so A
        // becomes $0000 and Z is wrongly set. Correct 8-bit path: A8's setter assigns the whole
        // 16-bit field, so A becomes $0042 — the high byte is not preserved and must not be. It
        // is non-architectural on a 6502 (CpuState.A is 16 bits only because the struct is shared
        // with the 65816), thirteen of the fourteen A-writing operations already zero it, and
        // Op.Lda preserved it before phase 7c only as an artifact of a hand-rolled expression
        // that has since folded onto A8's setter. See A8's remarks in Cpu.cs.
        Assert.Equal(0x0042, cpu.State.A);
        Assert.False(cpu.State.Z);
    }

    [Fact]
    public void Sta_OnA6502WithUnusedFlagClear_StillWritesTheEightBitValue()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x85, 0x10); // STA $10
        cpu.State.P &= unchecked((byte)~Flag.U);               // clear bit 5
        cpu.State.A = 0x1299;

        cpu.Step();

        // Buggy path: Op.Sta's else branch sets _data16, not _data, and ExecWrite (the 6502's
        // own STA micro-op) writes _data — which nothing set this instruction, so the stale
        // value (0 on a fresh core) lands in memory instead of A's low byte.
        Assert.Equal(0x99, ram[0x0010]);
    }
}
