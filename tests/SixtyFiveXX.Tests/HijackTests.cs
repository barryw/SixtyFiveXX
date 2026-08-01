using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class HijackTests
{
    private static (Cpu<FlatBus> Cpu, byte[] Ram) Machine(params byte[] program)
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, program);
        ram[0xFFFE] = 0x00; ram[0xFFFF] = 0x90;   // IRQ/BRK -> $9000
        ram[0xFFFA] = 0x00; ram[0xFFFB] = 0x80;   // NMI     -> $8000
        cpu.State.P = Flag.U;
        cpu.State.S = 0xFD;
        return (cpu, ram);
    }

    [Fact]
    public void Brk_VectorsThroughFffeWhenNoNmiArrives()
    {
        var (cpu, _) = Machine(0x00);   // BRK

        var cycles = cpu.Step();

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(7, cycles);
    }

    [Fact]
    public void Nmi_HijacksABrkInProgress()
    {
        var (cpu, ram) = Machine(0x00);   // BRK

        // Drive BRK's first four cycles, then assert NMI before the vector read.
        cpu.Tick();   // opcode fetch
        cpu.Tick();   // BrkPad
        cpu.Tick();   // push PCH
        cpu.Tick();   // push PCL
        cpu.SetNmi(true);
        cpu.Tick();   // push P
        cpu.Tick();   // vector low
        cpu.Tick();   // vector high

        Assert.Equal(0x8000, cpu.State.PC);          // NMI's vector, not BRK's
        Assert.Equal(Flag.B, ram[0x01FB] & Flag.B);  // but BRK's pushed B is still set
        Assert.True(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void HijackingConsumesTheNmiLatch()
    {
        var (cpu, _) = Machine(0x00, 0xEA, 0xEA);

        cpu.Tick(); cpu.Tick(); cpu.Tick(); cpu.Tick();
        cpu.SetNmi(true);
        cpu.Tick(); cpu.Tick(); cpu.Tick();
        Assert.Equal(0x8000, cpu.State.PC);

        // The latch was consumed, so the next boundary must not dispatch again.
        cpu.State.PC = 0x0201;
        cpu.Step();
        Assert.Equal(0x0202, cpu.State.PC);
    }

    [Fact]
    public void NmiArrivingAfterTheVectorReadDoesNotHijack()
    {
        var (cpu, _) = Machine(0x00, 0xEA);

        for (var i = 0; i < 6; i++) cpu.Tick();   // through the vector-low read
        cpu.SetNmi(true);
        cpu.Tick();                                // vector high

        Assert.Equal(0x9000, cpu.State.PC);        // BRK's own vector stood
    }
}
