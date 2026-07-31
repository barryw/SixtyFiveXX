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

    private sealed class RecordingBus(byte[] ram) : IBus
    {
        public byte Read(int address) => ram[address & 0xFFFF];
        public void Write(int address, byte value) => ram[address & 0xFFFF] = value;
    }
}
