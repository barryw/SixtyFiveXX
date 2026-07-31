using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class ControlFlowTests
{
    [Fact]
    public void JmpAbsolute_TakesThreeCycles()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x4C, 0x34, 0x12);

        var cycles = cpu.Step();

        Assert.Equal(0x1234, cpu.State.PC);
        Assert.Equal(3, cycles);
    }

    [Fact]
    public void JmpIndirect_TakesFiveCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x6C, 0x00, 0x30);
        ram[0x3000] = 0x78;
        ram[0x3001] = 0x56;

        var cycles = cpu.Step();

        Assert.Equal(0x5678, cpu.State.PC);
        Assert.Equal(5, cycles);
    }

    [Fact]
    public void JmpIndirect_ReproducesTheNmosPageWrapBug()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x6C, 0xFF, 0x30);   // JMP ($30FF)
        ram[0x30FF] = 0x78;
        ram[0x3100] = 0xEE;    // what a fixed CPU would read — must NOT be used
        ram[0x3000] = 0x56;    // what an NMOS 6502 actually reads

        cpu.Step();

        Assert.Equal(0x5678, cpu.State.PC);
    }

    [Fact]
    public void Jsr_PushesTheAddressOfItsLastByteAndTakesSixCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0300, 0x20, 0x00, 0x30);   // JSR $3000
        cpu.State.S = 0xFD;

        var cycles = cpu.Step();

        Assert.Equal(0x3000, cpu.State.PC);
        Assert.Equal(6, cycles);
        Assert.Equal(0xFB, cpu.State.S);
        Assert.Equal(0x03, ram[0x01FD]);    // high byte of $0302
        Assert.Equal(0x02, ram[0x01FC]);    // low byte of $0302 — the JSR's last byte
    }

    [Fact]
    public void JsrThenRts_ReturnsToTheInstructionAfterTheJsr()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x20, 0x00, 0x30);   // JSR $3000
        ram[0x3000] = 0x60;                                             // RTS

        cpu.Step();
        var cycles = cpu.Step();

        Assert.Equal(0x0203, cpu.State.PC);
        Assert.Equal(6, cycles);
        Assert.Equal(0xFD, cpu.State.S);
    }

    [Fact]
    public void Pha_PushesAccumulatorAndTakesThreeCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x48);
        cpu.State.A = 0x3C;
        cpu.State.S = 0xFF;

        var cycles = cpu.Step();

        Assert.Equal(0x3C, ram[0x01FF]);
        Assert.Equal(0xFE, cpu.State.S);
        Assert.Equal(3, cycles);
    }

    [Fact]
    public void Pla_PullsAccumulatorSetsFlagsAndTakesFourCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x68);
        cpu.State.S = 0xFE;
        ram[0x01FF] = 0x00;

        var cycles = cpu.Step();

        Assert.Equal(0x00, cpu.State.A);
        Assert.True(cpu.State.Z);
        Assert.Equal(0xFF, cpu.State.S);
        Assert.Equal(4, cycles);
    }

    [Fact]
    public void Php_PushesWithBreakAndUnusedBitsSet()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x08);
        cpu.State.P = Flag.C | Flag.Z;
        cpu.State.S = 0xFF;

        cpu.Step();

        Assert.Equal(Flag.C | Flag.Z | Flag.B | Flag.U, ram[0x01FF]);
    }

    [Fact]
    public void Plp_ClearsBreakAndForcesTheUnusedBit()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x28);
        cpu.State.S = 0xFE;
        ram[0x01FF] = 0xFF;

        cpu.Step();

        Assert.Equal(0xEF, cpu.State.P);   // B cleared, everything else including U retained
    }

    [Fact]
    public void Brk_PushesPcPlusTwoAndVectorsThroughFffe()
    {
        var (cpu, ram) = TestMachine.Flat(0x0300, 0x00);
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.C;
        ram[0xFFFE] = 0x00;
        ram[0xFFFF] = 0x90;

        var cycles = cpu.Step();

        Assert.Equal(0x9000, cpu.State.PC);
        Assert.Equal(7, cycles);
        Assert.Equal(0x03, ram[0x01FD]);                             // PC high of $0302
        Assert.Equal(0x02, ram[0x01FC]);                             // PC low
        Assert.Equal(Flag.U | Flag.C | Flag.B, ram[0x01FB]);         // pushed P has B set
        Assert.True(cpu.State.I);                                     // I is set on entry
        Assert.Equal(0xFA, cpu.State.S);
    }

    [Fact]
    public void Rti_RestoresStatusAndProgramCounterInSixCycles()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x40);
        cpu.State.S = 0xFA;
        ram[0x01FB] = Flag.C | Flag.B;    // pushed status, B set
        ram[0x01FC] = 0x34;               // PCL
        ram[0x01FD] = 0x12;               // PCH

        var cycles = cpu.Step();

        Assert.Equal(0x1234, cpu.State.PC);
        Assert.Equal(Flag.C | Flag.U, cpu.State.P);   // B masked out, U forced on
        Assert.Equal(0xFD, cpu.State.S);
        Assert.Equal(6, cycles);
    }

    [Fact]
    public void StackPointer_WrapsWithinPageOne()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x48);   // PHA
        cpu.State.A = 0x99;
        cpu.State.S = 0x00;

        cpu.Step();

        Assert.Equal(0x99, ram[0x0100]);
        Assert.Equal(0xFF, cpu.State.S);
    }
}
