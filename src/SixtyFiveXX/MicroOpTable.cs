namespace SixtyFiveXX;

/// <summary>
/// Expands a variant's <see cref="OpcodeInfo"/> descriptors into a flat micro-op array
/// plus a per-opcode entry index.
/// </summary>
/// <remarks>
/// Flat rather than jagged: one contiguous <see cref="Ops"/> array keeps whole
/// instruction sequences in the same cache lines, and the index is a single
/// <c>ushort</c> lookup.
/// </remarks>
internal sealed class MicroOpTable
{
    /// <summary>
    /// The micro-op table for <typeparamref name="TVariant"/>, built once per variant and
    /// cached for the lifetime of the process.
    /// </summary>
    /// <remarks>
    /// A <c>static</c> field on a generic type is a distinct storage location per closed
    /// generic type, so <see cref="Cache{TVariant}"/> gives each variant its own
    /// lazily-built table with no dictionary lookup.
    /// </remarks>
    public static MicroOpTable For<TVariant>() where TVariant : ICpuVariant => Cache<TVariant>.Table;

    private static class Cache<TVariant> where TVariant : ICpuVariant
    {
        public static readonly MicroOpTable Table = new(OpcodeTableFor(TVariant.Variant), TVariant.Variant);
    }

    /// <summary>
    /// Maps a <see cref="CpuVariant"/> to its opcode descriptors. The one place a new core
    /// wires its table in. <c>TVariant.Variant</c> is a compile-time constant per closed
    /// generic type, so <see cref="Cache{TVariant}"/>'s field initialiser runs this switch
    /// once per variant, not on every access.
    /// </summary>
    private static OpcodeInfo[] OpcodeTableFor(CpuVariant variant) => variant switch
    {
        // The 6510 is a 6502 with two on-chip registers. Same opcodes, same sequences —
        // its delta is that $00 and $01 never reach the bus, which no table can express.
        CpuVariant.Mos6502 or CpuVariant.Mos6510 => Opcodes6502.Table,
        CpuVariant.Synertek65C02 => Opcodes65C02.Table,
        CpuVariant.Rockwell65C02 => Opcodes65C02.RockwellTable,
        CpuVariant.Wdc65C02 => Opcodes65C02.WdcTable,
        CpuVariant.W65C816 => Opcodes65C816.Table,
        _ => throw new NotSupportedException($"No opcode table for {variant} yet."),
    };

    /// <summary>
    /// The micro-ops a variant substitutes where the NMOS and CMOS families differ.
    /// </summary>
    /// <remarks>
    /// Every one of these is resolved once per variant at table-build time, by emitting a
    /// different micro-op rather than testing the variant inside a shared one. A test
    /// inside the micro-op would put a branch on the per-cycle path and defeat the reason
    /// the variant is a type parameter at all.
    /// </remarks>
    private readonly record struct Sequences(
        MicroOp BrkPushP,
        MicroOp IntPushP,
        MicroOp RmwMiddle,
        MicroOp IndexFixup,
        MicroOp ReadPageCross,
        MicroOp RmwPageCross);

    private static readonly Sequences Nmos = new(
        MicroOp.PushPBrk, MicroOp.PushPInt,
        MicroOp.RmwModifyWrite, MicroOp.DummyReadFixup,
        MicroOp.ReadPageCross, MicroOp.DummyReadFixup);

    private static readonly Sequences Cmos = new(
        MicroOp.PushPBrkCmos, MicroOp.PushPIntCmos,
        MicroOp.RmwModifyRead, MicroOp.IndexFixupCmos,
        MicroOp.ReadPageCrossCmos, MicroOp.RmwPageCrossCmos);

    /// <summary>
    /// Placeholder sequences for the 65816. Six copies of <see cref="MicroOp.Unimplemented816"/>
    /// rather than <see cref="Nmos"/>, because NMOS is not a harmless placeholder here — it is
    /// wrong: the 65816 has its own native-mode BRK and IRQ vectors, and its read-modify-write
    /// direction depends on <c>E</c> at run time (datasheet Note 17, research document §7),
    /// neither of which a <see cref="Nmos"/> substitution would produce. This exists only so
    /// the shared <c>IrqEntry</c> section built in the constructor below — which reads
    /// <c>seq.IntPushP</c> unconditionally, for every variant, at table-build time — has
    /// something to read for the 65816 too. Nothing in phase 7b reaches it: no BRK, IRQ or NMI
    /// test runs against the 65816 yet, and <c>Reset()</c> drives its own path. The first cycle
    /// that does reach it, once phase 7d wires up 65816 interrupts, throws
    /// <see cref="NotImplementedException"/> naming <see cref="MicroOp.Unimplemented816"/>
    /// instead of quietly running NMOS.
    /// </summary>
    private static readonly Sequences NotYet816 = new(
        MicroOp.Unimplemented816, MicroOp.Unimplemented816, MicroOp.Unimplemented816,
        MicroOp.Unimplemented816, MicroOp.Unimplemented816, MicroOp.Unimplemented816);

    /// <remarks>
    /// Every variant is listed rather than defaulting to <see cref="Nmos"/>, so this fails
    /// as loudly as <see cref="OpcodeTableFor"/> does. A silent default is the worse
    /// failure here: a variant wired into the table switch and forgotten in this one would
    /// get NMOS interrupt, indexing and read-modify-write behaviour with no build-time
    /// signal at all — only a conformance failure, thousands of vectors later.
    /// </remarks>
    private static Sequences SequencesFor(CpuVariant variant) => variant switch
    {
        CpuVariant.Wdc65C02 or CpuVariant.Rockwell65C02 or CpuVariant.Synertek65C02 => Cmos,
        CpuVariant.Mos6502 or CpuVariant.Mos6510 => Nmos,

        // The 65816 has its own emission path — Emit816, below — which never reads these
        // fields; Emit() dispatches to it before any of the six are consulted. See
        // NotYet816's own remarks for why this arm returns that rather than Nmos, and why
        // it cannot simply be omitted.
        CpuVariant.W65C816 => NotYet816,

        _ => throw new NotSupportedException($"No micro-op sequences for {variant} yet."),
    };

    /// <summary>
    /// True for the indexed read-modify-writes that pay their fixup cycle unconditionally.
    /// </summary>
    /// <remarks>
    /// On NMOS that is all of them. On CMOS the shift and rotate forms became conditional —
    /// six cycles without a page cross, seven with — but <c>INC</c> and <c>DEC abs,X</c>
    /// stayed at seven regardless. Measured across all six opcodes' vectors; no reading of
    /// "read-modify-write does a dummy read instead of a dummy write" predicts the split.
    /// </remarks>
    private static bool IndexedRmwAlwaysPaysFixup(Sequences seq, Op op) =>
        seq.RmwPageCross == MicroOp.DummyReadFixup || op is Op.Inc or Op.Dec;

    /// <summary>True for the CMOS ADC and SBC, whose decimal mode costs an extra cycle.</summary>
    private static bool CmosArithmetic(OpcodeInfo info) => info.Operation is Op.AdcCmos or Op.SbcCmos;

    /// <summary>
    /// The fixup cycle an indexed write or read-modify-write pays after forming its address.
    /// </summary>
    private static MicroOp IndexedFixupFor(OpcodeInfo info, Sequences seq) =>
        info.Access == Access.ReadModifyWrite && !IndexedRmwAlwaysPaysFixup(seq, info.Operation)
            ? seq.RmwPageCross
            : seq.IndexFixup;

    /// <summary>Every opcode's micro-op sequence, concatenated, each terminated by <see cref="MicroOp.End"/>.</summary>
    public readonly MicroOp[] Ops;

    /// <summary>Opcode byte to its first micro-op's index in <see cref="Ops"/>.</summary>
    public readonly ushort[] Entry;

    /// <summary>The descriptors this table was built from.</summary>
    public readonly OpcodeInfo[] Info;

    /// <summary>
    /// Index of the hardware interrupt sequence in <see cref="Ops"/>. The dispatcher sets
    /// the CPU's vector field to <c>NmiVector</c> or <c>IrqVector</c> before entering this
    /// sequence. The sequence's own <c>PushPInt</c> micro-op may still redirect it once
    /// more, from <c>IrqVector</c> to <c>NmiVector</c>, if an NMI is latched before that
    /// cycle — a hijack of the IRQ dispatch already in progress. BRK's own sequence does
    /// the same in <c>PushPBrk</c>.
    /// </summary>
    public readonly ushort IrqEntry;

    /// <summary>Index of the reset sequence in <see cref="Ops"/>.</summary>
    public readonly ushort ResetEntry;

    private MicroOpTable(OpcodeInfo[] info, CpuVariant variant)
    {
        Info = info;
        Entry = new ushort[256];

        var seq = SequencesFor(variant);
        var ops = new List<MicroOp>(2048);

        for (var opcode = 0; opcode < 256; opcode++)
        {
            Entry[opcode] = (ushort)ops.Count;
            Emit(ops, info[opcode], seq, variant);
            ops.Add(MicroOp.End);
        }

        IrqEntry = (ushort)ops.Count;
        ops.AddRange([
            MicroOp.IntDummy,
            MicroOp.PushPch,
            MicroOp.PushPcl,
            seq.IntPushP,
            MicroOp.VectorLo,
            MicroOp.VectorHi,
        ]);
        ops.Add(MicroOp.End);

        // Reset behaves like an interrupt whose pushes are replaced by reads: S still
        // decrements three times, but nothing is written. Unlike IrqEntry, Reset() never
        // goes through FetchOpcode (there is no opcode to fetch), so the sequence spells
        // out both of the dummy PC reads hardware performs — FetchOpcode supplies the
        // first one for free everywhere else.
        ResetEntry = (ushort)ops.Count;
        ops.AddRange([
            MicroOp.IntDummy,
            MicroOp.IntDummy,
            MicroOp.StackDummyReadDec,
            MicroOp.StackDummyReadDec,
            MicroOp.StackDummyReadDec,
            MicroOp.VectorLo,
            MicroOp.VectorHi,
        ]);
        ops.Add(MicroOp.End);

        Ops = ops.ToArray();
    }

    /// <summary>Number of micro-ops before <see cref="MicroOp.End"/>, excluding the opcode fetch.</summary>
    public int SequenceLength(int opcode)
    {
        var i = Entry[opcode];
        var count = 0;
        while (Ops[i + count] != MicroOp.End) count++;
        return count;
    }

    private static void Emit(List<MicroOp> ops, OpcodeInfo info, Sequences seq, CpuVariant variant)
    {
        if (info.Operation == Op.Undefined) return;

        // The 65816 does not stretch the NMOS/CMOS mechanism below at all — see Emit816 and
        // the Sequences record's own remarks — so it is routed away before any of that logic
        // runs, not folded into it.
        if (variant == CpuVariant.W65C816)
        {
            Emit816(ops, info);
            return;
        }

        // Hand-written sequences: control flow and stack instructions do not decompose
        // into an addressing phase plus an access phase.
        if (info.Mode == AddrMode.Stack)
        {
            EmitStack(ops, info.Operation, seq.BrkPushP);
            return;
        }

        if (info.Mode == AddrMode.NopSingleCycle) return;   // fetch only; no further cycles

        if (info.Mode == AddrMode.NopAbsolute)
        {
            ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHi]);
            return;
        }

        if (info.Mode == AddrMode.NopAbsoluteExtra)
        {
            ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHi, MicroOp.NopAbsExtraRead]);
            return;
        }

        if (info.Mode == AddrMode.ZeroPageRelative)
        {
            // Five cycles when the branch is not taken, six when it is, seven across a page
            // — the ordinary branch tail does the last two.
            ops.AddRange([
                MicroOp.FetchAddrLo, MicroOp.RmwRead, MicroOp.BitBranchDummy,
                MicroOp.BitBranchFetch, MicroOp.BranchTaken, MicroOp.BitBranchFixup,
            ]);
            return;
        }

        if (info.Mode == AddrMode.Relative)
        {
            ops.AddRange([MicroOp.BranchFetch, MicroOp.BranchTaken, MicroOp.BranchFixup]);
            return;
        }

        if (info.Mode == AddrMode.Indirect)
        {
            // JMP ($nnnn)
            ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHi, MicroOp.JmpIndLo, MicroOp.JmpIndHi]);
            return;
        }

        if (info.Mode == AddrMode.IndirectFixed)
        {
            // CMOS JMP ($nnnn). One cycle longer than the NMOS form because the buggy
            // address is still read before the correct one — see MicroOp.JmpIndBugDummy.
            ops.AddRange([
                MicroOp.FetchAddrLo, MicroOp.FetchAddrHi,
                MicroOp.JmpIndLo, MicroOp.JmpIndBugDummy, MicroOp.PtrJmpHi,
            ]);
            return;
        }

        if (info.Mode == AddrMode.AbsoluteIndexedIndirect)
        {
            // CMOS JMP ($nnnn,X). JmpAbsXDummy folds X into the address it just read, so
            // JmpIndLo's own `_ptr = _addr` picks up the indexed pointer unchanged.
            ops.AddRange([
                MicroOp.FetchAddrLo, MicroOp.FetchAddrHi,
                MicroOp.JmpAbsXDummy, MicroOp.JmpIndLo, MicroOp.PtrJmpHi,
            ]);
            return;
        }

        if (info.Operation is Op.Wai or Op.Stp)
        {
            // Cycle 2 is a dummy read at PC, as for any implied instruction; the hold
            // micro-op then repeats for as long as the condition lasts.
            ops.AddRange([MicroOp.ImpliedDummy, info.Operation == Op.Wai ? MicroOp.WaiHold : MicroOp.StpHold]);
            return;
        }

        if (info.Operation == Op.Jam)
        {
            // Cycle 2 is a dummy read at PC; every cycle after that is held by JamHold,
            // which never advances the sequence position.
            ops.AddRange([MicroOp.ImpliedDummy, MicroOp.JamHold]);
            return;
        }

        if (info.Mode is AddrMode.Implied or AddrMode.Accumulator)
        {
            ops.Add(MicroOp.ImpliedExec);
            return;
        }

        if (info.Mode == AddrMode.Immediate)
        {
            // CMOS arithmetic spends one extra cycle in decimal mode, so its immediate form
            // carries a slot for it that the micro-op skips when D is clear.
            ops.AddRange(info.Operation is Op.AdcCmos or Op.SbcCmos
                ? [MicroOp.ImmExecCmosArith, MicroOp.BcdExtra]
                : [MicroOp.ImmExec]);
            return;
        }

        // The unstable stores form their address like a normal indexed write, but the
        // fixup cycle also folds the stored value into the address's high byte on a
        // page cross. Each addressing mode builds its own prefix; note that $93 is
        // (zp),Y and so fetches a pointer first, while the rest are absolute-indexed.
        if (info.Operation is Op.Sha or Op.Shx or Op.Shy or Op.Tas)
        {
            if (info.Mode == AddrMode.IndirectIndexed)
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.PtrReadLo, MicroOp.PtrReadHiY]);
            else if (info.Mode == AddrMode.AbsoluteX)
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHiX]);
            else
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHiY]);

            ops.AddRange([MicroOp.UnstableStoreFixup, MicroOp.ExecWrite]);
            return;
        }

        EmitAddressing(ops, info, seq);
        EmitAccess(ops, info, seq);
    }

    /// <summary>
    /// The 65816's emission path. Entirely separate from the NMOS/CMOS mechanism above: its
    /// addressing modes have no NMOS or CMOS counterpart for <see cref="Sequences"/> to
    /// substitute into, and the read-modify-write direction depends on <c>E</c> at run time
    /// rather than at table-build time (datasheet Note 17, research document §7), which no
    /// <see cref="Sequences"/> substitution can express. Bending the existing mechanism to fit
    /// a third family was tried and rejected; this is the separate path that decision calls
    /// for.
    /// </summary>
    /// <remarks>
    /// Task 3 landed the harness and the first opcode: <c>XCE</c>'s real two-cycle sequence —
    /// a fetch (implicit) plus <see cref="MicroOp.ImpliedExec816"/>'s internal cycle, research
    /// document §9 row 19a. Task 4 added <c>REP</c>/<c>SEP</c>'s real three-cycle sequence — a
    /// fetch, <see cref="MicroOp.RepSepOperand"/>, then <see cref="MicroOp.RepSepExec"/>'s
    /// internal cycle, §9's "Immediate, and REP/SEP". Tasks 5 and 6 add every remaining LDA/STA
    /// form via <see cref="EmitAddressed816"/> — the first place emission genuinely depends on
    /// <c>info.Mode</c> rather than <c>info.Operation</c> alone, which is why that method takes
    /// the whole descriptor and switches on the mode instead of being folded into the <c>if</c>
    /// chain here. Phase 7c task 3 made that the fall-through for every addressed opcode rather
    /// than an opt-in for two operations, so each <c>if</c> here now <c>return</c>s: without
    /// that, an opcode matching one of the early branches would go on to have an addressing
    /// sequence concatenated onto it.
    /// </remarks>
    private static void Emit816(List<MicroOp> ops, OpcodeInfo info)
    {
        if (info.Operation == Op.Xce)
        {
            ops.Add(MicroOp.ImpliedExec816);
            return;
        }

        if (info.Operation is Op.Rep or Op.Sep)
        {
            ops.AddRange([MicroOp.RepSepOperand, MicroOp.RepSepExec]);
            return;
        }

        // Everything else on the 65816 forms an effective address and then reads or writes it.
        // Routed by mode and access rather than by an ever-growing list of operations: the
        // emitter's own `default:` throw is the tripwire for a mode with no sequence, and
        // keeping every addressed opcode on one path is what makes it a real tripwire rather
        // than one that only fires for operations somebody remembered to list here.
        EmitAddressed816(ops, info);
    }

    /// <summary>
    /// Every 65816 addressing form, built directly against research document §9's per-mode
    /// blocks. Named <c>EmitDirectPage816</c> through task 5, when it covered only the
    /// seven direct-page modes; task 6 folded the remaining eight forms (absolute, long,
    /// stack-relative, immediate) into the same switch rather than a second method, since every
    /// one of them ends the same way — <c>info.Mode</c> drives the addressing prefix and, for
    /// every mode but immediate, which "+1" high-byte micro-op closes the sequence, while
    /// <c>info.Access</c> drives which pair of low/high access micro-ops does:
    /// <see cref="MicroOp.ReadExec816"/> and a read-high micro-op, or
    /// <see cref="MicroOp.ExecWrite816"/> and a write-high one. Immediate is the one mode that
    /// does not share that tail — it has no effective address for <see cref="MicroOp.ReadExec816"/>
    /// to read — so it returns before the <c>switch</c> rather than adding a case to it.
    /// <para>
    /// Called <c>EmitLdaSta816</c> until phase 7c task 3, when <c>ORA</c>/<c>AND</c>/<c>EOR</c>
    /// joined <c>LDA</c>/<c>STA</c> on it unchanged: nothing below reads <c>info.Operation</c>,
    /// only <c>info.Mode</c> and <c>info.Access</c>, so an operation that reuses a certified
    /// addressing mode costs a table entry and an <c>Exec</c> arm and nothing here.
    /// </para>
    /// </summary>
    private static void EmitAddressed816(List<MicroOp> ops, OpcodeInfo info)
    {
        if (info.Mode == AddrMode.Immediate)
        {
            // The accumulator's immediate forms — LDA/ORA/AND/EOR #; STA has none, and phase
            // 7c task 3 added the other three here. ImmExec816 ends the instruction
            // after the low byte when M selects 8 bits; ImmExecHigh816 supplies the second
            // operand byte otherwise. Distinct from AddrMode.ImmediateByte (REP/SEP's fixed
            // 8-bit operand, MicroOp.RepSepOperand/RepSepExec): this operand's width and the
            // instruction's byte count both depend on M at run time. Research document §9,
            // "Immediate, and REP/SEP".
            ops.AddRange([MicroOp.ImmExec816, MicroOp.ImmExecHigh816]);
            return;
        }

        switch (info.Mode)
        {
            case AddrMode.DirectPage:
                ops.AddRange([MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty]);
                break;

            case AddrMode.DirectPageX:
                ops.AddRange([MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty, MicroOp.DirectPageIndexX]);
                break;

            // Research document §12.3. Identical in shape to DirectPageX with Y substituted —
            // LDX and STX are the only instructions that use it.
            case AddrMode.DirectPageY:
                ops.AddRange([MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty, MicroOp.DirectPageIndexY]);
                break;

            case AddrMode.DirectPageIndirect:
                ops.AddRange([
                    MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty,
                    MicroOp.PtrReadLo816, MicroOp.DpPtrReadHi,
                ]);
                break;

            case AddrMode.DirectPageIndexedIndirectX:
                ops.AddRange([
                    MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty, MicroOp.DirectPageIndexX,
                    MicroOp.PtrReadLo816, MicroOp.DpPtrReadHi,
                ]);
                break;

            case AddrMode.DirectPageIndirectY:
                // Code-review fix (task 5): which of DpPtrReadHiY (read, can skip the indexing
                // cycle) or DpPtrReadHiYWrite (write, never skips) is selected here, at
                // table-build time, from info.Access — not at run time from info.Operation. See
                // MicroOp.DpPtrReadHiY. Task 6's abs,X/abs,Y reuse the same mechanism below.
                ops.AddRange([
                    MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty,
                    MicroOp.PtrReadLo816,
                    info.Access == Access.Write ? MicroOp.DpPtrReadHiYWrite : MicroOp.DpPtrReadHiY,
                    MicroOp.IndexDirectPageIndirectY,
                ]);
                break;

            case AddrMode.DirectPageIndirectLong:
                ops.AddRange([
                    MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty,
                    MicroOp.PtrReadLo816, MicroOp.LongPtrReadMid, MicroOp.LongPtrReadHi,
                ]);
                break;

            case AddrMode.DirectPageIndirectLongY:
                ops.AddRange([
                    MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty,
                    MicroOp.PtrReadLo816, MicroOp.LongPtrReadMid, MicroOp.LongPtrReadHiY,
                ]);
                break;

            // Task 6. Research document §9's "Absolute" block: no indexing at all, so the AAH
            // fetch folds DBR straight into _addr — see MicroOp.AbsHi.
            case AddrMode.Absolute:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.AbsHi]);
                break;

            // §9's "Absolute,X — row 6a": AbsHiIndexedX(Write) fetches AAH and precomputes both
            // the mis-indexed and real addresses; AbsIndexFixup is the conditional cycle 3a that
            // drives the former and installs the latter — selected read-vs-write at table-build
            // time from info.Access, the same mechanism (dp),Y above uses.
            case AddrMode.AbsoluteX:
                ops.AddRange([
                    MicroOp.FetchAddrLo,
                    info.Access == Access.Write ? MicroOp.AbsHiIndexedXWrite : MicroOp.AbsHiIndexedX,
                    MicroOp.AbsIndexFixup,
                ]);
                break;

            // §9's "Absolute,Y — row 7": identical in shape to AbsoluteX, Y substituted for X.
            case AddrMode.AbsoluteY:
                ops.AddRange([
                    MicroOp.FetchAddrLo,
                    info.Access == Access.Write ? MicroOp.AbsHiIndexedYWrite : MicroOp.AbsHiIndexedY,
                    MicroOp.AbsIndexFixup,
                ]);
                break;

            // §9's "Absolute Long — row 4a": a 24-bit operand, no DBR involved at all — the
            // third byte fetched here is the data bank outright. No indexing cycle exists for
            // this mode.
            case AddrMode.AbsoluteLong:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHi, MicroOp.FetchAddrBank]);
                break;

            // §9's "Absolute Long,X — row 5": as AbsoluteLong, but FetchAddrBankX folds X into
            // the 24-bit address in the same cycle — no separate indexing cycle for this mode
            // either, which is why long,X's formula carries no page-cross term at all.
            case AddrMode.AbsoluteLongX:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHi, MicroOp.FetchAddrBankX]);
                break;

            // §9's "Stack Relative — row 23": bank 0, and StackRelativePenalty is unconditional
            // — no DL-style skip the way DirectPagePenalty has, since sr,S is a "new" mode with
            // no direct-page penalty at all (research document §5).
            case AddrMode.StackRelative:
                ops.AddRange([MicroOp.FetchSrOffset, MicroOp.StackRelativePenalty]);
                break;

            // §9's "(Stack Relative),Y — row 24": as StackRelative, then a two-byte bank-0
            // pointer and an unconditional second internal cycle that indexes by Y through DBR.
            // Flat 8-m in Clark (§5) — no w, no p — so nothing here is ever skipped.
            case AddrMode.StackRelativeIndirectY:
                ops.AddRange([
                    MicroOp.FetchSrOffset, MicroOp.StackRelativePenalty,
                    MicroOp.PtrReadLo816, MicroOp.SrPtrReadHi, MicroOp.IndexStackRelativeIndirectY,
                ]);
                break;

            default:
                throw new InvalidOperationException(
                    $"{info.Mnemonic}: {info.Mode} has no 65816 addressing sequence.");
        }

        // Code-review fix (task 5), extended by task 6 and again by task 7: only the three plain
        // direct-page forms — dp, dp,X and dp,Y — and plain stack-relative are bank-0-confined
        // (their data access is 0,D+DO[+X|+Y] or 0,S+SO); dp,Y joined the set with the mode
        // itself in task 7, since it is direct-page addressing and confined exactly as dp,X is.
        // Every other mode's final access goes through DBR or the operand's own bank
        // byte, and its "+1" must carry into the next bank rather than wrap — Clark §5.2
        // Example 2, cited at Cpu.HighByteAddressCarry. (sr,S),Y is NOT in the bank-0-confined
        // set despite sharing sr,S's bank-0 pointer fetch: task 6 review found this mode's
        // *final* access, DBR,AA+Y (§9 row 24, cycles 7/7a), goes through DBR exactly like
        // (dp),Y's does — indistinguishable from (dp),Y's bank-carry requirement, and wrongly
        // grouped with plain sr,S here originally. Zero vector coverage: catching it needs
        // M=0 with the indexed pointer landing exactly on $xxFFFF, which no SingleStepTests
        // vector for $B3/$93 happens to hit across 10,000 tries each.
        var carry = info.Mode is not (AddrMode.DirectPage or AddrMode.DirectPageX
            or AddrMode.DirectPageY or AddrMode.StackRelative);

        if (info.Access == Access.Write)
        {
            ops.Add(MicroOp.ExecWrite816);
            ops.Add(carry ? MicroOp.ExecWriteHigh816Carry : MicroOp.ExecWriteHigh816);
        }
        else
        {
            ops.Add(MicroOp.ReadExec816);
            ops.Add(carry ? MicroOp.ReadExecHigh816Carry : MicroOp.ReadExecHigh816);
        }
    }

    /// <summary>Emits the cycles that form the effective address, up to but excluding the access.</summary>
    private static void EmitAddressing(List<MicroOp> ops, OpcodeInfo info, Sequences seq)
    {
        var access = info.Access;

        switch (info.Mode)
        {
            case AddrMode.ZeroPage:
                ops.Add(MicroOp.FetchAddrLo);
                break;

            case AddrMode.ZeroPageX:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.ZpIndexX]);
                break;

            case AddrMode.ZeroPageY:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.ZpIndexY]);
                break;

            case AddrMode.Absolute:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHi]);
                break;

            case AddrMode.AbsoluteX:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHiX]);
                if (access != Access.Read) ops.Add(IndexedFixupFor(info, seq));
                break;

            case AddrMode.AbsoluteY:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHiY]);
                if (access != Access.Read) ops.Add(IndexedFixupFor(info, seq));
                break;

            case AddrMode.IndexedIndirect:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.ZpIndexX, MicroOp.PtrReadLo, MicroOp.PtrReadHi]);
                break;

            case AddrMode.ZeroPageIndirect:
                // (zp) is (zp,X) without the indexing cycle. PtrReadHi already wraps the
                // pointer's high byte within page zero, which is what this mode needs too.
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.PtrReadLo, MicroOp.PtrReadHi]);
                break;

            case AddrMode.IndirectIndexed:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.PtrReadLo, MicroOp.PtrReadHiY]);
                if (access != Access.Read) ops.Add(IndexedFixupFor(info, seq));
                break;

            default:
                throw new InvalidOperationException($"{info.Mode} has no addressing sequence.");
        }
    }

    /// <summary>Emits the cycles that read, write, or read-modify-write the effective address.</summary>
    private static void EmitAccess(List<MicroOp> ops, OpcodeInfo info, Sequences seq)
    {
        var access = info.Access;
        var indexedRead = access == Access.Read &&
                          info.Mode is AddrMode.AbsoluteX or AddrMode.AbsoluteY or AddrMode.IndirectIndexed;

        switch (access)
        {
            case Access.Read when indexedRead && CmosArithmetic(info):
                ops.AddRange([MicroOp.ReadPageCrossCmosArith, MicroOp.ReadExecCmosArith, MicroOp.BcdExtra]);
                break;

            case Access.Read when indexedRead:
                ops.AddRange([seq.ReadPageCross, MicroOp.ReadExec]);
                break;

            case Access.Read when CmosArithmetic(info):
                ops.AddRange([MicroOp.ReadExecCmosArith, MicroOp.BcdExtra]);
                break;

            case Access.Read:
                ops.Add(MicroOp.ReadExec);
                break;

            case Access.Write:
                ops.Add(MicroOp.ExecWrite);
                break;

            case Access.ReadModifyWrite:
                ops.AddRange([MicroOp.RmwRead, seq.RmwMiddle, MicroOp.RmwWrite]);
                break;

            default:
                throw new InvalidOperationException($"{access} has no access sequence.");
        }
    }

    // brkPushP is the P-push micro-op BRK uses, NMOS or CMOS — see InterruptPushesFor.
    private static void EmitStack(List<MicroOp> ops, Op op, MicroOp brkPushP)
    {
        switch (op)
        {
            case Op.Jmp:   // JMP absolute
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.JmpAbs]);
                break;

            case Op.Jsr:
                ops.AddRange([
                    MicroOp.FetchAddrLo, MicroOp.StackDummyRead,
                    MicroOp.PushPch, MicroOp.PushPcl, MicroOp.JsrFinish,
                ]);
                break;

            case Op.Rts:
                ops.AddRange([
                    MicroOp.ImpliedDummy, MicroOp.StackDummyReadInc,
                    MicroOp.PullPcl, MicroOp.PullPch, MicroOp.RtsFinish,
                ]);
                break;

            case Op.Rti:
                ops.AddRange([
                    MicroOp.ImpliedDummy, MicroOp.StackDummyReadInc,
                    MicroOp.PullP, MicroOp.PullPcl, MicroOp.PullPch,
                ]);
                break;

            case Op.Brk:
                ops.AddRange([
                    MicroOp.BrkPad, MicroOp.PushPch, MicroOp.PushPcl,
                    brkPushP, MicroOp.VectorLo, MicroOp.VectorHi,
                ]);
                break;

            case Op.Pha or Op.Php or Op.Phx or Op.Phy:
                ops.AddRange([MicroOp.ImpliedDummy, MicroOp.Push]);
                break;

            case Op.Pla or Op.Plp or Op.Plx or Op.Ply:
                ops.AddRange([MicroOp.ImpliedDummy, MicroOp.StackDummyReadInc, MicroOp.Pull]);
                break;

            default:
                throw new InvalidOperationException($"{op} has no stack sequence.");
        }
    }
}
