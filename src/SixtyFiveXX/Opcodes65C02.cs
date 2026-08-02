namespace SixtyFiveXX;

/// <summary>
/// The shared 65C02 opcode table — the CMOS baseline every sub-variant starts from.
/// </summary>
/// <remarks>
/// <para>
/// All 256 opcodes are defined. The 65C02 has no JAM opcodes and no undocumented
/// instructions: every opcode the NMOS parts left undocumented became a NOP, though not a
/// uniform one — the seven distinct timing shapes below were measured from the
/// SingleStepTests vectors, not taken from a datasheet.
/// </para>
/// <para>
/// This is the <em>shared</em> table. The Rockwell and WDC sub-variants add their bit
/// operations on top of it, replacing entries that are NOPs here.
/// </para>
/// </remarks>
internal static class Opcodes65C02
{
    /// <summary>Opcode byte to descriptor. Always 256 entries, all defined.</summary>
    public static readonly OpcodeInfo[] Table = BuildTable();

    /// <summary>
    /// The Rockwell 65C02: the shared table plus <c>RMB</c>, <c>SMB</c>, <c>BBR</c> and
    /// <c>BBS</c>, which occupy thirty-two opcodes that are NOPs on Synertek.
    /// </summary>
    /// <remarks>
    /// Built by copying the shared table and overwriting those thirty-two, so the 224 rows
    /// the two sub-variants agree on cannot drift apart. Two independently maintained
    /// tables would be two places for a typo to hide behind a green suite for the other.
    /// </remarks>
    public static readonly OpcodeInfo[] RockwellTable = BuildRockwellTable();

    /// <summary>
    /// The WDC 65C02: Rockwell's table plus <c>WAI</c> and <c>STP</c>, which occupy two
    /// opcodes that are NOPs on the other two sub-variants.
    /// </summary>
    public static readonly OpcodeInfo[] WdcTable = BuildWdcTable();

    private static OpcodeInfo[] BuildWdcTable()
    {
        var t = (OpcodeInfo[])RockwellTable.Clone();

        // Additive on Rockwell, as Rockwell is on the shared table: WDC has the bit
        // operations too, so a third independent table would be a third place to drift.
        t[0xCB] = new OpcodeInfo("WAI", AddrMode.Implied, Op.Wai, Access.None);
        t[0xDB] = new OpcodeInfo("STP", AddrMode.Implied, Op.Stp, Access.None);

        return t;
    }

    private static OpcodeInfo[] BuildRockwellTable()
    {
        var t = (OpcodeInfo[])Table.Clone();

        // The bit index is encoded in bits 4-6 of the opcode, so each group is eight
        // opcodes sixteen apart. RMB/SMB are zero-page read-modify-writes; BBR/BBS read a
        // zero-page byte, test one bit, and branch.
        for (var bit = 0; bit < 8; bit++)
        {
            t[0x07 + bit * 0x10] = new OpcodeInfo($"RMB{bit}", AddrMode.ZeroPage, Op.Rmb, Access.ReadModifyWrite);
            t[0x87 + bit * 0x10] = new OpcodeInfo($"SMB{bit}", AddrMode.ZeroPage, Op.Smb, Access.ReadModifyWrite);
            t[0x0F + bit * 0x10] = new OpcodeInfo($"BBR{bit}", AddrMode.ZeroPageRelative, Op.Bbr, Access.None);
            t[0x8F + bit * 0x10] = new OpcodeInfo($"BBS{bit}", AddrMode.ZeroPageRelative, Op.Bbs, Access.None);
        }

        return t;
    }

    private static OpcodeInfo[] BuildTable()
    {
        var t = new OpcodeInfo[256];
        for (var i = 0; i < t.Length; i++) t[i] = OpcodeInfo.Undefined;

        void Set(int opcode, string mnemonic, AddrMode mode, Op op, Access access) =>
            t[opcode] = new OpcodeInfo(mnemonic, mode, op, access);

        // ADC
        Set(0x61, "ADC", AddrMode.IndexedIndirect,           Op.AdcCmos,   Access.Read);
        Set(0x65, "ADC", AddrMode.ZeroPage,                  Op.AdcCmos,   Access.Read);
        Set(0x69, "ADC", AddrMode.Immediate,                 Op.AdcCmos,   Access.Read);
        Set(0x6D, "ADC", AddrMode.Absolute,                  Op.AdcCmos,   Access.Read);
        Set(0x71, "ADC", AddrMode.IndirectIndexed,           Op.AdcCmos,   Access.Read);
        Set(0x72, "ADC", AddrMode.ZeroPageIndirect,          Op.AdcCmos,   Access.Read);
        Set(0x75, "ADC", AddrMode.ZeroPageX,                 Op.AdcCmos,   Access.Read);
        Set(0x79, "ADC", AddrMode.AbsoluteY,                 Op.AdcCmos,   Access.Read);
        Set(0x7D, "ADC", AddrMode.AbsoluteX,                 Op.AdcCmos,   Access.Read);

        // AND
        Set(0x21, "AND", AddrMode.IndexedIndirect,           Op.And,       Access.Read);
        Set(0x25, "AND", AddrMode.ZeroPage,                  Op.And,       Access.Read);
        Set(0x29, "AND", AddrMode.Immediate,                 Op.And,       Access.Read);
        Set(0x2D, "AND", AddrMode.Absolute,                  Op.And,       Access.Read);
        Set(0x31, "AND", AddrMode.IndirectIndexed,           Op.And,       Access.Read);
        Set(0x32, "AND", AddrMode.ZeroPageIndirect,          Op.And,       Access.Read);
        Set(0x35, "AND", AddrMode.ZeroPageX,                 Op.And,       Access.Read);
        Set(0x39, "AND", AddrMode.AbsoluteY,                 Op.And,       Access.Read);
        Set(0x3D, "AND", AddrMode.AbsoluteX,                 Op.And,       Access.Read);

        // ASL
        Set(0x06, "ASL", AddrMode.ZeroPage,                  Op.Asl,       Access.ReadModifyWrite);
        Set(0x0A, "ASL", AddrMode.Accumulator,               Op.AslA,      Access.None);
        Set(0x0E, "ASL", AddrMode.Absolute,                  Op.Asl,       Access.ReadModifyWrite);
        Set(0x16, "ASL", AddrMode.ZeroPageX,                 Op.Asl,       Access.ReadModifyWrite);
        Set(0x1E, "ASL", AddrMode.AbsoluteX,                 Op.Asl,       Access.ReadModifyWrite);

        // BCC
        Set(0x90, "BCC", AddrMode.Relative,                  Op.Bcc,       Access.None);

        // BCS
        Set(0xB0, "BCS", AddrMode.Relative,                  Op.Bcs,       Access.None);

        // BEQ
        Set(0xF0, "BEQ", AddrMode.Relative,                  Op.Beq,       Access.None);

        // BIT
        Set(0x24, "BIT", AddrMode.ZeroPage,                  Op.Bit,       Access.Read);
        Set(0x2C, "BIT", AddrMode.Absolute,                  Op.Bit,       Access.Read);
        Set(0x34, "BIT", AddrMode.ZeroPageX,                 Op.Bit,       Access.Read);
        Set(0x3C, "BIT", AddrMode.AbsoluteX,                 Op.Bit,       Access.Read);
        Set(0x89, "BIT", AddrMode.Immediate,                 Op.BitImm,    Access.Read);

        // BMI
        Set(0x30, "BMI", AddrMode.Relative,                  Op.Bmi,       Access.None);

        // BNE
        Set(0xD0, "BNE", AddrMode.Relative,                  Op.Bne,       Access.None);

        // BPL
        Set(0x10, "BPL", AddrMode.Relative,                  Op.Bpl,       Access.None);

        // BRA
        Set(0x80, "BRA", AddrMode.Relative,                  Op.Bra,       Access.None);

        // BRK
        Set(0x00, "BRK", AddrMode.Stack,                     Op.Brk,       Access.None);

        // BVC
        Set(0x50, "BVC", AddrMode.Relative,                  Op.Bvc,       Access.None);

        // BVS
        Set(0x70, "BVS", AddrMode.Relative,                  Op.Bvs,       Access.None);

        // CLC
        Set(0x18, "CLC", AddrMode.Implied,                   Op.Clc,       Access.None);

        // CLD
        Set(0xD8, "CLD", AddrMode.Implied,                   Op.Cld,       Access.None);

        // CLI
        Set(0x58, "CLI", AddrMode.Implied,                   Op.Cli,       Access.None);

        // CLV
        Set(0xB8, "CLV", AddrMode.Implied,                   Op.Clv,       Access.None);

        // CMP
        Set(0xC1, "CMP", AddrMode.IndexedIndirect,           Op.Cmp,       Access.Read);
        Set(0xC5, "CMP", AddrMode.ZeroPage,                  Op.Cmp,       Access.Read);
        Set(0xC9, "CMP", AddrMode.Immediate,                 Op.Cmp,       Access.Read);
        Set(0xCD, "CMP", AddrMode.Absolute,                  Op.Cmp,       Access.Read);
        Set(0xD1, "CMP", AddrMode.IndirectIndexed,           Op.Cmp,       Access.Read);
        Set(0xD2, "CMP", AddrMode.ZeroPageIndirect,          Op.Cmp,       Access.Read);
        Set(0xD5, "CMP", AddrMode.ZeroPageX,                 Op.Cmp,       Access.Read);
        Set(0xD9, "CMP", AddrMode.AbsoluteY,                 Op.Cmp,       Access.Read);
        Set(0xDD, "CMP", AddrMode.AbsoluteX,                 Op.Cmp,       Access.Read);

        // CPX
        Set(0xE0, "CPX", AddrMode.Immediate,                 Op.Cpx,       Access.Read);
        Set(0xE4, "CPX", AddrMode.ZeroPage,                  Op.Cpx,       Access.Read);
        Set(0xEC, "CPX", AddrMode.Absolute,                  Op.Cpx,       Access.Read);

        // CPY
        Set(0xC0, "CPY", AddrMode.Immediate,                 Op.Cpy,       Access.Read);
        Set(0xC4, "CPY", AddrMode.ZeroPage,                  Op.Cpy,       Access.Read);
        Set(0xCC, "CPY", AddrMode.Absolute,                  Op.Cpy,       Access.Read);

        // DEC
        Set(0x3A, "DEC", AddrMode.Accumulator,               Op.DecA,      Access.None);
        Set(0xC6, "DEC", AddrMode.ZeroPage,                  Op.Dec,       Access.ReadModifyWrite);
        Set(0xCE, "DEC", AddrMode.Absolute,                  Op.Dec,       Access.ReadModifyWrite);
        Set(0xD6, "DEC", AddrMode.ZeroPageX,                 Op.Dec,       Access.ReadModifyWrite);
        Set(0xDE, "DEC", AddrMode.AbsoluteX,                 Op.Dec,       Access.ReadModifyWrite);

        // DEX
        Set(0xCA, "DEX", AddrMode.Implied,                   Op.Dex,       Access.None);

        // DEY
        Set(0x88, "DEY", AddrMode.Implied,                   Op.Dey,       Access.None);

        // EOR
        Set(0x41, "EOR", AddrMode.IndexedIndirect,           Op.Eor,       Access.Read);
        Set(0x45, "EOR", AddrMode.ZeroPage,                  Op.Eor,       Access.Read);
        Set(0x49, "EOR", AddrMode.Immediate,                 Op.Eor,       Access.Read);
        Set(0x4D, "EOR", AddrMode.Absolute,                  Op.Eor,       Access.Read);
        Set(0x51, "EOR", AddrMode.IndirectIndexed,           Op.Eor,       Access.Read);
        Set(0x52, "EOR", AddrMode.ZeroPageIndirect,          Op.Eor,       Access.Read);
        Set(0x55, "EOR", AddrMode.ZeroPageX,                 Op.Eor,       Access.Read);
        Set(0x59, "EOR", AddrMode.AbsoluteY,                 Op.Eor,       Access.Read);
        Set(0x5D, "EOR", AddrMode.AbsoluteX,                 Op.Eor,       Access.Read);

        // INC
        Set(0x1A, "INC", AddrMode.Accumulator,               Op.IncA,      Access.None);
        Set(0xE6, "INC", AddrMode.ZeroPage,                  Op.Inc,       Access.ReadModifyWrite);
        Set(0xEE, "INC", AddrMode.Absolute,                  Op.Inc,       Access.ReadModifyWrite);
        Set(0xF6, "INC", AddrMode.ZeroPageX,                 Op.Inc,       Access.ReadModifyWrite);
        Set(0xFE, "INC", AddrMode.AbsoluteX,                 Op.Inc,       Access.ReadModifyWrite);

        // INX
        Set(0xE8, "INX", AddrMode.Implied,                   Op.Inx,       Access.None);

        // INY
        Set(0xC8, "INY", AddrMode.Implied,                   Op.Iny,       Access.None);

        // JMP
        Set(0x4C, "JMP", AddrMode.Stack,                     Op.Jmp,       Access.None);
        Set(0x6C, "JMP", AddrMode.IndirectFixed,             Op.Jmp,       Access.None);
        Set(0x7C, "JMP", AddrMode.AbsoluteIndexedIndirect,   Op.Jmp,       Access.None);

        // JSR
        Set(0x20, "JSR", AddrMode.Stack,                     Op.Jsr,       Access.None);

        // LDA
        Set(0xA1, "LDA", AddrMode.IndexedIndirect,           Op.Lda,       Access.Read);
        Set(0xA5, "LDA", AddrMode.ZeroPage,                  Op.Lda,       Access.Read);
        Set(0xA9, "LDA", AddrMode.Immediate,                 Op.Lda,       Access.Read);
        Set(0xAD, "LDA", AddrMode.Absolute,                  Op.Lda,       Access.Read);
        Set(0xB1, "LDA", AddrMode.IndirectIndexed,           Op.Lda,       Access.Read);
        Set(0xB2, "LDA", AddrMode.ZeroPageIndirect,          Op.Lda,       Access.Read);
        Set(0xB5, "LDA", AddrMode.ZeroPageX,                 Op.Lda,       Access.Read);
        Set(0xB9, "LDA", AddrMode.AbsoluteY,                 Op.Lda,       Access.Read);
        Set(0xBD, "LDA", AddrMode.AbsoluteX,                 Op.Lda,       Access.Read);

        // LDX
        Set(0xA2, "LDX", AddrMode.Immediate,                 Op.Ldx,       Access.Read);
        Set(0xA6, "LDX", AddrMode.ZeroPage,                  Op.Ldx,       Access.Read);
        Set(0xAE, "LDX", AddrMode.Absolute,                  Op.Ldx,       Access.Read);
        Set(0xB6, "LDX", AddrMode.ZeroPageY,                 Op.Ldx,       Access.Read);
        Set(0xBE, "LDX", AddrMode.AbsoluteY,                 Op.Ldx,       Access.Read);

        // LDY
        Set(0xA0, "LDY", AddrMode.Immediate,                 Op.Ldy,       Access.Read);
        Set(0xA4, "LDY", AddrMode.ZeroPage,                  Op.Ldy,       Access.Read);
        Set(0xAC, "LDY", AddrMode.Absolute,                  Op.Ldy,       Access.Read);
        Set(0xB4, "LDY", AddrMode.ZeroPageX,                 Op.Ldy,       Access.Read);
        Set(0xBC, "LDY", AddrMode.AbsoluteX,                 Op.Ldy,       Access.Read);

        // LSR
        Set(0x46, "LSR", AddrMode.ZeroPage,                  Op.Lsr,       Access.ReadModifyWrite);
        Set(0x4A, "LSR", AddrMode.Accumulator,               Op.LsrA,      Access.None);
        Set(0x4E, "LSR", AddrMode.Absolute,                  Op.Lsr,       Access.ReadModifyWrite);
        Set(0x56, "LSR", AddrMode.ZeroPageX,                 Op.Lsr,       Access.ReadModifyWrite);
        Set(0x5E, "LSR", AddrMode.AbsoluteX,                 Op.Lsr,       Access.ReadModifyWrite);

        // NOP
        Set(0xEA, "NOP", AddrMode.Implied,                   Op.Nop,       Access.None);

        // ORA
        Set(0x01, "ORA", AddrMode.IndexedIndirect,           Op.Ora,       Access.Read);
        Set(0x05, "ORA", AddrMode.ZeroPage,                  Op.Ora,       Access.Read);
        Set(0x09, "ORA", AddrMode.Immediate,                 Op.Ora,       Access.Read);
        Set(0x0D, "ORA", AddrMode.Absolute,                  Op.Ora,       Access.Read);
        Set(0x11, "ORA", AddrMode.IndirectIndexed,           Op.Ora,       Access.Read);
        Set(0x12, "ORA", AddrMode.ZeroPageIndirect,          Op.Ora,       Access.Read);
        Set(0x15, "ORA", AddrMode.ZeroPageX,                 Op.Ora,       Access.Read);
        Set(0x19, "ORA", AddrMode.AbsoluteY,                 Op.Ora,       Access.Read);
        Set(0x1D, "ORA", AddrMode.AbsoluteX,                 Op.Ora,       Access.Read);

        // PHA
        Set(0x48, "PHA", AddrMode.Stack,                     Op.Pha,       Access.None);

        // PHP
        Set(0x08, "PHP", AddrMode.Stack,                     Op.Php,       Access.None);

        // PHX
        Set(0xDA, "PHX", AddrMode.Stack,                     Op.Phx,       Access.None);

        // PHY
        Set(0x5A, "PHY", AddrMode.Stack,                     Op.Phy,       Access.None);

        // PLA
        Set(0x68, "PLA", AddrMode.Stack,                     Op.Pla,       Access.None);

        // PLP
        Set(0x28, "PLP", AddrMode.Stack,                     Op.Plp,       Access.None);

        // PLX
        Set(0xFA, "PLX", AddrMode.Stack,                     Op.Plx,       Access.None);

        // PLY
        Set(0x7A, "PLY", AddrMode.Stack,                     Op.Ply,       Access.None);

        // ROL
        Set(0x26, "ROL", AddrMode.ZeroPage,                  Op.Rol,       Access.ReadModifyWrite);
        Set(0x2A, "ROL", AddrMode.Accumulator,               Op.RolA,      Access.None);
        Set(0x2E, "ROL", AddrMode.Absolute,                  Op.Rol,       Access.ReadModifyWrite);
        Set(0x36, "ROL", AddrMode.ZeroPageX,                 Op.Rol,       Access.ReadModifyWrite);
        Set(0x3E, "ROL", AddrMode.AbsoluteX,                 Op.Rol,       Access.ReadModifyWrite);

        // ROR
        Set(0x66, "ROR", AddrMode.ZeroPage,                  Op.Ror,       Access.ReadModifyWrite);
        Set(0x6A, "ROR", AddrMode.Accumulator,               Op.RorA,      Access.None);
        Set(0x6E, "ROR", AddrMode.Absolute,                  Op.Ror,       Access.ReadModifyWrite);
        Set(0x76, "ROR", AddrMode.ZeroPageX,                 Op.Ror,       Access.ReadModifyWrite);
        Set(0x7E, "ROR", AddrMode.AbsoluteX,                 Op.Ror,       Access.ReadModifyWrite);

        // RTI
        Set(0x40, "RTI", AddrMode.Stack,                     Op.Rti,       Access.None);

        // RTS
        Set(0x60, "RTS", AddrMode.Stack,                     Op.Rts,       Access.None);

        // SBC
        Set(0xE1, "SBC", AddrMode.IndexedIndirect,           Op.SbcCmos,   Access.Read);
        Set(0xE5, "SBC", AddrMode.ZeroPage,                  Op.SbcCmos,   Access.Read);
        Set(0xE9, "SBC", AddrMode.Immediate,                 Op.SbcCmos,   Access.Read);
        Set(0xED, "SBC", AddrMode.Absolute,                  Op.SbcCmos,   Access.Read);
        Set(0xF1, "SBC", AddrMode.IndirectIndexed,           Op.SbcCmos,   Access.Read);
        Set(0xF2, "SBC", AddrMode.ZeroPageIndirect,          Op.SbcCmos,   Access.Read);
        Set(0xF5, "SBC", AddrMode.ZeroPageX,                 Op.SbcCmos,   Access.Read);
        Set(0xF9, "SBC", AddrMode.AbsoluteY,                 Op.SbcCmos,   Access.Read);
        Set(0xFD, "SBC", AddrMode.AbsoluteX,                 Op.SbcCmos,   Access.Read);

        // SEC
        Set(0x38, "SEC", AddrMode.Implied,                   Op.Sec,       Access.None);

        // SED
        Set(0xF8, "SED", AddrMode.Implied,                   Op.Sed,       Access.None);

        // SEI
        Set(0x78, "SEI", AddrMode.Implied,                   Op.Sei,       Access.None);

        // STA
        Set(0x81, "STA", AddrMode.IndexedIndirect,           Op.Sta,       Access.Write);
        Set(0x85, "STA", AddrMode.ZeroPage,                  Op.Sta,       Access.Write);
        Set(0x8D, "STA", AddrMode.Absolute,                  Op.Sta,       Access.Write);
        Set(0x91, "STA", AddrMode.IndirectIndexed,           Op.Sta,       Access.Write);
        Set(0x92, "STA", AddrMode.ZeroPageIndirect,          Op.Sta,       Access.Write);
        Set(0x95, "STA", AddrMode.ZeroPageX,                 Op.Sta,       Access.Write);
        Set(0x99, "STA", AddrMode.AbsoluteY,                 Op.Sta,       Access.Write);
        Set(0x9D, "STA", AddrMode.AbsoluteX,                 Op.Sta,       Access.Write);

        // STX
        Set(0x86, "STX", AddrMode.ZeroPage,                  Op.Stx,       Access.Write);
        Set(0x8E, "STX", AddrMode.Absolute,                  Op.Stx,       Access.Write);
        Set(0x96, "STX", AddrMode.ZeroPageY,                 Op.Stx,       Access.Write);

        // STY
        Set(0x84, "STY", AddrMode.ZeroPage,                  Op.Sty,       Access.Write);
        Set(0x8C, "STY", AddrMode.Absolute,                  Op.Sty,       Access.Write);
        Set(0x94, "STY", AddrMode.ZeroPageX,                 Op.Sty,       Access.Write);

        // STZ
        Set(0x64, "STZ", AddrMode.ZeroPage,                  Op.Stz,       Access.Write);
        Set(0x74, "STZ", AddrMode.ZeroPageX,                 Op.Stz,       Access.Write);
        Set(0x9C, "STZ", AddrMode.Absolute,                  Op.Stz,       Access.Write);
        Set(0x9E, "STZ", AddrMode.AbsoluteX,                 Op.Stz,       Access.Write);

        // TAX
        Set(0xAA, "TAX", AddrMode.Implied,                   Op.Tax,       Access.None);

        // TAY
        Set(0xA8, "TAY", AddrMode.Implied,                   Op.Tay,       Access.None);

        // TRB
        Set(0x14, "TRB", AddrMode.ZeroPage,                  Op.Trb,       Access.ReadModifyWrite);
        Set(0x1C, "TRB", AddrMode.Absolute,                  Op.Trb,       Access.ReadModifyWrite);

        // TSB
        Set(0x04, "TSB", AddrMode.ZeroPage,                  Op.Tsb,       Access.ReadModifyWrite);
        Set(0x0C, "TSB", AddrMode.Absolute,                  Op.Tsb,       Access.ReadModifyWrite);

        // TSX
        Set(0xBA, "TSX", AddrMode.Implied,                   Op.Tsx,       Access.None);

        // TXA
        Set(0x8A, "TXA", AddrMode.Implied,                   Op.Txa,       Access.None);

        // TXS
        Set(0x9A, "TXS", AddrMode.Implied,                   Op.Txs,       Access.None);

        // TYA
        Set(0x98, "TYA", AddrMode.Implied,                   Op.Tya,       Access.None);

        // Every opcode the NMOS parts left undocumented is a NOP here — but not a
        // uniform one. These seven shapes were measured from the vectors and cover all
        // 76 of them; a blanket one-cycle NOP fails most of the set.

        // 1 byte, 1 cycle
        Set(0x03, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x0B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x13, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x1B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x23, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x2B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x33, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x3B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x43, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x4B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x53, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x5B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x63, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x6B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x73, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x7B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x83, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x8B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x93, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0x9B, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xA3, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xAB, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xB3, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xBB, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xC3, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xD3, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xE3, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xEB, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xF3, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);
        Set(0xFB, "NOP", AddrMode.NopSingleCycle,            Op.Nop,       Access.None);

        // 1 byte, 2 cycles
        Set(0xCB, "NOP", AddrMode.Implied,                   Op.Nop,       Access.None);

        // 2 bytes, 2 cycles
        Set(0x02, "NOP", AddrMode.Immediate,                 Op.Nop,       Access.Read);
        Set(0x22, "NOP", AddrMode.Immediate,                 Op.Nop,       Access.Read);
        Set(0x42, "NOP", AddrMode.Immediate,                 Op.Nop,       Access.Read);
        Set(0x62, "NOP", AddrMode.Immediate,                 Op.Nop,       Access.Read);
        Set(0x82, "NOP", AddrMode.Immediate,                 Op.Nop,       Access.Read);
        Set(0xC2, "NOP", AddrMode.Immediate,                 Op.Nop,       Access.Read);
        Set(0xE2, "NOP", AddrMode.Immediate,                 Op.Nop,       Access.Read);

        // 2 bytes, 3 cycles, reads the operand
        Set(0x07, "NOP", AddrMode.ZeroPage,                  Op.NopRead,   Access.Read);
        Set(0x27, "NOP", AddrMode.ZeroPage,                  Op.NopRead,   Access.Read);
        Set(0x44, "NOP", AddrMode.ZeroPage,                  Op.NopRead,   Access.Read);
        Set(0x47, "NOP", AddrMode.ZeroPage,                  Op.NopRead,   Access.Read);
        Set(0x67, "NOP", AddrMode.ZeroPage,                  Op.NopRead,   Access.Read);
        Set(0x87, "NOP", AddrMode.ZeroPage,                  Op.NopRead,   Access.Read);
        Set(0xA7, "NOP", AddrMode.ZeroPage,                  Op.NopRead,   Access.Read);
        Set(0xC7, "NOP", AddrMode.ZeroPage,                  Op.NopRead,   Access.Read);
        Set(0xE7, "NOP", AddrMode.ZeroPage,                  Op.NopRead,   Access.Read);

        // 2 bytes, 4 cycles, reads the operand
        Set(0x17, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0x37, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0x54, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0x57, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0x77, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0x97, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0xB7, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0xD4, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0xD7, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0xDB, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0xF4, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);
        Set(0xF7, "NOP", AddrMode.ZeroPageX,                 Op.NopRead,   Access.Read);

        // 3 bytes, 3 cycles
        Set(0x0F, "NOP", AddrMode.NopAbsolute,               Op.Nop,       Access.None);
        Set(0x2F, "NOP", AddrMode.NopAbsolute,               Op.Nop,       Access.None);
        Set(0x4F, "NOP", AddrMode.NopAbsolute,               Op.Nop,       Access.None);
        Set(0x6F, "NOP", AddrMode.NopAbsolute,               Op.Nop,       Access.None);
        Set(0x8F, "NOP", AddrMode.NopAbsolute,               Op.Nop,       Access.None);
        Set(0xAF, "NOP", AddrMode.NopAbsolute,               Op.Nop,       Access.None);
        Set(0xCF, "NOP", AddrMode.NopAbsolute,               Op.Nop,       Access.None);
        Set(0xEF, "NOP", AddrMode.NopAbsolute,               Op.Nop,       Access.None);

        // 3 bytes, 4 cycles
        Set(0x1F, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0x3F, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0x5C, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0x5F, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0x7F, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0x9F, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0xBF, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0xDC, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0xDF, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0xFC, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);
        Set(0xFF, "NOP", AddrMode.NopAbsoluteExtra,          Op.Nop,       Access.None);

        return t;
    }
}
