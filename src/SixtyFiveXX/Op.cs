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

    // CMOS additions.

    /// <summary>Stores zero. CMOS only.</summary>
    Stz,

    /// <summary>Test and reset bits: Z from A AND M, then M &amp;= ~A. Read-modify-write. CMOS only.</summary>
    Trb,

    /// <summary>Test and set bits: Z from A AND M, then M |= A. Read-modify-write. CMOS only.</summary>
    Tsb,

    /// <summary>Branch always. CMOS only.</summary>
    Bra,

    /// <summary>
    /// CMOS <c>ADC</c> and <c>SBC</c>. Decimal mode differs from NMOS in the accumulator
    /// correction as well as the flags, so these are separate members rather than a variant
    /// test inside <see cref="Adc"/>. Derived from the vectors, not a datasheet: N and Z
    /// come from the final decimal result, while C and V are computed exactly as NMOS does
    /// — so "correct N/V/Z" overstates it, V included. Verified against every decimal
    /// vector of <c>$69</c>, <c>$72</c>, <c>$E9</c> and <c>$F2</c>.
    /// </summary>
    AdcCmos, SbcCmos,

    /// <summary>
    /// Rockwell's bit operations: reset or set one bit of a page-zero byte, and branch on
    /// one bit of it being clear or set. The bit index is not part of the operation — it
    /// comes from bits 4-6 of the opcode, exactly as the hardware decodes it, which is why
    /// these are four members rather than thirty-two.
    /// </summary>
    Rmb, Smb, Bbr, Bbs,

    /// <summary>
    /// The immediate form of BIT, which sets only Z. Every other addressing mode also
    /// copies the operand's top two bits into N and V; this one does not, so it needs its
    /// own member rather than a mode test inside <see cref="Bit"/>. Confirmed against all
    /// 10,000 <c>$89</c> vectors, none of which alters N or V. CMOS only.
    /// </summary>
    BitImm,

    // Index-register stack operations. CMOS only.
    Phx, Phy, Plx, Ply,

    /// <summary>
    /// Increment and decrement the accumulator. Separate members for the same reason
    /// <see cref="AslA"/> is separate from <see cref="Asl"/> — they take no effective
    /// address. CMOS only.
    /// </summary>
    IncA, DecA,

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

    // Undocumented immediate-mode instructions with flag behaviour unlike any
    // documented opcode.
    Anc, Alr, Arr, Sbx,

    // Undocumented and genuinely unstable on real silicon. Modelled as the
    // deterministic values the SingleStepTests vectors encode.
    Ane, Lxa, Las, Sha, Shx, Shy, Tas,

    /// <summary>Undocumented. Halts the processor until reset.</summary>
    Jam,
}
