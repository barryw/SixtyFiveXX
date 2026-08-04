namespace SixtyFiveXX;

/// <summary>
/// One CPU cycle's worth of work. Every member performs at most one bus access, so a
/// micro-op and a clock cycle are the same thing.
/// </summary>
/// <remarks>
/// The opcode-fetch cycle is implicit: it is performed by the tick loop, not by a
/// member of this enum. Sequences therefore describe cycle 2 onward. Likewise, a cycle
/// halted by RDY is handled entirely by the tick loop and executes no member of this
/// enum at all — it re-drives the bus without consulting the sequence.
/// </remarks>
internal enum MicroOp : byte
{
    /// <summary>Dummy read at PC; run the operation. Implied and accumulator modes.</summary>
    ImpliedExec,

    /// <summary>Read the operand at PC++; run the operation. Immediate mode.</summary>
    ImmExec,

    /// <summary>Dummy read at PC. Used as a filler cycle by stack instructions.</summary>
    ImpliedDummy,

    /// <summary>addr = Read(PC++).</summary>
    FetchAddrLo,

    /// <summary>addr |= Read(PC++) &lt;&lt; 8.</summary>
    FetchAddrHi,

    /// <summary>Read the high byte at PC++ and index by X, recording any page cross.</summary>
    FetchAddrHiX,

    /// <summary>Read the high byte at PC++ and index by Y, recording any page cross.</summary>
    FetchAddrHiY,

    /// <summary>Dummy read at addr; addr = (addr + X) &amp; 0xFF.</summary>
    ZpIndexX,

    /// <summary>Dummy read at addr; addr = (addr + Y) &amp; 0xFF.</summary>
    ZpIndexY,

    /// <summary>ptr = addr; tmp = Read(ptr). Low byte of an indirect pointer.</summary>
    PtrReadLo,

    /// <summary>addr = (Read((ptr + 1) &amp; 0xFF) &lt;&lt; 8) | tmp.</summary>
    PtrReadHi,

    /// <summary>As <see cref="PtrReadHi"/>, then index by Y, recording any page cross.</summary>
    PtrReadHiY,

    /// <summary>data = Read(addr); run the operation. Final cycle of a read instruction.</summary>
    ReadExec,

    /// <summary>
    /// data = Read(addr). If no page cross, run the operation and end the instruction;
    /// otherwise fix the high byte of addr and continue to <see cref="ReadExec"/>.
    /// </summary>
    ReadPageCross,

    /// <summary>Dummy read at addr; unconditionally fix addr if a page was crossed.</summary>
    DummyReadFixup,

    /// <summary>
    /// Dummy read at addr, then the unstable-store address correction: on a page cross
    /// the stored value's high-byte AND is folded into the address itself. Used only by
    /// SHA, SHX, SHY and TAS.
    /// </summary>
    UnstableStoreFixup,

    /// <summary>Run the operation to produce data, then Write(addr, data).</summary>
    ExecWrite,

    /// <summary>data = Read(addr). First cycle of a read-modify-write.</summary>
    RmwRead,

    /// <summary>Write(addr, data) with the original value, then run the operation. NMOS dummy write.</summary>
    RmwModifyWrite,

    /// <summary>Write(addr, data) with the modified value.</summary>
    RmwWrite,

    /// <summary>Read the displacement at PC++; end the instruction if the branch is not taken.</summary>
    BranchFetch,

    /// <summary>Dummy read at PC; adjust PC's low byte; end the instruction if no page was crossed.</summary>
    BranchTaken,

    /// <summary>Dummy read at the unfixed PC; correct PC's high byte.</summary>
    BranchFixup,

    /// <summary>PC = (Read(PC) &lt;&lt; 8) | addr. Final cycle of JMP absolute.</summary>
    JmpAbs,

    /// <summary>ptr = addr; tmp = Read(ptr). Low byte of a JMP indirect vector.</summary>
    JmpIndLo,

    /// <summary>
    /// PC = (Read((ptr &amp; 0xFF00) | ((ptr + 1) &amp; 0xFF)) &lt;&lt; 8) | tmp.
    /// Reproduces the NMOS page-wrap bug.
    /// </summary>
    JmpIndHi,

    /// <summary>Dummy read at $0100 + S.</summary>
    StackDummyRead,

    /// <summary>Dummy read at $0100 + S, then S++.</summary>
    StackDummyReadInc,

    /// <summary>Dummy read at $0100 + S, then S--. Used by the reset sequence.</summary>
    StackDummyReadDec,

    /// <summary>Write(0x100 + S, PC high); S--.</summary>
    PushPch,

    /// <summary>Write(0x100 + S, PC low); S--.</summary>
    PushPcl,

    /// <summary>PC = (Read(PC) &lt;&lt; 8) | addr. Final cycle of JSR.</summary>
    JsrFinish,

    /// <summary>tmp = Read(0x100 + S); S++.</summary>
    PullPcl,

    /// <summary>PC = (Read(0x100 + S) &lt;&lt; 8) | tmp.</summary>
    PullPch,

    /// <summary>Dummy read at PC; PC++. Final cycle of RTS.</summary>
    RtsFinish,

    /// <summary>
    /// The CMOS arithmetic read. Reads the operand, and finishes the instruction there
    /// unless <c>D</c> is set — decimal mode costs one more cycle, supplied by
    /// <see cref="BcdExtra"/>.
    /// </summary>
    ReadExecCmosArith,

    /// <summary>
    /// <see cref="ReadExecCmosArith"/> for immediate mode, reading the operand at PC.
    /// </summary>
    ImmExecCmosArith,

    /// <summary>
    /// The extra cycle CMOS <c>ADC</c>/<c>SBC</c> spend in decimal mode: a discarded repeat
    /// read of the effective address, then the operation.
    /// </summary>
    BcdExtra,

    /// <summary>
    /// <see cref="ReadPageCrossCmos"/> for CMOS arithmetic, which needs a third outcome:
    /// without a page cross this cycle is the real read, and it must still route into the
    /// decimal-mode extra cycle rather than finishing unconditionally.
    /// </summary>
    ReadPageCrossCmosArith,

    /// <summary>
    /// The CMOS middle cycle of a read-modify-write: a dummy <em>read</em> of the effective
    /// address where NMOS writes the unmodified value back. Same cycle count either way.
    /// </summary>
    RmwModifyRead,

    /// <summary>
    /// The CMOS indexing fixup: a discarded read at the <em>last operand byte</em> rather
    /// than at the mis-indexed address NMOS reads. That is <c>PC - 1</c> by the time this
    /// runs — <c>PC+2</c> for the absolute-indexed modes, <c>PC+1</c> for <c>(zp),Y</c>.
    /// Unconditional, so it is what writes and the always-7-cycle <c>INC</c>/<c>DEC abs,X</c>
    /// use.
    /// </summary>
    IndexFixupCmos,

    /// <summary>
    /// The CMOS conditional read fixup. Without a page cross this cycle is the real read
    /// and the instruction ends; with one it is a discarded read at the last operand byte
    /// and the following micro-op reads the corrected address. Same shape as
    /// <see cref="ReadPageCross"/>, differing only in which address the discarded read uses.
    /// </summary>
    ReadPageCrossCmos,

    /// <summary>
    /// The CMOS conditional fixup for an indexed read-modify-write. Without a page cross
    /// this cycle performs the RMW's own read and skips the <see cref="RmwRead"/> that
    /// follows, giving six cycles; with one it is a discarded read at the last operand byte
    /// and the full seven-cycle sequence runs.
    /// </summary>
    /// <remarks>
    /// Only the shift and rotate forms of <c>abs,X</c> use this. <c>INC</c> and
    /// <c>DEC abs,X</c> are seven cycles whether or not indexing crosses a page, and take
    /// <see cref="IndexFixupCmos"/> instead — a split with no counterpart on NMOS, where
    /// every indexed RMW always pays.
    /// </remarks>
    RmwPageCrossCmos,

    /// <summary>The discarded re-read of the byte BBR/BBS is testing.</summary>
    BitBranchDummy,

    /// <summary>
    /// BBR/BBS's page-cross cycle. An ordinary branch re-reads the half-corrected PC here;
    /// these re-read the byte after the displacement instead — the same address the
    /// preceding cycle read. Measured from the vectors, which show that address twice.
    /// </summary>
    BitBranchFixup,

    /// <summary>
    /// Fetches BBR/BBS's displacement and ends the instruction unless the tested bit selects
    /// the branch. The bit index comes from bits 4-6 of the opcode. The tested byte arrives
    /// in <c>data</c> from <see cref="RmwRead"/>; this replaces it with the displacement, so
    /// the ordinary <see cref="BranchTaken"/> and <see cref="BranchFixup"/> finish the job.
    /// </summary>
    BitBranchFetch,

    /// <summary>
    /// A discarded re-read of the high operand byte, used only by the three-byte
    /// four-cycle CMOS NOPs.
    /// </summary>
    NopAbsExtraRead,

    /// <summary>
    /// The discarded read the CMOS <c>JMP (abs)</c> performs at the address the NMOS core
    /// would have taken its high byte from — <c>(ptr &amp; $FF00) | ((ptr + 1) &amp; $FF)</c>.
    /// </summary>
    /// <remarks>
    /// This is the whole shape of the CMOS fix, and it is not what "the page-wrap bug is
    /// fixed, at the cost of one cycle" suggests. The 65C02 still reads the buggy address;
    /// it then reads the correct one and keeps that. When the pointer's low byte is not
    /// <c>$FF</c> the two addresses coincide, which is why every vector — wrapping or not —
    /// shows six cycles with the fifth and sixth reads adjacent. Adding a generic dummy
    /// cycle instead would produce the right cycle count and the wrong addresses.
    /// </remarks>
    JmpIndBugDummy,

    /// <summary>
    /// PC = (Read((ptr + 1) &amp; $FFFF) &lt;&lt; 8) | tmp — the high byte with no page wrap.
    /// The CMOS counterpart of <see cref="JmpIndHi"/>.
    /// </summary>
    PtrJmpHi,

    /// <summary>
    /// The discarded read <c>JMP (abs,X)</c> performs at the first operand byte, then
    /// indexes the pointer by X. The dummy is at that fixed address whether or not the
    /// indexing crosses a page, so this mode has no page-cross penalty.
    /// </summary>
    JmpAbsXDummy,

    /// <summary>P = Read(0x100 + S) with B cleared and U set; S++.</summary>
    PullP,

    /// <summary>Run the operation to produce data; Write(0x100 + S, data); S--.</summary>
    Push,

    /// <summary>data = Read(0x100 + S); run the operation.</summary>
    Pull,

    /// <summary>Dummy read at PC; PC++. BRK's signature byte.</summary>
    BrkPad,

    /// <summary>Dummy read at PC. Filler for the hardware interrupt sequence and, twice over, for reset.</summary>
    IntDummy,

    /// <summary>
    /// BRK's P push. Sets vector to the IRQ/BRK vector, then lets a pending NMI hijack it
    /// — this is the cycle on which the vector is committed. Write(0x100 + S, P) with B
    /// set; S--; set I.
    /// </summary>
    PushPBrk,

    /// <summary>
    /// A hardware interrupt's P push. Commits the vector on the same cycle as
    /// <see cref="PushPBrk"/>, but only redirects an IRQ-vectored sequence.
    /// Write(0x100 + S, P) with B clear; S--; set I.
    /// </summary>
    PushPInt,

    /// <summary>
    /// The CMOS form of <see cref="PushPBrk"/>. Two deltas, both on this one cycle: no NMI
    /// hijack, and <c>D</c> is cleared. The pushed byte still carries <c>D</c> as it was —
    /// the flag is cleared for the handler, not for the pushed copy — so <c>RTI</c> restores
    /// it. Write(0x100 + S, P) with B set; S--; set I; clear D.
    /// </summary>
    PushPBrkCmos,

    /// <summary>
    /// The CMOS form of <see cref="PushPInt"/>. As <see cref="PushPBrkCmos"/>: no hijack,
    /// and <c>D</c> cleared after the push. A latched NMI is left latched, so it is taken
    /// after the handler's first instruction rather than stealing this sequence's vector.
    /// Write(0x100 + S, P) with B clear; S--; set I; clear D.
    /// </summary>
    PushPIntCmos,

    /// <summary>
    /// tmp = Read(vector). On the BRK/interrupt path the vector was committed on the
    /// preceding <see cref="PushPBrk"/> or <see cref="PushPInt"/>. The reset sequence has no
    /// P push — it reaches this micro-op straight from <c>StackDummyReadDec</c> — so there
    /// the vector was committed directly by <c>Reset()</c> instead.
    /// </summary>
    VectorLo,

    /// <summary>
    /// PC = (Read(vector + 1) &lt;&lt; 8) | tmp. The final cycle of every interrupt
    /// sequence, and the one on which interrupt recognition is blacked out.
    /// </summary>
    VectorHi,

    /// <summary>Sequence terminator. Consumes no cycle.</summary>
    End,

    /// <summary>
    /// Holds the processor until an interrupt is signalled. WDC's <c>WAI</c>. The sequence
    /// position is held, so this micro-op repeats until IRQ is asserted or an NMI is
    /// latched — the <c>I</c> flag does not block the wake, only what happens afterwards.
    /// </summary>
    WaiHold,

    /// <summary>
    /// Holds the processor until reset. WDC's <c>STP</c>. Distinct from
    /// <see cref="JamHold"/>: a JAM is a decode failure that drives a fixed address pattern,
    /// while STP is a defined instruction that simply stops.
    /// </summary>
    StpHold,

    /// <summary>
    /// Drives the address bus while jammed and never advances. The sequence position is
    /// held, so this micro-op repeats for as long as the clock runs.
    /// </summary>
    JamHold,

    /// <summary>
    /// Placeholder for every 65816 sequence slot no task has filled in yet — see
    /// <c>MicroOpTable</c>'s <c>NotYet816</c> field, the only place this is used.
    /// Deliberately has no <c>case</c> in <c>Cpu.Execute</c>'s switch, so reaching one falls
    /// into that switch's own <c>default</c> arm and throws
    /// <see cref="NotImplementedException"/> naming this member, instead of silently
    /// running whichever NMOS or CMOS micro-op happened to occupy the slot. Phase 7d's
    /// interrupt work is what finally replaces every use of it.
    /// </summary>
    Unimplemented816,

    /// <summary>
    /// The 65816's implied-mode second cycle: an internal cycle at <c>PBR,PC</c> — no memory
    /// access at all — then run the operation. Research document §9, row 19a: cycle 2 of
    /// <c>XCE</c> (and every other implied 65816 instruction) drives <c>VDA=0 VPA=0</c>, which
    /// is <see cref="IBus.Internal"/>, not the dummy read <see cref="ImpliedExec"/> performs
    /// for the five 8-bit cores. <c>PC</c> already reflects the opcode fetch's increment, so
    /// the address is simply <c>(PBR &lt;&lt; 16) | PC</c> with no further adjustment.
    /// </summary>
    ImpliedExec816,
}

/// <summary>
/// The 65816's four bus-qualifier pins, in ASSERTED polarity rather than the datasheet's
/// electrical one. <c>VPB</c> and <c>MLB</c> are active-low on the real part — WDC's Table
/// 5-7 (research §9) prints <c>1</c> on those two columns when they are inactive — but the
/// SingleStepTests 65816 vectors encode them the other way round, a lowercase letter meaning
/// active. These flags match the vectors: a set bit always means "this pin is asserted."
/// </summary>
/// <remarks>
/// <see cref="Vda"/> and <see cref="Vpa"/> need no inversion when reading the datasheet — it
/// already prints them true-asserted. Only <see cref="Vpb"/> and <see cref="Mlb"/> are
/// inverted relative to Table 5-7's own column values.
/// </remarks>
[Flags]
internal enum BusPins : byte
{
    /// <summary>
    /// No bus-qualifier pin asserted: an internal cycle with no memory access at all. No
    /// 8-bit-core micro-op is ever classified this way — see <see cref="MicroOps.PinsFor"/>.
    /// </summary>
    None = 0,

    /// <summary>
    /// Valid Data Address. Asserted on an opcode fetch (together with <see cref="Vpa"/>) and
    /// on every other real or dummy read or write at an effective address, a pointer, the
    /// stack, or an interrupt/reset vector.
    /// </summary>
    Vda = 1,

    /// <summary>
    /// Valid Program Address. Asserted on an opcode fetch (together with <see cref="Vda"/>)
    /// and on every read of the live program counter — an operand fetch, or a dummy read that
    /// rereads PC without advancing it.
    /// </summary>
    Vpa = 2,

    /// <summary>
    /// Vector Pull, active-low on the part. This flag is set on exactly the two cycles that
    /// read an interrupt or reset vector: <see cref="MicroOp.VectorLo"/> and
    /// <see cref="MicroOp.VectorHi"/>.
    /// </summary>
    Vpb = 4,

    /// <summary>
    /// Memory Lock, active-low on the part. This flag is set on the cycles of a
    /// read-modify-write instruction that actually touch the target byte — the read, the
    /// modify, and the final write — so an external bus arbiter knows not to interrupt the
    /// sequence. Not set on the addressing-mode cycles that merely compute the target address,
    /// even when they occur inside an RMW instruction.
    /// </summary>
    Mlb = 8,
}

/// <summary>Classifies micro-ops by bus direction and bus-qualifier pins, for the RDY halt line and for readback.</summary>
internal static class MicroOps
{
    private static readonly bool[] Writes = BuildWriteTable();

    /// <summary>True when this micro-op drives a write. RDY does not halt a write cycle.</summary>
    public static bool IsWriteCycle(MicroOp op) => Writes[(int)op];

    /// <summary>
    /// True for the micro-ops that read PC and can be held indefinitely, so a cycle halted
    /// by RDY must drive PC rather than the effective-address register.
    /// </summary>
    /// <remarks>
    /// Only <c>WAI</c> and <c>STP</c> qualify. Every other read micro-op runs for a bounded
    /// number of cycles, so the stale address the halt path drives for those is a single
    /// wrong cycle — a known limitation recorded at the halt branch in <c>Cpu.Tick</c>.
    /// These two are unbounded: a hold lasts until an interrupt or a reset, so the wrong
    /// address would be driven for the entire wait. That matters most for exactly the case
    /// <c>WAI</c> exists to serve, synchronising with hardware that also drives RDY.
    /// </remarks>
    public static bool HoldsAtPc(MicroOp op) => op is MicroOp.WaiHold or MicroOp.StpHold;

    private static readonly BusPins[] Pins = BuildPinsTable();

    /// <summary>
    /// The <see cref="BusPins"/> this micro-op asserts, in ASSERTED polarity. A fixed
    /// property of the micro-op — looked up once per cycle, never recomputed from register
    /// state — so <c>Cpu.Tick</c> can record it on the hot path at the cost of one array
    /// index.
    /// </summary>
    public static BusPins PinsFor(MicroOp op) => Pins[(int)op];

    private static readonly bool[] InternalCycles = BuildInternalCycleTable();

    /// <summary>
    /// True for the micro-ops legitimately classified <see cref="BusPins.None"/> by
    /// <see cref="PinsFor"/> — a cycle that performs no bus access at all, rather than one
    /// nobody got around to classifying. Kept as its own explicit table, not a fallback,
    /// specifically so a genuinely unclassified micro-op still reads <see cref="BusPins.None"/>
    /// from <see cref="Pins"/> and still fails the "every micro-op is classified" test instead
    /// of silently passing it.
    /// </summary>
    public static bool IsInternalCycle(MicroOp op) => InternalCycles[(int)op];

    private static bool[] BuildWriteTable()
    {
        var writes = new bool[Enum.GetValues<MicroOp>().Length];

        foreach (var op in new[]
                 {
                     MicroOp.ExecWrite, MicroOp.RmwModifyWrite, MicroOp.RmwWrite,
                     MicroOp.PushPch, MicroOp.PushPcl, MicroOp.Push,
                     MicroOp.PushPBrk, MicroOp.PushPInt,
                     MicroOp.PushPBrkCmos, MicroOp.PushPIntCmos,
                 })
        {
            writes[(int)op] = true;
        }

        return writes;
    }

    /// <summary>
    /// Every existing micro-op's bus-qualifier pins. The opcode-fetch cycle itself is not a
    /// <see cref="MicroOp"/> member — it is performed by the tick loop before any sequence
    /// runs, per the remark on <see cref="MicroOp"/> — so it is not listed here; <c>Cpu.Tick</c>
    /// assigns its <see cref="BusPins.Vda"/> | <see cref="BusPins.Vpa"/> pins directly.
    /// <para>
    /// Every other micro-op splits on one question: does it read the <em>live</em> program
    /// counter, or does it touch some other address? A read of live PC (an operand fetch, or
    /// a dummy read that rereads PC without advancing it) is <see cref="BusPins.Vpa"/> alone.
    /// Everything else — an effective address, a pointer, the stack, a vector, or a discarded
    /// dummy read at a <em>computed</em> address such as <c>PC-1</c> or <c>PC-2</c> that does
    /// not track the live PC — is <see cref="BusPins.Vda"/> alone. This is why the CMOS
    /// indexing fixups (<see cref="MicroOp.IndexFixupCmos"/> and friends), which reread an
    /// already-consumed operand byte rather than advance through the instruction stream, land
    /// in the <see cref="BusPins.Vda"/> group: on the 65816 the analogous cycle is <c>IO</c>
    /// (neither pin), and of the two real pins available to the 8-bit cores, <c>Vda</c> is the
    /// nearer match for a discarded, non-advancing access.
    /// </para>
    /// <para>
    /// No 8-bit-core micro-op is classified <see cref="BusPins.None"/> — on those parts every
    /// cycle is a real bus access, so each is either a program fetch or a data access. Only
    /// <see cref="IsInternalCycle"/>'s three members legitimately read <c>None</c>.
    /// </para>
    /// </summary>
    private static BusPins[] BuildPinsTable()
    {
        var pins = new BusPins[Enum.GetValues<MicroOp>().Length];

        foreach (var op in new[]
                 {
                     MicroOp.ImpliedExec, MicroOp.ImmExec, MicroOp.ImpliedDummy,
                     MicroOp.FetchAddrLo, MicroOp.FetchAddrHi, MicroOp.FetchAddrHiX, MicroOp.FetchAddrHiY,
                     MicroOp.BranchFetch, MicroOp.BranchTaken, MicroOp.BranchFixup,
                     MicroOp.JmpAbs, MicroOp.JsrFinish, MicroOp.RtsFinish,
                     MicroOp.ImmExecCmosArith, MicroOp.BitBranchFetch,
                     MicroOp.BrkPad, MicroOp.IntDummy,
                     MicroOp.WaiHold, MicroOp.StpHold,
                 })
        {
            pins[(int)op] = BusPins.Vpa;
        }

        foreach (var op in new[]
                 {
                     MicroOp.ZpIndexX, MicroOp.ZpIndexY,
                     MicroOp.PtrReadLo, MicroOp.PtrReadHi, MicroOp.PtrReadHiY,
                     MicroOp.ReadExec, MicroOp.ReadPageCross, MicroOp.DummyReadFixup,
                     MicroOp.UnstableStoreFixup, MicroOp.ExecWrite,
                     MicroOp.JmpIndLo, MicroOp.JmpIndHi,
                     MicroOp.StackDummyRead, MicroOp.StackDummyReadInc, MicroOp.StackDummyReadDec,
                     MicroOp.PushPch, MicroOp.PushPcl, MicroOp.PullPcl, MicroOp.PullPch,
                     MicroOp.ReadExecCmosArith, MicroOp.BcdExtra, MicroOp.ReadPageCrossCmosArith,
                     MicroOp.IndexFixupCmos, MicroOp.ReadPageCrossCmos, MicroOp.RmwPageCrossCmos,
                     MicroOp.BitBranchDummy, MicroOp.BitBranchFixup,
                     MicroOp.NopAbsExtraRead, MicroOp.JmpIndBugDummy, MicroOp.PtrJmpHi, MicroOp.JmpAbsXDummy,
                     MicroOp.PullP, MicroOp.Push, MicroOp.Pull,
                     MicroOp.PushPBrk, MicroOp.PushPInt, MicroOp.PushPBrkCmos, MicroOp.PushPIntCmos,
                     MicroOp.JamHold,
                 })
        {
            pins[(int)op] = BusPins.Vda;
        }

        // The locked cycles of a read-modify-write — read, modify (NMOS writes here, CMOS
        // reads), and the final write. See BusPins.Mlb.
        foreach (var op in new[]
                 {
                     MicroOp.RmwRead, MicroOp.RmwModifyWrite, MicroOp.RmwModifyRead, MicroOp.RmwWrite,
                 })
        {
            pins[(int)op] = BusPins.Vda | BusPins.Mlb;
        }

        // Vector pulls. See BusPins.Vpb.
        pins[(int)MicroOp.VectorLo] = BusPins.Vda | BusPins.Vpb;
        pins[(int)MicroOp.VectorHi] = BusPins.Vda | BusPins.Vpb;

        return pins;
    }

    /// <summary>
    /// The three micro-ops <see cref="PinsFor"/> legitimately classifies <see cref="BusPins.None"/>.
    /// <see cref="MicroOp.End"/> consumes no cycle and is never dispatched to <c>Cpu.Execute</c>
    /// at all. <see cref="MicroOp.Unimplemented816"/> is a placeholder that throws
    /// <see cref="NotImplementedException"/> the moment it is reached, before driving any pin —
    /// <c>None</c> is therefore the honest recording of what it asserts (nothing, because it
    /// never gets that far), not a guess about what a future opcode in its slot will assert.
    /// <see cref="MicroOp.ImpliedExec816"/> is the first micro-op that is <c>None</c> because
    /// it genuinely drives neither pin — a real 65816 internal cycle, per research document §9.
    /// </summary>
    private static bool[] BuildInternalCycleTable()
    {
        var internalCycles = new bool[Enum.GetValues<MicroOp>().Length];

        foreach (var op in new[] { MicroOp.End, MicroOp.Unimplemented816, MicroOp.ImpliedExec816 })
        {
            internalCycles[(int)op] = true;
        }

        return internalCycles;
    }
}
