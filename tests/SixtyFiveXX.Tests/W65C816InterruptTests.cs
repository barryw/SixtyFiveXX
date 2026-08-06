using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The 65816's interrupts: two vector sets, a program-bank push that no eight-bit core has,
/// and the first cycles in this repository to assert VPB.
/// </summary>
/// <remarks>
/// Everything about <c>IRQ</c> and <c>NMI</c> is covered here and nowhere else. Research
/// document §14.2's gap 1: <c>SingleStepTests/65816</c> is one file per opcode per mode and
/// carries <b>no interrupt-line stimulus at all</b>, so the two hardware interrupts have no
/// vectors. <c>BRK</c> and <c>COP</c> share cycles 3 to 8 with them — Table 5-7's rows 22j and
/// 22a are identical from the program-bank push down — and those cycles are arbitrated by
/// <c>$00</c> and <c>$02</c>'s 40,000 vectors; what is left to this file is the two leading
/// cycles, the recognition timing, the <c>NMI</c>/<c>IRQ</c> vector selection, and the
/// emulation-mode clearing of bit 4 that only a hardware interrupt performs.
/// <para>
/// <c>BRK</c> and <c>COP</c> are unit-tested here too, despite being vector-covered, wherever a
/// two-second failure is worth more than a five-minute one — the vector addresses, the VPB
/// cycles, and the page-one wrap.
/// </para>
/// </remarks>
public class W65C816InterruptTests
{
    /// <summary>
    /// Runs one <c>NOP</c> with an interrupt line already asserted, so the poll that the next
    /// instruction boundary consults is computed during it — the same two-step idiom
    /// <see cref="InterruptTests"/> uses for the eight-bit cores. Returns the cycles the
    /// interrupt sequence itself took.
    /// </summary>
    private static long StepIntoTheHandler(Cpu<RefBus, W65C816Variant> cpu)
    {
        cpu.Step();     // the NOP completes first; its last cycle is where the poll happens
        return cpu.Step();
    }

    /// <summary>A machine with a NOP at PBR:PC and every one of the six vectors pointing somewhere distinct.</summary>
    private static (Cpu<RefBus, W65C816Variant> Cpu, BankedBus Ram) Machine(bool emulation, byte bank = 0x00)
    {
        var ram = new BankedBus();
        ram[(bank << 16) | 0xC000] = 0xEA;                          // NOP
        ram[(bank << 16) | 0xC001] = 0xEA;                          // and another, for the handler
        (ram[0xFFE4], ram[0xFFE5]) = (0x00, 0x11);                  // native COP  -> $001100
        (ram[0xFFE6], ram[0xFFE7]) = (0x00, 0x22);                  // native BRK  -> $002200
        (ram[0xFFEA], ram[0xFFEB]) = (0x00, 0x33);                  // native NMI  -> $003300
        (ram[0xFFEE], ram[0xFFEF]) = (0x00, 0x44);                  // native IRQ  -> $004400
        (ram[0xFFF4], ram[0xFFF5]) = (0x00, 0x55);                  // emulation COP -> $005500
        (ram[0xFFFA], ram[0xFFFB]) = (0x00, 0x66);                  // emulation NMI -> $006600
        (ram[0xFFFE], ram[0xFFFF]) = (0x00, 0x77);                  // emulation BRK/IRQ -> $007700

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = emulation;
        cpu.State.PBR = bank;
        cpu.State.S = emulation ? (ushort)0x01FF : (ushort)0x1FFF;
        return (cpu, ram);
    }

    // ---- BRK and COP: the vector addresses, the program-bank push, and the stack footprint.

    /// <summary>
    /// A native-mode BRK pushes PBR, then the return address, then P, reads the native BRK
    /// vector, and clears PBR so the handler runs in bank 0.
    /// </summary>
    [Fact]
    public void NativeBrk_PushesProgramBank_AndTakesTheNativeVector()
    {
        var ram = new BankedBus();
        ram[0x120000] = 0x00;           // BRK
        ram[0x120001] = 0xEE;           // signature byte
        ram[0xFFE6] = 0x34;             // native BRK vector — research document §14.2
        ram[0xFFE7] = 0x12;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.PBR = 0x12;
        cpu.State.PC = 0x0000;
        cpu.State.S = 0x1FFF;

        var cycles = cpu.Step();

        Assert.Equal(8, cycles);                // 8-e, Clark §6.3.1
        Assert.Equal(0x12, ram[0x001FFF]);      // PBR pushed first
        Assert.Equal(0x00, ram[0x001FFE]);      // PCH of the instruction's address plus 2
        Assert.Equal(0x02, ram[0x001FFD]);      // PCL
        Assert.Equal(0x1FFB, cpu.State.S);      // four bytes
        Assert.Equal(0x00, cpu.State.PBR);      // handler runs in bank 0
        Assert.Equal(0x1234, cpu.State.PC);
        Assert.True(cpu.State.I);
    }

    /// <summary>
    /// An emulation-mode BRK pushes no program bank and takes the eight-bit vector, so its
    /// stack footprint is three bytes rather than four.
    /// </summary>
    [Fact]
    public void EmulationBrk_PushesThreeBytes_AndTakesTheEmulationVector()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x00;             // BRK
        ram[0xC001] = 0xEE;
        ram[0xFFFE] = 0x34;
        ram[0xFFFF] = 0x12;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;
        cpu.State.S = 0x01FF;

        var cycles = cpu.Step();

        Assert.Equal(7, cycles);                // 8-e with e = 1
        Assert.Equal(0x01FC, cpu.State.S);      // three bytes, not four
        Assert.Equal(0x1234, cpu.State.PC);
    }

    /// <summary>
    /// COP is BRK with a different vector and nothing else — one arm of
    /// <c>EmitControlFlow816</c> serves both, so this is what proves the arm still
    /// discriminates them.
    /// </summary>
    [Theory]
    [InlineData(false, 0x1100)]     // native COP    -> $00FFE4
    [InlineData(true, 0x5500)]      // emulation COP -> $00FFF4
    public void Cop_TakesItsOwnVectorInEachMode(bool emulation, int expectedPc)
    {
        var (cpu, ram) = Machine(emulation);
        ram[0xC000] = 0x02;             // COP
        ram[0xC001] = 0xEE;             // signature byte

        cpu.Step();

        Assert.Equal(expectedPc, cpu.State.PC);
        Assert.Equal(0x00, cpu.State.PBR);
    }

    /// <summary>
    /// The two vector-read cycles assert VPB and no other core in this repository ever has.
    /// Asserted through the pin readback rather than through the vectors, because a unit test
    /// that fails in a second is worth more here than a 20,000-vector file that fails in five
    /// minutes.
    /// </summary>
    [Fact]
    public void TheVectorReadsAssertVectorPull()
    {
        var ram = new BankedBus();
        ram[0x120000] = 0x00;           // BRK
        ram[0x120001] = 0xEE;
        ram[0xFFE6] = 0x34;
        ram[0xFFE7] = 0x12;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.PBR = 0x12;
        cpu.State.PC = 0x0000;
        cpu.State.S = 0x1FFF;

        // Cycle-at-a-time rather than Step(), so every cycle's pins can be read — the idiom
        // PinTests.cs uses. Bounded by the instruction boundary rather than by a cycle count
        // this file deliberately does not hard-code: research §14.2 owns that number, not this
        // test.
        var pins = new List<BusPins>();
        do
        {
            cpu.Tick();
            pins.Add(cpu.LastPins);
        }
        while (!cpu.AtInstructionBoundary);

        Assert.Equal(0x1234, cpu.State.PC);

        var vectorCycles = pins
            .Select((p, i) => (Pins: p, Index: i))
            .Where(x => (x.Pins & BusPins.Vpb) != 0)
            .Select(x => x.Index)
            .ToList();

        // Exactly two, and they are the last two: VPB is asserted on the vector reads and
        // nowhere else in the sequence.
        Assert.Equal(2, vectorCycles.Count);
        Assert.Equal(pins.Count - 2, vectorCycles[0]);
        Assert.Equal(pins.Count - 1, vectorCycles[1]);

        // And VDA rides along with it on both, which no reasoning about the cycles' purpose
        // would give — research document §14.2 lists it among its most expensive findings.
        Assert.All(vectorCycles, i => Assert.Equal(BusPins.Vda | BusPins.Vpb, pins[i]));
    }

    /// <summary>
    /// An interrupt is on Clark §5.22's wrapping list, so in emulation mode its pushes stay
    /// inside page one instead of walking out the bottom of it. Same shape as the discriminating
    /// COP vector research document §14.1 quotes (<c>02 e 1</c>, <c>S = $3000</c> forced to
    /// <c>$0100</c>, writing <c>$0100</c>, <c>$01FF</c>, <c>$01FE</c>).
    /// </summary>
    [Fact]
    public void EmulationInterruptPushesWrapInsidePageOne()
    {
        var (cpu, ram) = Machine(emulation: true);
        ram[0xC000] = 0x00;             // BRK
        cpu.State.S = 0x0100;

        cpu.Step();

        Assert.Equal(0xC0, ram[0x000100]);      // PCH at the top of the descent
        Assert.Equal(0x02, ram[0x0001FF]);      // PCL, SL having wrapped $00 -> $FF
        Assert.Equal(0x01FD, cpu.State.S);      // and never left page one
    }

    // ---- WDM.

    /// <summary>
    /// <c>WDM</c> is two bytes and two cycles, and its second byte is never read. Research
    /// document §14.2/§3.4: Clark §6.7 says the byte "is read, but ignored", and all 20,000
    /// vectors say cycle 2 is an internal cycle with a null value and <c>---r</c> pins. The
    /// vectors win, and this is that finding as a unit test — the bus log is what makes "not
    /// read" checkable at all, since a discarded read leaves no other trace.
    /// </summary>
    [Fact]
    public void Wdm_IsTwoCycles_AndNeverReadsItsSecondByte()
    {
        var ram = new BankedBus();
        ram[0x125000] = 0x42;           // WDM
        ram[0x125001] = 0xEE;           // the operand byte the bus must never see

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.PBR = 0x12;
        cpu.State.PC = 0x5000;
        ram.Log.Clear();

        var cycles = cpu.Step();

        Assert.Equal(2, cycles);
        Assert.Equal(0x5002, cpu.State.PC);                     // two bytes consumed
        Assert.Equal(new[] { 0x125000 }, ram.Log.Select(a => a.Address));   // one access, the opcode
        Assert.Equal(BusPins.None, cpu.LastPins);               // and cycle 2 drove neither pin
    }

    // ---- IRQ and NMI. No vector in the SingleStepTests set reaches any of this.

    [Theory]
    [InlineData(false, false, 0x4400)]      // native IRQ    -> $00FFEE
    [InlineData(false, true, 0x3300)]       // native NMI    -> $00FFEA
    [InlineData(true, false, 0x7700)]       // emulation IRQ -> $00FFFE
    [InlineData(true, true, 0x6600)]        // emulation NMI -> $00FFFA
    public void HardwareInterrupt_TakesTheVectorItsModeAndKindSelect(bool emulation, bool nmi, int expectedPc)
    {
        var (cpu, _) = Machine(emulation);
        if (nmi) cpu.SetNmi(true); else cpu.SetIrq(true);

        StepIntoTheHandler(cpu);

        Assert.Equal(expectedPc, cpu.State.PC);
        Assert.Equal(0x00, cpu.State.PBR);
        Assert.True(cpu.State.I);
        Assert.False(cpu.State.D);
    }

    /// <summary>
    /// The program-bank push is the one cycle emulation mode omits, for a hardware interrupt
    /// exactly as for BRK — datasheet note 7, and §7.11.1/§7.11.2 for what it is that goes
    /// missing. Eight cycles and four bytes native, seven and three in emulation.
    /// </summary>
    [Theory]
    [InlineData(false, 8, 4)]
    [InlineData(true, 7, 3)]
    public void HardwareInterrupt_PushesTheProgramBankInNativeModeOnly(bool emulation, int cycles, int bytes)
    {
        var (cpu, ram) = Machine(emulation, bank: emulation ? (byte)0x00 : (byte)0x12);
        cpu.SetIrq(true);
        var before = cpu.State.S;

        var taken = StepIntoTheHandler(cpu);

        Assert.Equal(cycles, taken);
        Assert.Equal(before - bytes, cpu.State.S);
        if (!emulation) Assert.Equal(0x12, ram[before]);     // PBR, pushed first at 0,S
    }

    /// <summary>
    /// Datasheet note 11, printed on row 22a and <em>not</em> on row 22j: in emulation mode a
    /// hardware interrupt pushes bit 4 clear and BRK pushes it set, which is the whole of how a
    /// handler sharing <c>$00FFFE</c> tells the two apart. Table 8-1 says the same thing —
    /// "BRK bit=0 on stack if IRQ-NMIB, ABORTB".
    /// </summary>
    [Fact]
    public void EmulationMode_HardwareInterruptPushesBitFourClear_AndBrkPushesItSet()
    {
        var (irqCpu, irqRam) = Machine(emulation: true);
        irqCpu.SetIrq(true);
        StepIntoTheHandler(irqCpu);
        Assert.Equal(0, irqRam[0x01FD] & Flag.B);

        var (brkCpu, brkRam) = Machine(emulation: true);
        brkRam[0xC000] = 0x00;          // BRK
        brkCpu.Step();
        Assert.Equal(Flag.B, brkRam[0x01FD] & Flag.B);
    }

    /// <summary>
    /// In native mode bit 4 is the <c>x</c> flag and nothing forces it either way — Table 8-1,
    /// "X=X on stack always" — so the pushed byte is P verbatim for all four interrupts. Both
    /// width flags are set to opposed values because bit 4 (<c>x</c>) and bit 5 (<c>m</c>) are
    /// the eight-bit cores' B and U bits: a test that moved them together could not tell a
    /// correct implementation from one that forced both.
    /// </summary>
    [Theory]
    [InlineData(true, false)]       // x = 1, m = 0: bit 4 set, bit 5 clear
    [InlineData(false, true)]       // x = 0, m = 1: bit 4 clear, bit 5 set
    public void NativeMode_EveryInterruptPushesStatusVerbatim(bool xFlag, bool m)
    {
        foreach (var isBrk in new[] { false, true })
        {
            var (cpu, ram) = Machine(emulation: false);
            if (isBrk) ram[0xC000] = 0x00;
            cpu.State.XFlag = xFlag;
            cpu.State.M = m;
            cpu.State.D = true;                 // and D survives into the pushed copy
            cpu.State.C = true;
            var expected = cpu.State.P;

            if (isBrk) cpu.Step(); else { cpu.SetIrq(true); StepIntoTheHandler(cpu); }

            Assert.Equal(expected, ram[0x1FFC]);
            Assert.False(cpu.State.D);          // cleared for the handler, after the push
            Assert.True(cpu.State.I);
        }
    }

    /// <summary>
    /// The two leading cycles of a hardware interrupt, which no vector reaches: research
    /// document §14.2's row 22a prints cycle 1 as <c>VDA=1 VPA=1</c> at <c>PBR,PC</c> — a
    /// discarded read, the same pins an opcode fetch drives — and cycle 2 as <c>VDA=0 VPA=0</c>
    /// at the <em>same</em> address, an internal cycle with no bus access at all. PC does not
    /// advance across either: an interrupt consumes no opcode.
    /// </summary>
    [Fact]
    public void HardwareInterruptEntry_ReadsPcOnce_ThenDrivesItWithNoAccess()
    {
        var (cpu, ram) = Machine(emulation: false, bank: 0x12);
        cpu.SetIrq(true);
        cpu.Step();                     // the NOP
        ram.Log.Clear();

        cpu.Tick();
        Assert.Equal(BusPins.Vda | BusPins.Vpa, cpu.LastPins);
        Assert.Equal(0x12C001, cpu.LastAddress);
        Assert.Single(ram.Log);

        cpu.Tick();
        Assert.Equal(BusPins.None, cpu.LastPins);
        Assert.Equal(0x12C001, cpu.LastAddress);
        Assert.Single(ram.Log);         // no second access: cycle 2 touches no memory
        Assert.Equal(0xC001, cpu.State.PC);
    }

    /// <summary>
    /// <b>There is no NMI hijack on this part, and that is a recorded decision rather than an
    /// omission.</b> On the NMOS cores an NMI latched during a BRK steals BRK's vector — see
    /// <c>HijackTests</c> — and research document §14.2/§14.9 gap 2 records that <em>no</em>
    /// 65816 source mentions the case and that no vector can settle it, so the anomaly is not
    /// carried over on the eight-bit cores' authority. BRK reads its own vector; the NMI is
    /// still pending afterwards and is taken at the next boundary.
    /// <para>
    /// The second assertion pins the other half of the same decision: the recognition blackout
    /// <c>Cpu.Tick</c> performs on <c>MicroOp.VectorHi</c> — which guarantees the eight-bit
    /// cores execute at least one handler instruction before another interrupt — is
    /// deliberately <b>not</b> extended to <c>VectorHi816</c>. That blackout is a property of
    /// the NMOS die's own transistor chain and no source places it on this one, so the general
    /// family rule applies instead and the NMI is serviced immediately.
    /// </para>
    /// </summary>
    [Fact]
    public void NmiLatchedDuringBrk_DoesNotStealBrksVector()
    {
        var (cpu, ram) = Machine(emulation: false);
        ram[0xC000] = 0x00;             // BRK

        cpu.Tick();                     // opcode fetch
        cpu.Tick();                     // BrkPad816
        cpu.SetNmi(true);               // latched with five cycles of the sequence still to run
        while (!cpu.AtInstructionBoundary) cpu.Tick();

        Assert.Equal(0x2200, cpu.State.PC);      // BRK's own native vector, not NMI's $3300

        cpu.Step();

        Assert.Equal(0x3300, cpu.State.PC);      // and the NMI is taken next, still pending
    }

    /// <summary>
    /// A hardware interrupt sequence cannot re-enter itself: I is set on the P push, two cycles
    /// before the boundary, so the poll that the next fetch consults already sees it. Level-
    /// sensitive IRQ held asserted throughout is the case that would loop if it did not.
    /// </summary>
    [Fact]
    public void HeldIrq_DoesNotReEnterTheHandlerImmediately()
    {
        var (cpu, ram) = Machine(emulation: false);
        ram[0x004400] = 0xEA;           // a NOP at the native IRQ handler
        cpu.SetIrq(true);

        StepIntoTheHandler(cpu);
        Assert.Equal(0x4400, cpu.State.PC);

        cpu.Step();                     // the handler's first instruction runs
        Assert.Equal(0x4401, cpu.State.PC);
    }
}
