namespace SixtyFiveXX;

/// <summary>
/// The address/data bus a CPU core talks to. Implementations decide what any given
/// address means: RAM, ROM, I/O, or open bus.
/// </summary>
/// <remarks>
/// Addresses are 24-bit to accommodate the 65816. Cores narrower than that mask to
/// 16 bits before calling, so an 8-bit implementation may ignore the upper byte.
/// </remarks>
public interface IBus
{
    /// <summary>Reads one byte. Called at most once per CPU cycle.</summary>
    byte Read(int address);

    /// <summary>Writes one byte. Called at most once per CPU cycle.</summary>
    void Write(int address, byte value);
}

/// <summary>
/// A flat 64 KB memory with no address decoding. The default bus for test harnesses
/// and conformance suites.
/// </summary>
/// <remarks>
/// This is a <c>struct</c> so that a generic <c>Cpu&lt;TBus, TVariant&gt;</c> core specializes
/// against it and the JIT inlines every access down to a direct array index.
/// </remarks>
public readonly struct FlatBus : IBus
{
    private readonly byte[] _ram;

    /// <summary>Wraps an existing 64 KB array. The array is not copied.</summary>
    /// <exception cref="ArgumentException">The array is not exactly 65536 bytes.</exception>
    public FlatBus(byte[] ram)
    {
        ArgumentNullException.ThrowIfNull(ram);
        if (ram.Length != 0x10000)
            throw new ArgumentException($"Expected a 65536-byte array, got {ram.Length}.", nameof(ram));
        _ram = ram;
    }

    /// <summary>The backing memory. Mutating it is visible to the CPU immediately.</summary>
    public byte[] Ram => _ram;

    /// <inheritdoc />
    public byte Read(int address) => _ram[address & 0xFFFF];

    /// <inheritdoc />
    public void Write(int address, byte value) => _ram[address & 0xFFFF] = value;
}

/// <summary>
/// Adapts any <see cref="IBus"/> reference for use as a <c>struct</c> bus parameter.
/// </summary>
/// <remarks>
/// Costs one interface dispatch per access, so the JIT cannot inline memory accesses.
/// Use it when the bus must be chosen at runtime; write a dedicated struct bus when
/// throughput matters.
/// </remarks>
public readonly struct RefBus : IBus
{
    private readonly IBus _inner;

    /// <summary>Wraps the given bus.</summary>
    public RefBus(IBus inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public byte Read(int address) => _inner.Read(address);

    /// <inheritdoc />
    public void Write(int address, byte value) => _inner.Write(address, value);
}
