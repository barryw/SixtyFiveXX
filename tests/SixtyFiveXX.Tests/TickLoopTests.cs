using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class TickLoopTests
{
    /// <summary>Builds a CPU over 64 KB of RAM with the given bytes loaded at $0200 and PC there.</summary>
    private static (Cpu<FlatBus> Cpu, byte[] Ram) Machine(params byte[] program)
    {
        var ram = new byte[0x10000];
        program.CopyTo(ram, 0x0200);
        var cpu = new Cpu<FlatBus>(new FlatBus(ram));
        cpu.State.PC = 0x0200;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;
        return (cpu, ram);
    }

    private static void Ticks(Cpu<FlatBus> cpu, int count)
    {
        for (var i = 0; i < count; i++) cpu.Tick();
    }

    [Fact]
    public void Nop_TakesTwoCyclesAndAdvancesPcByOne()
    {
        var (cpu, _) = Machine(0xEA);

        Ticks(cpu, 2);

        Assert.Equal(0x0201, cpu.State.PC);
        Assert.Equal(2, cpu.Cycles);
        Assert.True(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void Nop_IsNotAtAnInstructionBoundaryMidInstruction()
    {
        var (cpu, _) = Machine(0xEA);

        cpu.Tick();

        Assert.False(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void LdaImmediate_LoadsTheOperandAndSetsFlags()
    {
        var (cpu, _) = Machine(0xA9, 0x00);

        Ticks(cpu, 2);

        Assert.Equal(0x00, cpu.State.A);
        Assert.True(cpu.State.Z);
        Assert.False(cpu.State.N);
        Assert.Equal(0x0202, cpu.State.PC);
        Assert.Equal(2, cpu.Cycles);
    }

    [Fact]
    public void LdaImmediate_SetsNegativeForHighBitOperands()
    {
        var (cpu, _) = Machine(0xA9, 0x80);

        Ticks(cpu, 2);

        Assert.Equal(0x80, cpu.State.A);
        Assert.False(cpu.State.Z);
        Assert.True(cpu.State.N);
    }

    [Fact]
    public void ImpliedInstructions_PerformADummyReadAtPc()
    {
        // NOP's second cycle reads the byte after the opcode without consuming it.
        var ram = new byte[0x10000];
        ram[0x0200] = 0xEA;
        ram[0x0201] = 0x42;
        var reads = new List<int>();
        var cpu = new Cpu<RefBus>(new RefBus(new WatchBus(ram, reads)));
        cpu.State.PC = 0x0200;

        cpu.Tick();
        Assert.Equal([0x0200], reads);

        cpu.Tick();
        Assert.Equal([0x0200, 0x0201], reads);
    }

    [Fact]
    public void Tax_TransfersAndSetsFlags()
    {
        var (cpu, _) = Machine(0xAA);
        cpu.State.A = 0xF0;

        Ticks(cpu, 2);

        Assert.Equal(0xF0, cpu.State.X);
        Assert.True(cpu.State.N);
        Assert.False(cpu.State.Z);
    }

    [Fact]
    public void Txs_DoesNotAffectFlags()
    {
        var (cpu, _) = Machine(0x9A);
        cpu.State.X = 0x00;
        cpu.State.P = Flag.U;

        Ticks(cpu, 2);

        Assert.Equal(0x00, cpu.State.S);
        Assert.False(cpu.State.Z);
    }

    [Theory]
    [InlineData(0x18, Flag.C, false)]  // CLC
    [InlineData(0x38, Flag.C, true)]   // SEC
    [InlineData(0xD8, Flag.D, false)]  // CLD
    [InlineData(0xF8, Flag.D, true)]   // SED
    [InlineData(0x58, Flag.I, false)]  // CLI
    [InlineData(0x78, Flag.I, true)]   // SEI
    [InlineData(0xB8, Flag.V, false)]  // CLV
    public void FlagInstructions_SetOrClearExactlyOneBit(byte opcode, byte flag, bool expected)
    {
        var (cpu, _) = Machine(opcode);
        cpu.State.P = expected ? (byte)0x00 : (byte)0xFF;

        Ticks(cpu, 2);

        Assert.Equal(expected, (cpu.State.P & flag) != 0);
    }

    // UndefinedOpcode_Throws was removed: it fixtured $02 as "undefined," but the JAM
    // opcodes (task 6) fill every remaining gap in Opcodes6502.Table, so no opcode byte
    // can reach FetchOpcode's UndefinedOpcodeException through this table anymore. The
    // throw itself stays in Cpu.cs — it guards future variant tables (65C02 etc.) that
    // will have real gaps of their own.

    private sealed class WatchBus(byte[] ram, List<int> reads) : IBus
    {
        public byte Read(int address)
        {
            reads.Add(address & 0xFFFF);
            return ram[address & 0xFFFF];
        }

        public void Write(int address, byte value) => ram[address & 0xFFFF] = value;
    }
}
