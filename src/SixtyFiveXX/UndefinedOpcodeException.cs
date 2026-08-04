namespace SixtyFiveXX;

/// <summary>
/// Thrown when the core fetches an opcode the configured variant does not implement.
/// </summary>
/// <remarks>
/// This throw is currently unreachable: all 256 entries in <see cref="Opcodes6502.Table"/>
/// are defined, and <c>Mos6502Variant</c> — the only variant in use today — resolves to
/// that same table. It is retained for the Phase 4 variant tables, where a 65C02 or other
/// variant may legitimately leave opcodes undefined. It is deliberately loud: silently
/// treating an unknown opcode as a NOP hides real bugs in the code under test.
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
