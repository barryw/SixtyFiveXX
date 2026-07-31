using SixtyFiveXX;

namespace SixtyFiveXX.Tests;

/// <summary>One recorded bus access.</summary>
public readonly record struct BusAccess(int Address, byte Value, bool IsWrite);

/// <summary>A bus over 64 KB of RAM that records every access in order.</summary>
public sealed class LoggingBus(byte[] ram, List<BusAccess> log) : IBus
{
    public byte Read(int address)
    {
        var value = ram[address & 0xFFFF];
        log.Add(new BusAccess(address & 0xFFFF, value, IsWrite: false));
        return value;
    }

    public void Write(int address, byte value)
    {
        ram[address & 0xFFFF] = value;
        log.Add(new BusAccess(address & 0xFFFF, value, IsWrite: true));
    }
}

/// <summary>Builds CPUs for unit tests.</summary>
public static class TestMachine
{
    /// <summary>A CPU over a plain 64 KB array, with <paramref name="program"/> loaded at PC.</summary>
    public static (Cpu<FlatBus> Cpu, byte[] Ram) Flat(ushort pc, params byte[] program)
    {
        var ram = new byte[0x10000];
        program.CopyTo(ram, pc);
        var cpu = new Cpu<FlatBus>(new FlatBus(ram));
        cpu.State.PC = pc;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;
        return (cpu, ram);
    }

    /// <summary>A CPU whose every bus access is recorded, for cycle-by-cycle assertions.</summary>
    public static (Cpu<RefBus> Cpu, byte[] Ram, List<BusAccess> Log) Logged(ushort pc, params byte[] program)
    {
        var ram = new byte[0x10000];
        program.CopyTo(ram, pc);
        var log = new List<BusAccess>();
        var cpu = new Cpu<RefBus>(new RefBus(new LoggingBus(ram, log)));
        cpu.State.PC = pc;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;
        return (cpu, ram, log);
    }
}
