namespace SixtyFiveXX;

/// <summary>
/// Decodes instructions from any <see cref="IBus"/> for any variant, driven by the same
/// opcode table the engine runs from.
/// </summary>
/// <remarks>
/// <para>
/// Text and behaviour cannot drift because there is nothing to drift from: the mnemonic
/// and the addressing mode come out of the row <c>MicroOpTable</c> turned into a micro-op
/// sequence. For the five 8-bit cores, adding an opcode makes it decodable in the same
/// commit that makes it executable, without exception. The 65816 does not have that
/// property yet: most of its addressing modes have no case in the <c>switch</c> below and
/// throw <see cref="NotSupportedException"/>, and <see cref="AddrMode.Immediate"/> decodes
/// at a fixed length that ignores the <c>m</c> flag. Phase 7e closes the gap. No count is
/// quoted deliberately — it has drifted with every task of phase 7c, and the <c>switch</c>
/// below is the live answer.
/// </para>
/// <para>
/// <strong>Decoding reads the bus.</strong> On a flat memory map that is free; on a bus
/// whose reads have side effects it is not, and the library cannot tell the difference.
/// A caller disassembling live I/O space needs a bus that knows to stay quiet.
/// </para>
/// </remarks>
public static class Disassembler
{
    /// <summary>Decodes the instruction at <paramref name="address"/>.</summary>
    /// <typeparam name="TBus">The bus to read from.</typeparam>
    /// <typeparam name="TVariant">The core whose opcode table decides what the bytes mean.</typeparam>
    /// <param name="bus">Memory to decode from. Read only, and never written.</param>
    /// <param name="address">Where the opcode sits. Wrapped to 16 bits, as the bus wraps.</param>
    /// <returns>The mnemonic, its operand text, and the bytes consumed.</returns>
    public static Instruction Decode<TBus, TVariant>(in TBus bus, int address)
        where TBus : struct, IBus
        where TVariant : struct, ICpuVariant
    {
        // The same table the engine resolved, reached the same way. A variant wired into
        // one path and forgotten in the other throws there rather than decoding as
        // something else here.
        var info = MicroOpTable.For<TVariant>().Info[bus.Read(address & 0xFFFF)];

        // Each arm reads exactly the operand bytes its instruction has. Reading all three up
        // front would be shorter, but it would touch addresses the instruction never touches
        // — which matters on the side-effecting buses warned about above, and triples the
        // reads for a caller decoding every instruction it executes.
        return info.Mode switch
        {
            AddrMode.Implied => new Instruction(info.Mnemonic, "", 1),
            AddrMode.Accumulator => new Instruction(info.Mnemonic, "A", 1),
            AddrMode.Immediate => new Instruction(info.Mnemonic, $"#${Operand8(bus, address, 1):X2}", 2),

            AddrMode.ZeroPage => new Instruction(info.Mnemonic, $"${Operand8(bus, address, 1):X2}", 2),
            AddrMode.ZeroPageX => new Instruction(info.Mnemonic, $"${Operand8(bus, address, 1):X2},X", 2),
            AddrMode.ZeroPageY => new Instruction(info.Mnemonic, $"${Operand8(bus, address, 1):X2},Y", 2),

            AddrMode.Absolute => new Instruction(info.Mnemonic, $"${Operand16(bus, address):X4}", 3),
            AddrMode.AbsoluteX => new Instruction(info.Mnemonic, $"${Operand16(bus, address):X4},X", 3),
            AddrMode.AbsoluteY => new Instruction(info.Mnemonic, $"${Operand16(bus, address):X4},Y", 3),

            // The NMOS page-wrap bug and its CMOS fix are the same three bytes and the same
            // notation; they differ only in where the second vector byte is fetched from.
            AddrMode.Indirect or AddrMode.IndirectFixed =>
                new Instruction(info.Mnemonic, $"(${Operand16(bus, address):X4})", 3),
            AddrMode.AbsoluteIndexedIndirect =>
                new Instruction(info.Mnemonic, $"(${Operand16(bus, address):X4},X)", 3),

            AddrMode.ZeroPageIndirect =>
                new Instruction(info.Mnemonic, $"(${Operand8(bus, address, 1):X2})", 2),
            AddrMode.IndexedIndirect =>
                new Instruction(info.Mnemonic, $"(${Operand8(bus, address, 1):X2},X)", 2),
            AddrMode.IndirectIndexed =>
                new Instruction(info.Mnemonic, $"(${Operand8(bus, address, 1):X2}),Y", 2),

            // Shown as the address landed on, not the displacement encoded. The base is the
            // byte after the instruction, which is why the length is added before the offset.
            AddrMode.Relative => new Instruction(
                info.Mnemonic, $"${BranchTarget(address, 2, Operand8(bus, address, 1)):X4}", 2),

            // BBR/BBS: a page-zero address, then a displacement measured from the end of a
            // three-byte instruction.
            AddrMode.ZeroPageRelative => new Instruction(
                info.Mnemonic,
                $"${Operand8(bus, address, 1):X2},${BranchTarget(address, 3, Operand8(bus, address, 2)):X4}",
                3),

            // The 65C02's undefined opcodes are NOPs, but not uniform ones: these two shapes
            // fetch operand bytes and discard them. A linear decode still has to step over
            // the bytes, so the operand is shown rather than hidden.
            AddrMode.NopSingleCycle => new Instruction(info.Mnemonic, "", 1),
            AddrMode.NopAbsolute or AddrMode.NopAbsoluteExtra =>
                new Instruction(info.Mnemonic, $"${Operand16(bus, address):X4}", 3),

            // An opcode this variant does not implement still occupies its one byte.
            AddrMode.Undefined => new Instruction(info.Mnemonic, "", 1),

            AddrMode.Stack => DecodeStack(info, bus, address),

            // Never a silent default. Phase 4 shipped a switch that quietly handed an
            // unmapped variant the NMOS profile, and the only signal was a conformance
            // failure thousands of vectors later.
            _ => throw new NotSupportedException($"No operand format for {info.Mode}."),
        };
    }

    /// <summary>
    /// <see cref="AddrMode.Stack"/> is the one mode that does not fix a length: it covers
    /// the pushes and pulls at one byte, <c>BRK</c> at two, and the absolute <c>JMP</c> and
    /// <c>JSR</c> at three. The operation tells them apart.
    /// </summary>
    private static Instruction DecodeStack<TBus>(OpcodeInfo info, in TBus bus, int address)
        where TBus : struct, IBus =>
        info.Operation switch
        {
            // The byte after BRK is fetched and discarded, never executed. Written as an
            // immediate because that is both what it is and what assemblers accept.
            Op.Brk => new Instruction(info.Mnemonic, $"#${Operand8(bus, address, 1):X2}", 2),
            Op.Jmp or Op.Jsr => new Instruction(info.Mnemonic, $"${Operand16(bus, address):X4}", 3),
            _ => new Instruction(info.Mnemonic, "", 1),
        };

    /// <summary>Reads an operand byte, wrapping at the top of memory as the bus does.</summary>
    private static int Operand8<TBus>(in TBus bus, int address, int offset) where TBus : struct, IBus =>
        bus.Read((address + offset) & 0xFFFF);

    /// <summary>Reads a little-endian operand word, wrapping at the top of memory.</summary>
    private static int Operand16<TBus>(in TBus bus, int address) where TBus : struct, IBus =>
        bus.Read((address + 1) & 0xFFFF) | (bus.Read((address + 2) & 0xFFFF) << 8);

    /// <summary>
    /// Where a branch lands: the byte after the instruction, plus the signed displacement.
    /// </summary>
    private static int BranchTarget(int address, int length, int displacement) =>
        (address + length + (sbyte)displacement) & 0xFFFF;
}
