using SixtyFiveXX.Variants;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// A 64 KB bus with the feedback register Klaus Dormann's interrupt test uses to drive
/// its own interrupt pins.
/// </summary>
/// <remarks>
/// <para>
/// The register lives at <c>$BFFC</c>. Bit 0 drives IRQ, bit 1 drives NMI: a written
/// <b>1</b> asserts the corresponding pin, a written <b>0</b> releases it. Bit 7 is a
/// diagnostic-stop signal the test raises when it detects a problem it cannot trap on.
/// </para>
/// <para>
/// The polarity is <b>not</b> what the test's own config comments suggest at a glance
/// (<c>I_drive = 1 ;0 = totem pole, 1 = open collector</c>, with an adjacent "0 -&gt;
/// I_port to force interrupt" remark that describes a different, unused DDR variant of
/// the macro). The <c>I_set</c>/<c>I_clr</c> macros actually assembled for this build's
/// configuration (no DDR) settle it unambiguously: <c>I_set</c> — which the test uses to
/// trigger an interrupt and then poll for it — computes its value with
/// <c>ora #(1&lt;&lt;bit)</c>, and every call site that arms IRQ or NMI writes
/// <c>$01</c>, <c>$02</c> or <c>$03</c> (<c>6502_interrupt_test.lst</c>, e.g.
/// <c>.05c2 09 02  ora #$02</c> arming NMI). <c>I_clr</c> clears the bit to release. A
/// written-0-asserts polarity was tried first and fails immediately: the test's own
/// startup housekeeping (<c>#I_clr IRQ_bit</c> then <c>#I_clr NMI_bit</c>, both of which
/// store a byte with bit 1 already 0) would then present as an NMI falling edge on the
/// very first write to this register, before <c>I_src</c> is armed, trapping at
/// <c>$074A</c> after 67 cycles.
/// </para>
/// </remarks>
public sealed class FeedbackBus : IBus
{
    /// <summary>Address of the feedback register.</summary>
    public const int FeedbackPort = 0xBFFC;

    private readonly byte[] _ram;
    private Cpu<RefBus, Mos6502Variant>? _cpu;

    /// <summary>Wraps a 64 KB image.</summary>
    public FeedbackBus(byte[] ram)
    {
        ArgumentNullException.ThrowIfNull(ram);
        if (ram.Length != 0x10000)
            throw new ArgumentException($"Expected 65536 bytes, got {ram.Length}.", nameof(ram));
        _ram = ram;
    }

    /// <summary>True once the test has raised the diagnostic-stop bit.</summary>
    public bool DiagnosticStop { get; private set; }

    /// <summary>
    /// Connects the CPU whose pins this register drives. Set after construction because
    /// the CPU needs the bus and the bus needs the CPU.
    /// </summary>
    public void Attach(Cpu<RefBus, Mos6502Variant> cpu) => _cpu = cpu;

    /// <inheritdoc />
    public byte Read(int address) => _ram[address & 0xFFFF];

    /// <inheritdoc />
    public void Write(int address, byte value)
    {
        address &= 0xFFFF;
        _ram[address] = value;

        if (address != FeedbackPort || _cpu is null) return;

        // A written 1 asserts the pin, 0 releases it — see the polarity note above.
        _cpu.SetIrq((value & 0x01) != 0);
        _cpu.SetNmi((value & 0x02) != 0);

        if ((value & 0x80) != 0) DiagnosticStop = true;
    }
}
