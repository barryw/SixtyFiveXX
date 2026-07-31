namespace SixtyFiveXX;

/// <summary>Bit masks for the processor status register.</summary>
public static class Flag
{
    /// <summary>Carry.</summary>
    public const byte C = 0x01;

    /// <summary>Zero.</summary>
    public const byte Z = 0x02;

    /// <summary>Interrupt disable.</summary>
    public const byte I = 0x04;

    /// <summary>Decimal mode.</summary>
    public const byte D = 0x08;

    /// <summary>Break. Exists only in the copy of P pushed to the stack.</summary>
    public const byte B = 0x10;

    /// <summary>Unused. Reads as set on NMOS parts.</summary>
    public const byte U = 0x20;

    /// <summary>Overflow.</summary>
    public const byte V = 0x40;

    /// <summary>Negative.</summary>
    public const byte N = 0x80;
}

/// <summary>The architectural register file of an 8-bit 65xx core.</summary>
public struct CpuState
{
    /// <summary>Program counter.</summary>
    public ushort PC;

    /// <summary>Accumulator.</summary>
    public byte A;

    /// <summary>X index register.</summary>
    public byte X;

    /// <summary>Y index register.</summary>
    public byte Y;

    /// <summary>Stack pointer. The stack lives at $0100 + S.</summary>
    public byte S;

    /// <summary>Processor status register. See <see cref="Flag"/>.</summary>
    public byte P;

    /// <summary>Carry flag.</summary>
    public bool C
    {
        readonly get => (P & Flag.C) != 0;
        set => P = value ? (byte)(P | Flag.C) : (byte)(P & ~Flag.C);
    }

    /// <summary>Zero flag.</summary>
    public bool Z
    {
        readonly get => (P & Flag.Z) != 0;
        set => P = value ? (byte)(P | Flag.Z) : (byte)(P & ~Flag.Z);
    }

    /// <summary>Interrupt disable flag.</summary>
    public bool I
    {
        readonly get => (P & Flag.I) != 0;
        set => P = value ? (byte)(P | Flag.I) : (byte)(P & ~Flag.I);
    }

    /// <summary>Decimal mode flag.</summary>
    public bool D
    {
        readonly get => (P & Flag.D) != 0;
        set => P = value ? (byte)(P | Flag.D) : (byte)(P & ~Flag.D);
    }

    /// <summary>Overflow flag.</summary>
    public bool V
    {
        readonly get => (P & Flag.V) != 0;
        set => P = value ? (byte)(P | Flag.V) : (byte)(P & ~Flag.V);
    }

    /// <summary>Negative flag.</summary>
    public bool N
    {
        readonly get => (P & Flag.N) != 0;
        set => P = value ? (byte)(P | Flag.N) : (byte)(P & ~Flag.N);
    }

    /// <inheritdoc />
    public override readonly string ToString() =>
        $"PC:{PC:X4} A:{A:X2} X:{X:X2} Y:{Y:X2} S:{S:X2} P:{P:X2}";
}
