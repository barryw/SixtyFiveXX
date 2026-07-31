namespace SixtyFiveXX.Conformance;

/// <summary>One bus access, in the shape SingleStepTests records them.</summary>
public readonly record struct Cycle(int Address, byte Value, bool IsWrite)
{
    /// <inheritdoc />
    public override string ToString() => $"[${Address:X4}, ${Value:X2}, {(IsWrite ? "write" : "read")}]";
}

/// <summary>
/// A 64 KB bus that appends every access to a caller-owned list.
/// </summary>
/// <remarks>
/// A <c>struct</c> so the core specializes against it: with 1.5 million vectors to run,
/// an interface dispatch per cycle is a measurable share of the suite's runtime. Both
/// fields are references, so copying the struct still shares the RAM and the log.
/// </remarks>
public readonly struct HarteBus(byte[] ram, List<Cycle> log) : IBus
{
    /// <inheritdoc />
    public byte Read(int address)
    {
        var value = ram[address & 0xFFFF];
        log.Add(new Cycle(address & 0xFFFF, value, IsWrite: false));
        return value;
    }

    /// <inheritdoc />
    public void Write(int address, byte value)
    {
        ram[address & 0xFFFF] = value;
        log.Add(new Cycle(address & 0xFFFF, value, IsWrite: true));
    }
}
