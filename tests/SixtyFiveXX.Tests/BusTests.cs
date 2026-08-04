using SixtyFiveXX;
using Xunit;

namespace SixtyFiveXX.Tests;

public class BusTests
{
    [Fact]
    public void FlatBus_RoundTripsAByte()
    {
        var ram = new byte[0x10000];
        var bus = new FlatBus(ram);

        bus.Write(0x1234, 0xAB);

        Assert.Equal(0xAB, bus.Read(0x1234));
        Assert.Equal(0xAB, ram[0x1234]);
    }

    [Fact]
    public void FlatBus_MasksAddressesToSixteenBits()
    {
        var ram = new byte[0x10000];
        var bus = new FlatBus(ram);

        bus.Write(0x1_0042, 0x7F);

        Assert.Equal(0x7F, ram[0x0042]);
        Assert.Equal(0x7F, bus.Read(0x0042));
    }

    [Fact]
    public void RefBus_ForwardsToTheWrappedBus()
    {
        var ram = new byte[0x10000];
        IBus inner = new RecordingBus(ram);
        var bus = new RefBus(inner);

        bus.Write(0x00FF, 0x11);

        Assert.Equal(0x11, bus.Read(0x00FF));
    }

    [Fact]
    public void RefBus_ForwardsInternalCyclesToTheInnerBus()
    {
        var inner = new RecordingBus(new byte[0x10000]);
        var bus = new RefBus(inner);

        bus.Internal(0x7E1234);

        Assert.Single(inner.Internals);
        Assert.Equal(0x7E1234, inner.Internals[0]);
    }

    [Fact]
    public void FlatBus_AcceptsInternalCyclesWithoutTouchingMemory()
    {
        var ram = new byte[0x10000];
        ram[0x1234] = 0xAB;
        var bus = new FlatBus(ram);

        bus.Internal(0x1234);

        Assert.Equal(0xAB, ram[0x1234]);
    }

    [Fact]
    public void Internal_IsOptionalForBusesThatDoNotCareAboutIt()
    {
        // The default implementation exists so that adding this member breaks nobody. A bus
        // written before the 65816 must still satisfy IBus, and the inherited default must
        // do nothing rather than throw.
        IBus bus = new DefaultOnlyBus();

        Assert.Null(Record.Exception(() => bus.Internal(0x1234)));
    }

    private sealed class RecordingBus(byte[] ram) : IBus
    {
        public List<int> Internals { get; } = [];
        public byte Read(int address) => ram[address & 0xFFFF];
        public void Write(int address, byte value) => ram[address & 0xFFFF] = value;
        public void Internal(int address) => Internals.Add(address);
    }

    /// <summary>A bus written before the 65816 existed: it does not implement Internal at all.</summary>
    private readonly struct DefaultOnlyBus : IBus
    {
        public byte Read(int address) => 0;
        public void Write(int address, byte value) { }
    }
}
