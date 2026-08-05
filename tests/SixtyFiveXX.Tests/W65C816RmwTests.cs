using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// The 65816's read-modify-write cycle, which is the one behaviour in this project whose bus
/// direction is decided at run time rather than at table-build time (datasheet Note 17,
/// research document §13.1). These tests assert on the bus log rather than on memory, because
/// the distinguishing fact — whether the middle cycle writes or reads — leaves no trace in the
/// final memory state at all.
/// </summary>
public class W65C816RmwTests
{
    /// <summary>
    /// Note 17: in emulation mode the middle cycle is a WRITE of the unmodified value, the NMOS
    /// double-write. Fails against a core that emits the CMOS dummy read in both modes — the
    /// final memory would be identical, and only the log distinguishes them.
    /// </summary>
    [Fact]
    public void AslDirectPage_EmulationMode_WritesTheOriginalValueBeforeTheResult()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x06;       // ASL dp
        ram[0xC001] = 0x10;
        ram[0x000010] = 0x21;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = true;       // emulation: m is forced to 1, so this is an 8-bit RMW
        cpu.State.DP = 0x0000;

        cpu.Step();

        var writes = ram.Log.FindAll(a => a.Write && a.Address == 0x000010);
        Assert.Equal(2, writes.Count);
        Assert.Equal(0x21, writes[0].Value);   // the unmodified value, written back first
        Assert.Equal(0x42, writes[1].Value);   // then the result
    }

    /// <summary>
    /// The native-mode counterpart: one write, not two. Together with the test above this pins
    /// both arms of Note 17, so neither can be deleted unnoticed.
    /// </summary>
    [Fact]
    public void AslDirectPage_NativeMode_DoesNotWriteTheOriginalValueBack()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x06;       // ASL dp
        ram[0xC001] = 0x10;
        ram[0x000010] = 0x21;

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = true;       // 8-bit accumulator, so the same one-byte RMW
        cpu.State.DP = 0x0000;

        cpu.Step();

        var writes = ram.Log.FindAll(a => a.Write && a.Address == 0x000010);
        Assert.Single(writes);
        Assert.Equal(0x42, writes[0].Value);
    }

    /// <summary>
    /// A 16-bit read-modify-write reads low-then-high and writes high-then-low (research
    /// document §13.2). Asserting the order, not just the bytes, is the point: a core that
    /// wrote low-then-high would leave identical memory.
    /// </summary>
    [Fact]
    public void AslAbsolute_SixteenBit_WritesTheHighByteFirst()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0E;       // ASL abs
        ram[0xC001] = 0x00;
        ram[0xC002] = 0x20;       // AA = $2000
        ram[0x002000] = 0x34;     // low
        ram[0x002001] = 0x12;     // high -> operand $1234

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;      // 16-bit
        cpu.State.DBR = 0x00;

        cpu.Step();

        var writes = ram.Log.FindAll(a => a.Write);
        Assert.Equal(2, writes.Count);
        Assert.Equal(0x002001, writes[0].Address);   // high byte first
        Assert.Equal(0x24, writes[0].Value);         // $1234 << 1 = $2468
        Assert.Equal(0x002000, writes[1].Address);
        Assert.Equal(0x68, writes[1].Value);
    }

    /// <summary>
    /// A 16-bit RMW through a DBR-relative mode carries into the next bank, exactly as a 16-bit
    /// load does (Clark §5.2 Example 2). Zero vector coverage is likely here for the same reason
    /// it was for the loads: it needs m=0 with the effective address landing on $xxFFFF.
    /// </summary>
    [Fact]
    public void AslAbsolute_SixteenBit_CarriesTheHighByteIntoTheNextBank()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x0E;       // ASL abs
        ram[0xC001] = 0xFF;
        ram[0xC002] = 0xFF;       // AA = $FFFF
        ram[0x12FFFF] = 0x34;     // low, DBR,AA
        ram[0x130000] = 0x12;     // high, carried into bank $13
        ram[0x120000] = 0x99;     // decoy: where a bank-wrapping read would land

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.DBR = 0x12;

        cpu.Step();

        Assert.Equal(0x68, ram[0x12FFFF]);
        Assert.Equal(0x24, ram[0x130000]);
        Assert.Equal(0x99, ram[0x120000]);   // untouched
    }

    /// <summary>
    /// The symmetric case: direct page is bank-0 confined, so a 16-bit RMW whose low byte sits
    /// at $00FFFF takes its high byte from $000000, not $010000. Fails if DirectPage is dropped
    /// from EmitAddressed816's bank-0 exclusion set for the read-modify-write path.
    /// </summary>
    [Fact]
    public void AslDirectPage_SixteenBit_WrapsTheHighByteWithinBankZero()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0x06;       // ASL dp
        ram[0xC001] = 0x00;
        ram[0x00FFFF] = 0x34;     // low, 0,D+DO
        ram[0x000000] = 0x12;     // high, wrapped within bank 0
        ram[0x010000] = 0x99;     // decoy

        var cpu = Banked816TestMachine.Make(ram);
        cpu.State.E = false;
        cpu.State.M = false;
        cpu.State.DP = 0xFFFF;    // D + DO = $FFFF

        cpu.Step();

        Assert.Equal(0x68, ram[0x00FFFF]);
        Assert.Equal(0x24, ram[0x000000]);
        Assert.Equal(0x99, ram[0x010000]);
    }
}
