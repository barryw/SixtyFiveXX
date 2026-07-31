using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class ControlApiTests
{
    [Fact]
    public void Reset_TakesSevenCyclesAndLoadsTheResetVector()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200);
        ram[0xFFFC] = 0x00;
        ram[0xFFFD] = 0xE0;
        cpu.State.S = 0x00;

        cpu.Reset();
        var cycles = cpu.Step();

        Assert.Equal(0xE000, cpu.State.PC);
        Assert.Equal(7, cycles);
        Assert.Equal(0xFD, cpu.State.S);   // S decrements three times, wrapping from $00
        Assert.True(cpu.State.I);
    }

    [Fact]
    public void Reset_ReadsButNeverWrites()
    {
        var ram = new byte[0x10000];
        ram[0xFFFC] = 0x34;
        ram[0xFFFD] = 0x12;
        var log = new List<BusAccess>();
        var cpu = new Cpu<RefBus>(new RefBus(new LoggingBus(ram, log)));
        cpu.State.PC = 0x0200;
        cpu.State.S = 0xFD;

        cpu.Reset();
        cpu.Step();

        Assert.Equal(0x1234, cpu.State.PC);
        Assert.DoesNotContain(log, access => access.IsWrite);
        Assert.Equal(7, log.Count);
    }

    [Fact]
    public void Step_RunsExactlyOneInstructionAndReturnsItsCycleCount()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xAD, 0x34, 0x12, 0xEA);
        ram[0x1234] = 0x42;

        var first = cpu.Step();
        var second = cpu.Step();

        Assert.Equal(4, first);      // LDA abs
        Assert.Equal(2, second);     // NOP
        Assert.Equal(0x42, cpu.State.A);
        Assert.Equal(0x0204, cpu.State.PC);
    }

    [Fact]
    public void Run_ExecutesAtLeastTheRequestedCycles()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA, 0xEA, 0xEA, 0xEA, 0xEA);

        cpu.Run(6);

        Assert.Equal(6, cpu.Cycles);
        Assert.Equal(0x0203, cpu.State.PC);   // three NOPs
    }

    [Fact]
    public void Run_StopsMidInstructionWhenTheBudgetRunsOut()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0xAD, 0x34, 0x12);
        ram[0x1234] = 0x42;

        cpu.Run(2);

        Assert.Equal(2, cpu.Cycles);
        Assert.False(cpu.AtInstructionBoundary);
    }

    [Fact]
    public void RunUntil_StopsAtTheFirstInstructionBoundaryMatchingThePredicate()
    {
        // LDA #$01 / LDA #$02 / LDA #$03
        var (cpu, _) = TestMachine.Flat(0x0200, 0xA9, 0x01, 0xA9, 0x02, 0xA9, 0x03);

        var cycles = cpu.RunUntil(c => c.State.A == 0x02);

        Assert.Equal(0x02, cpu.State.A);
        Assert.Equal(4, cycles);
        Assert.Equal(0x0204, cpu.State.PC);
    }

    [Fact]
    public void RunUntil_HonoursItsCycleCeiling()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x4C, 0x00, 0x02);   // JMP $0200 — infinite loop

        var cycles = cpu.RunUntil(c => c.State.A == 0xFF, maxCycles: 30);

        Assert.Equal(0x00, cpu.State.A);
        Assert.True(cycles >= 30, $"Expected at least 30 cycles, got {cycles}.");
        Assert.True(cycles < 36, $"Expected the ceiling to stop it promptly, got {cycles}.");
    }

    [Fact]
    public void ResetCycleCount_ZeroesTheCounterWithoutTouchingState()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xA9, 0x7B);

        cpu.Step();
        cpu.ResetCycleCount();

        Assert.Equal(0, cpu.Cycles);
        Assert.Equal(0x7B, cpu.State.A);
        Assert.Equal(0x0202, cpu.State.PC);
    }
}
