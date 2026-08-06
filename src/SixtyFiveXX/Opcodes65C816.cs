namespace SixtyFiveXX;

/// <summary>
/// The WDC 65C816 opcode table.
/// </summary>
/// <remarks>
/// Two hundred opcodes are defined: every addressing form of <c>LDA</c> and
/// <c>STA</c> (<c>STA</c> has no immediate form), plus <c>XCE</c>, <c>REP</c> and <c>SEP</c> —
/// phase 7b's thirty-two, chosen so the variant, its table and its reset semantics could be
/// exercised end to end before any 65816 micro-op sequence existed — phase 7c task 3's
/// forty-five: <c>ORA</c>, <c>AND</c> and <c>EOR</c> in all fifteen addressing forms each;
/// task 4's twenty-one: <c>CMP</c> in all fifteen, plus <c>CPX</c> and <c>CPY</c> in three each
/// — the first opcodes here sized by <c>x</c> rather than <c>m</c>; task 5's thirty:
/// <c>ADC</c> and <c>SBC</c> in all fifteen each, the first opcodes here with a decimal mode;
/// and task 6's five: <c>BIT</c>, immediate plus its three read-only addressing forms, whose
/// immediate opcode is a genuinely different operation — <c>Op.BitImm</c> sets Z alone — not
/// just a narrower addressing mode of <c>Op.Bit</c>; and task 7's twenty: <c>LDX</c> and
/// <c>LDY</c> in five forms each, <c>STX</c> and <c>STY</c> in three each, and <c>STZ</c> in
/// four — the only ones here that do not reuse an addressing sequence phase 7b certified, since
/// <c>LDX</c>/<c>STX</c>'s <c>dp,Y</c> (<see cref="AddrMode.DirectPageY"/>) is used by no other
/// instruction on the part and so had to be added with them; and phase 7c′ task 2's sixteen:
/// <c>ASL</c>, <c>LSR</c>, <c>ROL</c> and <c>ROR</c> in <c>dp</c>, <c>dp,X</c>, <c>abs</c> and
/// <c>abs,X</c> each — the first <see cref="Access.ReadModifyWrite"/> entries on this part, and
/// with them datasheet Note 17, the one behaviour here whose bus direction is decided at run
/// time rather than at table-build time (research document §13.1); task 3's twelve:
/// <c>INC</c> and <c>DEC</c> on memory in <c>dp</c>, <c>dp,X</c>, <c>abs</c> and <c>abs,X</c>
/// each, plus <c>TSB</c> and <c>TRB</c> in <c>dp</c> and <c>abs</c> each — <c>TSB</c>/<c>TRB</c>
/// set Z from the AND of A and memory over the operative width, like <c>BIT</c>, but leave N and
/// V untouched, unlike <c>BIT</c>; and task 4's six: <c>ASL</c>, <c>LSR</c>, <c>ROL</c>,
/// <c>ROR</c>, <c>INC</c> and <c>DEC</c> on the accumulator — the first opcodes here with no
/// operand at all, <see cref="AddrMode.Accumulator"/> and <see cref="Access.None"/>, sized by
/// <c>m</c> like every other accumulator operation but declaring no <see cref="Width"/>, since
/// they never reach a width-deciding micro-op (<c>MicroOpTable.Emit816</c>'s
/// <see cref="AddrMode.Implied"/>/<see cref="AddrMode.Accumulator"/> branch routes them through
/// <c>MicroOp.ImpliedExec816</c> instead); and task 5's thirteen: the twelve transfers —
/// <c>TAX</c>, <c>TAY</c>, <c>TXA</c>, <c>TYA</c>, <c>TXS</c>, <c>TSX</c>, <c>TXY</c>,
/// <c>TYX</c>, <c>TCD</c>, <c>TDC</c>, <c>TCS</c> and <c>TSC</c> — plus <c>XBA</c>. The transfers'
/// width comes from the destination register, not from the source (research document §13.4): an
/// index destination is sized by <c>x</c>, an accumulator destination by <c>m</c>, and the four
/// <c>TC*</c>/<c>T*C</c> forms plus <c>TXS</c> are always 16-bit because <c>D</c> and <c>S</c>
/// have no narrow form. <c>XBA</c> is the odd one out twice over: its <c>N</c>/<c>Z</c> come from
/// the new low byte as an 8-bit result whatever <c>m</c> says (§13.5), and it is the only implied
/// opcode on the part that takes three cycles rather than two, so it is the first here to need a
/// sequence of its own rather than the shared implied one; and task 6's twelve: the seven flag
/// instructions (<c>CLC</c>, <c>SEC</c>, <c>CLI</c>, <c>SEI</c>, <c>CLV</c>, <c>CLD</c>,
/// <c>SED</c>), <c>INX</c>/<c>INY</c>/<c>DEX</c>/<c>DEY</c> and <c>NOP</c> — 200 + 12 = 212. The
/// flag instructions and <c>NOP</c> touch no width-dependent register; the index increments are
/// sized by <c>x</c>, the same implied-mode shape task 5's accumulator forms use; and phase 7d
/// task 2's thirteen: the seven pushes (<c>PHA</c>, <c>PHP</c>, <c>PHX</c>, <c>PHY</c>,
/// <c>PHB</c>, <c>PHD</c>, <c>PHK</c>) and the six pulls (<c>PLA</c>, <c>PLP</c>, <c>PLX</c>,
/// <c>PLY</c>, <c>PLB</c>, <c>PLD</c>) — 212 + 13 = 225. These are the first entries here to take
/// <see cref="AddrMode.Stack"/> and so the first routed through
/// <c>MicroOpTable.EmitControlFlow816</c> rather than <c>EmitAddressed816</c>. They declare no
/// <see cref="Width"/> despite four of them being sized by <c>m</c> and four by <c>x</c>: they
/// fetch no operand from memory and reach no width-deciding micro-op, so each arm tests its own
/// flag through <c>Cpu.StackIsWide</c> (research document §14.1); and phase 7d task 3's three:
/// <c>BRK</c>, <c>COP</c> and <c>WDM</c> — 225 + 3 = 228. The first two share Table 5-7's row 22j
/// with each other and cycles 3 to 8 with the hardware interrupts' row 22a, so they also bring in
/// the part's own <c>IRQ</c> and <c>NMI</c> sequences, which have no opcodes and no vectors
/// (research document §14.2). <c>WDM</c> is none of that — a reserved two-byte, two-cycle
/// no-operation whose second byte is never read; and phase 7d task 4's two: the block moves
/// <c>MVN</c> (<c>$54</c>) and <c>MVP</c> (<c>$44</c>) — 228 + 2 = 230. They are the only entries
/// here taking <see cref="AddrMode.BlockMove"/>, and the only instruction on the part that
/// rewinds <c>PC</c>: one whole instruction per byte moved, re-entered by the next fetch until
/// the count in the sixteen-bit accumulator runs out (research document §14.3); phase 7d task 5's
/// two: the halts <c>WAI</c> (<c>$CB</c>) and <c>STP</c> (<c>$DB</c>) — 230 + 2 = 232; and phase
/// 7d task 6's ten: the eight conditional branches, <c>BRA</c> and <c>BRL</c> — 232 + 10 = 242.
/// The nine short branches take <see cref="AddrMode.Relative"/>, shared with the five eight-bit
/// cores but emitted to micro-ops of this part's own; <c>BRL</c> brings in the one addressing mode
/// this task adds, <see cref="AddrMode.RelativeLong"/>. <see cref="Access.None"/> and no
/// <see cref="Width"/> on all ten: a displacement is not an operand fetched at a width the flags
/// select, and <c>BRL</c>'s is sixteen bits whatever <c>m</c> and <c>x</c> say.
/// <para>
/// The remaining 14 entries are <see cref="OpcodeInfo.Undefined"/> and throw
/// <see cref="UndefinedOpcodeException"/> on fetch. Phase 7d's later tasks fill them in: the five
/// jumps (<c>$4C</c>, <c>$6C</c>, <c>$7C</c>, <c>$5C</c>, <c>$DC</c>), the three calls
/// (<c>JSR abs</c>, <c>JSR (abs,X)</c>, <c>JSL</c>), the three returns (<c>RTI</c>, <c>RTS</c>,
/// <c>RTL</c>) and the three stack-addressing pushes (<c>PEA</c>, <c>PEI</c>, <c>PER</c>) —
/// research document §14.8's table of all 44, less this task's ten and the four tasks 4 and 5
/// landed.
/// </para>
/// </remarks>
internal static class Opcodes65C816
{
    /// <summary>Opcode byte to descriptor. 242 entries defined, 14 undefined.</summary>
    public static readonly OpcodeInfo[] Table = BuildTable();

    private static OpcodeInfo[] BuildTable()
    {
        var t = new OpcodeInfo[256];
        for (var i = 0; i < t.Length; i++) t[i] = OpcodeInfo.Undefined;

        void Set(int opcode, string mnemonic, AddrMode mode, Op op, Access access,
                 Width width = Width.None) =>
            t[opcode] = new OpcodeInfo(mnemonic, mode, op, access, width);

        // LDA — every addressing form the 65816 has.
        Set(0xA9, "LDA", AddrMode.Immediate,                  Op.Lda, Access.Read, Width.M);
        Set(0xA5, "LDA", AddrMode.DirectPage,                 Op.Lda, Access.Read, Width.M);
        Set(0xB5, "LDA", AddrMode.DirectPageX,                Op.Lda, Access.Read, Width.M);
        Set(0xAD, "LDA", AddrMode.Absolute,                   Op.Lda, Access.Read, Width.M);
        Set(0xBD, "LDA", AddrMode.AbsoluteX,                  Op.Lda, Access.Read, Width.M);
        Set(0xB9, "LDA", AddrMode.AbsoluteY,                  Op.Lda, Access.Read, Width.M);
        Set(0xA1, "LDA", AddrMode.DirectPageIndexedIndirectX, Op.Lda, Access.Read, Width.M);
        Set(0xB1, "LDA", AddrMode.DirectPageIndirectY,        Op.Lda, Access.Read, Width.M);
        Set(0xB2, "LDA", AddrMode.DirectPageIndirect,         Op.Lda, Access.Read, Width.M);
        Set(0xA7, "LDA", AddrMode.DirectPageIndirectLong,     Op.Lda, Access.Read, Width.M);
        Set(0xB7, "LDA", AddrMode.DirectPageIndirectLongY,    Op.Lda, Access.Read, Width.M);
        Set(0xAF, "LDA", AddrMode.AbsoluteLong,               Op.Lda, Access.Read, Width.M);
        Set(0xBF, "LDA", AddrMode.AbsoluteLongX,              Op.Lda, Access.Read, Width.M);
        Set(0xA3, "LDA", AddrMode.StackRelative,              Op.Lda, Access.Read, Width.M);
        Set(0xB3, "LDA", AddrMode.StackRelativeIndirectY,     Op.Lda, Access.Read, Width.M);

        // STA — the same forms as LDA, minus immediate: there is no such thing as STA #imm.
        Set(0x85, "STA", AddrMode.DirectPage,                 Op.Sta, Access.Write, Width.M);
        Set(0x95, "STA", AddrMode.DirectPageX,                Op.Sta, Access.Write, Width.M);
        Set(0x8D, "STA", AddrMode.Absolute,                   Op.Sta, Access.Write, Width.M);
        Set(0x9D, "STA", AddrMode.AbsoluteX,                  Op.Sta, Access.Write, Width.M);
        Set(0x99, "STA", AddrMode.AbsoluteY,                  Op.Sta, Access.Write, Width.M);
        Set(0x81, "STA", AddrMode.DirectPageIndexedIndirectX, Op.Sta, Access.Write, Width.M);
        Set(0x91, "STA", AddrMode.DirectPageIndirectY,        Op.Sta, Access.Write, Width.M);
        Set(0x92, "STA", AddrMode.DirectPageIndirect,         Op.Sta, Access.Write, Width.M);
        Set(0x87, "STA", AddrMode.DirectPageIndirectLong,     Op.Sta, Access.Write, Width.M);
        Set(0x97, "STA", AddrMode.DirectPageIndirectLongY,    Op.Sta, Access.Write, Width.M);
        Set(0x8F, "STA", AddrMode.AbsoluteLong,               Op.Sta, Access.Write, Width.M);
        Set(0x9F, "STA", AddrMode.AbsoluteLongX,              Op.Sta, Access.Write, Width.M);
        Set(0x83, "STA", AddrMode.StackRelative,              Op.Sta, Access.Write, Width.M);
        Set(0x93, "STA", AddrMode.StackRelativeIndirectY,     Op.Sta, Access.Write, Width.M);

        // The three logical operations, in every addressing form the 65816 has. Each reuses an
        // addressing sequence phase 7b certified against LDA/STA — the operation changes, the
        // cycles do not. Width.M for all of them: they move through the accumulator.
        Set(0x09, "ORA", AddrMode.Immediate,                  Op.Ora, Access.Read, Width.M);
        Set(0x05, "ORA", AddrMode.DirectPage,                 Op.Ora, Access.Read, Width.M);
        Set(0x15, "ORA", AddrMode.DirectPageX,                Op.Ora, Access.Read, Width.M);
        Set(0x0D, "ORA", AddrMode.Absolute,                   Op.Ora, Access.Read, Width.M);
        Set(0x1D, "ORA", AddrMode.AbsoluteX,                  Op.Ora, Access.Read, Width.M);
        Set(0x19, "ORA", AddrMode.AbsoluteY,                  Op.Ora, Access.Read, Width.M);
        Set(0x01, "ORA", AddrMode.DirectPageIndexedIndirectX, Op.Ora, Access.Read, Width.M);
        Set(0x11, "ORA", AddrMode.DirectPageIndirectY,        Op.Ora, Access.Read, Width.M);
        Set(0x12, "ORA", AddrMode.DirectPageIndirect,         Op.Ora, Access.Read, Width.M);
        Set(0x07, "ORA", AddrMode.DirectPageIndirectLong,     Op.Ora, Access.Read, Width.M);
        Set(0x17, "ORA", AddrMode.DirectPageIndirectLongY,    Op.Ora, Access.Read, Width.M);
        Set(0x0F, "ORA", AddrMode.AbsoluteLong,               Op.Ora, Access.Read, Width.M);
        Set(0x1F, "ORA", AddrMode.AbsoluteLongX,              Op.Ora, Access.Read, Width.M);
        Set(0x03, "ORA", AddrMode.StackRelative,              Op.Ora, Access.Read, Width.M);
        Set(0x13, "ORA", AddrMode.StackRelativeIndirectY,     Op.Ora, Access.Read, Width.M);

        Set(0x29, "AND", AddrMode.Immediate,                  Op.And, Access.Read, Width.M);
        Set(0x25, "AND", AddrMode.DirectPage,                 Op.And, Access.Read, Width.M);
        Set(0x35, "AND", AddrMode.DirectPageX,                Op.And, Access.Read, Width.M);
        Set(0x2D, "AND", AddrMode.Absolute,                   Op.And, Access.Read, Width.M);
        Set(0x3D, "AND", AddrMode.AbsoluteX,                  Op.And, Access.Read, Width.M);
        Set(0x39, "AND", AddrMode.AbsoluteY,                  Op.And, Access.Read, Width.M);
        Set(0x21, "AND", AddrMode.DirectPageIndexedIndirectX, Op.And, Access.Read, Width.M);
        Set(0x31, "AND", AddrMode.DirectPageIndirectY,        Op.And, Access.Read, Width.M);
        Set(0x32, "AND", AddrMode.DirectPageIndirect,         Op.And, Access.Read, Width.M);
        Set(0x27, "AND", AddrMode.DirectPageIndirectLong,     Op.And, Access.Read, Width.M);
        Set(0x37, "AND", AddrMode.DirectPageIndirectLongY,    Op.And, Access.Read, Width.M);
        Set(0x2F, "AND", AddrMode.AbsoluteLong,               Op.And, Access.Read, Width.M);
        Set(0x3F, "AND", AddrMode.AbsoluteLongX,              Op.And, Access.Read, Width.M);
        Set(0x23, "AND", AddrMode.StackRelative,              Op.And, Access.Read, Width.M);
        Set(0x33, "AND", AddrMode.StackRelativeIndirectY,     Op.And, Access.Read, Width.M);

        Set(0x49, "EOR", AddrMode.Immediate,                  Op.Eor, Access.Read, Width.M);
        Set(0x45, "EOR", AddrMode.DirectPage,                 Op.Eor, Access.Read, Width.M);
        Set(0x55, "EOR", AddrMode.DirectPageX,                Op.Eor, Access.Read, Width.M);
        Set(0x4D, "EOR", AddrMode.Absolute,                   Op.Eor, Access.Read, Width.M);
        Set(0x5D, "EOR", AddrMode.AbsoluteX,                  Op.Eor, Access.Read, Width.M);
        Set(0x59, "EOR", AddrMode.AbsoluteY,                  Op.Eor, Access.Read, Width.M);
        Set(0x41, "EOR", AddrMode.DirectPageIndexedIndirectX, Op.Eor, Access.Read, Width.M);
        Set(0x51, "EOR", AddrMode.DirectPageIndirectY,        Op.Eor, Access.Read, Width.M);
        Set(0x52, "EOR", AddrMode.DirectPageIndirect,         Op.Eor, Access.Read, Width.M);
        Set(0x47, "EOR", AddrMode.DirectPageIndirectLong,     Op.Eor, Access.Read, Width.M);
        Set(0x57, "EOR", AddrMode.DirectPageIndirectLongY,    Op.Eor, Access.Read, Width.M);
        Set(0x4F, "EOR", AddrMode.AbsoluteLong,               Op.Eor, Access.Read, Width.M);
        Set(0x5F, "EOR", AddrMode.AbsoluteLongX,              Op.Eor, Access.Read, Width.M);
        Set(0x43, "EOR", AddrMode.StackRelative,              Op.Eor, Access.Read, Width.M);
        Set(0x53, "EOR", AddrMode.StackRelativeIndirectY,     Op.Eor, Access.Read, Width.M);

        // Compare against the accumulator: fifteen forms, Width.M.
        Set(0xC9, "CMP", AddrMode.Immediate,                  Op.Cmp, Access.Read, Width.M);
        Set(0xC5, "CMP", AddrMode.DirectPage,                 Op.Cmp, Access.Read, Width.M);
        Set(0xD5, "CMP", AddrMode.DirectPageX,                Op.Cmp, Access.Read, Width.M);
        Set(0xCD, "CMP", AddrMode.Absolute,                   Op.Cmp, Access.Read, Width.M);
        Set(0xDD, "CMP", AddrMode.AbsoluteX,                  Op.Cmp, Access.Read, Width.M);
        Set(0xD9, "CMP", AddrMode.AbsoluteY,                  Op.Cmp, Access.Read, Width.M);
        Set(0xC1, "CMP", AddrMode.DirectPageIndexedIndirectX, Op.Cmp, Access.Read, Width.M);
        Set(0xD1, "CMP", AddrMode.DirectPageIndirectY,        Op.Cmp, Access.Read, Width.M);
        Set(0xD2, "CMP", AddrMode.DirectPageIndirect,         Op.Cmp, Access.Read, Width.M);
        Set(0xC7, "CMP", AddrMode.DirectPageIndirectLong,     Op.Cmp, Access.Read, Width.M);
        Set(0xD7, "CMP", AddrMode.DirectPageIndirectLongY,    Op.Cmp, Access.Read, Width.M);
        Set(0xCF, "CMP", AddrMode.AbsoluteLong,               Op.Cmp, Access.Read, Width.M);
        Set(0xDF, "CMP", AddrMode.AbsoluteLongX,              Op.Cmp, Access.Read, Width.M);
        Set(0xC3, "CMP", AddrMode.StackRelative,              Op.Cmp, Access.Read, Width.M);
        Set(0xD3, "CMP", AddrMode.StackRelativeIndirectY,     Op.Cmp, Access.Read, Width.M);

        // Compare against an index register: three forms each, and Width.X — the first opcodes
        // on this core whose operand width comes from x rather than m.
        Set(0xE0, "CPX", AddrMode.Immediate,  Op.Cpx, Access.Read, Width.X);
        Set(0xE4, "CPX", AddrMode.DirectPage, Op.Cpx, Access.Read, Width.X);
        Set(0xEC, "CPX", AddrMode.Absolute,   Op.Cpx, Access.Read, Width.X);

        Set(0xC0, "CPY", AddrMode.Immediate,  Op.Cpy, Access.Read, Width.X);
        Set(0xC4, "CPY", AddrMode.DirectPage, Op.Cpy, Access.Read, Width.X);
        Set(0xCC, "CPY", AddrMode.Absolute,   Op.Cpy, Access.Read, Width.X);

        // Arithmetic: fifteen forms each, Width.M, and the same addressing sequences the six
        // full-mode ALU operations share (research document §12.5). $EB is deliberately absent
        // from the SBC block — on the 65816 that byte is XBA, not the NMOS 6502's undocumented
        // SBC alias.
        Set(0x69, "ADC", AddrMode.Immediate,                  Op.Adc816, Access.Read, Width.M);
        Set(0x65, "ADC", AddrMode.DirectPage,                 Op.Adc816, Access.Read, Width.M);
        Set(0x75, "ADC", AddrMode.DirectPageX,                Op.Adc816, Access.Read, Width.M);
        Set(0x6D, "ADC", AddrMode.Absolute,                   Op.Adc816, Access.Read, Width.M);
        Set(0x7D, "ADC", AddrMode.AbsoluteX,                  Op.Adc816, Access.Read, Width.M);
        Set(0x79, "ADC", AddrMode.AbsoluteY,                  Op.Adc816, Access.Read, Width.M);
        Set(0x61, "ADC", AddrMode.DirectPageIndexedIndirectX, Op.Adc816, Access.Read, Width.M);
        Set(0x71, "ADC", AddrMode.DirectPageIndirectY,        Op.Adc816, Access.Read, Width.M);
        Set(0x72, "ADC", AddrMode.DirectPageIndirect,         Op.Adc816, Access.Read, Width.M);
        Set(0x67, "ADC", AddrMode.DirectPageIndirectLong,     Op.Adc816, Access.Read, Width.M);
        Set(0x77, "ADC", AddrMode.DirectPageIndirectLongY,    Op.Adc816, Access.Read, Width.M);
        Set(0x6F, "ADC", AddrMode.AbsoluteLong,               Op.Adc816, Access.Read, Width.M);
        Set(0x7F, "ADC", AddrMode.AbsoluteLongX,              Op.Adc816, Access.Read, Width.M);
        Set(0x63, "ADC", AddrMode.StackRelative,              Op.Adc816, Access.Read, Width.M);
        Set(0x73, "ADC", AddrMode.StackRelativeIndirectY,     Op.Adc816, Access.Read, Width.M);

        Set(0xE9, "SBC", AddrMode.Immediate,                  Op.Sbc816, Access.Read, Width.M);
        Set(0xE5, "SBC", AddrMode.DirectPage,                 Op.Sbc816, Access.Read, Width.M);
        Set(0xF5, "SBC", AddrMode.DirectPageX,                Op.Sbc816, Access.Read, Width.M);
        Set(0xED, "SBC", AddrMode.Absolute,                   Op.Sbc816, Access.Read, Width.M);
        Set(0xFD, "SBC", AddrMode.AbsoluteX,                  Op.Sbc816, Access.Read, Width.M);
        Set(0xF9, "SBC", AddrMode.AbsoluteY,                  Op.Sbc816, Access.Read, Width.M);
        Set(0xE1, "SBC", AddrMode.DirectPageIndexedIndirectX, Op.Sbc816, Access.Read, Width.M);
        Set(0xF1, "SBC", AddrMode.DirectPageIndirectY,        Op.Sbc816, Access.Read, Width.M);
        Set(0xF2, "SBC", AddrMode.DirectPageIndirect,         Op.Sbc816, Access.Read, Width.M);
        Set(0xE7, "SBC", AddrMode.DirectPageIndirectLong,     Op.Sbc816, Access.Read, Width.M);
        Set(0xF7, "SBC", AddrMode.DirectPageIndirectLongY,    Op.Sbc816, Access.Read, Width.M);
        Set(0xEF, "SBC", AddrMode.AbsoluteLong,               Op.Sbc816, Access.Read, Width.M);
        Set(0xFF, "SBC", AddrMode.AbsoluteLongX,              Op.Sbc816, Access.Read, Width.M);
        Set(0xE3, "SBC", AddrMode.StackRelative,              Op.Sbc816, Access.Read, Width.M);
        Set(0xF3, "SBC", AddrMode.StackRelativeIndirectY,     Op.Sbc816, Access.Read, Width.M);

        // BIT. The immediate form is a different operation, not a different mode of the same one:
        // it sets Z alone and leaves N and V untouched. Op.BitImm already models that for the
        // 65C02 and needs only widening here.
        Set(0x89, "BIT", AddrMode.Immediate,   Op.BitImm, Access.Read, Width.M);
        Set(0x24, "BIT", AddrMode.DirectPage,  Op.Bit,    Access.Read, Width.M);
        Set(0x34, "BIT", AddrMode.DirectPageX, Op.Bit,    Access.Read, Width.M);
        Set(0x2C, "BIT", AddrMode.Absolute,    Op.Bit,    Access.Read, Width.M);
        Set(0x3C, "BIT", AddrMode.AbsoluteX,   Op.Bit,    Access.Read, Width.M);

        // Index loads and stores, and STZ. LDX/LDY/STX/STY are Width.X — they move through an
        // index register. STZ is Width.M: it stores an accumulator-width zero, despite naming no
        // register at all.
        Set(0xA2, "LDX", AddrMode.Immediate,    Op.Ldx, Access.Read,  Width.X);
        Set(0xA6, "LDX", AddrMode.DirectPage,   Op.Ldx, Access.Read,  Width.X);
        Set(0xB6, "LDX", AddrMode.DirectPageY,  Op.Ldx, Access.Read,  Width.X);
        Set(0xAE, "LDX", AddrMode.Absolute,     Op.Ldx, Access.Read,  Width.X);
        Set(0xBE, "LDX", AddrMode.AbsoluteY,    Op.Ldx, Access.Read,  Width.X);

        Set(0xA0, "LDY", AddrMode.Immediate,    Op.Ldy, Access.Read,  Width.X);
        Set(0xA4, "LDY", AddrMode.DirectPage,   Op.Ldy, Access.Read,  Width.X);
        Set(0xB4, "LDY", AddrMode.DirectPageX,  Op.Ldy, Access.Read,  Width.X);
        Set(0xAC, "LDY", AddrMode.Absolute,     Op.Ldy, Access.Read,  Width.X);
        Set(0xBC, "LDY", AddrMode.AbsoluteX,    Op.Ldy, Access.Read,  Width.X);

        Set(0x86, "STX", AddrMode.DirectPage,   Op.Stx, Access.Write, Width.X);
        Set(0x96, "STX", AddrMode.DirectPageY,  Op.Stx, Access.Write, Width.X);
        Set(0x8E, "STX", AddrMode.Absolute,     Op.Stx, Access.Write, Width.X);

        Set(0x84, "STY", AddrMode.DirectPage,   Op.Sty, Access.Write, Width.X);
        Set(0x94, "STY", AddrMode.DirectPageX,  Op.Sty, Access.Write, Width.X);
        Set(0x8C, "STY", AddrMode.Absolute,     Op.Sty, Access.Write, Width.X);

        Set(0x64, "STZ", AddrMode.DirectPage,   Op.Stz, Access.Write, Width.M);
        Set(0x74, "STZ", AddrMode.DirectPageX,  Op.Stz, Access.Write, Width.M);
        Set(0x9C, "STZ", AddrMode.Absolute,     Op.Stz, Access.Write, Width.M);
        Set(0x9E, "STZ", AddrMode.AbsoluteX,    Op.Stz, Access.Write, Width.M);

        // Read-modify-write shifts. Width.M — the operand comes from memory and is sized by m.
        Set(0x06, "ASL", AddrMode.DirectPage,  Op.Asl, Access.ReadModifyWrite, Width.M);
        Set(0x16, "ASL", AddrMode.DirectPageX, Op.Asl, Access.ReadModifyWrite, Width.M);
        Set(0x0E, "ASL", AddrMode.Absolute,    Op.Asl, Access.ReadModifyWrite, Width.M);
        Set(0x1E, "ASL", AddrMode.AbsoluteX,   Op.Asl, Access.ReadModifyWrite, Width.M);

        Set(0x46, "LSR", AddrMode.DirectPage,  Op.Lsr, Access.ReadModifyWrite, Width.M);
        Set(0x56, "LSR", AddrMode.DirectPageX, Op.Lsr, Access.ReadModifyWrite, Width.M);
        Set(0x4E, "LSR", AddrMode.Absolute,    Op.Lsr, Access.ReadModifyWrite, Width.M);
        Set(0x5E, "LSR", AddrMode.AbsoluteX,   Op.Lsr, Access.ReadModifyWrite, Width.M);

        Set(0x26, "ROL", AddrMode.DirectPage,  Op.Rol, Access.ReadModifyWrite, Width.M);
        Set(0x36, "ROL", AddrMode.DirectPageX, Op.Rol, Access.ReadModifyWrite, Width.M);
        Set(0x2E, "ROL", AddrMode.Absolute,    Op.Rol, Access.ReadModifyWrite, Width.M);
        Set(0x3E, "ROL", AddrMode.AbsoluteX,   Op.Rol, Access.ReadModifyWrite, Width.M);

        Set(0x66, "ROR", AddrMode.DirectPage,  Op.Ror, Access.ReadModifyWrite, Width.M);
        Set(0x76, "ROR", AddrMode.DirectPageX, Op.Ror, Access.ReadModifyWrite, Width.M);
        Set(0x6E, "ROR", AddrMode.Absolute,    Op.Ror, Access.ReadModifyWrite, Width.M);
        Set(0x7E, "ROR", AddrMode.AbsoluteX,   Op.Ror, Access.ReadModifyWrite, Width.M);

        Set(0xE6, "INC", AddrMode.DirectPage,  Op.Inc, Access.ReadModifyWrite, Width.M);
        Set(0xF6, "INC", AddrMode.DirectPageX, Op.Inc, Access.ReadModifyWrite, Width.M);
        Set(0xEE, "INC", AddrMode.Absolute,    Op.Inc, Access.ReadModifyWrite, Width.M);
        Set(0xFE, "INC", AddrMode.AbsoluteX,   Op.Inc, Access.ReadModifyWrite, Width.M);

        Set(0xC6, "DEC", AddrMode.DirectPage,  Op.Dec, Access.ReadModifyWrite, Width.M);
        Set(0xD6, "DEC", AddrMode.DirectPageX, Op.Dec, Access.ReadModifyWrite, Width.M);
        Set(0xCE, "DEC", AddrMode.Absolute,    Op.Dec, Access.ReadModifyWrite, Width.M);
        Set(0xDE, "DEC", AddrMode.AbsoluteX,   Op.Dec, Access.ReadModifyWrite, Width.M);

        Set(0x04, "TSB", AddrMode.DirectPage,  Op.Tsb, Access.ReadModifyWrite, Width.M);
        Set(0x0C, "TSB", AddrMode.Absolute,    Op.Tsb, Access.ReadModifyWrite, Width.M);

        Set(0x14, "TRB", AddrMode.DirectPage,  Op.Trb, Access.ReadModifyWrite, Width.M);
        Set(0x1C, "TRB", AddrMode.Absolute,    Op.Trb, Access.ReadModifyWrite, Width.M);

        // Accumulator forms. AddrMode.Accumulator, Access.None, and no Width: they fetch nothing.
        Set(0x0A, "ASL", AddrMode.Accumulator, Op.AslA, Access.None);
        Set(0x4A, "LSR", AddrMode.Accumulator, Op.LsrA, Access.None);
        Set(0x2A, "ROL", AddrMode.Accumulator, Op.RolA, Access.None);
        Set(0x6A, "ROR", AddrMode.Accumulator, Op.RorA, Access.None);
        Set(0x1A, "INC", AddrMode.Accumulator, Op.IncA, Access.None);
        Set(0x3A, "DEC", AddrMode.Accumulator, Op.DecA, Access.None);

        // Mode switch and status-bit instructions. REP/SEP take AddrMode.ImmediateByte, not
        // AddrMode.Immediate: their operand is always 8 bits and they are flat 3-cycle
        // instructions regardless of m or x (datasheet Note 1, research document §5/§9) —
        // unlike LDA #, whose operand width and cycle count both depend on m at run time.
        Set(0xFB, "XCE", AddrMode.Implied,      Op.Xce, Access.None);
        Set(0xC2, "REP", AddrMode.ImmediateByte, Op.Rep, Access.Read);
        Set(0xE2, "SEP", AddrMode.ImmediateByte, Op.Sep, Access.Read);

        // Transfers and XBA. Implied, no operand, so no Width.
        Set(0xAA, "TAX", AddrMode.Implied, Op.Tax, Access.None);
        Set(0xA8, "TAY", AddrMode.Implied, Op.Tay, Access.None);
        Set(0x8A, "TXA", AddrMode.Implied, Op.Txa, Access.None);
        Set(0x98, "TYA", AddrMode.Implied, Op.Tya, Access.None);
        Set(0x9A, "TXS", AddrMode.Implied, Op.Txs, Access.None);
        Set(0xBA, "TSX", AddrMode.Implied, Op.Tsx, Access.None);
        Set(0x9B, "TXY", AddrMode.Implied, Op.Txy, Access.None);
        Set(0xBB, "TYX", AddrMode.Implied, Op.Tyx, Access.None);
        Set(0x5B, "TCD", AddrMode.Implied, Op.Tcd, Access.None);
        Set(0x7B, "TDC", AddrMode.Implied, Op.Tdc, Access.None);
        Set(0x1B, "TCS", AddrMode.Implied, Op.Tcs, Access.None);
        Set(0x3B, "TSC", AddrMode.Implied, Op.Tsc, Access.None);

        // XBA is implied like the twelve above but 3 cycles rather than 2 — research document
        // §13.5, Table 5-7 row 19b. MicroOpTable.Emit816 gives it its own branch for that.
        Set(0xEB, "XBA", AddrMode.Implied, Op.Xba, Access.None);

        // Flag instructions, the index increments and NOP. All implied, all two cycles. The
        // seven flag opcodes and NOP touch no width-dependent register and need no widening;
        // INX/INY/DEX/DEY are sized by x, the same implied-mode shape as the accumulator forms
        // above.
        Set(0x18, "CLC", AddrMode.Implied, Op.Clc, Access.None);
        Set(0x38, "SEC", AddrMode.Implied, Op.Sec, Access.None);
        Set(0x58, "CLI", AddrMode.Implied, Op.Cli, Access.None);
        Set(0x78, "SEI", AddrMode.Implied, Op.Sei, Access.None);
        Set(0xB8, "CLV", AddrMode.Implied, Op.Clv, Access.None);
        Set(0xD8, "CLD", AddrMode.Implied, Op.Cld, Access.None);
        Set(0xF8, "SED", AddrMode.Implied, Op.Sed, Access.None);

        Set(0xE8, "INX", AddrMode.Implied, Op.Inx, Access.None);
        Set(0xC8, "INY", AddrMode.Implied, Op.Iny, Access.None);
        Set(0xCA, "DEX", AddrMode.Implied, Op.Dex, Access.None);
        Set(0x88, "DEY", AddrMode.Implied, Op.Dey, Access.None);

        Set(0xEA, "NOP", AddrMode.Implied, Op.Nop, Access.None);

        // The two halts. AddrMode.Implied, but MicroOpTable.Emit816 intercepts the operation
        // ahead of the implied branch: both are three cycles rather than two, and both then hold
        // (research document §14.4). Access.None and Width.None — neither touches memory or a
        // width-dependent register.
        Set(0xCB, "WAI", AddrMode.Implied, Op.Wai, Access.None);
        Set(0xDB, "STP", AddrMode.Implied, Op.Stp, Access.None);

        // The stack. All AddrMode.Stack — the mode this codebase uses for hand-written
        // sequences — and all Width.None: they fetch no operand from memory, so each arm
        // tests its own flag. PHP/PHB/PHK/PLB move one byte whatever m and x say; PHD/PLD
        // move two; PHA/PLA are sized by m and PHX/PHY/PLX/PLY by x.
        Set(0x48, "PHA", AddrMode.Stack, Op.Pha, Access.None);
        Set(0x08, "PHP", AddrMode.Stack, Op.Php, Access.None);
        Set(0xDA, "PHX", AddrMode.Stack, Op.Phx, Access.None);
        Set(0x5A, "PHY", AddrMode.Stack, Op.Phy, Access.None);
        Set(0x8B, "PHB", AddrMode.Stack, Op.Phb, Access.None);
        Set(0x0B, "PHD", AddrMode.Stack, Op.Phd, Access.None);
        Set(0x4B, "PHK", AddrMode.Stack, Op.Phk, Access.None);

        Set(0x68, "PLA", AddrMode.Stack, Op.Pla, Access.None);
        Set(0x28, "PLP", AddrMode.Stack, Op.Plp, Access.None);
        Set(0xFA, "PLX", AddrMode.Stack, Op.Plx, Access.None);
        Set(0x7A, "PLY", AddrMode.Stack, Op.Ply, Access.None);
        Set(0xAB, "PLB", AddrMode.Stack, Op.Plb, Access.None);
        Set(0x2B, "PLD", AddrMode.Stack, Op.Pld, Access.None);

        // Interrupts. BRK and COP are two-byte instructions whose second byte is fetched and
        // discarded; WDM is a reserved two-byte no-operation that WDC guarantees will never be
        // given a meaning on this part.
        //
        // WDM takes AddrMode.ImmediateByte, not AddrMode.Implied: it is two bytes long, and
        // ImmediateByte already means exactly "one operand byte, always eight bits" — the mode
        // REP and SEP use. That the byte is never actually READ (research document §14.2/§3.4,
        // measured) is a property of the cycle, not of the operand's existence: PC still steps
        // over it, and a disassembler that called this one byte would decode the next
        // instruction from the middle of this one. Access.None rather than REP/SEP's
        // Access.Read for the same measurement: no bus access happens.
        Set(0x00, "BRK", AddrMode.Stack,         Op.Brk, Access.None);
        Set(0x02, "COP", AddrMode.Stack,         Op.Cop, Access.None);
        Set(0x42, "WDM", AddrMode.ImmediateByte, Op.Wdm, Access.None);

        // Block moves. Two operand bytes, both banks, and one instruction per byte moved:
        // the sequence rewinds PC so the next fetch re-executes it until the count runs out.
        //
        // Access.None and Width.None despite reading and writing memory on every iteration:
        // AddrMode.BlockMove is routed by MicroOpTable.Emit816 to its own six-micro-op sequence
        // before either field is consulted, and the two registers whose width matters here are
        // read at the operative width by the micro-ops themselves (Cpu.IndexX/IndexY) — the same
        // shape AddrMode.Stack's thirteen entries take. The count in A is sixteen bits whatever
        // m says (research document §14.3), so there is no accumulator width to declare either.
        Set(0x54, "MVN", AddrMode.BlockMove, Op.Mvn, Access.None);
        Set(0x44, "MVP", AddrMode.BlockMove, Op.Mvp, Access.None);

        // Branches. Eight conditional, BRA unconditional, and BRL with a sixteen-bit
        // displacement. Width.None throughout: a displacement is not an operand fetched at
        // a width the flags select.
        Set(0x10, "BPL", AddrMode.Relative, Op.Bpl, Access.None);
        Set(0x30, "BMI", AddrMode.Relative, Op.Bmi, Access.None);
        Set(0x50, "BVC", AddrMode.Relative, Op.Bvc, Access.None);
        Set(0x70, "BVS", AddrMode.Relative, Op.Bvs, Access.None);
        Set(0x90, "BCC", AddrMode.Relative, Op.Bcc, Access.None);
        Set(0xB0, "BCS", AddrMode.Relative, Op.Bcs, Access.None);
        Set(0xD0, "BNE", AddrMode.Relative, Op.Bne, Access.None);
        Set(0xF0, "BEQ", AddrMode.Relative, Op.Beq, Access.None);
        Set(0x80, "BRA", AddrMode.Relative, Op.Bra, Access.None);

        Set(0x82, "BRL", AddrMode.RelativeLong, Op.Brl, Access.None);

        return t;
    }
}
