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

    /// <summary>WDC only. Halts until an interrupt is signalled.</summary>
    Wai,

    /// <summary>WDC only. Halts until reset.</summary>
    Stp,

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

    // 65816 mode control and status bits.

    /// <summary>
    /// Exchange carry and emulation flags. The only instruction that changes
    /// <see cref="CpuState.E"/>. 65816 only.
    /// </summary>
    Xce,

    /// <summary>Reset status bits: clears each flag whose bit is set in the operand. 65816 only.</summary>
    Rep,

    /// <summary>Set status bits: sets each flag whose bit is set in the operand. 65816 only.</summary>
    Sep,

    /// <summary>
    /// The 65816's <c>ADC</c> and <c>SBC</c>. Separate members from both <see cref="Adc"/>
    /// (NMOS) and <see cref="AdcCmos"/>, by the 65816 design spec's pre-committed rule — reuse
    /// only on a source stating the behaviour is identical, and Clark states a divergence
    /// instead: decimal mode costs the 65816 no extra cycle, unlike the 65C02 (research
    /// document §12.2). These are also the only arithmetic members that operate at two widths,
    /// and no 65C02 has a 16-bit decimal mode for the 16-bit path to have inherited — research
    /// document §12.1.
    /// </summary>
    Adc816, Sbc816,

    /// <summary>Transfer X to Y, and Y to X. Sized by the <c>x</c> flag. 65816 only.</summary>
    Txy, Tyx,

    /// <summary>
    /// Transfers between the 16-bit accumulator and the direct and stack registers. All four move
    /// all sixteen bits regardless of <c>m</c> and <c>x</c> — the registers they touch have no
    /// narrow form. <c>Tcs</c> sets no flags, as <see cref="Txs"/> does not. 65816 only.
    /// </summary>
    Tcd, Tdc, Tcs, Tsc,

    /// <summary>
    /// Exchange the two halves of the 16-bit accumulator. Flags come from the new low byte as an
    /// 8-bit result regardless of <c>m</c> — research document §13.5. 65816 only.
    /// </summary>
    Xba,

    /// <summary>
    /// Push and pull the data bank register, and push the program bank register. One byte
    /// each, regardless of <c>m</c> and <c>x</c>; <c>PLB</c> sets <c>N</c> and <c>Z</c> from
    /// the eight-bit value, and there is no <c>PLK</c> — the program bank is changed only by
    /// a long jump, a long call, a long return or an interrupt. 65816 only.
    /// </summary>
    Phb, Phk, Plb,

    /// <summary>
    /// Push and pull the direct register. Always sixteen bits — <c>D</c> has no narrow form —
    /// and <c>PLD</c>'s <c>N</c> and <c>Z</c> come from all sixteen. 65816 only.
    /// </summary>
    Phd, Pld,

    /// <summary>
    /// The co-processor software interrupt, <c>$02</c>. Two bytes and <c>8-e</c> cycles, exactly
    /// as <see cref="Brk"/> is: it shares Table 5-7's row 22j with it and differs only in which
    /// vector it reads (research document §14.2). 65816 only.
    /// </summary>
    Cop,

    /// <summary>
    /// The reserved no-operation, <c>$42</c>. Two bytes and two cycles, and its second byte is
    /// never read — the cycle that would fetch it is an internal cycle (research document
    /// §14.2/§3.4, measured against all 20,000 vectors; Clark §6.7's "The second byte is read,
    /// but ignored" is wrong). WDC guarantees the opcode will never be given a meaning on this
    /// part. 65816 only.
    /// </summary>
    Wdm,

    /// <summary>
    /// The two hardware interrupts, as operations. Neither is an opcode: <c>FetchOpcode</c>
    /// assigns one of them when it diverts into the interrupt sequence instead of fetching, so
    /// that the sequence's shared micro-ops can tell an <c>IRQ</c> from an <c>NMI</c> from a
    /// <c>BRK</c>. Three separate things depend on knowing which: which vector
    /// <c>Cpu.Vector816</c> selects, whether the pushed <c>P</c> has bit 4 forced clear in
    /// emulation mode (datasheet note 11 — hardware interrupts only, not <see cref="Brk"/> or
    /// <see cref="Cop"/>), and whether the pushes wrap inside page one (Clark §5.22 —
    /// <em>all</em> interrupts do, see <c>Cpu.StackWrapsInPageOne</c>). Every core assigns
    /// them; only the 65816 reads them.
    /// </summary>
    Irq, Nmi,

    /// <summary>
    /// The two block moves, <c>$54</c> and <c>$44</c>. One instruction per byte moved: seven
    /// cycles that read <c>SBA,X</c>, write <c>DBA,Y</c>, decrement the sixteen-bit accumulator
    /// and then rewind <c>PC</c> onto the opcode unless the count has run out, so the next fetch
    /// re-executes the same instruction. <see cref="Mvn"/> increments both index registers and
    /// <see cref="Mvp"/> decrements them; that direction is the only thing either micro-op
    /// sequence reads this member for. Research document §14.3. 65816 only — these appear in no
    /// eight-bit table.
    /// </summary>
    Mvn, Mvp,

    /// <summary>
    /// The long branch, <c>$82</c>. <see cref="Bra"/> with a signed sixteen-bit displacement:
    /// three bytes, a flat four cycles in both modes, and no conditional cycle of any kind —
    /// there is no not-taken case and no page-cross penalty (research document §14.5, datasheet
    /// Table 5-7 row 21 and Clark §6.2.1.2). Like every branch on this part it wraps inside the
    /// program bank and never carries into <c>PBR</c>. A separate member from <see cref="Bra"/>
    /// only because the displacement's width decides the sequence, not the condition. 65816
    /// only.
    /// </summary>
    Brl,
}
