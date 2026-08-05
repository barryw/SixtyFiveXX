namespace SixtyFiveXX;

/// <summary>
/// Thrown when the core fetches an opcode the configured variant does not implement.
/// </summary>
/// <remarks>
/// Unreachable for the five 8-bit cores: all 256 entries in each of their tables are defined.
/// Reachable for <c>W65C816Variant</c>, whose opcode table is still being filled in — phase 7b
/// landed <c>LDA</c>/<c>STA</c> across all 15 addressing modes plus <c>XCE</c>, <c>REP</c> and
/// <c>SEP</c>, and phases 7c, 7c′ and 7d add the rest. No count is quoted here deliberately: it
/// moves with every task of those phases, and a number in this comment would be wrong more often
/// than right. <c>Opcodes65C816.Table</c> is the live answer, and
/// <c>Harte816Tests.ExpectedImplementedOpcodes</c> is the one place a count is asserted rather
/// than described. The throw is deliberately loud: silently treating an unknown opcode as a NOP
/// hides real bugs in the code under test.
/// </remarks>
public sealed class UndefinedOpcodeException : Exception
{
    /// <summary>The opcode byte that was fetched.</summary>
    public byte Opcode { get; }

    /// <summary>The address the opcode was fetched from.</summary>
    public ushort Address { get; }

    /// <summary>
    /// The program bank the opcode was fetched from, on the 65816. <c>null</c> for every
    /// other core — see the two-argument constructor, which those cores use because they have
    /// no program bank register and a flat 64 KB address space.
    /// </summary>
    public byte? Bank { get; }

    /// <summary>Creates the exception for a flat 64 KB address space.</summary>
    public UndefinedOpcodeException(byte opcode, ushort address)
        : base($"Undefined opcode ${opcode:X2} at ${address:X4}.")
    {
        Opcode = opcode;
        Address = address;
    }

    /// <summary>
    /// Creates the exception for the 65816, whose 24-bit address space makes
    /// <paramref name="address"/> alone ambiguous between banks — naming
    /// <paramref name="bank"/> too is what tells <c>$00:C000</c> apart from <c>$01:C000</c>.
    /// </summary>
    public UndefinedOpcodeException(byte opcode, ushort address, byte bank)
        : base($"Undefined opcode ${opcode:X2} at ${(bank << 16 | address):X6}.")
    {
        Opcode = opcode;
        Address = address;
        Bank = bank;
    }
}
