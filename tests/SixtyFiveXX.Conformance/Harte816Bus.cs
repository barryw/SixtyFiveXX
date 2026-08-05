namespace SixtyFiveXX.Conformance;

/// <summary>What kind of bus activity one 65816 cycle performed.</summary>
public enum Cycle816Kind
{
    /// <summary>A real read.</summary>
    Read,

    /// <summary>A real write.</summary>
    Write,

    /// <summary>
    /// No memory access at all — <see cref="IBus.Internal"/>. The address is still driven and
    /// still asserted by the vectors; there is simply no value to check against it.
    /// </summary>
    Internal,
}

/// <summary>One bus access, in the shape the SingleStepTests <c>65816</c> vectors record them.</summary>
public readonly record struct Cycle816(int Address, byte? Value, Cycle816Kind Kind)
{
    /// <inheritdoc />
    public override string ToString() =>
        Kind == Cycle816Kind.Internal
            ? $"[${Address:X6}, --, internal]"
            : $"[${Address:X6}, ${Value:X2}, {(Kind == Cycle816Kind.Write ? "write" : "read")}]";
}

/// <summary>
/// A 24-bit bus that appends every access — read, write, or internal — to a caller-owned list.
/// </summary>
/// <remarks>
/// A sibling to <see cref="HarteBus"/>, not an edit of it: the 65816 is the first core with a
/// third kind of cycle (<see cref="Cycle816Kind.Internal"/>), and its address space is 16 MB
/// rather than 64 KB.
/// <para>
/// Backed by a <see cref="Dictionary{TKey, TValue}"/> rather than a flat array. A vector's
/// <c>ram</c> entries land at scattered 24-bit addresses to exercise bank-crossing behaviour,
/// so only a handful of bytes are ever touched per vector; a dictionary sized to what is
/// actually referenced is far cheaper to clear between the 20,000 vectors one opcode runs than
/// a 16 MB array would be.
/// </para>
/// <para>
/// A <c>struct</c> so the core specializes against it, as <see cref="HarteBus"/> already does:
/// with 20,000 vectors to run per opcode, an interface dispatch per cycle is a measurable
/// share of the suite's runtime. Both fields are references, so copying the struct still
/// shares the RAM and the log.
/// </para>
/// </remarks>
public readonly struct Harte816Bus(Dictionary<int, byte> ram, List<Cycle816> log) : IBus
{
    /// <inheritdoc />
    public byte Read(int address)
    {
        var addr = address & 0xFFFFFF;
        ram.TryGetValue(addr, out var value);
        log.Add(new Cycle816(addr, value, Cycle816Kind.Read));
        return value;
    }

    /// <inheritdoc />
    public void Write(int address, byte value)
    {
        var addr = address & 0xFFFFFF;
        ram[addr] = value;
        log.Add(new Cycle816(addr, value, Cycle816Kind.Write));
    }

    /// <inheritdoc />
    public void Internal(int address)
    {
        var addr = address & 0xFFFFFF;
        log.Add(new Cycle816(addr, null, Cycle816Kind.Internal));
    }
}
