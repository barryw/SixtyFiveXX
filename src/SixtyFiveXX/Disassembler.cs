namespace SixtyFiveXX;

/// <summary>
/// Decodes instructions from any <see cref="IBus"/> for any variant, driven by the same
/// opcode table the engine runs from.
/// </summary>
/// <remarks>
/// <para>
/// Text and behaviour cannot drift because there is nothing to drift from: the mnemonic
/// and the addressing mode come out of the row <c>MicroOpTable</c> turned into a micro-op
/// sequence. Adding an opcode makes it decodable in the same commit that makes it
/// executable, without exception, and that now holds on all six cores: every addressing mode
/// any of their tables uses has an arm in the <c>switch</c> below. The
/// <see cref="NotSupportedException"/> at the end is not reachable for a defined opcode of
/// any shipped variant and stays as the loud failure a seventh core's new mode would get.
/// </para>
/// <para>
/// One mode's length is not a property of the encoding. On the 65816
/// <see cref="AddrMode.Immediate"/> is two bytes when the operative width flag says eight
/// bits and three when it says sixteen, and <em>which</em> flag applies is the operation's
/// own — the accumulator operations follow <c>m</c> and the index operations <c>x</c>. That
/// is why the width-carrying overload exists, and why an assumed default is documented on
/// the other one rather than hidden.
/// </para>
/// <para>
/// <strong>Decoding reads the bus.</strong> On a flat memory map that is free; on a bus
/// whose reads have side effects it is not, and the library cannot tell the difference.
/// A caller disassembling live I/O space needs a bus that knows to stay quiet.
/// </para>
/// </remarks>
public static class Disassembler
{
    /// <inheritdoc cref="Decode{TBus, TVariant}(in TBus, int, bool, bool)"/>
    /// <remarks>
    /// Both operand widths are taken as eight-bit. That is exactly right for the five 8-bit
    /// cores, which have no width flags at all, and is the 65816's reset state — but on that
    /// part it is an assumption, not a fact, and a caller decoding native-mode code with
    /// 16-bit registers must use the overload that says so.
    /// </remarks>
    public static Instruction Decode<TBus, TVariant>(in TBus bus, int address)
        where TBus : struct, IBus
        where TVariant : struct, ICpuVariant =>
        Decode<TBus, TVariant>(bus, address, wideAccumulator: false, wideIndex: false);

    /// <summary>Decodes the instruction at <paramref name="address"/>.</summary>
    /// <typeparam name="TBus">The bus to read from.</typeparam>
    /// <typeparam name="TVariant">The core whose opcode table decides what the bytes mean.</typeparam>
    /// <param name="bus">Memory to decode from. Read only, and never written.</param>
    /// <param name="address">
    /// Where the opcode sits. Wrapped to the width the variant fetches over: 16 bits on the
    /// eight-bit cores, 24 on the 65816, which fetches from <c>PBR:PC</c> across 16 MB.
    /// </param>
    /// <param name="wideAccumulator">
    /// True when the accumulator and memory are 16 bits — the 65816's <c>m = 0</c>. Decides
    /// the length of <c>LDA #</c> and its siblings, which the byte stream does not encode.
    /// Named for the width rather than for the flag because <c>m = 1</c> means <em>eight</em>
    /// bits, and a parameter called <c>m</c> would invert under the reader.
    /// </param>
    /// <param name="wideIndex">
    /// True when X and Y are 16 bits — the 65816's <c>x = 0</c>. Decides the length of
    /// <c>LDX #</c>, <c>LDY #</c>, <c>CPX #</c> and <c>CPY #</c>.
    /// </param>
    /// <returns>The mnemonic, its operand text, and the bytes consumed.</returns>
    public static Instruction Decode<TBus, TVariant>(
        in TBus bus, int address, bool wideAccumulator, bool wideIndex)
        where TBus : struct, IBus
        where TVariant : struct, ICpuVariant
    {
        // The same table the engine resolved, reached the same way. A variant wired into
        // one path and forgotten in the other throws there rather than decoding as
        // something else here.
        var info = MicroOpTable.For<TVariant>().Info[bus.Read(address & AddressMask<TVariant>())];

        // Each arm reads exactly the operand bytes its instruction has. Reading all three up
        // front would be shorter, but it would touch addresses the instruction never touches
        // — which matters on the side-effecting buses warned about above, and triples the
        // reads for a caller decoding every instruction it executes.
        return info.Mode switch
        {
            AddrMode.Implied => new Instruction(info.Mnemonic, "", 1),
            AddrMode.Accumulator => new Instruction(info.Mnemonic, "A", 1),

            // The one mode whose length is not a property of the encoding. Which flag sizes it
            // is a property of the operation, and the opcode table already declares that per
            // opcode — Width.M for the operations that move through the accumulator, Width.X
            // for the ones that move through an index register. Read from there rather than
            // from a list of Op values kept in this file, so a table edit cannot leave the
            // disassembler behind. The five 8-bit cores never take this arm: their tables
            // declare no Width at all, so info.Width is permanently Width.None there and the
            // JIT sees a constant false for the flags besides.
            AddrMode.Immediate when (info.Width == Width.M && wideAccumulator) ||
                                    (info.Width == Width.X && wideIndex) =>
                new Instruction(info.Mnemonic, $"#${Operand16<TBus, TVariant>(bus, address):X4}", 3),
            AddrMode.Immediate =>
                new Instruction(info.Mnemonic, $"#${Operand8<TBus, TVariant>(bus, address, 1):X2}", 2),

            // REP, SEP and WDM. Written like an immediate because that is what assemblers take,
            // but a fixed eight bits that no width flag reaches (datasheet Table 5-7 Note 1).
            AddrMode.ImmediateByte =>
                new Instruction(info.Mnemonic, $"#${Operand8<TBus, TVariant>(bus, address, 1):X2}", 2),

            // Page zero on the eight-bit cores and the direct page on the 65816 differ in how
            // the address is formed — D is added, and the result is always bank 0 — but not in
            // how the operand is written, so they share an arm rather than duplicating one.
            AddrMode.ZeroPage or AddrMode.DirectPage =>
                new Instruction(info.Mnemonic, $"${Operand8<TBus, TVariant>(bus, address, 1):X2}", 2),
            AddrMode.ZeroPageX or AddrMode.DirectPageX =>
                new Instruction(info.Mnemonic, $"${Operand8<TBus, TVariant>(bus, address, 1):X2},X", 2),
            AddrMode.ZeroPageY or AddrMode.DirectPageY =>
                new Instruction(info.Mnemonic, $"${Operand8<TBus, TVariant>(bus, address, 1):X2},Y", 2),

            AddrMode.Absolute =>
                new Instruction(info.Mnemonic, $"${Operand16<TBus, TVariant>(bus, address):X4}", 3),
            AddrMode.AbsoluteX =>
                new Instruction(info.Mnemonic, $"${Operand16<TBus, TVariant>(bus, address):X4},X", 3),
            AddrMode.AbsoluteY =>
                new Instruction(info.Mnemonic, $"${Operand16<TBus, TVariant>(bus, address):X4},Y", 3),

            // 24-bit absolute. Six hex digits rather than four is the whole of the notation:
            // the operand carries its own bank byte and addresses anywhere in the 16 MB space.
            AddrMode.AbsoluteLong =>
                new Instruction(info.Mnemonic, $"${Operand24<TBus, TVariant>(bus, address):X6}", 4),
            AddrMode.AbsoluteLongX =>
                new Instruction(info.Mnemonic, $"${Operand24<TBus, TVariant>(bus, address):X6},X", 4),

            // The NMOS page-wrap bug and its CMOS fix are the same three bytes and the same
            // notation; they differ only in where the second vector byte is fetched from.
            AddrMode.Indirect or AddrMode.IndirectFixed =>
                new Instruction(info.Mnemonic, $"(${Operand16<TBus, TVariant>(bus, address):X4})", 3),
            AddrMode.AbsoluteIndexedIndirect =>
                new Instruction(info.Mnemonic, $"(${Operand16<TBus, TVariant>(bus, address):X4},X)", 3),

            // Square brackets are how every 65816 assembler distinguishes a long indirect —
            // one that takes a 24-bit pointer, and therefore its bank, out of memory — from the
            // sixteen-bit indirects written in parentheses. JML [abs] is the only user of this.
            AddrMode.AbsoluteIndirectLong =>
                new Instruction(info.Mnemonic, $"[${Operand16<TBus, TVariant>(bus, address):X4}]", 3),

            AddrMode.ZeroPageIndirect or AddrMode.DirectPageIndirect =>
                new Instruction(info.Mnemonic, $"(${Operand8<TBus, TVariant>(bus, address, 1):X2})", 2),
            AddrMode.IndexedIndirect or AddrMode.DirectPageIndexedIndirectX =>
                new Instruction(info.Mnemonic, $"(${Operand8<TBus, TVariant>(bus, address, 1):X2},X)", 2),
            AddrMode.IndirectIndexed or AddrMode.DirectPageIndirectY =>
                new Instruction(info.Mnemonic, $"(${Operand8<TBus, TVariant>(bus, address, 1):X2}),Y", 2),

            AddrMode.DirectPageIndirectLong =>
                new Instruction(info.Mnemonic, $"[${Operand8<TBus, TVariant>(bus, address, 1):X2}]", 2),
            AddrMode.DirectPageIndirectLongY =>
                new Instruction(info.Mnemonic, $"[${Operand8<TBus, TVariant>(bus, address, 1):X2}],Y", 2),

            AddrMode.StackRelative =>
                new Instruction(info.Mnemonic, $"${Operand8<TBus, TVariant>(bus, address, 1):X2},S", 2),
            AddrMode.StackRelativeIndirectY =>
                new Instruction(info.Mnemonic, $"(${Operand8<TBus, TVariant>(bus, address, 1):X2},S),Y", 2),

            // MVN/MVP. The stream is opcode, destination bank, source bank; the text is
            // source, destination — the reverse (research document §14.3, and §15.1 measured
            // MVN $12,$34 assembling to 54 34 12). Rendering the two bytes in the order they
            // were read produces a move in the opposite direction that an assembler accepts
            // without complaint, because both operands are syntactically valid banks. Hence
            // offset 2 first.
            AddrMode.BlockMove => new Instruction(
                info.Mnemonic,
                $"${Operand8<TBus, TVariant>(bus, address, 2):X2}," +
                $"${Operand8<TBus, TVariant>(bus, address, 1):X2}",
                3),

            // Shown as the address landed on, not the displacement encoded. The base is the
            // byte after the instruction, which is why the length is added before the offset.
            AddrMode.Relative => new Instruction(
                info.Mnemonic,
                BranchTarget<TVariant>(address, 2, (sbyte)Operand8<TBus, TVariant>(bus, address, 1)),
                2),

            // BRL, and the only operand read as a signed sixteen-bit word.
            AddrMode.RelativeLong => new Instruction(
                info.Mnemonic,
                BranchTarget<TVariant>(address, 3, (short)Operand16<TBus, TVariant>(bus, address)),
                3),

            // BBR/BBS: a page-zero address, then a displacement measured from the end of a
            // three-byte instruction.
            AddrMode.ZeroPageRelative => new Instruction(
                info.Mnemonic,
                $"${Operand8<TBus, TVariant>(bus, address, 1):X2}," +
                BranchTarget<TVariant>(address, 3, (sbyte)Operand8<TBus, TVariant>(bus, address, 2)),
                3),

            // The 65C02's undefined opcodes are NOPs, but not uniform ones: these two shapes
            // fetch operand bytes and discard them. A linear decode still has to step over
            // the bytes, so the operand is shown rather than hidden.
            AddrMode.NopSingleCycle => new Instruction(info.Mnemonic, "", 1),
            AddrMode.NopAbsolute or AddrMode.NopAbsoluteExtra =>
                new Instruction(info.Mnemonic, $"${Operand16<TBus, TVariant>(bus, address):X4}", 3),

            // An opcode this variant does not implement still occupies its one byte.
            AddrMode.Undefined => new Instruction(info.Mnemonic, "", 1),

            AddrMode.Stack => DecodeStack<TBus, TVariant>(info, bus, address),

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
    private static Instruction DecodeStack<TBus, TVariant>(OpcodeInfo info, in TBus bus, int address)
        where TBus : struct, IBus
        where TVariant : struct, ICpuVariant =>
        info.Operation switch
        {
            // The byte after BRK or COP is fetched and discarded, never executed. Written as an
            // immediate because that is both what it is and what assemblers accept.
            Op.Brk or Op.Cop =>
                new Instruction(info.Mnemonic, $"#${Operand8<TBus, TVariant>(bus, address, 1):X2}", 2),

            // PEA's operand is not an address it dereferences — it is the literal word pushed —
            // but it is written and encoded exactly like an absolute one, which is why 64tass
            // accepts PEA @w $1234 and why it shares this arm.
            Op.Jmp or Op.Jsr or Op.Pea =>
                new Instruction(info.Mnemonic, $"${Operand16<TBus, TVariant>(bus, address):X4}", 3),

            // PEI's operand really is a direct-page offset, and is written in parentheses
            // because that is what it does: push the word found there.
            Op.Pei =>
                new Instruction(info.Mnemonic, $"(${Operand8<TBus, TVariant>(bus, address, 1):X2})", 2),

            // PER encodes a signed sixteen-bit displacement exactly as BRL does and is
            // rendered the same way — as the address it computes, not the displacement it
            // stores. Its bytes therefore depend on where the instruction sits (research
            // document §15.1, measured at four origins).
            Op.Per => new Instruction(
                info.Mnemonic,
                BranchTarget<TVariant>(address, 3, (short)Operand16<TBus, TVariant>(bus, address)),
                3),

            // The pushes, the pulls, RTS, RTI and RTL: one byte and no operand.
            _ => new Instruction(info.Mnemonic, "", 1),
        };

    /// <summary>Reads an operand byte, wrapping where <see cref="OperandAddress{TVariant}"/> says.</summary>
    private static int Operand8<TBus, TVariant>(in TBus bus, int address, int offset)
        where TBus : struct, IBus
        where TVariant : struct, ICpuVariant =>
        bus.Read(OperandAddress<TVariant>(address, offset));

    /// <summary>Reads a little-endian operand word, wrapping the same way.</summary>
    private static int Operand16<TBus, TVariant>(in TBus bus, int address)
        where TBus : struct, IBus
        where TVariant : struct, ICpuVariant =>
        bus.Read(OperandAddress<TVariant>(address, 1)) |
        (bus.Read(OperandAddress<TVariant>(address, 2)) << 8);

    /// <summary>
    /// Reads a little-endian 24-bit operand — the long modes' <c>AAL</c>, <c>AAH</c>,
    /// <c>AAB</c>. 65816 only, because no other variant has a four-byte instruction.
    /// </summary>
    private static int Operand24<TBus, TVariant>(in TBus bus, int address)
        where TBus : struct, IBus
        where TVariant : struct, ICpuVariant =>
        Operand16<TBus, TVariant>(bus, address) |
        (bus.Read(OperandAddress<TVariant>(address, 3)) << 16);

    /// <summary>
    /// How much of the decode address is significant: 16 bits for the eight-bit cores, 24 for
    /// the 65816, which fetches from <c>PBR:PC</c> across 16 MB.
    /// </summary>
    /// <remarks>
    /// Guarded by the same compile-time <c>TVariant.Variant</c> test <see cref="Cpu{TBus, TVariant}"/>
    /// uses for <c>PcAddress</c>: the constant is fixed per closed generic type, so the JIT sees
    /// <c>if (false)</c> and folds this to the bare <c>0xFFFF</c> for the five 8-bit cores. Their
    /// decode path is therefore the same instruction it was before the 65816 existed.
    /// </remarks>
    private static int AddressMask<TVariant>() where TVariant : struct, ICpuVariant =>
        TVariant.Variant == CpuVariant.W65C816 ? 0xFFFFFF : 0xFFFF;

    /// <summary>
    /// Where an operand byte lives. On the 65816 the program counter wraps within the program
    /// bank and never carries into the next one (research document §2.2/§2.4), so the bank is
    /// preserved and only the low 16 bits advance — the same shape <c>Cpu.HighByteAddressBank0</c>
    /// gives the engine. On the eight-bit cores it is a plain 16-bit wrap, which is what this
    /// method folds to there.
    /// </summary>
    private static int OperandAddress<TVariant>(int address, int offset)
        where TVariant : struct, ICpuVariant =>
        TVariant.Variant == CpuVariant.W65C816
            ? (address & 0xFF0000) | ((address + offset) & 0xFFFF)
            : (address + offset) & 0xFFFF;

    /// <summary>
    /// Where a branch lands, already written as an operand: the byte after the instruction,
    /// plus the signed displacement, which the caller has sign-extended to the width its mode
    /// encodes — eight bits for <see cref="AddrMode.Relative"/> and
    /// <see cref="AddrMode.ZeroPageRelative"/>, sixteen for <see cref="AddrMode.RelativeLong"/>
    /// and <c>PER</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The add wraps within the program bank and never carries into <c>PBR</c></strong>
    /// (research document §14.5), so a branch's target is always in the instruction's own bank
    /// — which is precisely <see cref="OperandAddress{TVariant}"/>'s rule, reused here rather
    /// than restated. Before this the arithmetic masked to 16 bits unconditionally, which was
    /// invisible while the decode address was masked to bank 0 and became a dropped bank the
    /// moment it was not.
    /// </para>
    /// <para>
    /// Six digits when the bank is non-zero and four when it is not. Four is the notation
    /// research §15.1 measured, and every eight-bit core keeps it because their targets cannot
    /// exceed <c>$FFFF</c>; six is the only rendering that says which bank a banked target is
    /// in, and a sixteen-bit one would print <c>$C036</c> for a branch in bank <c>$12</c> and
    /// read as bank 0.
    /// </para>
    /// </remarks>
    private static string BranchTarget<TVariant>(int address, int length, int displacement)
        where TVariant : struct, ICpuVariant
    {
        var target = OperandAddress<TVariant>(address, length + displacement);
        return target > 0xFFFF ? $"${target:X6}" : $"${target:X4}";
    }
}
