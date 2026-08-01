namespace SixtyFiveXX;

/// <summary>
/// One CPU cycle's worth of work. Every member performs at most one bus access, so a
/// micro-op and a clock cycle are the same thing.
/// </summary>
/// <remarks>
/// The opcode-fetch cycle is implicit: it is performed by the tick loop, not by a
/// member of this enum. Sequences therefore describe cycle 2 onward.
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

    /// <summary>Write(0x100 + S, P) with B set for BRK and clear for IRQ/NMI; S--; set I.</summary>
    PushPBrk,

    /// <summary>Write(0x100 + S, P) with B clear; S--; set I.</summary>
    PushPInt,

    /// <summary>
    /// If a pending NMI can hijack this sequence (vector is currently the IRQ/BRK
    /// vector), redirect vector to the NMI vector and consume the latch. Then
    /// tmp = Read(vector).
    /// </summary>
    VectorLo,

    /// <summary>PC = (Read(vector + 1) &lt;&lt; 8) | tmp.</summary>
    VectorHi,

    /// <summary>Sequence terminator. Consumes no cycle.</summary>
    End,

    /// <summary>
    /// Drives the address bus while jammed and never advances. The sequence position is
    /// held, so this micro-op repeats for as long as the clock runs.
    /// </summary>
    JamHold,
}
