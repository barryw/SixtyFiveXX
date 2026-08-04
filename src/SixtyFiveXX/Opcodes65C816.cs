namespace SixtyFiveXX;

/// <summary>
/// The WDC 65C816 opcode table — phase 7b's slice of it.
/// </summary>
/// <remarks>
/// Only thirty-two opcodes are defined: every addressing form of <c>LDA</c> and <c>STA</c>
/// (<c>STA</c> has no immediate form), plus <c>XCE</c>, <c>REP</c> and <c>SEP</c>. They were
/// chosen so the variant, its table and its reset semantics can be exercised end to end
/// before any 65816 micro-op sequence exists. The remaining 224 entries are
/// <see cref="OpcodeInfo.Undefined"/> and throw <see cref="UndefinedOpcodeException"/> on
/// fetch; later tasks in this phase fill the rest of the instruction set in.
/// </remarks>
internal static class Opcodes65C816
{
    /// <summary>Opcode byte to descriptor. 32 entries defined, 224 undefined.</summary>
    public static readonly OpcodeInfo[] Table = BuildTable();

    private static OpcodeInfo[] BuildTable()
    {
        var t = new OpcodeInfo[256];
        for (var i = 0; i < t.Length; i++) t[i] = OpcodeInfo.Undefined;

        void Set(int opcode, string mnemonic, AddrMode mode, Op op, Access access) =>
            t[opcode] = new OpcodeInfo(mnemonic, mode, op, access);

        // LDA — every addressing form the 65816 has.
        Set(0xA9, "LDA", AddrMode.Immediate,                  Op.Lda, Access.Read);
        Set(0xA5, "LDA", AddrMode.DirectPage,                 Op.Lda, Access.Read);
        Set(0xB5, "LDA", AddrMode.DirectPageX,                Op.Lda, Access.Read);
        Set(0xAD, "LDA", AddrMode.Absolute,                   Op.Lda, Access.Read);
        Set(0xBD, "LDA", AddrMode.AbsoluteX,                  Op.Lda, Access.Read);
        Set(0xB9, "LDA", AddrMode.AbsoluteY,                  Op.Lda, Access.Read);
        Set(0xA1, "LDA", AddrMode.DirectPageIndexedIndirectX, Op.Lda, Access.Read);
        Set(0xB1, "LDA", AddrMode.DirectPageIndirectY,        Op.Lda, Access.Read);
        Set(0xB2, "LDA", AddrMode.DirectPageIndirect,         Op.Lda, Access.Read);
        Set(0xA7, "LDA", AddrMode.DirectPageIndirectLong,     Op.Lda, Access.Read);
        Set(0xB7, "LDA", AddrMode.DirectPageIndirectLongY,    Op.Lda, Access.Read);
        Set(0xAF, "LDA", AddrMode.AbsoluteLong,               Op.Lda, Access.Read);
        Set(0xBF, "LDA", AddrMode.AbsoluteLongX,              Op.Lda, Access.Read);
        Set(0xA3, "LDA", AddrMode.StackRelative,              Op.Lda, Access.Read);
        Set(0xB3, "LDA", AddrMode.StackRelativeIndirectY,     Op.Lda, Access.Read);

        // STA — the same forms as LDA, minus immediate: there is no such thing as STA #imm.
        Set(0x85, "STA", AddrMode.DirectPage,                 Op.Sta, Access.Write);
        Set(0x95, "STA", AddrMode.DirectPageX,                Op.Sta, Access.Write);
        Set(0x8D, "STA", AddrMode.Absolute,                   Op.Sta, Access.Write);
        Set(0x9D, "STA", AddrMode.AbsoluteX,                  Op.Sta, Access.Write);
        Set(0x99, "STA", AddrMode.AbsoluteY,                  Op.Sta, Access.Write);
        Set(0x81, "STA", AddrMode.DirectPageIndexedIndirectX, Op.Sta, Access.Write);
        Set(0x91, "STA", AddrMode.DirectPageIndirectY,        Op.Sta, Access.Write);
        Set(0x92, "STA", AddrMode.DirectPageIndirect,         Op.Sta, Access.Write);
        Set(0x87, "STA", AddrMode.DirectPageIndirectLong,     Op.Sta, Access.Write);
        Set(0x97, "STA", AddrMode.DirectPageIndirectLongY,    Op.Sta, Access.Write);
        Set(0x8F, "STA", AddrMode.AbsoluteLong,               Op.Sta, Access.Write);
        Set(0x9F, "STA", AddrMode.AbsoluteLongX,              Op.Sta, Access.Write);
        Set(0x83, "STA", AddrMode.StackRelative,              Op.Sta, Access.Write);
        Set(0x93, "STA", AddrMode.StackRelativeIndirectY,     Op.Sta, Access.Write);

        // Mode switch and status-bit instructions. REP/SEP share Immediate's operand shape —
        // Emit816 is what will give their run-time width and cycle count, not the mode here.
        Set(0xFB, "XCE", AddrMode.Implied,   Op.Xce, Access.None);
        Set(0xC2, "REP", AddrMode.Immediate, Op.Rep, Access.Read);
        Set(0xE2, "SEP", AddrMode.Immediate, Op.Sep, Access.Read);

        return t;
    }
}
