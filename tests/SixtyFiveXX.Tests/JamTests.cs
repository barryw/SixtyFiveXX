using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class JamTests
{
    [Theory]
    [InlineData(0x02)] [InlineData(0x12)] [InlineData(0x22)] [InlineData(0x32)]
    [InlineData(0x42)] [InlineData(0x52)] [InlineData(0x62)] [InlineData(0x72)]
    [InlineData(0x92)] [InlineData(0xB2)] [InlineData(0xD2)] [InlineData(0xF2)]
    public void JamOpcodes_HaltTheProcessor(byte opcode)
    {
        var (cpu, _) = TestMachine.Flat(0x0200, opcode);

        cpu.Run(20);

        Assert.True(cpu.IsJammed);
        Assert.Equal(0x0201, cpu.State.PC);   // PC advanced past the opcode only
    }

    [Fact]
    public void Jam_ProducesTheDocumentedBusPattern()
    {
        var (cpu, _, log) = TestMachine.Logged(0x0200, 0x02);

        cpu.Run(11);

        Assert.Equal(11, log.Count);
        Assert.All(log, a => Assert.False(a.IsWrite));
        Assert.Equal(0x0200, log[0].Address);   // opcode fetch
        Assert.Equal(0x0201, log[1].Address);   // dummy read at PC
        Assert.Equal(0xFFFF, log[2].Address);
        Assert.Equal(0xFFFE, log[3].Address);
        Assert.Equal(0xFFFE, log[4].Address);
        Assert.Equal(0xFFFF, log[5].Address);
        Assert.Equal(0xFFFF, log[10].Address);
    }

    [Fact]
    public void Step_ReturnsInsteadOfSpinningOnAJam()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x02);

        var cycles = cpu.Step();

        Assert.True(cpu.IsJammed);
        Assert.True(cycles > 0, "Step must consume cycles before returning.");
    }

    [Fact]
    public void RunUntil_StopsWhenTheProcessorJams()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0x02);

        var cycles = cpu.RunUntil(c => c.State.A == 0xFF, maxCycles: 1000);

        Assert.True(cpu.IsJammed);
        Assert.True(cycles < 1000, $"Expected an early stop on jam, ran {cycles} cycles.");
    }

    [Fact]
    public void Reset_ClearsTheJammedState()
    {
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x02);
        ram[0xFFFC] = 0x00;
        ram[0xFFFD] = 0x80;

        cpu.Run(10);
        Assert.True(cpu.IsJammed);

        cpu.Reset();
        cpu.Step();

        Assert.False(cpu.IsJammed);
        Assert.Equal(0x8000, cpu.State.PC);
    }

    [Fact]
    public void UnjammedCpu_ReportsNotJammed()
    {
        var (cpu, _) = TestMachine.Flat(0x0200, 0xEA);

        cpu.Step();

        Assert.False(cpu.IsJammed);
    }

    [Fact]
    public void JammedCpu_IgnoresIrqAndNmi()
    {
        // A jam holds _mpc >= 0 forever, so FetchOpcode — the only place either interrupt
        // is dispatched — never runs again. Nothing pins that interaction directly; this
        // does. IRQ/BRK and NMI vectors are set to distinct, recognisable targets so a
        // dispatch that slipped through would move PC somewhere identifiable.
        var (cpu, ram) = TestMachine.Flat(0x0200, 0x02);   // JAM
        ram[0xFFFE] = 0x00; ram[0xFFFF] = 0x90;             // IRQ/BRK -> $9000
        ram[0xFFFA] = 0x00; ram[0xFFFB] = 0x80;             // NMI     -> $8000
        cpu.State.P = Flag.U;                               // I clear: only the jam should block IRQ

        cpu.Run(11);
        Assert.True(cpu.IsJammed);

        cpu.SetIrq(true);
        cpu.SetNmi(true);
        cpu.Run(20);

        Assert.True(cpu.IsJammed);
        Assert.Equal(0x0201, cpu.State.PC);   // unmoved: no dispatch to either vector
    }
}
