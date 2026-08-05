namespace SixtyFiveXX;

/// <summary>
/// The WDC 65C816 opcode table — phase 7b's slice of it.
/// </summary>
/// <remarks>
/// A hundred and twenty-eight opcodes are defined: every addressing form of <c>LDA</c> and
/// <c>STA</c> (<c>STA</c> has no immediate form), plus <c>XCE</c>, <c>REP</c> and <c>SEP</c> —
/// phase 7b's thirty-two, chosen so the variant, its table and its reset semantics could be
/// exercised end to end before any 65816 micro-op sequence existed — phase 7c task 3's
/// forty-five: <c>ORA</c>, <c>AND</c> and <c>EOR</c> in all fifteen addressing forms each;
/// task 4's twenty-one: <c>CMP</c> in all fifteen, plus <c>CPX</c> and <c>CPY</c> in three each
/// — the first opcodes here sized by <c>x</c> rather than <c>m</c>; and task 5's thirty:
/// <c>ADC</c> and <c>SBC</c> in all fifteen each, the first opcodes here with a decimal mode.
/// Every one of them reuses an addressing sequence phase 7b already certified. The remaining
/// 128 entries are <see cref="OpcodeInfo.Undefined"/> and throw
/// <see cref="UndefinedOpcodeException"/> on fetch; later tasks in this phase fill the rest of
/// the instruction set in.
/// </remarks>
internal static class Opcodes65C816
{
    /// <summary>Opcode byte to descriptor. 128 entries defined, 128 undefined.</summary>
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

        // Mode switch and status-bit instructions. REP/SEP take AddrMode.ImmediateByte, not
        // AddrMode.Immediate: their operand is always 8 bits and they are flat 3-cycle
        // instructions regardless of m or x (datasheet Note 1, research document §5/§9) —
        // unlike LDA #, whose operand width and cycle count both depend on m at run time.
        Set(0xFB, "XCE", AddrMode.Implied,      Op.Xce, Access.None);
        Set(0xC2, "REP", AddrMode.ImmediateByte, Op.Rep, Access.Read);
        Set(0xE2, "SEP", AddrMode.ImmediateByte, Op.Sep, Access.Read);

        return t;
    }
}
