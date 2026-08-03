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
/// A bit set in the direction register makes the corresponding port bit an output; reads
/// then return what was last written. An input bit reads from the pin.
/// </para>
/// </remarks>
internal struct CpuPort
{
    private byte _direction;
    private byte _output;

    /// <summary>Reads <c>$00</c> or <c>$01</c>.</summary>
    /// <param name="register">0 for the direction register, 1 for the port.</param>
    public readonly byte Read(int register) => register == 0 ? _direction : ReadPort();

    /// <summary>Writes <c>$00</c> or <c>$01</c>.</summary>
    public void Write(int register, byte value)
    {
        if (register == 0) _direction = value;
        else _output = value;
    }

    /// <summary>
    /// Output bits read back what was written; input bits read the pin.
    /// </summary>
    private readonly byte ReadPort() => (byte)(_output & _direction);
}
