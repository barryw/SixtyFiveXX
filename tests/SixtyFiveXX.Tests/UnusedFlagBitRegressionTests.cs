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
        cpu.State.A = 0x1234;                                  // nonzero high byte the 8-bit path must preserve

        cpu.Step();

        // Buggy path: _data16 has never been written on this core and reads 0, so A becomes
        // $0000 and Z is wrongly set. Correct 8-bit path: only the low byte is replaced.
        Assert.Equal(0x1242, cpu.State.A);
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
