namespace SixtyFiveXX;

/// <summary>
/// Thrown when the core fetches an opcode the configured variant does not implement.
/// </summary>
/// <remarks>
/// This throw is currently unreachable: all 256 entries in <see cref="Opcodes6502.Table"/>
/// are defined, and <see cref="Cpu{TBus}"/>'s only constructor hardcodes
/// <c>MicroOpTable.Mos6502</c>, whose constructor is private. It is retained for the
/// Phase 4 variant tables, where a 65C02 or other variant may legitimately leave opcodes
/// undefined. It is deliberately loud: silently treating an unknown opcode as a NOP
/// hides real bugs in the code under test.
/// </remarks>
public sealed class UndefinedOpcodeException : Exception
{
    /// <summary>The opcode byte that was fetched.</summary>
    public byte Opcode { get; }

    /// <summary>The address the opcode was fetched from.</summary>
    public ushort Address { get; }

    /// <summary>Creates the exception.</summary>
    public UndefinedOpcodeException(byte opcode, ushort address)
        : base($"Undefined opcode ${opcode:X2} at ${address:X4}.")
    {
        Opcode = opcode;
        Address = address;
    }
}
