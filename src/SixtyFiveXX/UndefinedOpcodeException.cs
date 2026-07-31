namespace SixtyFiveXX;

/// <summary>
/// Thrown when the core fetches an opcode the configured variant does not implement.
/// </summary>
/// <remarks>
/// Undocumented NMOS opcodes are not implemented in Phase 1, so every one of them
/// raises this. It is deliberately loud: silently treating an unknown opcode as a NOP
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
