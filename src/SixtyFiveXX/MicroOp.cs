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
    /// The 65816's implied-mode second cycle: an internal cycle at <c>PBR,PC</c> — no memory
    /// access at all — then run the operation. Research document §9, row 19a: cycle 2 of
    /// <c>XCE</c> (and every other implied 65816 instruction) drives <c>VDA=0 VPA=0</c>, which
    /// is <see cref="IBus.Internal"/>, not the dummy read <see cref="ImpliedExec"/> performs
    /// for the five 8-bit cores. <c>PC</c> already reflects the opcode fetch's increment, so
    /// the address is simply <c>(PBR &lt;&lt; 16) | PC</c> with no further adjustment.
    /// </summary>
    ImpliedExec816,

    /// <summary>
    /// <see cref="ImpliedExec816"/>'s first half: the same internal cycle at <c>PBR,PC</c>, with
    /// no <c>Exec()</c>. <c>XBA</c> is the only instruction that needs it, and the only implied
    /// opcode on the part that is 3 cycles rather than 2 — research document §13.5, quoting
    /// datasheet Table 5-7 row <b>19b</b>, which <c>XBA</c> has to itself rather than sharing row
    /// 19a with the twelve transfers:
    /// <code>
    /// XBA                                               1 opcode, 1 byte, 3 cycles
    ///   1              VDA=1 VPA=1 MLB=1   PBR,PC       OpCode      RWB=1
    ///   2              VDA=0 VPA=0 MLB=1   PBR,PC+1     IO          RWB=1
    ///   3              VDA=0 VPA=0 MLB=1   PBR,PC+1     IO          RWB=1
    /// </code>
    /// Two internal cycles at the same address, both <c>PBR,PC+1</c> — row 19a's single <c>IO</c>
    /// cycle repeated, not a new shape, which is why this is <see cref="ImpliedExec816"/> split
    /// rather than a new address calculation. Sequenced ahead of it so the operation still runs
    /// on the last cycle. Deliberately <em>not</em> <see cref="DirectPagePenalty"/>, which drives
    /// <c>PC - 1</c>; here <c>PC</c> already reflects the opcode fetch's increment and so is
    /// §13.5's "PC+1" unadjusted, exactly as in <see cref="ImpliedExec816"/>.
    /// </summary>
    ImpliedInternal816,

    /// <summary>
    /// <c>REP</c>/<c>SEP</c>'s cycle 2: reads the one-byte operand at <c>PBR,PC</c> into
    /// <c>_data</c>, then <c>PC++</c>. No <c>Exec()</c> here — datasheet Note 1 (research
    /// document §9) spends a whole extra cycle before the operation actually runs, unlike
    /// <see cref="ImmExec"/>'s ordinary immediate mode, which reads and executes in the same
    /// cycle. Drives <c>VDA=0 VPA=1</c>, the same pins <see cref="FetchAddrLo"/> and every
    /// other live-PC operand read use.
    /// </summary>
    RepSepOperand,

    /// <summary>
    /// <c>REP</c>/<c>SEP</c>'s cycle 3, and the reason the instruction is 3 cycles rather than
    /// 2: an internal cycle, then run the operation. Datasheet Note 1, verbatim: "REP, SEP are
    /// always 3 cycle instructions and VPA is low during the third cycle. The address bus is
    /// PC+1 during the third cycle." Note 1 states VPA alone; VDA is settled by research
    /// document §9's own convention rather than stated outright — every cycle in that section
    /// that is neither a live program-stream read nor an actual data access drives
    /// <c>VDA=0 VPA=0</c>, with no counterexample, so this is <see cref="IBus.Internal"/>
    /// exactly as <see cref="ImpliedExec816"/> is. "PC+1" is the operand byte's own address —
    /// the same one <see cref="RepSepOperand"/> just read — not a third, unfetched byte;
    /// <c>PC</c> has already advanced past it by this cycle, so the address is <c>PC - 1</c>
    /// from here, mirroring how <see cref="MicroOp.NopAbsExtraRead"/> re-derives an
    /// already-consumed operand's address the same way. Verified against all 40,000 vectors
    /// across <c>$C2</c> and <c>$E2</c>, both <c>.e</c> and <c>.n</c>.
    /// </summary>
    RepSepExec,

    // Task 5: the seven direct-page addressing modes, built against research document §9's
    // per-mode blocks. Task 6 reuses every one of these for the absolute, long and
    // stack-relative family.

    /// <summary>
    /// The direct-page family's operand fetch: <c>DO</c>, the one-byte offset from <c>D</c> —
    /// research document §9's "Direct" block, cycle 2 (address <c>PBR,PC+1</c>), and the
    /// identical first operand cycle of every other direct-page mode in that section. Folds
    /// <c>D</c> into the offset immediately, setting <c>_addr</c> to <c>(D + DO) &amp; $FFFF</c>
    /// — confined to bank 0 — rather than carrying the bare byte forward, since every later
    /// cycle in every mode reads <c>_addr</c> as the direct-page address already formed. Skips
    /// the following <see cref="DirectPagePenalty"/> slot when <c>DL == $00</c> — datasheet
    /// Note 2 — by advancing <c>_mpc</c> an extra step, the idiom
    /// <see cref="ReadPageCrossCmosArith"/> already uses to skip <see cref="BcdExtra"/>.
    /// </summary>
    FetchDpOffset,

    /// <summary>
    /// The direct-page penalty: an internal cycle at the offset byte's own address (<c>PC - 1</c>
    /// by the time this runs, mirroring <see cref="RepSepExec"/>'s own re-derivation), taken
    /// only when <see cref="FetchDpOffset"/> did not skip it — i.e. <c>DL != $00</c>. Research
    /// document §9, every direct-page block's "2a (2)" row.
    /// </summary>
    DirectPagePenalty,

    /// <summary>
    /// The unconditional indexing cycle <c>dp,X</c> and <c>(dp,X)</c> share: an internal cycle
    /// at the offset byte's own address, then <c>X</c> folded into <c>_addr</c>. Research
    /// document §9's "Direct,X" cycle 3 and "(Direct,X)" cycle 3, both unconditional — unlike
    /// the 6502's <see cref="ZpIndexX"/>, which performs a real dummy read at the unindexed
    /// address rather than an internal cycle. In emulation mode with <c>DL == $00</c> the low
    /// byte wraps within the page, keeping <c>DH</c> fixed — research document §7, quoting
    /// Clark's appendix: "the DH register need not be zero" — instead of the plain 16-bit add a
    /// non-wrapping index would need.
    /// </summary>
    DirectPageIndexX,

    /// <summary>
    /// <c>dp,Y</c>'s indexing cycle: <see cref="DirectPageIndexX"/> with <c>Y</c> substituted for
    /// <c>X</c> — an internal cycle at the offset byte's own address, then <c>Y</c> folded into
    /// <c>_addr</c>, wrapping within the page in emulation mode when <c>DL == $00</c> and taking a
    /// plain 16-bit add otherwise. Research document §12.3. Unlike <see cref="DirectPageIndexX"/>,
    /// no indirect mode shares it: <c>dp,Y</c> is used by <c>LDX</c> and <c>STX</c> and by nothing
    /// else on the part, so this micro-op has exactly two callers.
    /// </summary>
    DirectPageIndexY,

    /// <summary>
    /// The low byte of an indirect direct-page pointer: <c>ptr = _addr</c> (the direct-page
    /// address <see cref="FetchDpOffset"/>, and for the indexed form
    /// <see cref="DirectPageIndexX"/>, already formed); <c>tmp = Read(ptr)</c>. Shared by every
    /// indirect mode in this family — two-byte pointers (<c>(dp)</c>, <c>(dp,X)</c>,
    /// <c>(dp),Y</c>) and three-byte ones (<c>[dp]</c>, <c>[dp],Y</c>) alike, since the first
    /// pointer byte is read identically either way. Research document §9's "(Direct,X)" cycle 4,
    /// "(Direct)"/"(Direct),Y" cycle 3, and "[Direct]"/"[Direct],Y" cycle 3.
    /// </summary>
    PtrReadLo816,

    /// <summary>
    /// The high byte of a two-byte direct-page pointer whose data is addressed through
    /// <c>DBR</c>: reads at <c>ptr + 1</c>, confined to bank 0 (wrapping at the bank boundary,
    /// not a page boundary — research document §7), then sets <c>_addr</c> to <c>DBR,AA</c>.
    /// Used by <c>(dp)</c> and <c>(dp,X)</c> — research document §9's "(Direct)" cycle 4 and
    /// "(Direct,X)" cycle 5. <c>(dp),Y</c> uses <see cref="DpPtrReadHiY"/> instead, which folds
    /// in the index.
    /// <para>
    /// Code-review fix: this is one of the "old" indirect modes (present on the 65C02) — Clark
    /// §5.1.1, verbatim: "Page boundary wrapping only occurs in emulation mode, and only for
    /// 'old' instructions and addressing modes." So the <c>ptr + 1</c> read itself, not only the
    /// index add <see cref="DirectPageIndexX"/> already guards, must wrap within the page in
    /// emulation mode — on <c>D == $0000</c>, not <c>DL == $00</c>; see
    /// <see cref="Cpu{TBus,TVariant}.DirectPagePointerHighAddress"/> for the condition, the vector that
    /// discriminates it from the index add's condition, and why. Measured: research document
    /// §12.7.
    /// </para>
    /// </summary>
    DpPtrReadHi,

    /// <summary>
    /// <c>(dp),Y</c>'s pointer high byte: as <see cref="DpPtrReadHi"/> — including the same
    /// page-wrap condition — but immediately forms the mis-indexed intermediate address —
    /// <c>DBR,AAH,AAL+YL</c>, research document §9's "(Direct),Y" cycle 4a — and records whether
    /// the low-byte add crossed a page, so the following <see cref="IndexDirectPageIndirectY"/>
    /// slot knows both what address to drive if taken and whether to be taken at all. Stashes
    /// the pointer's own unindexed 16-bit value in <c>_ptr</c> for that micro-op to form the
    /// true address from. Skips <see cref="IndexDirectPageIndirectY"/> when datasheet Note 4's
    /// condition is not met — no page cross, and <c>x = 1</c>.
    /// <para>
    /// Read-only: <see cref="MicroOpTable"/> emits this for <c>LDA</c> and any other reading
    /// opcode's <c>(dp),Y</c>; a writing opcode gets <see cref="DpPtrReadHiYWrite"/> instead,
    /// which never skips. Code-review fix: the skip used to test <c>_op == Op.Lda</c> at run
    /// time, which hard-codes one opcode and would misclassify every other reading opcode
    /// phase 7c adds (<c>ADC</c>, <c>AND</c>, <c>CMP</c>, <c>EOR</c>, <c>ORA</c>, <c>SBC</c>) as
    /// a write. <c>info.Access</c> is known at table-build time, so the split moved there
    /// instead, which also drops the comparison from the per-cycle path entirely.
    /// </para>
    /// </summary>
    DpPtrReadHiY,

    /// <summary>
    /// <see cref="DpPtrReadHiY"/> for a writing opcode's <c>(dp),Y</c> — <c>STA</c> today.
    /// Identical address formation, but never skips <see cref="IndexDirectPageIndirectY"/>:
    /// datasheet Note 4, "add 1 cycle for indexing across page boundaries, or write, or X=0" —
    /// a write pays the indexing cycle unconditionally. See <see cref="DpPtrReadHiY"/>'s remarks
    /// for why the split exists.
    /// </summary>
    DpPtrReadHiYWrite,

    /// <summary>
    /// <c>(dp),Y</c>'s indexing cycle: an internal cycle at the mis-indexed address
    /// <see cref="DpPtrReadHiY"/> formed, then <c>_addr</c> is replaced by the true address —
    /// the unindexed pointer plus the full-width index, which may carry into the next bank
    /// (research document §7: not a confined mode). Only reached when datasheet Note 4's
    /// condition holds for a read; see <see cref="DpPtrReadHiY"/> for how that is decided
    /// without spending a cycle on the check. Research document §9, "(Direct),Y" cycle 4a.
    /// </summary>
    IndexDirectPageIndirectY,

    /// <summary>
    /// The middle byte of a three-byte direct-page pointer — <c>AAH</c> — read at
    /// <c>ptr + 1</c>, bank-0 confined. Stashes the 16-bit <c>AAH:AAL</c> pair in <c>_addr</c>
    /// as scratch; the bank byte has not been read yet. Research document §9, "[Direct]" cycle
    /// 4, shared with "[Direct],Y".
    /// </summary>
    LongPtrReadMid,

    /// <summary>
    /// The pointer's own bank byte — <c>AAB</c> — read at <c>ptr + 2</c>, bank-0 confined; then
    /// combined with the 16-bit value <see cref="LongPtrReadMid"/> stashed to form the final
    /// 24-bit address. Unlike every other pointer form in this family, the data bank comes from
    /// the pointer itself, not <c>DBR</c>. Research document §9, "[Direct]" cycle 5.
    /// </summary>
    LongPtrReadHi,

    /// <summary>
    /// <see cref="LongPtrReadHi"/> for <c>[dp],Y</c>: reads <c>AAB</c>, then adds the full-width
    /// <c>Y</c> to the combined 24-bit address in the same cycle — no separate indexing cycle
    /// exists for this mode. Research document §9's "[Direct],Y" section states outright: "No
    /// indexing cycle at all", which is why this mode's cycle count matches <c>[dp]</c>'s
    /// exactly despite the extra addition — the add is free, folded into a cycle that already
    /// exists rather than spending one on a page-cross fixup the 24-bit add never needs.
    /// </summary>
    LongPtrReadHiY,

    /// <summary>
    /// The low byte of a direct-page family read: <c>data = Read(_addr)</c>. When <c>M</c>
    /// selects 8 bits, runs the operation and ends the instruction there, skipping
    /// <see cref="ReadExecHigh816"/> entirely — the mechanism by which the second cycle is
    /// "skipped by the low-byte micro-op ending the instruction". When <c>M</c> selects 16 bits,
    /// leaves the operation for <see cref="ReadExecHigh816"/> to run once the high byte is in
    /// hand. Every direct-page mode's final read cycle — research document §9, each block's
    /// "Data Low" row.
    /// </summary>
    ReadExec816,

    /// <summary>
    /// The high byte of a 16-bit direct-page family read, for the bank-0-confined forms only —
    /// plain <c>dp</c> and <c>dp,X</c>, whose data access is <c>0,D+DO(+X)</c>. Reads at
    /// <c>_addr + 1</c>, wrapping the low 16 bits within whichever bank <c>_addr</c> already
    /// carries (always bank 0 here — Clark §5.1.2: the direct page is "confined to" bank 0),
    /// combines it with the byte <see cref="ReadExec816"/> already read, and runs the operation.
    /// Only reached when <c>M</c> selects 16 bits. Every direct-page mode's "Data High" row,
    /// note (1): "Add 1 cycle for M=0".
    /// <para>
    /// Code-review fix: the five indirect direct-page forms are <em>not</em> bank-0-confined —
    /// their data access goes through <c>DBR</c> or the pointer's own bank byte — and use
    /// <see cref="ReadExecHigh816Carry"/> instead, which lets the <c>+1</c> carry into the next
    /// bank. Splitting these was necessary because one shared "+1" address formula cannot serve
    /// both families correctly; see <see cref="ReadExecHigh816Carry"/> for the citation.
    /// </para>
    /// </summary>
    ReadExecHigh816,

    /// <summary>
    /// <see cref="ReadExecHigh816"/> for the DBR-relative and long direct-page forms — every
    /// indirect one: <c>(dp)</c>, <c>(dp,X)</c>, <c>(dp),Y</c>, <c>[dp]</c>, <c>[dp],Y</c>. Reads
    /// at <c>_addr + 1</c> as a plain 24-bit add, so a low-16-bit overflow carries into the next
    /// bank instead of wrapping within the current one.
    /// <para>
    /// Bruce Clark, "65C816 Opcodes" §5.2, Example 2, verbatim: "If the DBR is $12 and the m flag
    /// is 0, then LDA $FFFF loads the low byte of the data from address $12FFFF, and the high
    /// byte from address $130000" — and §5.1.2: "Otherwise, wrapping does not occur at bank
    /// boundaries." Zero vector coverage: no <c>.n</c> vector places the low access at
    /// <c>bank:$FFFF</c>, so nothing in the suite would have caught the bank-preserving formula
    /// being used here too.
    /// </para>
    /// </summary>
    ReadExecHigh816Carry,

    /// <summary>
    /// The low byte of a direct-page family write: runs the operation to produce the value,
    /// then <c>Write(_addr, ...)</c>. As <see cref="ReadExec816"/>, 8-bit width ends the
    /// instruction here; 16-bit width leaves the high byte for <see cref="ExecWriteHigh816"/> or
    /// <see cref="ExecWriteHigh816Carry"/>.
    /// </summary>
    ExecWrite816,

    /// <summary>
    /// The high byte of a 16-bit direct-page family write, at the same bank-0-confined <c>+1</c>
    /// address <see cref="ReadExecHigh816"/> reads. Only reached when <c>M</c> selects 16 bits.
    /// Plain <c>dp</c> and <c>dp,X</c> only — see <see cref="ReadExecHigh816"/>.
    /// </summary>
    ExecWriteHigh816,

    /// <summary>
    /// <see cref="ExecWriteHigh816"/> for the DBR-relative and long forms, at the same
    /// bank-carrying <c>+1</c> address <see cref="ReadExecHigh816Carry"/> reads. See there for
    /// the citation.
    /// </summary>
    ExecWriteHigh816Carry,

    // Task 6: the absolute, long, stack-relative and immediate forms — research document §9's
    // "Absolute", "Absolute,X — row 6a, and Absolute,Y — row 7", "Absolute Long — row 4a, and
    // Absolute Long,X — row 5", "Stack Relative — row 23", "(Stack Relative),Y — row 24" and
    // "Immediate, and REP/SEP" blocks. Every mode's final access reuses task 5's ReadExec816 /
    // ReadExecHigh816(Carry) / ExecWrite816 / ExecWriteHigh816(Carry) unchanged — those four are
    // already generic over what filled <c>_addr</c>, which is all any of these modes' formation
    // cycles need to do. Only immediate breaks that shape, because it has no effective address
    // at all; it gets its own pair below.

    /// <summary>
    /// Immediate's low byte: <c>data = Read(PBR,PC); PC++</c>. When <c>M</c> selects 8 bits, runs
    /// the operation and ends the instruction there — the same "low-byte micro-op ends the
    /// instruction" shape as <see cref="ReadExec816"/>, but reading the live program counter
    /// rather than an effective address, so <c>LDA #</c> costs one byte and one cycle less than
    /// every memory-addressing mode. Research document §9, "Immediate, and REP/SEP": "the operand
    /// fetched at PBR,PC+1". <c>LDA #</c> (<c>$A9</c>) is the only opcode that uses this —
    /// <c>STA</c> has no immediate form, and <c>REP</c>/<c>SEP</c> use the fixed-width
    /// <see cref="RepSepOperand"/>/<see cref="RepSepExec"/> pair instead, since their operand
    /// never widens with <c>M</c>.
    /// </summary>
    ImmExec816,

    /// <summary>
    /// Immediate's high byte, reached only when <see cref="ImmExec816"/> left <c>M</c> selecting
    /// 16 bits: <c>data16 = (Read(PBR,PC) &lt;&lt; 8) | data; PC++</c>, then runs the operation.
    /// Research document §9: "plus PBR,PC+2 when the width flag is 0 — Note 1's 'add 1 byte for
    /// immediate only'."
    /// </summary>
    ImmExecHigh816,

    /// <summary>
    /// Absolute's (unindexed) high byte: reads <c>AAH</c> at <c>PBR,PC+2</c> — the same bus access
    /// <see cref="FetchAddrHi"/> performs — then folds in <c>DBR</c> so <c>_addr</c> is already
    /// <c>DBR,AA</c> by the time the access tail runs, with no cycle spent on the fold: research
    /// document §9's "Absolute" block has no separate bank-forming row between AAH (cycle 3) and
    /// the data access (cycle 4). Distinct from <see cref="FetchAddrHi"/> rather than a reuse of
    /// it, because <c>abs,X</c>/<c>abs,Y</c> need the bank fold deferred past their own indexing
    /// cycle — see <see cref="AbsHiIndexedX"/> — so a single hi-fetch micro-op cannot serve both
    /// shapes.
    /// </summary>
    AbsHi,

    /// <summary>
    /// <c>abs,X</c>'s unconditional high-byte cycle — research document §9's "Absolute,X — row
    /// 6a" cycle 3: reads <c>AAH</c> at <c>PBR,PC+2</c>, then precomputes both addresses the
    /// following conditional cycle needs without spending one on the precomputation — the
    /// mis-indexed intermediate <c>DBR,AAH,AAL+XL</c> (high byte un-carried, left in <c>_addr</c>
    /// for <see cref="AbsIndexFixup"/> to drive if taken) and the real, possibly bank-carrying
    /// target <c>DBR,AA+X</c> (stashed in <c>_ptr</c>, reused as scratch exactly as <c>(dp),Y</c>'s
    /// <c>DirectPageIndirectYHigh</c> reuses it for the unindexed pointer). Skips
    /// <see cref="AbsIndexFixup"/> when datasheet Note 4's condition is not met — no page cross,
    /// and <c>X=1</c> (8-bit index) — the same test <c>DpPtrReadHiY</c> uses; skipping is safe
    /// because with no carry out of the low byte, the mis-indexed and real addresses are the same
    /// value. Read only; a writing opcode's <c>abs,X</c> gets <see cref="AbsHiIndexedXWrite"/>
    /// instead, selected at table-build time from <c>info.Access</c> — the same mechanism task 5's
    /// review put in place for <c>DpPtrReadHiY</c>/<c>DpPtrReadHiYWrite</c>.
    /// </summary>
    AbsHiIndexedX,

    /// <summary>
    /// <see cref="AbsHiIndexedX"/> for a writing opcode's <c>abs,X</c> — <c>STA</c> today. Same
    /// address formation, but never skips <see cref="AbsIndexFixup"/>: datasheet Note 4, "add 1
    /// cycle for indexing across page boundaries, or write, or X=0" — a write pays the indexing
    /// cycle unconditionally.
    /// </summary>
    AbsHiIndexedXWrite,

    /// <summary>
    /// <see cref="AbsHiIndexedX"/> with <c>Y</c> substituted for <c>X</c> — research document §9's
    /// "Absolute,Y — row 7", identical in shape.
    /// </summary>
    AbsHiIndexedY,

    /// <summary>
    /// <see cref="AbsHiIndexedXWrite"/> with <c>Y</c> substituted for <c>X</c> — a writing
    /// opcode's <c>abs,Y</c>.
    /// </summary>
    AbsHiIndexedYWrite,

    /// <summary>
    /// <c>abs,X</c>/<c>abs,Y</c>'s conditional indexing cycle — research document §9's "Absolute,X
    /// — row 6a" cycle 3a: an internal cycle at the mis-indexed address one of the four
    /// <c>AbsHiIndexed*</c> micro-ops left in <c>_addr</c>, then <c>_addr</c> is replaced by the
    /// real target already precomputed into <c>_ptr</c>. Index-agnostic — it does not need to
    /// know whether <c>X</c> or <c>Y</c> formed the target, because the preceding cycle already
    /// resolved that — which is why one micro-op serves all four <c>AbsHiIndexed*</c> variants
    /// rather than needing its own X/Y split.
    /// </summary>
    AbsIndexFixup,

    /// <summary>
    /// <c>long</c>'s third operand byte, <c>AAB</c>: reads at <c>PBR,PC+3</c>, then combines it
    /// with the 16-bit <c>AA</c> <see cref="FetchAddrLo"/>/<see cref="FetchAddrHi"/> already
    /// formed to produce the full 24-bit address — the operand's own bank, never <c>DBR</c>.
    /// Research document §9, "Absolute Long — row 4a" cycle 4.
    /// </summary>
    FetchAddrBank,

    /// <summary>
    /// <see cref="FetchAddrBank"/> for <c>long,X</c>: reads <c>AAB</c>, then adds the full-width
    /// <c>X</c> to the combined 24-bit address in the same cycle, wrapping only at the 16 MB
    /// boundary — no separate indexing cycle exists for this mode. Research document §9,
    /// "Absolute Long,X — row 5": "Also no indexing cycle for long,X — again matching Clark's flat
    /// 6-m."
    /// </summary>
    FetchAddrBankX,

    /// <summary>
    /// <c>sr,S</c>'s operand fetch: <c>SO</c>, the one-byte offset from <c>S</c> — research
    /// document §9's "Stack Relative — row 23" cycle 2. Folds <c>S</c> into the offset
    /// immediately, setting <c>_addr</c> to <c>(S + SO) &amp; $FFFF</c> — always bank 0, and a
    /// plain 16-bit wrap with no direct-page-style <c>E</c>/<c>DL</c> page-wrap special case: per
    /// research document §7, <c>stack,S</c> is a "new" addressing mode, so none of the "old"
    /// modes' emulation-mode wrapping rules apply to it, and it uses the full 16-bit <c>S</c>
    /// (already carrying <c>SH = $01</c> in emulation mode via the continuously-held invariant)
    /// rather than an 8-bit low-byte-only add pinned to page 1.
    /// </summary>
    FetchSrOffset,

    /// <summary>
    /// <c>sr,S</c>'s unconditional internal cycle: an internal cycle at the offset byte's own
    /// address (<c>PC - 1</c> by the time this runs, as <see cref="DirectPagePenalty"/> and
    /// <see cref="RepSepExec"/> both re-derive theirs). Unlike <see cref="DirectPagePenalty"/>,
    /// this is never skipped — research document §9, "Stack Relative — row 23" cycle 3: "<c>&lt;-
    /// unconditional, and there is no (2) penalty</c>". <c>w</c> appears only on direct-page
    /// modes (research document §5); <c>sr,S</c> pays a flat cycle here regardless of <c>D</c>.
    /// </summary>
    StackRelativePenalty,

    /// <summary>
    /// <c>(sr,S),Y</c>'s pointer high byte: reads <c>AAH</c> at <c>ptr + 1</c>, always bank 0 and
    /// never page-wrapped (a "new" mode, like <see cref="FetchSrOffset"/>), then combines it with
    /// the low byte <see cref="PtrReadLo816"/> already read into the unindexed 16-bit <c>AA</c>.
    /// Research document §9, "(Stack Relative),Y — row 24" cycle 5.
    /// </summary>
    SrPtrReadHi,

    /// <summary>
    /// <c>(sr,S),Y</c>'s second internal cycle: an internal cycle that redrives the address
    /// <see cref="SrPtrReadHi"/> just read — <c>0,S+SO+1</c>, not <c>PBR,PC+1</c> — then
    /// <c>_addr</c> becomes the real target <c>DBR,AA+Y</c>, which may carry into the next bank
    /// (this mode is not bank-confined, unlike plain <c>sr,S</c>). Unconditional, unlike
    /// <c>(dp),Y</c>'s analogous <see cref="IndexDirectPageIndirectY"/>: research document §9's
    /// "(Stack Relative),Y — row 24" cycle 6 has no gating note, and Clark's flat <c>8-m</c> (§5)
    /// carries no <c>p</c> term, so this never skips.
    /// </summary>
    IndexStackRelativeIndirectY,

    // Phase 7c′ task 2: the 65816's read-modify-write access class, research document §13.1's
    // rows 10b (dp), 16b (dp,X), 1d (abs) and 6b (abs,X). Six slots, of which any one execution
    // runs three (8-bit) or five (16-bit) — the rest are skipped by the preceding micro-op, the
    // same conditional-slot idiom DirectPagePenalty uses, and the reason every one of them keeps
    // a static entry in MicroOps.IsWriteCycle rather than one micro-op branching on E at run
    // time. That table is consulted on every tick of all six cores, because RDY must never halt
    // a write cycle, so an E test there would land on five cores that have no E.

    /// <summary>
    /// The RMW's low-byte read — research document §13.1's cycle 3 (row 10b), 4 (16b/1d) or 5
    /// (6b), <c>Data Low</c>, <c>RWB=1</c>, <c>MLB</c> asserted. At 8-bit width it skips the
    /// high-byte read, and in native mode the NMOS write-form as well, so the sequence continues
    /// at <see cref="RmwModifyRead816"/>; in emulation mode it continues at
    /// <see cref="RmwModifyWrite816"/> instead. Datasheet Note 17 is decided here, at run time
    /// from <c>E</c>, and nowhere else.
    /// </summary>
    RmwRead816,

    /// <summary>
    /// The RMW's high-byte read for the bank-0-confined modes — <c>dp</c> and <c>dp,X</c>;
    /// research document §13.1's cycle 3a (row 10b) or 4a (16b), <c>0,D+DO[+X]+1</c>. Reached
    /// only when <c>M</c> selects 16 bits, which is native-mode-only (emulation forces
    /// <c>m = 1</c>), so it skips the NMOS write-form unconditionally rather than testing
    /// <c>E</c>.
    /// </summary>
    RmwReadHigh816,

    /// <summary>
    /// <see cref="RmwReadHigh816"/> for the DBR-relative modes — <c>abs</c> and <c>abs,X</c>;
    /// research document §13.1's cycle 4a (row 1d) or 5a (6b), <c>DBR,AA[+X]+1</c>. The <c>+1</c>
    /// carries into the next bank: see <c>Cpu.HighByteAddressCarry</c>.
    /// </summary>
    RmwReadHigh816Carry,

    /// <summary>
    /// The emulation-mode middle cycle — datasheet Note 17, verbatim: "In the emulation mode,
    /// during a R-M-W instruction the RWB is low during both write and modify cycles." The NMOS
    /// double-write: the UNMODIFIED value goes back before the result. Emulation forces
    /// <c>m = 1</c>, so this form is always 8-bit and always skips both the internal middle cycle
    /// and the high-byte write that follow it.
    /// </summary>
    /// <remarks>
    /// Its pins are <see cref="BusPins.Mlb"/> alone — the same as
    /// <see cref="RmwModifyRead816"/>'s, and the only micro-op here that writes memory while
    /// asserting neither <c>VDA</c> nor <c>VPA</c>. Note 17 governs <c>RWB</c> and nothing else,
    /// so the emulation and native middle cycles are pin-identical apart from direction. Measured
    /// against the vectors, not cited: research document §13.1's "Measured" note, which closed
    /// §13.6's gap 2. The <em>value</em> written — the unmodified one — was §13.6 gap 3, an
    /// inference from the certified NMOS part rather than a 65816 source, and the same vectors
    /// corroborated it.
    /// </remarks>
    RmwModifyWrite816,

    /// <summary>
    /// The native-mode middle cycle for the bank-0-confined modes — research document §13.1's
    /// cycle 4 (row 10b) or 5 (16b), <c>(3),(17)</c>: <c>VDA=0 VPA=0</c>, data bus <c>IO</c>,
    /// <c>RWB=1</c>. An internal cycle, <b>not</b> the 65C02's dummy read — no memory access at
    /// all — but with <c>MLB</c> still asserted, so its pins are <see cref="BusPins.Mlb"/> alone,
    /// a combination no other micro-op in this codebase drives. The address is the <c>+1</c>
    /// (high) one at 16 bits, which is what all four rows print; §13.6 gap 1 records the 8-bit
    /// case as unstated by any source, so <c>_addr</c> is driven there and the vectors settle it.
    /// </summary>
    RmwModifyRead816,

    /// <summary>
    /// <see cref="RmwModifyRead816"/> for the DBR-relative modes, driving the bank-carrying
    /// <c>+1</c> address. Two variants for the same reason the read and write halves have two:
    /// whether the <c>+1</c> carries into the next bank is a property of the addressing mode,
    /// decided at table-build time, not something a shared micro-op can test.
    /// </summary>
    RmwModifyRead816Carry,

    /// <summary>
    /// The 16-bit RMW's high-byte write, which comes <em>first</em> — research document §13.2:
    /// the reads go low-then-high and the writes go high-then-low, cycle 5a (row 10b) or 6a
    /// (16b/1d) or 7a (6b), <c>RWB=0</c>. Bank-0-confined form, for <c>dp</c> and <c>dp,X</c>.
    /// Reached only at 16-bit width.
    /// </summary>
    RmwWriteHigh816,

    /// <summary>
    /// <see cref="RmwWriteHigh816"/> for the DBR-relative modes, at the bank-carrying <c>+1</c>
    /// address <see cref="RmwReadHigh816Carry"/> read.
    /// </summary>
    RmwWriteHigh816Carry,

    /// <summary>
    /// The RMW's final low-byte write — research document §13.1's cycle 5 (row 10b), 6 (16b/1d)
    /// or 7 (6b), <c>RWB=0</c>, at <c>_addr</c>. Writes <c>_data</c> at 8 bits and
    /// <c>_data16</c>'s low byte at 16, and is the last cycle of the instruction in all three
    /// cases. One micro-op, not two: the low address never needs a <c>+1</c>, so there is nothing
    /// for a carrying variant to differ about.
    /// </summary>
    RmwWrite816,

    // Phase 7d task 2: the 65816's own stack access path. Research document §14.1, Table 5-7
    // rows 22c (the seven pushes) and 22b (the six pulls). Every one of these drives
    // Cpu.StackAddress816() — the full sixteen bits of S, in bank 0 — rather than the five
    // 8-bit cores' hard-coded 0x0100 + SL.

    /// <summary>
    /// Cycle 2 of every 65816 push: an internal cycle at <c>PBR,PC+1</c> that also runs the
    /// operation, leaving the value to push in <c>_data16</c>. Research document §14.1, row 22c
    /// — a push has exactly one <c>IO</c> cycle and a pull has two, which is the whole of the
    /// one-cycle difference between the two rows. Skips the following
    /// <see cref="PushHigh816"/> slot when the push is one byte wide, the same conditional-slot
    /// idiom <see cref="FetchDpOffset"/> uses for <see cref="DirectPagePenalty"/>; width comes
    /// from <c>Cpu.StackIsWide</c>, not from <c>_wide</c>, because these opcodes fetch no
    /// operand and so declare no <see cref="Width"/>.
    /// </summary>
    StackPushInternal816,

    /// <summary>
    /// A 16-bit push's high byte, written first at <c>0,S</c> — row 22c's <c>3a</c>, gated by
    /// note <c>(1)</c>. The stack descends, so pushing high first puts the low byte at the lower
    /// address; research document §14.1 question 2 corroborates it from four separate Clark
    /// passages as well as the row.
    /// </summary>
    PushHigh816,

    /// <summary>
    /// Every push's low byte, written last at <c>0,S</c> (row 22c's <c>3</c>, which is
    /// <c>0,S-1</c> when <see cref="PushHigh816"/> ran first and moved <c>S</c> down). Also the
    /// only write cycle of a one-byte push.
    /// </summary>
    PushLow816,

    /// <summary>
    /// Every pull's low byte, read at <c>0,S+1</c> — row 22b's cycle <c>4</c>. Ends the
    /// instruction here when the pull is one byte wide, which is what makes the following
    /// <see cref="PullHigh816"/> slot cost nothing for <c>PLP</c>, <c>PLB</c> and a narrow
    /// <c>PLA</c>/<c>PLX</c>/<c>PLY</c>.
    /// </summary>
    PullLow816,

    /// <summary>
    /// A 16-bit pull's high byte, read at <c>0,S+2</c> — row 22b's <c>4a</c>, gated by note
    /// <c>(1)</c> — then the operation runs against the assembled 16-bit value.
    /// </summary>
    PullHigh816,

    /// <summary>
    /// The 65816's reset stack decrement: <see cref="StackDummyReadDec"/> with the address taken
    /// from <c>Cpu.StackAddress816()</c> instead of a hard-coded <c>0x0100 + SL</c>. Reset always
    /// enters emulation mode, where the two expressions agree — this exists so the reset section
    /// computes the right address because it is the right address, not because another invariant
    /// happens to hold. <c>W65C816ReachabilityTests</c> is what asserts the 65816 never reaches
    /// the 8-bit form.
    /// </summary>
    StackDummyReadDec816,

    // Phase 7d task 3: the 65816's interrupts. Research document §14.2, Table 5-7 rows 22j
    // (BRK/COP) and 22a (IRQ/NMI/RESET/ABORT). The two rows are identical from cycle 3 on, so
    // the five members from PushPbr816 down are shared by both sequences; only the two leading
    // cycles differ, and each of those owns the emulation-mode skip of the program-bank push.
    // These are also the first micro-ops in this repository to assert VPB.

    /// <summary>
    /// <c>BRK</c>/<c>COP</c>'s cycle 2 — row 22j's <c>Signature</c> read at <c>PBR,PC+1</c>,
    /// <c>VDA=0 VPA=1</c>. The byte is fetched and discarded, and <c>PC</c> advances past it,
    /// which is why both instructions push their own address plus 2 (Clark §6.3.1, measured
    /// across all 40,000 vectors). Also chooses the vector — <c>Cpu.Vector816</c> — and skips
    /// <see cref="PushPbr816"/> when <c>e = 1</c>, the same conditional-slot idiom
    /// <see cref="FetchDpOffset"/> uses for <see cref="DirectPagePenalty"/>.
    /// </summary>
    BrkPad816,

    /// <summary>
    /// A hardware interrupt's cycle 2 — row 22a's <c>IO</c> at <c>PBR,PC</c>, <c>VDA=0
    /// VPA=0</c>, no memory access. Cycle 1 is <c>Cpu.FetchOpcode</c>'s own discarded read at
    /// the same address, which the row prints as <c>VDA=1 VPA=1</c>; nothing advances <c>PC</c>
    /// here, since a hardware interrupt consumes no opcode. Skips <see cref="PushPbr816"/> when
    /// <c>e = 1</c>, exactly as <see cref="BrkPad816"/> does.
    /// <para>
    /// Not <see cref="ImpliedInternal816"/>, whose address happens to coincide but which owns
    /// no skip and which every push, pull and <c>XBA</c> already shares.
    /// </para>
    /// </summary>
    IntInternal816,

    /// <summary>
    /// The program-bank push, at <c>0,S</c> — cycle 3 of both rows, and the cycle emulation
    /// mode omits. No eight-bit core has it. Datasheet §7.11.1: <em>"In the Native mode,
    /// previous PBR contents are automatically saved on Stack"</em>; §7.11.2: in emulation mode
    /// they <em>"are not automatically saved"</em>.
    /// </summary>
    PushPbr816,

    /// <summary>
    /// The return address, high byte then low — cycles 4 and 5 of both rows, at <c>0,S-1</c>
    /// and <c>0,S-2</c>. The 65816 forms of <see cref="PushPch"/> and <see cref="PushPcl"/>:
    /// they drive <c>Cpu.StackAddress816</c>, the full sixteen bits of <c>S</c> in bank 0,
    /// rather than a hard-coded <c>$0100 + SL</c>, and they wrap inside page one in emulation
    /// mode only because an interrupt is on <c>Cpu.StackWrapsInPageOne</c>'s list (Clark §5.22).
    /// </summary>
    PushPch816,

    /// <inheritdoc cref="PushPch816"/>
    PushPcl816,

    /// <summary>
    /// The status push — cycle 6 of both rows — then <c>I</c> set and <c>D</c> cleared, in that
    /// order (Clark §6.3.1, measured). Serves all four interrupts: <c>BRK</c> and <c>COP</c>
    /// push <c>P</c> verbatim in both modes, and a hardware interrupt does too except in
    /// emulation mode, where bit 4 is forced to <c>0</c> — datasheet note 11, printed on row
    /// 22a and not on 22j. <b>No NMI hijack</b>, unlike <see cref="PushPBrk"/> and
    /// <see cref="PushPInt"/>: research document §14.2/§14.9 gap 2 records that no 65816 source
    /// mentions the case and that no vector can settle it, so the NMOS anomaly is deliberately
    /// not carried over. See <c>Cpu.Execute</c>'s arm for the full reasoning.
    /// </summary>
    PushPInt816,

    /// <summary>
    /// The two vector reads — cycles 7 and 8 of both rows, at <c>0,VA</c> and <c>0,VA+1</c>,
    /// bank 0. The only cycles in this repository that assert <see cref="BusPins.Vpb"/>
    /// alongside <see cref="BusPins.Vda"/>: both rows print <c>VPB=0 VDA=1 VPA=0</c>, and the
    /// datasheet says it again in prose on p. 30 — <em>"The VP output is low during the two
    /// cycles used for vector location access"</em>. Confirmed character for character by the
    /// vectors, whose last two cycles read <c>d-vr…</c>.
    /// <para>
    /// <see cref="VectorHi816"/> also clears <c>PBR</c>, in both modes (datasheet §7.11.1 and
    /// §7.11.2), which is what puts the handler in bank 0 — the rows' own <c>0,AAV</c>
    /// next-opcode cell. Distinct from <see cref="VectorLo"/>/<see cref="VectorHi"/>, which the
    /// 65816's reset sequence still shares: those clear no bank register, and reset has
    /// <c>Cpu.Reset</c> to do it instead.
    /// </para>
    /// </summary>
    VectorLo816,

    /// <inheritdoc cref="VectorLo816"/>
    VectorHi816,

    // The block moves, MVN and MVP. Research document §14.3, Table 5-7 rows 9a and 9b, whose
    // seven cycles are the opcode fetch plus the six members below. One instruction per byte
    // moved: no member here loops, and none holds a position of its own — the sequence runs to
    // MicroOp.End every time and BlockMoveNext rewinds PC so the NEXT FETCH re-enters it. That
    // is what lets an interrupt land between iterations (Clark §6.6, "they can only be
    // interrupted every seventh cycle"), which an internal loop would make impossible.

    /// <summary>
    /// Cycle 2, <c>DBA</c>: the destination bank byte, fetched at <c>PBR,PC+1</c> and written
    /// straight into <c>DBR</c>. Datasheet §7.18 — the register takes "the value of the second
    /// byte of the instruction" — so the write happens here, on the fetch, and the following
    /// write cycle reads <c>DBR</c> back rather than a latch. Row 9a/9b prints <c>VDA=0
    /// VPA=1</c>, an ordinary operand fetch.
    /// </summary>
    BlockMoveDestBank,

    /// <summary>
    /// Cycle 3, <c>SBA</c>: the source bank byte, fetched at <c>PBR,PC+2</c> and kept only for
    /// this iteration's own read — it goes into no register, which is why <c>MVN</c> re-fetches
    /// both operand bytes on every one of its iterations rather than carrying them along. Same
    /// pins as <see cref="BlockMoveDestBank"/>.
    /// </summary>
    BlockMoveSrcBank,

    /// <summary>
    /// Cycle 4: the read at <c>SBA,X</c>, with <c>X</c> as it stands before this iteration's
    /// update. <c>VDA=1 VPA=0</c> — a data access in a bank that is neither <c>PBR</c> nor
    /// <c>DBR</c>. The index is the operative width (<c>Cpu.IndexX</c>) and the address wraps
    /// inside the source bank: Clark §5.1.2, "source,destination addressing … wraps at both the
    /// source and destination bank boundaries".
    /// </summary>
    BlockMoveRead,

    /// <summary>
    /// Cycle 5: the write at <c>DBA,Y</c> — <c>DBR</c>, which
    /// <see cref="BlockMoveDestBank"/> has already updated, and <c>Y</c> before this iteration's
    /// update. Wraps inside the destination bank exactly as the read wraps inside the source.
    /// </summary>
    BlockMoveWrite,

    /// <summary>
    /// Cycle 6: the first of two internal cycles, both driving the address just written —
    /// <c>DBA,Y</c>, not <c>PBR,PC</c>. Research document §14.3 calls this the one place in
    /// this phase where an internal cycle's address is a data address rather than a program
    /// one; every other <c>IO</c> row in §9, §13 and §14.1 drives a program-counter address.
    /// </summary>
    BlockMoveInternal,

    /// <summary>
    /// Cycle 7: the second internal cycle at the same <c>DBA,Y</c>, and the iteration's whole
    /// register update — <c>A</c> down by one over a full sixteen bits whatever <c>m</c> says
    /// (Clark §6.6, measured in §14.3), <c>X</c> and <c>Y</c> by one at the operative index
    /// width in the opcode's own direction, and then the rewind: <c>PC</c> back by 3 unless the
    /// decremented accumulator has reached <c>$FFFF</c>.
    /// </summary>
    /// <remarks>
    /// This micro-op ends the instruction the same way on both paths — by being the last one
    /// before <see cref="End"/> — and differs only in where <c>PC</c> points afterwards. It is
    /// deliberately not written as a loop that holds its own sequence position: Clark §6.6 says
    /// <c>MVN</c> and <c>MVP</c> "can be interrupted by IRQ and NMI before the move is complete
    /// (unlike every other instruction …); however, they can only be interrupted every seventh
    /// cycle", and a genuine instruction boundary here is what produces exactly that, through
    /// the interrupt poll every instruction already gets and with no special case anywhere.
    /// </remarks>
    BlockMoveNext,
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
    /// read-modify-write instruction that actually touch the target byte — on the 8-bit cores
    /// the read, the modify, and the final write; on the 65816 five of those cycles when
    /// <c>m = 0</c> (two reads, the internal middle cycle, two writes) — so an external bus
    /// arbiter knows not to interrupt the sequence. Not set on the addressing-mode cycles that
    /// merely compute the target address, even when they occur inside an RMW instruction:
    /// research document §13.1 confirms that second sentence on this part, since row 6b's
    /// indexing <c>IO</c> at the mis-indexed address prints <c>MLB=1</c> while the very next
    /// cycle prints <c>MLB=0</c>. Note that the 65816's middle cycle asserts this pin while
    /// performing no bus access at all — see <see cref="MicroOps.IsInternalCycle"/>.
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
    /// True for the micro-ops that perform no bus access at all — a real internal cycle, rather
    /// than one nobody got around to classifying. Kept as its own explicit table, not a
    /// fallback, specifically so a genuinely unclassified micro-op still reads
    /// <see cref="BusPins.None"/> from <see cref="Pins"/> and still fails the "every micro-op is
    /// classified" test instead of silently passing it.
    /// </summary>
    /// <remarks>
    /// This used to be worded as "the micro-ops <see cref="PinsFor"/> legitimately classifies
    /// <see cref="BusPins.None"/>", because until phase 7c′ the two coincided. They no longer
    /// do: <see cref="MicroOp.RmwModifyRead816"/> and
    /// <see cref="MicroOp.RmwModifyRead816Carry"/> perform no access yet assert
    /// <see cref="BusPins.Mlb"/> — the 65816 holds memory locked across its read-modify-write
    /// sequence, internal cycle included (research document §13.1). "Performs no bus access" is
    /// the property this table has always meant; <c>None</c> pins were a consequence of it that
    /// happened to hold for every earlier member.
    /// </remarks>
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
                     MicroOp.ExecWrite816, MicroOp.ExecWriteHigh816, MicroOp.ExecWriteHigh816Carry,
                     MicroOp.RmwModifyWrite816, MicroOp.RmwWriteHigh816, MicroOp.RmwWriteHigh816Carry,
                     MicroOp.RmwWrite816,
                     MicroOp.PushHigh816, MicroOp.PushLow816,
                     MicroOp.PushPbr816, MicroOp.PushPch816, MicroOp.PushPcl816, MicroOp.PushPInt816,
                     MicroOp.BlockMoveWrite,
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
    /// <see cref="IsInternalCycle"/>'s members legitimately read <c>None</c>.
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
                     MicroOp.RepSepOperand, MicroOp.FetchDpOffset,
                     MicroOp.ImmExec816, MicroOp.ImmExecHigh816,
                     MicroOp.AbsHi, MicroOp.AbsHiIndexedX, MicroOp.AbsHiIndexedXWrite,
                     MicroOp.AbsHiIndexedY, MicroOp.AbsHiIndexedYWrite,
                     MicroOp.FetchAddrBank, MicroOp.FetchAddrBankX, MicroOp.FetchSrOffset,
                     // BRK/COP's signature byte is a live-PC read like any other operand fetch
                     // — research document §14.2's row 22j prints VDA=0 VPA=1 on it, and every
                     // one of the 40,000 vectors' cycle 2 reads "-p-r…".
                     MicroOp.BrkPad816,
                     // The block moves' two operand fetches: research document §14.3's rows
                     // 9a/9b print VDA=0 VPA=1 on both, and every one of the 40,000 vectors
                     // reads "-p-r…" on cycles 2 and 3.
                     MicroOp.BlockMoveDestBank, MicroOp.BlockMoveSrcBank,
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
                     MicroOp.PtrReadLo816, MicroOp.DpPtrReadHi, MicroOp.DpPtrReadHiY, MicroOp.DpPtrReadHiYWrite,
                     MicroOp.LongPtrReadMid, MicroOp.LongPtrReadHi, MicroOp.LongPtrReadHiY,
                     MicroOp.ReadExec816, MicroOp.ReadExecHigh816, MicroOp.ReadExecHigh816Carry,
                     MicroOp.ExecWrite816, MicroOp.ExecWriteHigh816, MicroOp.ExecWriteHigh816Carry,
                     MicroOp.SrPtrReadHi,
                     // The 65816's stack accesses. A stack access is a data access, never a
                     // program one: research document §14.1's rows 22b and 22c print VDA=1
                     // VPA=0 on every one of them, and MLB and VPB stay inactive throughout.
                     MicroOp.PushHigh816, MicroOp.PushLow816,
                     MicroOp.PullLow816, MicroOp.PullHigh816,
                     MicroOp.StackDummyReadDec816,
                     // The interrupts' four pushes. Rows 22a and 22j print VDA=1 VPA=0 on every
                     // one, exactly as the pushes above; VPB and MLB stay inactive until the
                     // vector reads.
                     MicroOp.PushPbr816, MicroOp.PushPch816, MicroOp.PushPcl816, MicroOp.PushPInt816,
                     // The block moves' two real accesses, in banks that are neither PBR nor
                     // the one the other one uses: rows 9a/9b print VDA=1 VPA=0 on each, and
                     // the vectors read "d--r…" then "d--w…".
                     MicroOp.BlockMoveRead, MicroOp.BlockMoveWrite,
                 })
        {
            pins[(int)op] = BusPins.Vda;
        }

        // The locked cycles of a read-modify-write — read, modify (NMOS writes here, CMOS
        // reads), and the final write. See BusPins.Mlb. The 65816's four real accesses are here
        // too: one or two reads and one or two writes, all carrying VDA with MLB, exactly as
        // research document §13.1's four rows print them. Its MIDDLE cycle is the exception, in
        // BOTH modes, and is classified separately below.
        foreach (var op in new[]
                 {
                     MicroOp.RmwRead, MicroOp.RmwModifyWrite, MicroOp.RmwModifyRead, MicroOp.RmwWrite,
                     MicroOp.RmwRead816, MicroOp.RmwReadHigh816, MicroOp.RmwReadHigh816Carry,
                     MicroOp.RmwWriteHigh816, MicroOp.RmwWriteHigh816Carry, MicroOp.RmwWrite816,
                 })
        {
            pins[(int)op] = BusPins.Vda | BusPins.Mlb;
        }

        // The 65816's RMW middle cycle: MLB asserted with NEITHER address-valid pin. Research
        // document §13.1 — all four RMW rows print VDA=0 VPA=0, MLB=1 on it, and §13.1's
        // "Measured" note records that this holds in emulation mode too, where the cycle is a
        // real write. So all three forms carry the same pins and datasheet Note 17 changes RWB
        // and only RWB, exactly as its wording says.
        //
        // RmwModifyWrite816 is the one micro-op in this codebase that WRITES memory while
        // asserting neither VDA nor VPA — unusual enough that research §13.6 recorded it as an
        // open question rather than assuming it; the vectors closed it. The other two perform no
        // access at all, which is why only they are in IsInternalCycle's table.
        pins[(int)MicroOp.RmwModifyRead816] = BusPins.Mlb;
        pins[(int)MicroOp.RmwModifyRead816Carry] = BusPins.Mlb;
        pins[(int)MicroOp.RmwModifyWrite816] = BusPins.Mlb;

        // Vector pulls. See BusPins.Vpb. The 65816's own pair carries the identical
        // combination: research document §14.2's rows 22a and 22j both print VPB=0 VDA=1
        // VPA=0, and the vectors' last two cycles read "d-vr…" — VDA set, VPA clear, VPB set,
        // read. That VDA is asserted on a cycle whose whole purpose is fetching an address is
        // §14.2's own warning: no amount of reasoning about the cycle's purpose gives it.
        pins[(int)MicroOp.VectorLo] = BusPins.Vda | BusPins.Vpb;
        pins[(int)MicroOp.VectorHi] = BusPins.Vda | BusPins.Vpb;
        pins[(int)MicroOp.VectorLo816] = BusPins.Vda | BusPins.Vpb;
        pins[(int)MicroOp.VectorHi816] = BusPins.Vda | BusPins.Vpb;

        return pins;
    }

    /// <summary>
    /// The micro-ops <see cref="PinsFor"/> legitimately classifies <see cref="BusPins.None"/>.
    /// <see cref="MicroOp.End"/> consumes no cycle and is never dispatched to <c>Cpu.Execute</c>
    /// at all. It was joined here until phase 7d task 3 by <c>Unimplemented816</c>, a
    /// placeholder that threw the moment it was reached; the 65816 interrupt sequence was its
    /// last emitter and it is now deleted.
    /// <see cref="MicroOp.ImpliedExec816"/> is the first micro-op that is <c>None</c> because
    /// it genuinely drives neither pin — a real 65816 internal cycle, per research document §9.
    /// <see cref="MicroOp.ImpliedInternal816"/> is that same cycle without the <c>Exec()</c>,
    /// <c>XBA</c>'s extra one, and is <c>None</c> for exactly the same reason — research document
    /// §13.5's row 19b drives <c>VDA=0 VPA=0</c> on both of its <c>IO</c> cycles.
    /// <see cref="MicroOp.RepSepExec"/> is the next, for the same reason — see its own remarks
    /// for why VDA is 0 there despite Note 1 stating only VPA outright.
    /// <see cref="MicroOp.DirectPagePenalty"/>, <see cref="MicroOp.DirectPageIndexX"/> and
    /// <see cref="MicroOp.IndexDirectPageIndirectY"/> are phase 7b task 5's three — every one of
    /// them is an <c>IO</c> row in research document §9's direct-page blocks, driving an address
    /// with no memory access at all, the same shape as the first two.
    /// <see cref="MicroOp.AbsIndexFixup"/>, <see cref="MicroOp.StackRelativePenalty"/> and
    /// <see cref="MicroOp.IndexStackRelativeIndirectY"/> are phase 7b task 6's three — the
    /// conditional indexing cycle of <c>abs,X</c>/<c>abs,Y</c> (§9 row 6a/7's "3a"), <c>sr,S</c>'s
    /// unconditional penalty cycle (§9 row 23's "3"), and <c>(sr,S),Y</c>'s second unconditional
    /// internal cycle (§9 row 24's "6") — each an <c>IO</c> row with no memory access, the same
    /// shape as phase 7b task 5's three.
    /// <see cref="MicroOp.DirectPageIndexY"/> is phase 7c task 7's one — <c>dp,Y</c>'s indexing
    /// cycle, the same <c>IO</c> row of §9's direct-page block that
    /// <see cref="MicroOp.DirectPageIndexX"/> already occupies, with Y substituted.
    /// <see cref="MicroOp.RmwModifyRead816"/> and <see cref="MicroOp.RmwModifyRead816Carry"/> are
    /// phase 7c′ task 2's two, and the first members that are <em>not</em>
    /// <see cref="BusPins.None"/> — see this method's own summary. They are here because
    /// <c>Cpu.Tick</c>'s RDY halt path uses this table to choose between <c>InternalCycle</c> and
    /// <c>ReadBus</c>, and would otherwise fake a read on a halted cycle that accesses nothing.
    /// <see cref="MicroOp.StackPushInternal816"/> is phase 7d task 2's one — cycle 2 of every
    /// 65816 push, an <c>IO</c> row at <c>PBR,PC+1</c> in research document §14.1's row 22c. The
    /// pulls' two internal cycles are <see cref="MicroOp.ImpliedInternal816"/>, already here:
    /// row 22b drives the identical address on both of them, so they need no member of their own.
    /// <see cref="MicroOp.IntInternal816"/> is phase 7d task 3's one — a hardware interrupt's
    /// cycle 2, research document §14.2's row 22a, an <c>IO</c> at <c>PBR,PC</c>. It does need a
    /// member of its own despite driving the address <see cref="MicroOp.ImpliedInternal816"/>
    /// would: it owns the emulation-mode skip of the program-bank push.
    /// <see cref="MicroOp.BlockMoveInternal"/> and <see cref="MicroOp.BlockMoveNext"/> are phase
    /// 7d task 4's two — cycles 6 and 7 of <c>MVN</c>/<c>MVP</c>, the only internal cycles in this
    /// codebase whose address is a <em>data</em> address (<c>DBA,Y</c>, the byte just written)
    /// rather than a program one; research document §14.3 flags exactly that. Two members rather
    /// than one repeated, because the second also performs the iteration's register update and
    /// the rewind.
    /// </summary>
    private static bool[] BuildInternalCycleTable()
    {
        var internalCycles = new bool[Enum.GetValues<MicroOp>().Length];

        foreach (var op in new[]
                 {
                     MicroOp.End, MicroOp.ImpliedExec816,
                     MicroOp.ImpliedInternal816, MicroOp.RepSepExec,
                     MicroOp.DirectPagePenalty, MicroOp.DirectPageIndexX, MicroOp.DirectPageIndexY,
                     MicroOp.IndexDirectPageIndirectY,
                     MicroOp.AbsIndexFixup, MicroOp.StackRelativePenalty, MicroOp.IndexStackRelativeIndirectY,
                     MicroOp.RmwModifyRead816, MicroOp.RmwModifyRead816Carry,
                     MicroOp.StackPushInternal816,
                     MicroOp.IntInternal816,
                     MicroOp.BlockMoveInternal, MicroOp.BlockMoveNext,
                 })
        {
            internalCycles[(int)op] = true;
        }

        return internalCycles;
    }
}
