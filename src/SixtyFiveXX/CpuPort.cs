namespace SixtyFiveXX;

/// <summary>
/// The 6510's on-chip data-direction and I/O port registers at <c>$00</c> and <c>$01</c>.
/// </summary>
/// <remarks>
/// <para>
/// These are CPU registers, not memory: the core answers them itself and the access never
/// reaches the bus, so whatever RAM lives at <c>$0000</c>/<c>$0001</c> is untouched and
/// unreadable while the port is active.
/// </para>
/// <para>
/// A bit set in the direction register makes the corresponding port bit an output, and
/// reads return what was last written to it. An input bit reads its pin — and on a bare
/// core nothing drives the pin, which is where the floating behaviour below comes in.
/// </para>
/// <para>
/// <strong>The charge is modelled; the fade is not.</strong> All eight bits are implemented
/// on the chip, but bits 6 and 7 have no connection to the outside, so the pin behaves like
/// a small capacitor: driving it as an output charges it, and after switching to input the
/// charge is what reads return — until it decays after some hundreds of microseconds. This
/// models the charge and holds it indefinitely. The decay is a deliberate non-goal: VICE's
/// own notes call its timing guesswork, the same posture this project takes toward the
/// unstable NMOS opcodes' magic constants. VICE's <c>cpuport/test1</c> requires the charge
/// and deliberately avoids the fade, reading back "within reasonable time, ie before it
/// faded away" — so the part that is guesswork is also the part nothing gates.
/// </para>
/// <para>
/// The charge is modelled for all eight bits rather than only 6 and 7. On the chip they are
/// implemented alike; what differs is that bits 0-5 reach real pins, so on hardware
/// something else usually drives them. A bare core has nothing attached, and modelling them
/// uniformly is both simpler and closer to the silicon than special-casing two bits.
/// External drive — the C64's pull-ups on bits 0, 1, 2 and 4, which are board components
/// rather than anything the CPU does — belongs to a machine personality.
/// </para>
/// </remarks>
internal struct CpuPort
{
    private byte _direction;
    private byte _output;

    /// <summary>
    /// The last value driven onto each pin while it was an output. Read back by input bits,
    /// and never updated while a bit is an input — which is the property
    /// <c>cpuport/test1</c> spends four of its six assertions on.
    /// </summary>
    private byte _charge;

    /// <summary>Reads <c>$00</c> or <c>$01</c>.</summary>
    /// <param name="register">0 for the direction register, 1 for the port.</param>
    public readonly byte Read(int register) => register == 0 ? _direction : ReadPort();

    /// <summary>Writes <c>$00</c> or <c>$01</c>.</summary>
    public void Write(int register, byte value)
    {
        if (register == 0) _direction = value;
        else _output = value;

        // Bits that are currently outputs drive their pins. Doing this after a direction
        // write as well as a port write is what makes the two orderings equivalent: a bit
        // switched to output starts driving whatever the latch already holds.
        _charge = (byte)((_charge & ~_direction) | (_output & _direction));
    }

    /// <summary>
    /// Output bits read the latch; input bits read their pin, which on a bare core is the
    /// charge left behind the last time that bit was driven.
    /// </summary>
    private readonly byte ReadPort() => (byte)((_output & _direction) | (_charge & ~_direction));
}
