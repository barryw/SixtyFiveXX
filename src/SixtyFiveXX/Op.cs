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

    // Branch conditions
    Bcc, Bcs, Beq, Bmi, Bne, Bpl, Bvc, Bvs,
}
