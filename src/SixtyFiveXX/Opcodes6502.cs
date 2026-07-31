namespace SixtyFiveXX;

/// <summary>
/// The MOS 6502 opcode table: the 151 documented opcodes, the 27 undocumented NOPs, and
/// the 42 undocumented combination read-modify-write opcodes (SLO, RLA, SRE, RRA, DCP,
/// ISC). Every remaining entry is <see cref="OpcodeInfo.Undefined"/>.
/// </summary>
internal static class Opcodes6502
{
    /// <summary>Opcode byte to descriptor. Always 256 entries.</summary>
    public static readonly OpcodeInfo[] Table = BuildTable();

    private static OpcodeInfo[] BuildTable()
    {
        var t = new OpcodeInfo[256];
        for (var i = 0; i < t.Length; i++) t[i] = OpcodeInfo.Undefined;

        void Set(int opcode, string mnemonic, AddrMode mode, Op op, Access access) =>
            t[opcode] = new OpcodeInfo(mnemonic, mode, op, access);

        // ADC
        Set(0x69, "ADC", AddrMode.Immediate,       Op.Adc, Access.Read);
        Set(0x65, "ADC", AddrMode.ZeroPage,        Op.Adc, Access.Read);
        Set(0x75, "ADC", AddrMode.ZeroPageX,       Op.Adc, Access.Read);
        Set(0x6D, "ADC", AddrMode.Absolute,        Op.Adc, Access.Read);
        Set(0x7D, "ADC", AddrMode.AbsoluteX,       Op.Adc, Access.Read);
        Set(0x79, "ADC", AddrMode.AbsoluteY,       Op.Adc, Access.Read);
        Set(0x61, "ADC", AddrMode.IndexedIndirect, Op.Adc, Access.Read);
        Set(0x71, "ADC", AddrMode.IndirectIndexed, Op.Adc, Access.Read);

        // AND
        Set(0x29, "AND", AddrMode.Immediate,       Op.And, Access.Read);
        Set(0x25, "AND", AddrMode.ZeroPage,        Op.And, Access.Read);
        Set(0x35, "AND", AddrMode.ZeroPageX,       Op.And, Access.Read);
        Set(0x2D, "AND", AddrMode.Absolute,        Op.And, Access.Read);
        Set(0x3D, "AND", AddrMode.AbsoluteX,       Op.And, Access.Read);
        Set(0x39, "AND", AddrMode.AbsoluteY,       Op.And, Access.Read);
        Set(0x21, "AND", AddrMode.IndexedIndirect, Op.And, Access.Read);
        Set(0x31, "AND", AddrMode.IndirectIndexed, Op.And, Access.Read);

        // ASL
        Set(0x0A, "ASL", AddrMode.Accumulator, Op.AslA, Access.None);
        Set(0x06, "ASL", AddrMode.ZeroPage,    Op.Asl,  Access.ReadModifyWrite);
        Set(0x16, "ASL", AddrMode.ZeroPageX,   Op.Asl,  Access.ReadModifyWrite);
        Set(0x0E, "ASL", AddrMode.Absolute,    Op.Asl,  Access.ReadModifyWrite);
        Set(0x1E, "ASL", AddrMode.AbsoluteX,   Op.Asl,  Access.ReadModifyWrite);

        // Branches
        Set(0x90, "BCC", AddrMode.Relative, Op.Bcc, Access.None);
        Set(0xB0, "BCS", AddrMode.Relative, Op.Bcs, Access.None);
        Set(0xF0, "BEQ", AddrMode.Relative, Op.Beq, Access.None);
        Set(0x30, "BMI", AddrMode.Relative, Op.Bmi, Access.None);
        Set(0xD0, "BNE", AddrMode.Relative, Op.Bne, Access.None);
        Set(0x10, "BPL", AddrMode.Relative, Op.Bpl, Access.None);
        Set(0x50, "BVC", AddrMode.Relative, Op.Bvc, Access.None);
        Set(0x70, "BVS", AddrMode.Relative, Op.Bvs, Access.None);

        // BIT
        Set(0x24, "BIT", AddrMode.ZeroPage, Op.Bit, Access.Read);
        Set(0x2C, "BIT", AddrMode.Absolute, Op.Bit, Access.Read);

        // BRK
        Set(0x00, "BRK", AddrMode.Stack, Op.Brk, Access.None);

        // Flag operations
        Set(0x18, "CLC", AddrMode.Implied, Op.Clc, Access.None);
        Set(0xD8, "CLD", AddrMode.Implied, Op.Cld, Access.None);
        Set(0x58, "CLI", AddrMode.Implied, Op.Cli, Access.None);
        Set(0xB8, "CLV", AddrMode.Implied, Op.Clv, Access.None);
        Set(0x38, "SEC", AddrMode.Implied, Op.Sec, Access.None);
        Set(0xF8, "SED", AddrMode.Implied, Op.Sed, Access.None);
        Set(0x78, "SEI", AddrMode.Implied, Op.Sei, Access.None);

        // CMP
        Set(0xC9, "CMP", AddrMode.Immediate,       Op.Cmp, Access.Read);
        Set(0xC5, "CMP", AddrMode.ZeroPage,        Op.Cmp, Access.Read);
        Set(0xD5, "CMP", AddrMode.ZeroPageX,       Op.Cmp, Access.Read);
        Set(0xCD, "CMP", AddrMode.Absolute,        Op.Cmp, Access.Read);
        Set(0xDD, "CMP", AddrMode.AbsoluteX,       Op.Cmp, Access.Read);
        Set(0xD9, "CMP", AddrMode.AbsoluteY,       Op.Cmp, Access.Read);
        Set(0xC1, "CMP", AddrMode.IndexedIndirect, Op.Cmp, Access.Read);
        Set(0xD1, "CMP", AddrMode.IndirectIndexed, Op.Cmp, Access.Read);

        // CPX / CPY
        Set(0xE0, "CPX", AddrMode.Immediate, Op.Cpx, Access.Read);
        Set(0xE4, "CPX", AddrMode.ZeroPage,  Op.Cpx, Access.Read);
        Set(0xEC, "CPX", AddrMode.Absolute,  Op.Cpx, Access.Read);
        Set(0xC0, "CPY", AddrMode.Immediate, Op.Cpy, Access.Read);
        Set(0xC4, "CPY", AddrMode.ZeroPage,  Op.Cpy, Access.Read);
        Set(0xCC, "CPY", AddrMode.Absolute,  Op.Cpy, Access.Read);

        // DEC / INC and the register forms
        Set(0xC6, "DEC", AddrMode.ZeroPage,  Op.Dec, Access.ReadModifyWrite);
        Set(0xD6, "DEC", AddrMode.ZeroPageX, Op.Dec, Access.ReadModifyWrite);
        Set(0xCE, "DEC", AddrMode.Absolute,  Op.Dec, Access.ReadModifyWrite);
        Set(0xDE, "DEC", AddrMode.AbsoluteX, Op.Dec, Access.ReadModifyWrite);
        Set(0xE6, "INC", AddrMode.ZeroPage,  Op.Inc, Access.ReadModifyWrite);
        Set(0xF6, "INC", AddrMode.ZeroPageX, Op.Inc, Access.ReadModifyWrite);
        Set(0xEE, "INC", AddrMode.Absolute,  Op.Inc, Access.ReadModifyWrite);
        Set(0xFE, "INC", AddrMode.AbsoluteX, Op.Inc, Access.ReadModifyWrite);
        Set(0xCA, "DEX", AddrMode.Implied, Op.Dex, Access.None);
        Set(0x88, "DEY", AddrMode.Implied, Op.Dey, Access.None);
        Set(0xE8, "INX", AddrMode.Implied, Op.Inx, Access.None);
        Set(0xC8, "INY", AddrMode.Implied, Op.Iny, Access.None);

        // EOR
        Set(0x49, "EOR", AddrMode.Immediate,       Op.Eor, Access.Read);
        Set(0x45, "EOR", AddrMode.ZeroPage,        Op.Eor, Access.Read);
        Set(0x55, "EOR", AddrMode.ZeroPageX,       Op.Eor, Access.Read);
        Set(0x4D, "EOR", AddrMode.Absolute,        Op.Eor, Access.Read);
        Set(0x5D, "EOR", AddrMode.AbsoluteX,       Op.Eor, Access.Read);
        Set(0x59, "EOR", AddrMode.AbsoluteY,       Op.Eor, Access.Read);
        Set(0x41, "EOR", AddrMode.IndexedIndirect, Op.Eor, Access.Read);
        Set(0x51, "EOR", AddrMode.IndirectIndexed, Op.Eor, Access.Read);

        // JMP / JSR / RTS / RTI
        Set(0x4C, "JMP", AddrMode.Stack,    Op.Jmp, Access.None);
        Set(0x6C, "JMP", AddrMode.Indirect, Op.Jmp, Access.None);
        Set(0x20, "JSR", AddrMode.Stack,    Op.Jsr, Access.None);
        Set(0x60, "RTS", AddrMode.Stack,    Op.Rts, Access.None);
        Set(0x40, "RTI", AddrMode.Stack,    Op.Rti, Access.None);

        // LDA
        Set(0xA9, "LDA", AddrMode.Immediate,       Op.Lda, Access.Read);
        Set(0xA5, "LDA", AddrMode.ZeroPage,        Op.Lda, Access.Read);
        Set(0xB5, "LDA", AddrMode.ZeroPageX,       Op.Lda, Access.Read);
        Set(0xAD, "LDA", AddrMode.Absolute,        Op.Lda, Access.Read);
        Set(0xBD, "LDA", AddrMode.AbsoluteX,       Op.Lda, Access.Read);
        Set(0xB9, "LDA", AddrMode.AbsoluteY,       Op.Lda, Access.Read);
        Set(0xA1, "LDA", AddrMode.IndexedIndirect, Op.Lda, Access.Read);
        Set(0xB1, "LDA", AddrMode.IndirectIndexed, Op.Lda, Access.Read);

        // LDX / LDY
        Set(0xA2, "LDX", AddrMode.Immediate, Op.Ldx, Access.Read);
        Set(0xA6, "LDX", AddrMode.ZeroPage,  Op.Ldx, Access.Read);
        Set(0xB6, "LDX", AddrMode.ZeroPageY, Op.Ldx, Access.Read);
        Set(0xAE, "LDX", AddrMode.Absolute,  Op.Ldx, Access.Read);
        Set(0xBE, "LDX", AddrMode.AbsoluteY, Op.Ldx, Access.Read);
        Set(0xA0, "LDY", AddrMode.Immediate, Op.Ldy, Access.Read);
        Set(0xA4, "LDY", AddrMode.ZeroPage,  Op.Ldy, Access.Read);
        Set(0xB4, "LDY", AddrMode.ZeroPageX, Op.Ldy, Access.Read);
        Set(0xAC, "LDY", AddrMode.Absolute,  Op.Ldy, Access.Read);
        Set(0xBC, "LDY", AddrMode.AbsoluteX, Op.Ldy, Access.Read);

        // LSR
        Set(0x4A, "LSR", AddrMode.Accumulator, Op.LsrA, Access.None);
        Set(0x46, "LSR", AddrMode.ZeroPage,    Op.Lsr,  Access.ReadModifyWrite);
        Set(0x56, "LSR", AddrMode.ZeroPageX,   Op.Lsr,  Access.ReadModifyWrite);
        Set(0x4E, "LSR", AddrMode.Absolute,    Op.Lsr,  Access.ReadModifyWrite);
        Set(0x5E, "LSR", AddrMode.AbsoluteX,   Op.Lsr,  Access.ReadModifyWrite);

        // NOP
        Set(0xEA, "NOP", AddrMode.Implied, Op.Nop, Access.None);

        // ORA
        Set(0x09, "ORA", AddrMode.Immediate,       Op.Ora, Access.Read);
        Set(0x05, "ORA", AddrMode.ZeroPage,        Op.Ora, Access.Read);
        Set(0x15, "ORA", AddrMode.ZeroPageX,       Op.Ora, Access.Read);
        Set(0x0D, "ORA", AddrMode.Absolute,        Op.Ora, Access.Read);
        Set(0x1D, "ORA", AddrMode.AbsoluteX,       Op.Ora, Access.Read);
        Set(0x19, "ORA", AddrMode.AbsoluteY,       Op.Ora, Access.Read);
        Set(0x01, "ORA", AddrMode.IndexedIndirect, Op.Ora, Access.Read);
        Set(0x11, "ORA", AddrMode.IndirectIndexed, Op.Ora, Access.Read);

        // Stack instructions
        Set(0x48, "PHA", AddrMode.Stack, Op.Pha, Access.None);
        Set(0x08, "PHP", AddrMode.Stack, Op.Php, Access.None);
        Set(0x68, "PLA", AddrMode.Stack, Op.Pla, Access.None);
        Set(0x28, "PLP", AddrMode.Stack, Op.Plp, Access.None);

        // ROL / ROR
        Set(0x2A, "ROL", AddrMode.Accumulator, Op.RolA, Access.None);
        Set(0x26, "ROL", AddrMode.ZeroPage,    Op.Rol,  Access.ReadModifyWrite);
        Set(0x36, "ROL", AddrMode.ZeroPageX,   Op.Rol,  Access.ReadModifyWrite);
        Set(0x2E, "ROL", AddrMode.Absolute,    Op.Rol,  Access.ReadModifyWrite);
        Set(0x3E, "ROL", AddrMode.AbsoluteX,   Op.Rol,  Access.ReadModifyWrite);
        Set(0x6A, "ROR", AddrMode.Accumulator, Op.RorA, Access.None);
        Set(0x66, "ROR", AddrMode.ZeroPage,    Op.Ror,  Access.ReadModifyWrite);
        Set(0x76, "ROR", AddrMode.ZeroPageX,   Op.Ror,  Access.ReadModifyWrite);
        Set(0x6E, "ROR", AddrMode.Absolute,    Op.Ror,  Access.ReadModifyWrite);
        Set(0x7E, "ROR", AddrMode.AbsoluteX,   Op.Ror,  Access.ReadModifyWrite);

        // SBC
        Set(0xE9, "SBC", AddrMode.Immediate,       Op.Sbc, Access.Read);
        Set(0xE5, "SBC", AddrMode.ZeroPage,        Op.Sbc, Access.Read);
        Set(0xF5, "SBC", AddrMode.ZeroPageX,       Op.Sbc, Access.Read);
        Set(0xED, "SBC", AddrMode.Absolute,        Op.Sbc, Access.Read);
        Set(0xFD, "SBC", AddrMode.AbsoluteX,       Op.Sbc, Access.Read);
        Set(0xF9, "SBC", AddrMode.AbsoluteY,       Op.Sbc, Access.Read);
        Set(0xE1, "SBC", AddrMode.IndexedIndirect, Op.Sbc, Access.Read);
        Set(0xF1, "SBC", AddrMode.IndirectIndexed, Op.Sbc, Access.Read);

        // STA / STX / STY
        Set(0x85, "STA", AddrMode.ZeroPage,        Op.Sta, Access.Write);
        Set(0x95, "STA", AddrMode.ZeroPageX,       Op.Sta, Access.Write);
        Set(0x8D, "STA", AddrMode.Absolute,        Op.Sta, Access.Write);
        Set(0x9D, "STA", AddrMode.AbsoluteX,       Op.Sta, Access.Write);
        Set(0x99, "STA", AddrMode.AbsoluteY,       Op.Sta, Access.Write);
        Set(0x81, "STA", AddrMode.IndexedIndirect, Op.Sta, Access.Write);
        Set(0x91, "STA", AddrMode.IndirectIndexed, Op.Sta, Access.Write);
        Set(0x86, "STX", AddrMode.ZeroPage,  Op.Stx, Access.Write);
        Set(0x96, "STX", AddrMode.ZeroPageY, Op.Stx, Access.Write);
        Set(0x8E, "STX", AddrMode.Absolute,  Op.Stx, Access.Write);
        Set(0x84, "STY", AddrMode.ZeroPage,  Op.Sty, Access.Write);
        Set(0x94, "STY", AddrMode.ZeroPageX, Op.Sty, Access.Write);
        Set(0x8C, "STY", AddrMode.Absolute,  Op.Sty, Access.Write);

        // Transfers
        Set(0xAA, "TAX", AddrMode.Implied, Op.Tax, Access.None);
        Set(0xA8, "TAY", AddrMode.Implied, Op.Tay, Access.None);
        Set(0xBA, "TSX", AddrMode.Implied, Op.Tsx, Access.None);
        Set(0x8A, "TXA", AddrMode.Implied, Op.Txa, Access.None);
        Set(0x9A, "TXS", AddrMode.Implied, Op.Txs, Access.None);
        Set(0x98, "TYA", AddrMode.Implied, Op.Tya, Access.None);

        // ---- Undocumented: multi-byte NOPs -------------------------------------
        // The implied forms match $EA exactly. The rest read their operand and throw
        // it away, so they cost their addressing mode's cycles — including the
        // page-cross penalty on the absolute,X forms.
        foreach (var op in new[] { 0x1A, 0x3A, 0x5A, 0x7A, 0xDA, 0xFA })
            Set(op, "NOP", AddrMode.Implied, Op.Nop, Access.None);

        foreach (var op in new[] { 0x80, 0x82, 0x89, 0xC2, 0xE2 })
            Set(op, "NOP", AddrMode.Immediate, Op.NopRead, Access.Read);

        foreach (var op in new[] { 0x04, 0x44, 0x64 })
            Set(op, "NOP", AddrMode.ZeroPage, Op.NopRead, Access.Read);

        foreach (var op in new[] { 0x14, 0x34, 0x54, 0x74, 0xD4, 0xF4 })
            Set(op, "NOP", AddrMode.ZeroPageX, Op.NopRead, Access.Read);

        Set(0x0C, "NOP", AddrMode.Absolute, Op.NopRead, Access.Read);

        foreach (var op in new[] { 0x1C, 0x3C, 0x5C, 0x7C, 0xDC, 0xFC })
            Set(op, "NOP", AddrMode.AbsoluteX, Op.NopRead, Access.Read);

        // ---- Undocumented: combination read-modify-writes -----------------------
        // All six share one addressing set. Every indexed form pays the page-cross
        // fixup unconditionally, because read-modify-write always does.
        void SetCombo(string mnemonic, Op operation, int zpX, int zp, int abs,
                      int zpYIndirect, int zeroPageX, int absY, int absX)
        {
            Set(zpX,         mnemonic, AddrMode.IndexedIndirect, operation, Access.ReadModifyWrite);
            Set(zp,          mnemonic, AddrMode.ZeroPage,        operation, Access.ReadModifyWrite);
            Set(abs,         mnemonic, AddrMode.Absolute,        operation, Access.ReadModifyWrite);
            Set(zpYIndirect, mnemonic, AddrMode.IndirectIndexed, operation, Access.ReadModifyWrite);
            Set(zeroPageX,   mnemonic, AddrMode.ZeroPageX,       operation, Access.ReadModifyWrite);
            Set(absY,        mnemonic, AddrMode.AbsoluteY,       operation, Access.ReadModifyWrite);
            Set(absX,        mnemonic, AddrMode.AbsoluteX,       operation, Access.ReadModifyWrite);
        }

        SetCombo("SLO", Op.Slo, 0x03, 0x07, 0x0F, 0x13, 0x17, 0x1B, 0x1F);
        SetCombo("RLA", Op.Rla, 0x23, 0x27, 0x2F, 0x33, 0x37, 0x3B, 0x3F);
        SetCombo("SRE", Op.Sre, 0x43, 0x47, 0x4F, 0x53, 0x57, 0x5B, 0x5F);
        SetCombo("RRA", Op.Rra, 0x63, 0x67, 0x6F, 0x73, 0x77, 0x7B, 0x7F);
        SetCombo("DCP", Op.Dcp, 0xC3, 0xC7, 0xCF, 0xD3, 0xD7, 0xDB, 0xDF);
        SetCombo("ISC", Op.Isc, 0xE3, 0xE7, 0xEF, 0xF3, 0xF7, 0xFB, 0xFF);

        return t;
    }
}
