namespace SixtyFiveXX;

/// <summary>How an instruction computes its effective address.</summary>
internal enum AddrMode : byte
{
    /// <summary>No operand. Two cycles, with a dummy read at PC.</summary>
    Implied,

    /// <summary>Operates on the accumulator. Cycle-identical to <see cref="Implied"/>.</summary>
    Accumulator,

    /// <summary>The operand byte follows the opcode.</summary>
    Immediate,

    /// <summary>One-byte address in page zero.</summary>
    ZeroPage,

    /// <summary>Page zero, indexed by X, wrapping within the page.</summary>
    ZeroPageX,

    /// <summary>Page zero, indexed by Y, wrapping within the page.</summary>
    ZeroPageY,

    /// <summary>Two-byte little-endian address.</summary>
    Absolute,

    /// <summary>Absolute, indexed by X. Reads may cost an extra cycle on a page cross.</summary>
    AbsoluteX,

    /// <summary>Absolute, indexed by Y. Reads may cost an extra cycle on a page cross.</summary>
    AbsoluteY,

    /// <summary>Indirect, used only by JMP. Reproduces the NMOS page-wrap bug.</summary>
    Indirect,

    /// <summary>
    /// Indirect JMP with the page-wrap bug fixed — the CMOS form, and a cycle longer than
    /// <see cref="Indirect"/>. A separate mode rather than a variant test at emission time,
    /// so the table stays the single source of truth for both timing and behaviour.
    /// </summary>
    IndirectFixed,

    /// <summary>(zp) — indirect through a page-zero pointer, with no indexing. CMOS only.</summary>
    ZeroPageIndirect,

    /// <summary>(abs,X) — absolute indexed indirect, used only by JMP. CMOS only.</summary>
    AbsoluteIndexedIndirect,

    /// <summary>(zp,X) — indexed indirect.</summary>
    IndexedIndirect,

    /// <summary>(zp),Y — indirect indexed. Reads may cost an extra cycle on a page cross.</summary>
    IndirectIndexed,

    /// <summary>Signed 8-bit branch displacement.</summary>
    Relative,

    /// <summary>Hand-written sequence: pushes, pulls, JSR, RTS, RTI, BRK, JMP absolute.</summary>
    Stack,

    /// <summary>
    /// Two operands: a page-zero address then a signed branch displacement. Used only by
    /// Rockwell's <c>BBR</c> and <c>BBS</c>, which read a byte, test one bit of it, and
    /// branch — the only 65xx addressing mode that both reads memory and branches.
    /// </summary>
    ZeroPageRelative,

    // The CMOS NOP timing shapes. Every opcode the NMOS parts left undocumented became a
    // NOP on the 65C02, but not a uniform one: the shapes below were measured from the
    // vectors and cover all 76 of them, alongside Implied (one, $CB), Immediate (seven),
    // ZeroPage and ZeroPageX (nine and twelve, which do perform their mode's read).

    /// <summary>
    /// One byte, one cycle: the opcode fetch and nothing else. Emits no micro-ops at all,
    /// so it is the only shape whose instruction is over before the sequence begins.
    /// </summary>
    NopSingleCycle,

    /// <summary>Three bytes, three cycles: both operand bytes fetched, no access performed.</summary>
    NopAbsolute,

    /// <summary>Three bytes, four cycles: as <see cref="NopAbsolute"/>, then a discarded re-read of the high operand byte.</summary>
    NopAbsoluteExtra,

    // 65816-only modes. Bank rules are research document §9's, transcribed from WDC
    // datasheet Table 5-7, and are quoted here rather than inferred.

    /// <summary>
    /// An immediate operand that is always one byte, regardless of the <c>m</c>/<c>x</c>
    /// width flags — WDC datasheet Table 5-7 Note 1, verbatim: "REP, SEP are always 3 cycle
    /// instructions." Unlike <see cref="Immediate"/>, whose length and cycle count grow by
    /// one when the relevant width flag is 0, <c>REP</c>/<c>SEP</c>'s operand is a fixed
    /// 8-bit flag mask that never widens. A distinct mode rather than a per-<c>Op</c> test
    /// inside the emitter, so the opcode table stays the single source of truth for both
    /// operand length and cycle count.
    /// </summary>
    ImmediateByte,

    /// <summary>
    /// <c>dp</c> — one-byte offset from <c>D</c>. Data is addressed at <c>0,D+DO</c>: always
    /// bank 0, never <c>DBR</c>.
    /// </summary>
    DirectPage,

    /// <summary>
    /// <c>dp,X</c> — <see cref="DirectPage"/> indexed by X before the read. Data is
    /// addressed at <c>0,D+DO+X</c>, still bank 0.
    /// </summary>
    DirectPageX,

    /// <summary>
    /// <c>dp,Y</c> — <see cref="DirectPage"/> indexed by Y before the read. Data is in bank 0,
    /// and the index add wraps within the direct page when <c>E == 1</c> and <c>DL == $00</c>,
    /// exactly as <see cref="DirectPageX"/> does. Used by <c>LDX</c> and <c>STX</c> and by no
    /// other 65816 instruction. Distinct from <see cref="ZeroPageY"/>, which is the eight-bit
    /// cores' mode and has neither the direct register nor the emulation-mode condition.
    /// </summary>
    DirectPageY,

    /// <summary>
    /// <c>(dp)</c> — a two-byte pointer fetched from bank 0 at <c>0,D+DO</c>; the data it
    /// points at is addressed through <c>DBR</c>.
    /// </summary>
    DirectPageIndirect,

    /// <summary>
    /// <c>(dp),Y</c> — <see cref="DirectPageIndirect"/>'s pointer, then indexed by Y once the
    /// data address is formed through <c>DBR</c>.
    /// </summary>
    DirectPageIndirectY,

    /// <summary>
    /// <c>(dp,X)</c> — the direct-page offset is indexed by X first, then the two-byte
    /// pointer is fetched from bank 0 at <c>0,D+DO+X</c>; the data is addressed through
    /// <c>DBR</c>.
    /// </summary>
    DirectPageIndexedIndirectX,

    /// <summary>
    /// <c>[dp]</c> — a three-byte pointer fetched from bank 0 at <c>0,D+DO</c>. The data
    /// bank is the pointer's own third byte, not <c>DBR</c>.
    /// </summary>
    DirectPageIndirectLong,

    /// <summary>
    /// <c>[dp],Y</c> — <see cref="DirectPageIndirectLong"/>'s pointer, then indexed by Y
    /// against the pointer's own bank byte. No indexing fixup cycle exists for this mode.
    /// </summary>
    DirectPageIndirectLongY,

    /// <summary>
    /// 24-bit absolute. Three operand bytes — <c>AAL</c>, <c>AAH</c>, <c>AAB</c> — fetched
    /// from <c>PBR</c>; the data is addressed at <c>AAB,AA</c>, the operand's own bank, never
    /// <c>DBR</c>.
    /// </summary>
    AbsoluteLong,

    /// <summary>
    /// <see cref="AbsoluteLong"/> indexed by X. Data is addressed at <c>AAB,AA+X</c>. No
    /// indexing fixup cycle exists for this mode.
    /// </summary>
    AbsoluteLongX,

    /// <summary>
    /// <c>sr,S</c> — a one-byte offset from <c>S</c>. Data is addressed at <c>0,S+SO</c>,
    /// always bank 0.
    /// </summary>
    StackRelative,

    /// <summary>
    /// <c>(sr,S),Y</c> — <see cref="StackRelative"/>'s two-byte pointer, fetched from bank 0
    /// at <c>0,S+SO</c>, then indexed by Y once the data address is formed through
    /// <c>DBR</c>.
    /// </summary>
    StackRelativeIndirectY,

    /// <summary>Not implemented by this variant.</summary>
    Undefined,
}
