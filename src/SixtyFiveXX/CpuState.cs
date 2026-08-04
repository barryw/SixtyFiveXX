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

    /// <summary>
    /// Index register width select, 65816 native mode. 1 selects 8-bit index registers.
    /// <para>
    /// Deliberately the same bit as <see cref="B"/>. Bit 4 is the break flag in emulation
    /// mode and the index-width select in native mode — one bit, two meanings, which is what
    /// the silicon does. Confirmed by Clark §4 and by the WDC datasheet §2.8 ("the Break flag
    /// is written to stack memory as bit 4"). Eyes &amp; Lichty p. 72 places the break flag at
    /// bit 5 and is wrong; see the research document §3.1 before changing this.
    /// </para>
    /// </summary>
    public const byte X = 0x10;

    /// <summary>Unused. Reads as set on NMOS parts.</summary>
    public const byte U = 0x20;

    /// <summary>
    /// Accumulator and memory width select, 65816 native mode. 1 selects an 8-bit
    /// accumulator. The same bit as <see cref="U"/>; see <see cref="X"/>.
    /// </summary>
    public const byte M = 0x20;

    /// <summary>Overflow.</summary>
    public const byte V = 0x40;

    /// <summary>Negative.</summary>
    public const byte N = 0x80;
}

/// <summary>The architectural register file of a 65xx core.</summary>
/// <remarks>
/// Sized for the 65816 on every variant. The 8-bit cores use the low byte of each widened
/// register and leave <see cref="DP"/>, <see cref="DBR"/>, <see cref="PBR"/> and
/// <see cref="E"/> alone; <c>TVariant.Variant</c> folds away the mode checks they never
/// take, exactly as it already does for the 6510's port.
/// </remarks>
public struct CpuState
{
    /// <summary>Program counter. 16-bit on every variant; the 65816 pairs it with <see cref="PBR"/>.</summary>
    public ushort PC;

    /// <summary>
    /// Accumulator. The full 16 bits — what Bruce Clark's reference calls the "C accumulator",
    /// reserving "A" for its low byte. Named <c>A</c> here because that is what the
    /// conformance vectors call the 16-bit value.
    /// </summary>
    public ushort A;

    /// <summary>X index register. The high byte is forced to $00 whenever the x flag is 1.</summary>
    public ushort X;

    /// <summary>Y index register. The high byte is forced to $00 whenever the x flag is 1.</summary>
    public ushort Y;

    /// <summary>
    /// Stack pointer. 8 bits on every core before the 65816, where the stack is at
    /// $0100 + S; 16 bits in 65816 native mode, where it is anywhere in bank 0.
    /// </summary>
    public ushort S;

    /// <summary>
    /// Direct register — the 65816's page-zero relocation base. Called <c>D</c> by WDC, but
    /// <see cref="D"/> is already the decimal-mode flag on this type, so <c>DP</c> it is.
    /// </summary>
    public ushort DP;

    /// <summary>Processor status register. See <see cref="Flag"/>.</summary>
    public byte P;

    /// <summary>Data bank register. The bank absolute and indexed data accesses use. 65816 only.</summary>
    public byte DBR;

    /// <summary>Program bank register. The bank instructions are fetched from. 65816 only.</summary>
    public byte PBR;

    /// <summary>
    /// Emulation mode. True selects 6502 emulation, which forces the m and x flags to 1, the
    /// high bytes of X and Y to $00, and the high byte of S to $01. Only <c>XCE</c> changes it.
    /// </summary>
    public bool E;

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

    /// <summary>
    /// Accumulator and memory width select. True means 8-bit. Meaningful only in native mode;
    /// emulation mode forces it to true.
    /// </summary>
    public bool M
    {
        readonly get => (P & Flag.M) != 0;
        set => P = value ? (byte)(P | Flag.M) : (byte)(P & ~Flag.M);
    }

    /// <summary>
    /// Index register width select. True means 8-bit. Named <c>XFlag</c> rather than <c>X</c>
    /// because <see cref="X"/> is the index register itself.
    /// </summary>
    public bool XFlag
    {
        readonly get => (P & Flag.X) != 0;
        set => P = value ? (byte)(P | Flag.X) : (byte)(P & ~Flag.X);
    }

    /// <inheritdoc />
    public override readonly string ToString()
    {
        var core = $"PC:{PC:X4} A:{A:X4} X:{X:X4} Y:{Y:X4} S:{S:X4} P:{P:X2}";

        return DBR == 0 && PBR == 0 && DP == 0 && !E
            ? core
            : $"{core} DBR:{DBR:X2} PBR:{PBR:X2} DP:{DP:X4} E:{(E ? 1 : 0)}";
    }
}
