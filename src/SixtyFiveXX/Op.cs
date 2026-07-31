namespace SixtyFiveXX;

/// <summary>
/// A distinct operation. Addressing is described separately by <see cref="AddrMode"/>,
/// so one member covers every addressing form of an instruction.
/// </summary>
internal enum Op : byte
{
    /// <summary>Not implemented by this variant.</summary>
    Undefined = 0,

    // Loads and stores
    Lda, Ldx, Ldy, Sta, Stx, Sty,

    // Transfers
    Tax, Tay, Tsx, Txa, Txs, Tya,

    // Stack
    Pha, Php, Pla, Plp,

    // Arithmetic and logic
    Adc, Sbc, And, Ora, Eor, Bit,
    Cmp, Cpx, Cpy,
    Inc, Dec, Inx, Iny, Dex, Dey,

    // Shifts. The accumulator forms are distinct because they take no effective address.
    Asl, Lsr, Rol, Ror,
    AslA, LsrA, RolA, RorA,

    // Flags
    Clc, Cld, Cli, Clv, Sec, Sed, Sei,

    // Control flow
    Jmp, Jsr, Rts, Rti, Brk, Nop,

    /// <summary>
    /// An undocumented NOP that still performs its addressing mode's read and discards
    /// the result. Distinct from <see cref="Nop"/> so the discarded read is deliberate.
    /// </summary>
    NopRead,

    // Undocumented combination read-modify-writes: a shift or increment on memory,
    // followed by an ALU operation against the accumulator, sharing one operand.
    Slo, Rla, Sre, Rra, Dcp, Isc,

    // Branch conditions
    Bcc, Bcs, Beq, Bmi, Bne, Bpl, Bvc, Bvs,

    /// <summary>Undocumented. Loads both the accumulator and X from one read.</summary>
    Lax,

    /// <summary>Undocumented. Stores the bitwise AND of the accumulator and X. Sets no flags.</summary>
    Sax,
}
