using SixtyFiveXX.Variants;

namespace SixtyFiveXX.Tests;

/// <summary>
/// A full 24-bit address space, unlike <see cref="FlatBus"/> (64 KB, masks away the bank
/// entirely). Required by any 65816 test that needs to distinguish two different
/// <em>banks</em>, which a 16-bit-masking bus cannot do: both a bank-wrapping and a
/// bank-carrying formula collapse to the same masked address once the bank is discarded.
/// Sparse (<see cref="Dictionary{TKey,TValue}"/>-backed, unset reads as $00), the same shape
/// <c>Harte816Bus</c> in the conformance project uses. Extracted from task 5's
/// <c>W65C816DirectPageIndirectTests</c> so task 6's absolute/long/stack-relative tests can
/// share it rather than duplicating it.
/// </summary>
internal sealed class BankedBus : IBus
{
    private readonly Dictionary<int, byte> _ram = [];

    public byte this[int address]
    {
        get => _ram.GetValueOrDefault(address & 0xFFFFFF);
        set => _ram[address & 0xFFFFFF] = value;
    }

    /// <summary>
    /// Every access in order, for tests that must assert on bus <em>direction</em> rather than on
    /// the value left in memory — which is the whole of what datasheet Note 17 is about. Opt-in:
    /// nothing clears it, so a test that does not read it pays only the list allocation.
    /// </summary>
    public readonly List<(int Address, byte Value, bool Write)> Log = [];

    public byte Read(int address)
    {
        var value = this[address];
        Log.Add((address & 0xFFFFFF, value, false));
        return value;
    }

    public void Write(int address, byte value)
    {
        this[address] = value;
        Log.Add((address & 0xFFFFFF, value, true));
    }
}

/// <summary>Builds a 65816 core over a <see cref="BankedBus"/>, for tests that need real banks.</summary>
internal static class Banked816TestMachine
{
    public static Cpu<RefBus, W65C816Variant> Make(BankedBus ram, ushort pc = 0xC000)
    {
        var cpu = new Cpu<RefBus, W65C816Variant>(new RefBus(ram));
        cpu.State.PC = pc;
        cpu.State.S = 0x01FF;
        return cpu;
    }
}
