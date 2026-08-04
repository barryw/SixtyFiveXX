using System.Reflection;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Code-review coverage for three defects in Task 5's seven direct-page addressing modes, none
/// of which any SingleStepTests/65816 vector reaches — see the task 5 review and
/// <c>docs/superpowers/research/2026-08-03-65816-reference-sources.md</c> §7 and §9.
/// <list type="bullet">
/// <item>Finding 2: a 16-bit access's high byte must carry into the next bank for every
/// DBR-relative and long mode, and must stay confined to bank 0 only for plain direct page.</item>
/// <item>Finding 3: the indirect pointer's own <c>+1</c> read must page-wrap in emulation mode
/// with <c>DL == $00</c> for the "old" modes — <c>(dp)</c>, <c>(dp,X)</c>, <c>(dp),Y</c> — and
/// must not for the "new" <c>[dp]</c>/<c>[dp],Y</c>.</item>
/// <item>Finding 4: the indexing-cycle skip for <c>(dp),Y</c> must be selected from
/// <c>info.Access</c> at table-build time, not from <c>_op == Op.Lda</c> at run time.</item>
/// </list>
/// </summary>
public class W65C816DirectPageIndirectTests
{
    /// <summary>
    /// A full 24-bit address space, unlike <see cref="FlatBus"/> (64 KB, masks away the bank
    /// entirely) — required here because these tests exist specifically to distinguish two
    /// different <em>banks</em>, which a 16-bit-masking bus cannot do: both the wrapping and the
    /// non-wrapping formula collapse to the same masked address once the bank is discarded.
    /// Sparse (<see cref="Dictionary{TKey,TValue}"/>-backed, unset reads as $00), the same shape
    /// <c>Harte816Bus</c> in the conformance project uses.
    /// </summary>
    private sealed class BankedBus : IBus
    {
        private readonly Dictionary<int, byte> _ram = [];
        public byte this[int address]
        {
            get => _ram.GetValueOrDefault(address & 0xFFFFFF);
            set => _ram[address & 0xFFFFFF] = value;
        }
        public byte Read(int address) => this[address];
        public void Write(int address, byte value) => this[address] = value;
    }

    private static Cpu<RefBus, W65C816Variant> MakeCpu(BankedBus ram, ushort pc = 0xC000)
    {
        var cpu = new Cpu<RefBus, W65C816Variant>(new RefBus(ram));
        cpu.State.PC = pc;
        cpu.State.S = 0x01FF;
        return cpu;
    }

    // ---- Finding 2: bank-0 wrap vs. bank carry -----------------------------------------

    /// <summary>
    /// Regression guard for the bank-0-confined family (plain direct page): must keep wrapping
    /// within bank 0, not start carrying into bank 1. D + DO = $FFFF, forcing the high-byte "+1"
    /// read to either wrap to $000000 (correct) or, if plain direct page were wrongly switched to
    /// the carrying formula, spill into $010000.
    /// </summary>
    [Fact]
    public void LdaDirectPage_SixteenBit_WrapsTheHighByteWithinBankZero()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xA5;              // LDA dp
        ram[0xC001] = 0xFF;              // DO
        ram[0x00FFFF] = 0x34;            // low byte at D+DO = $FF00+$FF = $FFFF
        ram[0x000000] = 0x12;            // high byte, wrapped within bank 0
        ram[0x010000] = 0x99;            // decoy: where a wrongly-carrying read would land

        var cpu = MakeCpu(ram);
        cpu.State.E = false;
        cpu.State.M = false;             // 16-bit accumulator
        cpu.State.DP = 0xFF00;           // D + DO = $FFFF

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.A);
    }

    /// <summary>
    /// Finding 2's actual fix. Clark, "65C816 Opcodes" §5.2, Example 2, verbatim: "If the DBR is
    /// $12 and the m flag is 0, then LDA $FFFF loads the low byte of the data from address
    /// $12FFFF, and the high byte from address $130000." <c>(dp)</c> is a DBR-relative mode, so
    /// the same rule applies once the pointer resolves to $FFFF. A 64 KB bus could not tell this
    /// test apart from the wrapping case — both formulas alias to the same masked address once
    /// the bank is discarded — which is exactly why the bug had zero vector coverage.
    /// </summary>
    [Fact]
    public void LdaDirectPageIndirect_SixteenBit_CarriesTheHighByteIntoTheNextBank()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xB2;              // LDA (dp)
        ram[0xC001] = 0x10;              // DO
        ram[0x000010] = 0xFF;            // pointer low
        ram[0x000011] = 0xFF;            // pointer high -> AA = $FFFF
        ram[0x12FFFF] = 0x34;            // data low, DBR,AA
        ram[0x130000] = 0x12;            // data high, carried into bank $13
        ram[0x120000] = 0x99;            // decoy: where a wrongly-wrapping read would land

        var cpu = MakeCpu(ram);
        cpu.State.E = false;
        cpu.State.M = false;             // 16-bit accumulator
        cpu.State.DP = 0x0000;
        cpu.State.DBR = 0x12;

        cpu.Step();

        Assert.Equal(0x1234, cpu.State.A);
    }

    // ---- Finding 3: pointer's own +1 read, old vs. new modes ---------------------------

    /// <summary>
    /// Finding 3's actual fix. Clark's appendix, verbatim: "if the D register is $0000 (and the e
    /// flag is 1), then LDA ($FF) uses a pointer whose low byte is at $0000FF and whose high byte
    /// is at $000000 (like the 65C02)". <c>(dp)</c> is an "old" mode (present on the 65C02), so
    /// with <c>E = 1</c> and <c>DL = $00</c> the pointer's own high-byte read must wrap within the
    /// page instead of the plain <c>ptr + 1</c> a non-wrapping read would use.
    /// </summary>
    [Fact]
    public void LdaDirectPageIndirect_InEmulationModeWithDlZero_PointerHighByteWrapsWithinThePage()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xB2;              // LDA (dp)
        ram[0xC001] = 0xFF;              // DO -> pointer at $0000FF
        ram[0x0000FF] = 0x34;            // pointer low
        ram[0x000000] = 0x12;            // pointer high, wrapped within the page
        ram[0x000100] = 0x56;            // decoy: where a non-wrapping read would take the high byte from
        ram[0x001234] = 0x99;            // data at the correctly-wrapped pointer, AA = $1234, DBR = $00

        var cpu = MakeCpu(ram);
        cpu.State.E = true;              // emulation mode forces M = 1 too
        cpu.State.DP = 0x0000;           // DL = $00

        cpu.Step();

        Assert.Equal(0x99, cpu.State.A & 0xFF);
    }

    /// <summary>
    /// The other half of Finding 3, and the reason the fix cannot be a blanket "always wrap in
    /// emulation mode with DL = $00": Clark §5.1.1, verbatim, page wrapping applies "only for
    /// 'old' instructions and addressing modes, i.e. instructions and addressing modes that are
    /// available on the 65C02." <c>[dp]</c> is new to the 65816, so under the identical E/DL
    /// condition its pointer's middle-byte read must NOT wrap — it stays at the plain
    /// <c>ptr + 1</c>, one page past the pointer's low byte.
    /// </summary>
    [Fact]
    public void LdaDirectPageIndirectLong_InEmulationModeWithDlZero_PointerReadsDoNotWrap()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xA7;              // LDA [dp]
        ram[0xC001] = 0xFF;              // DO -> pointer at $0000FF
        ram[0x0000FF] = 0x34;            // pointer low
        ram[0x000100] = 0x12;            // pointer mid (AAH), NOT wrapped
        ram[0x000101] = 0x00;            // pointer bank (AAB), NOT wrapped
        ram[0x000000] = 0x56;            // decoy: where a wrongly-wrapping read would take the mid byte from
        ram[0x001234] = 0x77;            // data at the correctly-formed pointer, AAB,AA = $001234

        var cpu = MakeCpu(ram);
        cpu.State.E = true;
        cpu.State.DP = 0x0000;           // DL = $00

        cpu.Step();

        Assert.Equal(0x77, cpu.State.A & 0xFF);
    }

    // ---- Finding 4: the (dp),Y indexing-cycle skip must come from info.Access ----------

    /// <summary>
    /// Finding 4's actual fix, exercised directly against <c>MicroOpTable.EmitDirectPage816</c>
    /// via reflection: it is <c>private static</c>, and no opcode other than <c>LDA</c>/<c>STA</c>
    /// exists in the 65816 table yet for this to reach through the public opcode-execution API
    /// (phase 7c adds the rest). <c>ADC</c> stands in for "some future reading opcode that is not
    /// literally <c>Op.Lda</c>" — the exact case the review found: a runtime
    /// <c>_op == Op.Lda</c> comparison would misclassify it as a write. The fix moves the
    /// decision to table-build time, keyed on <c>info.Access</c>, so any <c>Access.Read</c>
    /// operation — not only <c>LDA</c> by name — gets the skippable
    /// <see cref="MicroOp.DpPtrReadHiY"/>.
    /// </summary>
    [Fact]
    public void EmitDirectPage816_ForAReadThatIsNotLda_StillSelectsTheSkippableMicroOp()
    {
        var ops = EmitDirectPage816(new OpcodeInfo("ADC", AddrMode.DirectPageIndirectY, Op.Adc, Access.Read));

        Assert.Contains(MicroOp.DpPtrReadHiY, ops);
        Assert.DoesNotContain(MicroOp.DpPtrReadHiYWrite, ops);
    }

    /// <summary>
    /// The write side of the same decision: <c>STA</c>'s <c>(dp),Y</c> must select
    /// <see cref="MicroOp.DpPtrReadHiYWrite"/>, which never skips the indexing cycle — datasheet
    /// Note 4, "or write". Pinned alongside the read case so the two are proven to genuinely
    /// diverge, not just both happen to contain <see cref="MicroOp.DpPtrReadHiY"/>.
    /// </summary>
    [Fact]
    public void EmitDirectPage816_ForAWrite_SelectsTheNeverSkippingMicroOp()
    {
        var ops = EmitDirectPage816(new OpcodeInfo("STA", AddrMode.DirectPageIndirectY, Op.Sta, Access.Write));

        Assert.Contains(MicroOp.DpPtrReadHiYWrite, ops);
        Assert.DoesNotContain(MicroOp.DpPtrReadHiY, ops);
    }

    private static List<MicroOp> EmitDirectPage816(OpcodeInfo info)
    {
        var method = typeof(MicroOpTable).GetMethod("EmitDirectPage816", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "MicroOpTable.EmitDirectPage816 not found by reflection — has it been renamed?");

        var ops = new List<MicroOp>();
        method.Invoke(null, [ops, info]);
        return ops;
    }
}
