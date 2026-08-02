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

    /// <summary>Not implemented by this variant.</summary>
    Undefined,
}
