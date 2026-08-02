using BenchmarkDotNet.Attributes;
using SixtyFiveXX.Variants;

namespace SixtyFiveXX.Benchmarks;

/// <summary>Measures raw tick throughput against a flat 64 KB memory.</summary>
[MemoryDiagnoser]
public class CoreBenchmark
{
    private Cpu<FlatBus, Mos6502Variant> _cpu = null!;

    /// <summary>Cycles executed per invocation.</summary>
    [Params(10_000_000)]
    public int Cycles { get; set; }

    /// <summary>Builds a fresh CPU before each run.</summary>
    [IterationSetup]
    public void Setup() => _cpu = Workload.Build();

    /// <summary>Ticks the core one cycle at a time — the path a machine personality drives.</summary>
    [Benchmark(Baseline = true)]
    public long Tick()
    {
        for (var i = 0; i < Cycles; i++) _cpu.Tick();
        return _cpu.Cycles;
    }

    /// <summary>Runs whole instructions, the path a test harness drives.</summary>
    [Benchmark]
    public long Step()
    {
        while (_cpu.Cycles < Cycles) _cpu.Step();
        return _cpu.Cycles;
    }
}
