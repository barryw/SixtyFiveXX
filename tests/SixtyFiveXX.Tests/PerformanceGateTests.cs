using System.Diagnostics;
using SixtyFiveXX;
using Xunit;
using Xunit.Abstractions;

namespace SixtyFiveXX.Tests;

/// <summary>
/// A coarse throughput floor, run in its own CI stage. BenchmarkDotNet produces the
/// real number; this exists so a catastrophic regression fails the build.
/// </summary>
[Trait("Category", "Performance")]
public class PerformanceGateTests(ITestOutputHelper output)
{
    private const long FloorHz = 50_000_000;
    private const int MeasuredCycles = 50_000_000;

    [Fact]
    public void Core_SustainsAtLeastFiftyMegahertz()
    {
        var cpu = BuildWorkload();

        // Warm up so the JIT has tiered up before the measured run.
        for (var i = 0; i < 2_000_000; i++) cpu.Tick();

        cpu = BuildWorkload();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < MeasuredCycles; i++) cpu.Tick();
        stopwatch.Stop();

        var hz = MeasuredCycles / stopwatch.Elapsed.TotalSeconds;
        output.WriteLine($"{hz / 1_000_000:F1} MHz simulated ({stopwatch.ElapsedMilliseconds} ms).");

        // A benchmark that has fallen out of its own program measures nothing useful.
        // The workload occupies $0200-$021E; anything else means execution derailed.
        Assert.True(cpu.State.PC >= 0x0200 && cpu.State.PC <= 0x021E,
            $"Workload derailed to ${cpu.State.PC:X4} — the measurement is not of the intended program.");

        Assert.True(hz >= FloorHz,
            $"Throughput fell to {hz / 1_000_000:F1} MHz, below the {FloorHz / 1_000_000} MHz floor.");
    }

    private static Cpu<FlatBus> BuildWorkload()
    {
        var ram = new byte[0x10000];
        byte[] program =
        [
            0xA9, 0x01, 0x85, 0x10, 0xA5, 0x10, 0xAD, 0x00, 0x30,
            0xBD, 0x00, 0x30, 0x9D, 0x00, 0x31, 0xA1, 0x20, 0xB1, 0x22,
            0xEE, 0x00, 0x32, 0x69, 0x05, 0xE8, 0xC8, 0xD0, 0xE4, // BNE -28 back to the top
            0x4C, 0x00, 0x02,
        ];
        program.CopyTo(ram, 0x0200);
        ram[0x0020] = 0x00; ram[0x0021] = 0x34;
        ram[0x0022] = 0x00; ram[0x0023] = 0x35;

        var cpu = new Cpu<FlatBus>(new FlatBus(ram));
        cpu.State.PC = 0x0200;
        cpu.State.S = 0xFD;
        cpu.State.P = Flag.U | Flag.I;
        return cpu;
    }
}
