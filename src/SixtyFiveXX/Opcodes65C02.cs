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
/// What is here today is what the CMOS interrupt work needs: <c>BRK</c>, and the flag
/// instructions its tests drive. The rest arrives with the base-CMOS task.
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

        return t;
    }
}
