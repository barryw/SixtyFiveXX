namespace SixtyFiveXX;

/// <summary>
/// The shared 65C02 opcode table — the CMOS baseline every sub-variant starts from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This table is incomplete.</strong> Phase 4 builds it across several tasks; every
/// opcode not yet filled in is <see cref="OpcodeInfo.Undefined"/> and throws
/// <see cref="UndefinedOpcodeException"/> if executed. That is the honest state, not a
/// placeholder: an undefined entry is exactly what the exception type exists for, and
/// <c>Coverage_IsReportedHonestly</c> reports the real count so a green suite is never
/// mistaken for a certified core. The Harte gate for this variant does not run until the
/// table is complete.
/// </para>
/// <para>
/// What is here today is one opcode for every CMOS addressing mode and operation, so each
/// is exercised by a unit test as it is added rather than first meeting a vector in a
/// 2,560,000-case run. What is missing is the bulk: the instructions CMOS inherits
/// unchanged from NMOS, and the undocumented opcodes that become NOPs with per-opcode
/// timings. Those arrive with the base-CMOS task.
/// </para>
/// </remarks>
internal static class Opcodes65C02
{
    /// <summary>Opcode byte to descriptor. Always 256 entries; not all are defined yet.</summary>
    public static readonly OpcodeInfo[] Table = BuildTable();

    private static OpcodeInfo[] BuildTable()
    {
        var t = new OpcodeInfo[256];
        for (var i = 0; i < t.Length; i++) t[i] = OpcodeInfo.Undefined;

        void Set(int opcode, string mnemonic, AddrMode mode, Op op, Access access) =>
            t[opcode] = new OpcodeInfo(mnemonic, mode, op, access);

        Set(0x00, "BRK", AddrMode.Stack, Op.Brk, Access.None);
        Set(0x40, "RTI", AddrMode.Stack, Op.Rti, Access.None);

        // Flag instructions. Unchanged from NMOS, and the ones the interrupt tests drive.
        Set(0x18, "CLC", AddrMode.Implied, Op.Clc, Access.None);
        Set(0x38, "SEC", AddrMode.Implied, Op.Sec, Access.None);
        Set(0x58, "CLI", AddrMode.Implied, Op.Cli, Access.None);
        Set(0x78, "SEI", AddrMode.Implied, Op.Sei, Access.None);
        Set(0xB8, "CLV", AddrMode.Implied, Op.Clv, Access.None);
        Set(0xD8, "CLD", AddrMode.Implied, Op.Cld, Access.None);
        Set(0xF8, "SED", AddrMode.Implied, Op.Sed, Access.None);

        Set(0xEA, "NOP", AddrMode.Implied, Op.Nop, Access.None);

        // The CMOS-only instructions and addressing modes. Enough of the table to exercise
        // every new AddrMode and Op; the rest of the 256 arrives with the base-CMOS task.

        Set(0x64, "STZ", AddrMode.ZeroPage,  Op.Stz, Access.Write);
        Set(0x74, "STZ", AddrMode.ZeroPageX, Op.Stz, Access.Write);
        Set(0x9C, "STZ", AddrMode.Absolute,  Op.Stz, Access.Write);
        Set(0x9E, "STZ", AddrMode.AbsoluteX, Op.Stz, Access.Write);

        Set(0x04, "TSB", AddrMode.ZeroPage, Op.Tsb, Access.ReadModifyWrite);
        Set(0x0C, "TSB", AddrMode.Absolute, Op.Tsb, Access.ReadModifyWrite);
        Set(0x14, "TRB", AddrMode.ZeroPage, Op.Trb, Access.ReadModifyWrite);
        Set(0x1C, "TRB", AddrMode.Absolute, Op.Trb, Access.ReadModifyWrite);

        Set(0x80, "BRA", AddrMode.Relative, Op.Bra, Access.None);

        Set(0xDA, "PHX", AddrMode.Stack, Op.Phx, Access.None);
        Set(0x5A, "PHY", AddrMode.Stack, Op.Phy, Access.None);
        Set(0xFA, "PLX", AddrMode.Stack, Op.Plx, Access.None);
        Set(0x7A, "PLY", AddrMode.Stack, Op.Ply, Access.None);

        Set(0x1A, "INC", AddrMode.Accumulator, Op.IncA, Access.None);
        Set(0x3A, "DEC", AddrMode.Accumulator, Op.DecA, Access.None);

        // JMP (abs) loses the page-wrap bug and gains a cycle; JMP (abs,X) is new.
        Set(0x4C, "JMP", AddrMode.Stack,                   Op.Jmp, Access.None);
        Set(0x6C, "JMP", AddrMode.IndirectFixed,           Op.Jmp, Access.None);
        Set(0x7C, "JMP", AddrMode.AbsoluteIndexedIndirect, Op.Jmp, Access.None);

        // (zp) — every ALU instruction that has a (zp,X) form gains one.
        Set(0x12, "ORA", AddrMode.ZeroPageIndirect, Op.Ora, Access.Read);
        Set(0x32, "AND", AddrMode.ZeroPageIndirect, Op.And, Access.Read);
        Set(0x52, "EOR", AddrMode.ZeroPageIndirect, Op.Eor, Access.Read);
        Set(0x72, "ADC", AddrMode.ZeroPageIndirect, Op.Adc, Access.Read);
        Set(0x92, "STA", AddrMode.ZeroPageIndirect, Op.Sta, Access.Write);
        Set(0xB2, "LDA", AddrMode.ZeroPageIndirect, Op.Lda, Access.Read);
        Set(0xD2, "CMP", AddrMode.ZeroPageIndirect, Op.Cmp, Access.Read);
        Set(0xF2, "SBC", AddrMode.ZeroPageIndirect, Op.Sbc, Access.Read);

        // Indexed forms, for the CMOS indexing deltas: the page-cross dummy read moves to
        // the last operand byte, and the shift/rotate abs,X forms pay their fixup only on
        // an actual cross while INC/DEC abs,X always pay.
        Set(0x1D, "ORA", AddrMode.AbsoluteX, Op.Ora, Access.Read);
        Set(0xB1, "LDA", AddrMode.IndirectIndexed, Op.Lda, Access.Read);
        Set(0x9D, "STA", AddrMode.AbsoluteX, Op.Sta, Access.Write);
        Set(0x1E, "ASL", AddrMode.AbsoluteX, Op.Asl, Access.ReadModifyWrite);
        Set(0xFE, "INC", AddrMode.AbsoluteX, Op.Inc, Access.ReadModifyWrite);
        Set(0x06, "ASL", AddrMode.ZeroPage,  Op.Asl, Access.ReadModifyWrite);

        // BIT gains an immediate form and two indexed ones.
        Set(0x89, "BIT", AddrMode.Immediate, Op.BitImm, Access.Read);
        Set(0x34, "BIT", AddrMode.ZeroPageX, Op.Bit, Access.Read);
        Set(0x3C, "BIT", AddrMode.AbsoluteX, Op.Bit, Access.Read);

        return t;
    }
}
