using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class BranchTests
{
    [Fact]
    public void UntakenBranch_TakesTwoCyclesAndFallsThrough()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xD0, 0x10);   // BNE +$10
        cpu.State.Z = true;                                     // equal, so BNE is not taken

        var cycles = TestMachine.StepOne(cpu);

        Assert.Equal(0x0202, cpu.State.PC);
        Assert.Equal(2, cycles);
    }

    [Fact]
    public void TakenBranch_TakesThreeCyclesWithinAPage()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xD0, 0x10);   // BNE +$10
        cpu.State.Z = false;

        var cycles = TestMachine.StepOne(cpu);

        Assert.Equal(0x0212, cpu.State.PC);                     // $0202 + $10
        Assert.Equal(3, cycles);
    }

    [Fact]
    public void TakenBackwardBranch_ComputesASignedDisplacement()
    {
        var (cpu, _) = TestMachine.Flat(0x0250, 0xD0, 0xFB);   // BNE -5
        cpu.State.Z = false;

        var cycles = TestMachine.StepOne(cpu);

        Assert.Equal(0x024D, cpu.State.PC);                     // $0252 - 5
        Assert.Equal(3, cycles);
    }

    [Fact]
    public void TakenBranch_TakesFourCyclesForwardAcrossAPage()
    {
        var (cpu, _, log) = TestMachine.Logged(0x02F0, 0xD0, 0x20);   // BNE +$20
        cpu.State.Z = false;

        var cycles = TestMachine.StepOne(cpu);

        Assert.Equal(0x0312, cpu.State.PC);                     // $02F2 + $20
        Assert.Equal(4, cycles);
        Assert.Equal(
        [
            new BusAccess(0x02F0, 0xD0, false),
            new BusAccess(0x02F1, 0x20, false),
            new BusAccess(0x02F2, 0x00, false),   // dummy read at the byte after the branch
            new BusAccess(0x0212, 0x00, false),   // dummy read at the unfixed PC
        ], log);
    }

    [Fact]
    public void TakenBranch_TakesFourCyclesBackwardAcrossAPage()
    {
        var (cpu, _) = TestMachine.Flat(0x0305, 0xD0, 0x80);   // BNE -128
        cpu.State.Z = false;

        var cycles = TestMachine.StepOne(cpu);

        Assert.Equal(0x0287, cpu.State.PC);                     // $0307 - $80
        Assert.Equal(4, cycles);
    }

    [Theory]
    [InlineData(0x90, Flag.C, false, true)]   // BCC taken when C clear
    [InlineData(0x90, Flag.C, true,  false)]
    [InlineData(0xB0, Flag.C, true,  true)]   // BCS taken when C set
    [InlineData(0xB0, Flag.C, false, false)]
    [InlineData(0xF0, Flag.Z, true,  true)]   // BEQ
    [InlineData(0xD0, Flag.Z, false, true)]   // BNE
    [InlineData(0x30, Flag.N, true,  true)]   // BMI
    [InlineData(0x10, Flag.N, false, true)]   // BPL
    [InlineData(0x70, Flag.V, true,  true)]   // BVS
    [InlineData(0x50, Flag.V, false, true)]   // BVC
    public void EachBranch_TestsItsOwnFlag(byte opcode, byte flag, bool flagSet, bool expectTaken)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode, 0x04);
        cpu.State.P = flagSet ? (byte)(Flag.U | flag) : Flag.U;

        var cycles = TestMachine.StepOne(cpu);

        Assert.Equal(expectTaken ? 0x0206 : 0x0202, cpu.State.PC);
        Assert.Equal(expectTaken ? 3 : 2, cycles);
    }
}
