using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

/// <summary>
/// Defect 2, carried since phase 7b: a set of micro-ops compute a bare sixteen-bit PC or a
/// bare $0100 + SL, both of which are wrong on a 65816 — program reads are at PBR,PC and the
/// native-mode stack is a full sixteen bits anywhere in bank 0.
/// <para>
/// The fix is that the 65816 reaches none of them. That is asserted here rather than by
/// inspection, because inspection is what let the defect survive four phases.
/// </para>
/// </summary>
public class W65C816ReachabilityTests
{
    /// <summary>
    /// Every micro-op below computes an address the 65816 cannot use. The list is produced
    /// mechanically, not from memory: every <c>case MicroOp.X</c> in <c>Cpu.Execute</c> whose
    /// body passes a bare <c>_s.PC</c> to <c>ReadBus</c>/<c>WriteBus</c>, or contains the
    /// literal <c>0x0100</c>.
    /// <para>
    /// <c>NopAbsExtraRead</c> and <c>JmpAbsXDummy</c> are the two the grep's literal pattern
    /// misses and judgement adds: they read <c>(_s.PC - 1) &amp; 0xFFFF</c> and
    /// <c>(_s.PC - 2) &amp; 0xFFFF</c>, which is a bare sixteen-bit PC with the bank dropped
    /// exactly as <c>ReadBus(_s.PC)</c> is. Members the grep hits but that compute neither kind
    /// of address are deliberately absent: <c>ImpliedDummy</c> and <c>ImmExec</c> already call
    /// <c>PcAddress()</c> and are bank-aware; <c>JmpIndLo</c>, <c>JmpIndHi</c>,
    /// <c>JmpIndBugDummy</c>, <c>PtrJmpHi</c>, <c>BitBranchDummy</c> and <c>BitBranchFixup</c>
    /// read <c>_ptr</c> or <c>_addr</c>; and <c>JamHold</c> drives the literal
    /// <c>$FFFF</c>/<c>$FFFE</c> jam pattern.
    /// </para>
    /// </summary>
    private static readonly MicroOp[] EightBitOnly =
    [
        MicroOp.ImpliedExec,
        MicroOp.FetchAddrHiX, MicroOp.FetchAddrHiY,
        MicroOp.BranchFetch, MicroOp.BranchTaken, MicroOp.BranchFixup,
        MicroOp.BitBranchFetch,
        MicroOp.JmpAbs, MicroOp.JsrFinish, MicroOp.RtsFinish,
        MicroOp.NopAbsExtraRead, MicroOp.JmpAbsXDummy,
        MicroOp.BrkPad, MicroOp.IntDummy,
        MicroOp.StackDummyRead, MicroOp.StackDummyReadInc, MicroOp.StackDummyReadDec,
        MicroOp.PushPch, MicroOp.PushPcl, MicroOp.PullPcl, MicroOp.PullPch,
        MicroOp.PullP, MicroOp.Push, MicroOp.Pull,
        MicroOp.PushPBrk, MicroOp.PushPInt, MicroOp.PushPBrkCmos, MicroOp.PushPIntCmos,
        MicroOp.WaiHold, MicroOp.StpHold,
    ];

    [Fact]
    public void No816SequenceReachesAnEightBitOnlyMicroOp()
    {
        var table = MicroOpTable.For<W65C816Variant>();
        var banned = new HashSet<MicroOp>(EightBitOnly);

        for (var opcode = 0; opcode < 256; opcode++)
        {
            for (var i = table.Entry[opcode]; table.Ops[i] != MicroOp.End; i++)
            {
                Assert.False(banned.Contains(table.Ops[i]),
                    $"${opcode:X2} {table.Info[opcode].Mnemonic} reaches {table.Ops[i]}, " +
                    "which computes a bare 16-bit PC or a bare $0100 + SL.");
            }
        }
    }

    [Fact]
    public void NeitherThe816InterruptNorItsResetSectionReachesOne()
    {
        var table = MicroOpTable.For<W65C816Variant>();
        var banned = new HashSet<MicroOp>(EightBitOnly);

        foreach (var (name, entry) in new[]
                 {
                     ("IrqEntry", table.IrqEntry), ("ResetEntry", table.ResetEntry),
                 })
        {
            for (var i = entry; table.Ops[i] != MicroOp.End; i++)
            {
                Assert.False(banned.Contains(table.Ops[i]),
                    $"{name} reaches {table.Ops[i]}, which computes a bare 16-bit PC " +
                    "or a bare $0100 + SL.");
            }
        }
    }

    /// <summary>
    /// The counterweight: the same deny-list must still be reachable on the eight-bit cores,
    /// where every one of these micro-ops is correct. Without this, deleting an entry to make
    /// the two tests above pass would look like a fix.
    /// </summary>
    [Fact]
    public void EveryDeniedMicroOpIsStillReachedByAnEightBitCore()
    {
        var reached = new HashSet<MicroOp>();

        foreach (var table in new[]
                 {
                     MicroOpTable.For<Mos6502Variant>(), MicroOpTable.For<Wdc65C02Variant>(),
                     MicroOpTable.For<Rockwell65C02Variant>(),
                 })
        {
            foreach (var op in table.Ops) reached.Add(op);
        }

        foreach (var op in EightBitOnly)
        {
            Assert.True(reached.Contains(op),
                $"{op} is on the 65816 deny-list but no 8-bit core reaches it either — it is " +
                "dead, or the deny-list has drifted.");
        }
    }
}
