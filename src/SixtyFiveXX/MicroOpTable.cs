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

        // The 65816 consults none of these six. Emit() dispatches to Emit816 before any of them
        // is read, and phase 7d task 2 gave the part its own IrqEntry and ResetEntry sections
        // in the constructor below, which was the last thing that read one (seq.IntPushP) on
        // its behalf. The arm exists only because this switch deliberately refuses a silent
        // default; the value returned is arbitrary, and Nmos is the arbitrary value that is
        // already here. It replaced a NotYet816 placeholder built from six copies of a
        // throwing Unimplemented816 micro-op, which existed purely to keep the then-shared
        // IrqEntry section from running NMOS pushes on this part; phase 7d task 3 gave the part
        // its real interrupt sequence and deleted that micro-op outright.
        CpuVariant.W65C816 => Nmos,

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
    /// the CPU's vector field before entering this sequence — to <c>NmiVector</c> or
    /// <c>IrqVector</c> on the five eight-bit cores, and through <c>Cpu.Vector816</c> on the
    /// 65816, which has two sets of four (research document §14.2). On the NMOS cores the
    /// sequence's own <c>PushPInt</c> micro-op may still redirect it once more, from
    /// <c>IrqVector</c> to <c>NmiVector</c>, if an NMI is latched before that cycle — a hijack
    /// of the IRQ dispatch already in progress, which <c>PushPBrk</c> also performs for BRK.
    /// Neither the CMOS cores nor the 65816 has it: see <c>MicroOp.PushPInt816</c>.
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
        if (variant == CpuVariant.W65C816)
        {
            // Research document §14.2, Table 5-7 row 22a: eight cycles native, seven in
            // emulation (note 7). Cycle 1 is Cpu.FetchOpcode's own discarded read at PBR,PC —
            // the same free read every core's interrupt entry gets — and IntInternal816 is
            // cycle 2, the row's IO at the same address. It also skips PushPbr816 when e = 1,
            // which is the one cycle emulation mode omits. Cycles 3 to 8 are shared verbatim
            // with row 22j, which is why EmitControlFlow816's BRK/COP arm lists the same five
            // micro-ops from PushPbr816 down; the two rows differ only in their leading pair.
            //
            // Which vector is read is decided before this sequence starts, by FetchOpcode
            // through Cpu.Vector816 — only the dispatcher knows an IRQ from an NMI, and on
            // this part nothing in the sequence can change the answer afterwards (no NMI
            // hijack: MicroOp.PushPInt816).
            ops.AddRange([
                MicroOp.IntInternal816, MicroOp.PushPbr816,
                MicroOp.PushPch816, MicroOp.PushPcl816, MicroOp.PushPInt816,
                MicroOp.VectorLo816, MicroOp.VectorHi816,
            ]);
        }
        else
        {
            ops.AddRange([
                MicroOp.IntDummy,
                MicroOp.PushPch,
                MicroOp.PushPcl,
                seq.IntPushP,
                MicroOp.VectorLo,
                MicroOp.VectorHi,
            ]);
        }

        ops.Add(MicroOp.End);

        // Reset behaves like an interrupt whose pushes are replaced by reads: S still
        // decrements three times, but nothing is written. Unlike IrqEntry, Reset() never
        // goes through FetchOpcode (there is no opcode to fetch), so the sequence spells
        // out both of the dummy PC reads hardware performs — FetchOpcode supplies the
        // first one for free everywhere else.
        //
        // The 65816 gets its own copy for the same reason it gets its own IrqEntry, even though
        // Reset() forces E = true before entering here and so cannot run this outside emulation
        // mode: IntDummy reads a bare 16-bit PC and StackDummyReadDec drives a bare 0x0100 + SL,
        // and both are right on this part only because another invariant happens to hold. The
        // bank-aware ImpliedDummy (same Vpa classification as IntDummy) and
        // StackDummyReadDec816 compute the right address outright. No cycle of the sequence
        // changes: PBR is 0 after Reset() and SH is $01, so the two spellings agree cycle for
        // cycle today. VectorLo/VectorHi are shared unchanged — they read $FFFC/$FFFD as a
        // literal bank-0 address, which is correct on every variant including this one.
        ResetEntry = (ushort)ops.Count;
        ops.AddRange(variant == CpuVariant.W65C816
            ?
            [
                MicroOp.ImpliedDummy,
                MicroOp.ImpliedDummy,
                MicroOp.StackDummyReadDec816,
                MicroOp.StackDummyReadDec816,
                MicroOp.StackDummyReadDec816,
                MicroOp.VectorLo,
                MicroOp.VectorHi,
            ]
            : new[]
            {
                MicroOp.IntDummy,
                MicroOp.IntDummy,
                MicroOp.StackDummyReadDec,
                MicroOp.StackDummyReadDec,
                MicroOp.StackDummyReadDec,
                MicroOp.VectorLo,
                MicroOp.VectorHi,
            });
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

        // WDM ($42) is two bytes and two cycles, and its second byte is NEVER read: the cycle
        // that would fetch it is an internal cycle. Research document §14.2/§3.4, measured —
        // all 20,000 vectors show cycle 2 with a null value and the pin string "---r…", and PC
        // advancing by exactly 2. Clark §6.7's "The second byte is read, but ignored" is the
        // one claim on the point in any source and it is wrong; the vectors win, and §3.4
        // records it as a source error.
        //
        // So the cycle is row 19a's implied IO unchanged — ImpliedExec816 — and the whole of
        // Op.Wdm's operation is the second PC increment. Ahead of the implied/accumulator
        // branch below, which would otherwise emit the same micro-op and stop PC one byte
        // short; WDM's own AddrMode.ImmediateByte would fall through to EmitAddressed816 and
        // throw, so this branch is what gives the mode its meaning here.
        if (info.Operation == Op.Wdm)
        {
            ops.Add(MicroOp.ImpliedExec816);
            return;
        }

        // XBA is implied, but 3 cycles rather than the implied block's 2: research document
        // §13.5, datasheet Table 5-7 row 19b, which XBA has to itself. Its cycles 2 and 3 are
        // both row 19a's IO cycle at PBR,PC+1, so the sequence is that cycle twice over, with
        // the operation on the last one. Ahead of the general implied branch below, which stays
        // unconditional: making that branch test for XBA would put a run-time check on the path
        // every other implied opcode takes to get the shape only this one needs.
        if (info.Operation == Op.Xba)
        {
            ops.AddRange([MicroOp.ImpliedInternal816, MicroOp.ImpliedExec816]);
            return;
        }

        // WAI and STP. Research document §14.4, Table 5-7 rows 19d and 19c: three cycles each —
        // the opcode fetch, then TWO IO cycles at PBR,PC+1, which is XBA's row 19b shape with no
        // operation on either, hence ImpliedInternal816 twice. The hold is the fourth cycle, and
        // §14.4 measured what the vectors record for it: [null, null, "--------"], no address and
        // no access, in all 40,000. Ahead of the implied branch below, which would otherwise stop
        // at two cycles and never hold at all.
        if (info.Operation is Op.Wai or Op.Stp)
        {
            ops.AddRange([
                MicroOp.ImpliedInternal816, MicroOp.ImpliedInternal816,
                info.Operation == Op.Wai ? MicroOp.WaiHold816 : MicroOp.StpHold816,
            ]);
            return;
        }

        // Every 65816 implied and accumulator-mode instruction is two cycles: the opcode fetch,
        // then one internal cycle at PBR,PC+1 (research document §9 row 19a, the shape XCE
        // already uses). They fetch no operand, so they declare no Width and never reach a
        // width-deciding micro-op; each arm in Cpu.Exec tests the flag its own result depends on.
        if (info.Mode is AddrMode.Implied or AddrMode.Accumulator)
        {
            ops.Add(MicroOp.ImpliedExec816);
            return;
        }

        // The branches. Research document §14.5, Table 5-7 row 20: the displacement fetch, then
        // one conditional internal cycle if the branch is taken (Note 5), then a second one if
        // the taken branch crossed a page AND E is set (Note 6). Three micro-ops of its own
        // rather than the eight-bit BranchFetch/BranchTaken/BranchFixup, which compute a bare
        // sixteen-bit PC — W65C816ReachabilityTests asserts this core reaches none of them.
        if (info.Mode == AddrMode.Relative)
        {
            ops.AddRange([MicroOp.BranchFetch816, MicroOp.BranchTaken816, MicroOp.BranchFixup816]);
            return;
        }

        // BRL. Row 21: a flat four cycles with no conditional slot at all — no not-taken case
        // and no page-cross penalty in either mode. Its two displacement bytes are an ordinary
        // pair of PBR,PC operand fetches, so they reuse FetchAddrLo and FetchAddrHi and only the
        // internal cycle that performs the sixteen-bit add is new.
        if (info.Mode == AddrMode.RelativeLong)
        {
            ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHi, MicroOp.BranchLong816]);
            return;
        }

        // The two long control transfers, ahead of every mode test below. $5C (JML long) and
        // $22 (JSL long) both take AddrMode.AbsoluteLong, which EmitAddressed816 also handles
        // for LDA long and thirteen other addressed opcodes — so routing on the mode first would
        // hand a long jump a long LOAD's addressing sequence and an access tail, silently. The
        // operation is what distinguishes them, and it is tested here rather than inside
        // EmitAddressed816 so that method keeps reading info.Mode and info.Access alone.
        if (info.Operation is Op.Jml or Op.Jsl)
        {
            EmitControlFlow816(ops, info);
            return;
        }

        // Control flow, the stack and the interrupts do not decompose into an addressing phase
        // plus an access phase — the same reason EmitStack exists for the five 8-bit cores.
        // Routed by mode here and switched by operation there. AddrMode.Indirect,
        // AbsoluteIndexedIndirect and AbsoluteIndirectLong join AddrMode.Stack because the
        // disassembler formats an operand from them (Disassembler.Decode's own arms) while the
        // sequence they need is hand-written like every other jump's.
        if (info.Mode is AddrMode.Stack or AddrMode.Indirect
            or AddrMode.AbsoluteIndexedIndirect or AddrMode.AbsoluteIndirectLong)
        {
            EmitControlFlow816(ops, info);
            return;
        }

        // The block moves. Research document §14.3, Table 5-7 rows 9a/9b: seven cycles per byte
        // moved, of which the fetch is the first and these six are the rest. One instruction per
        // byte — BlockMoveNext rewinds PC rather than looping, so the sequence below runs to
        // MicroOp.End on every iteration and the next fetch re-enters it. Its own mode rather
        // than an operation test, since nothing else on the part has this shape.
        if (info.Mode == AddrMode.BlockMove)
        {
            ops.AddRange([
                MicroOp.BlockMoveDestBank, MicroOp.BlockMoveSrcBank,
                MicroOp.BlockMoveRead, MicroOp.BlockMoveWrite,
                MicroOp.BlockMoveInternal, MicroOp.BlockMoveNext,
            ]);
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
    /// The 65816's hand-written sequences: the pushes and pulls, the calls and returns, the
    /// interrupts, and the three stack-addressing pushes. Switched on
    /// <see cref="OpcodeInfo.Operation"/> rather than on the mode, because
    /// <see cref="AddrMode.Stack"/> covers instructions of one, two, three and four bytes and
    /// only the operation tells them apart — the same shape <see cref="EmitStack"/> has for
    /// the eight-bit cores, and the same shape <c>Disassembler.DecodeStack</c> relies on.
    /// </summary>
    private static void EmitControlFlow816(List<MicroOp> ops, OpcodeInfo info)
    {
        switch (info.Operation)
        {
            // Cycle 2 is one internal cycle at PBR,PC+1 that also forms the value; the high
            // slot is skipped when the push is one byte wide. Research document §14.1, row 22c.
            case Op.Pha or Op.Php or Op.Phx or Op.Phy or Op.Phb or Op.Phd or Op.Phk:
                ops.AddRange([
                    MicroOp.StackPushInternal816, MicroOp.PushHigh816, MicroOp.PushLow816,
                ]);
                break;

            // TWO internal cycles, both at PBR,PC+1 — that asymmetry with the push is the whole
            // of the one-cycle difference between rows 22b and 22c, and the datasheet flags it
            // in row 22b's own label ("Different than N6502") rather than in a note. Both are
            // ImpliedInternal816, XBA's cycle, because row 22b drives the identical address on
            // each; measured against 28.n, whose cycles 2 and 3 are both PBR,PC+1 and neither
            // is a stack address. PullLow816 ends the instruction when the pull is one byte
            // wide, so the high slot costs nothing.
            case Op.Pla or Op.Plp or Op.Plx or Op.Ply or Op.Plb or Op.Pld:
                ops.AddRange([
                    MicroOp.ImpliedInternal816, MicroOp.ImpliedInternal816,
                    MicroOp.PullLow816, MicroOp.PullHigh816,
                ]);
                break;

            // BRK and COP: research document §14.2, Table 5-7 row 22j — eight cycles native,
            // seven in emulation, where BrkPad816 skips the program-bank push (note 7). The two
            // differ in nothing but the vector Cpu.Vector816 picks from Op.Brk versus Op.Cop,
            // which is why one arm serves both. Cycles 3 to 8 are row 22a's verbatim, so the
            // five micro-ops from PushPbr816 down are the hardware interrupt sequence's own —
            // see the constructor's IrqEntry section.
            case Op.Brk or Op.Cop:
                ops.AddRange([
                    MicroOp.BrkPad816, MicroOp.PushPbr816,
                    MicroOp.PushPch816, MicroOp.PushPcl816, MicroOp.PushPInt816,
                    MicroOp.VectorLo816, MicroOp.VectorHi816,
                ]);
                break;

            // ---- Phase 7d task 7: the jumps, the calls and the returns. Research document
            // §14.6. Four operations here address memory more than one way, so these arms read
            // info.Mode as well as info.Operation — JMP has three forms, JSR and JML two each,
            // and only the mode tells them apart.

            // Row 1b: three cycles, the destination assembled from two operand fetches.
            case Op.Jmp when info.Mode == AddrMode.Stack:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.JmpAbs816]);
                break;

            // Row 3b: five cycles, a two-byte pointer in bank 0. NOT the eight-bit
            // JmpIndLo/JmpIndHi pair — that one reproduces the NMOS page-wrap bug, which Clark
            // §5.4 states this part does not have.
            case Op.Jmp when info.Mode == AddrMode.Indirect:
                ops.AddRange([
                    MicroOp.FetchAddrLo, MicroOp.FetchAddrHi,
                    MicroOp.JmpIndLo816, MicroOp.JmpIndHi816,
                ]);
                break;

            // Row 3a: row 3b plus the pointer's third byte, which becomes the program bank.
            case Op.Jml when info.Mode == AddrMode.AbsoluteIndirectLong:
                ops.AddRange([
                    MicroOp.FetchAddrLo, MicroOp.FetchAddrHi,
                    MicroOp.JmpIndLo816, MicroOp.JmpIndHi816, MicroOp.JmlIndBank816,
                ]);
                break;

            // Row 4b: row 1b's two fetches, then the bank byte. Reached through Emit816's
            // operation test, ahead of AddrMode.AbsoluteLong's addressed sequence.
            case Op.Jml:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHi, MicroOp.JmpLong816]);
                break;

            // Row 2a: six cycles, an unconditional indexing cycle, and a pointer in bank K.
            case Op.Jmp:
                ops.AddRange([
                    MicroOp.FetchAddrLo, MicroOp.FetchAddrHi, MicroOp.JmpAbsXInternal816,
                    MicroOp.JmpAbsXLo816, MicroOp.JmpAbsXHi816,
                ]);
                break;

            // Row 1c: six cycles. JsrFetchHi816 leaves PC on the last operand byte, which is
            // both the address the internal cycle drives and the value the two pushes take —
            // Clark §6.2.2.1's "one less than the address of the next instruction".
            case Op.Jsr when info.Mode == AddrMode.Stack:
                ops.AddRange([
                    MicroOp.FetchAddrLo, MicroOp.JsrFetchHi816, MicroOp.ImpliedInternal816,
                    MicroOp.PushPch816, MicroOp.JsrPushPcl816,
                ]);
                break;

            // Row 2b, and the one sequence in this task whose SHAPE is the finding: the two
            // pushes are cycles 3 and 4, before cycle 5 fetches AAH (§14.6 answer 3). Nothing
            // else on the part interleaves a push into the middle of operand fetching. The
            // pushed value is PC after the AAL fetch alone, which is already the instruction's
            // own address plus 2, so no adjustment is needed anywhere.
            case Op.Jsr:
                ops.AddRange([
                    MicroOp.FetchAddrLo, MicroOp.PushPch816, MicroOp.PushPcl816,
                    MicroOp.FetchAddrHi, MicroOp.JmpAbsXInternal816,
                    MicroOp.JmpAbsXLo816, MicroOp.JmpAbsXHi816,
                ]);
                break;

            // Row 4c: eight cycles, and the second place a sequence's order is the finding. The
            // OLD program bank is pushed at cycle 4, before cycle 6 reads the new one; cycle 5
            // is an internal cycle at a stack address, which no other instruction but RTS has.
            case Op.Jsl:
                ops.AddRange([
                    MicroOp.FetchAddrLo, MicroOp.FetchAddrHi,
                    MicroOp.PushPbr816, MicroOp.StackInternal816, MicroOp.JslBank816,
                    MicroOp.PushPch816, MicroOp.JsrPushPcl816,
                ]);
                break;

            // Row 22g: seven cycles native, six in emulation, where RtiPullPch816 skips the
            // program-bank pull (note 7). P comes FIRST — §14.6 answer 6 — and is restored
            // without the shared PullP's ~Flag.B mask, which is the index-width bit here.
            case Op.Rti:
                ops.AddRange([
                    MicroOp.ImpliedInternal816, MicroOp.ImpliedInternal816,
                    MicroOp.PullP816, MicroOp.PullPcl816, MicroOp.RtiPullPch816,
                    MicroOp.RtiPullPbr816,
                ]);
                break;

            // Row 22h: six cycles, the last an internal cycle at the stack byte just pulled.
            case Op.Rts:
                ops.AddRange([
                    MicroOp.ImpliedInternal816, MicroOp.ImpliedInternal816,
                    MicroOp.PullPcl816, MicroOp.ReturnPullPch816, MicroOp.StackInternal816,
                ]);
                break;

            // Row 22i: also six cycles, but for a different reason — the sixth is the bank pull,
            // not RTS's internal cycle, and RTL is three bytes in BOTH modes (no note 7 on the
            // row, and Clark gives a flat 6).
            case Op.Rtl:
                ops.AddRange([
                    MicroOp.ImpliedInternal816, MicroOp.ImpliedInternal816,
                    MicroOp.PullPcl816, MicroOp.ReturnPullPch816, MicroOp.PullPbr816,
                ]);
                break;

            // ---- Phase 7d task 8: the three stack-addressing pushes. Research document §14.7,
            // Table 5-7 rows 22d, 22e and 22f. All three end in the same unconditional
            // PushHigh816/PushLow816 pair — sixteen bits whatever m and x say (Clark §6.8.1) —
            // and differ only in how the four, five or three cycles before it form the value.

            // Row 22d: five cycles, and the shortest of the three. Two operand fetches and two
            // writes, with no internal cycle at all: PEA is the one instruction on the part that
            // pushes without one, because the value needs no computing.
            case Op.Pea:
                ops.AddRange([
                    MicroOp.FetchAddrLo, MicroOp.PeaFetchHi816,
                    MicroOp.PushHigh816, MicroOp.PushLow816,
                ]);
                break;

            // Row 22e: six cycles, seven when DL != $00 — the ONLY opcode in this phase carrying
            // a `w` term (research document §14.8), and the only direct-page instruction in it.
            // The first two micro-ops are the plain direct-page prefix, penalty slot included,
            // exactly as every dp addressing mode uses them.
            case Op.Pei:
                ops.AddRange([
                    MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty,
                    MicroOp.PtrReadLo816, MicroOp.PeiReadHigh816,
                    MicroOp.PushHigh816, MicroOp.PushLow816,
                ]);
                break;

            // Row 22f: six cycles. Two displacement bytes, then one internal cycle that performs
            // the add — the same shape BRL has, and for the same reason, but the result is pushed
            // rather than jumped to.
            case Op.Per:
                ops.AddRange([
                    MicroOp.FetchAddrLo, MicroOp.FetchAddrHi, MicroOp.PerCompute816,
                    MicroOp.PushHigh816, MicroOp.PushLow816,
                ]);
                break;

            default:
                throw new InvalidOperationException(
                    $"{info.Mnemonic}: {info.Operation} has no 65816 control-flow sequence.");
        }
    }

    /// <summary>
    /// Every 65816 addressing form, built directly against research document §9's per-mode
    /// blocks. Named <c>EmitDirectPage816</c> through phase 7b task 5, when it covered only the
    /// seven direct-page modes; phase 7b task 6 folded the remaining eight forms (absolute, long,
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
                // Code-review fix (task 5): which of DpPtrReadHiY (a plain read, which can skip
                // the indexing cycle) or DpPtrReadHiYWrite (never skips) is selected here, at
                // table-build time, from info.Access — not at run time from info.Operation. See
                // MicroOp.DpPtrReadHiY. Task 6's abs,X/abs,Y reuse the same mechanism below.
                //
                // The predicate is "not a plain read", not "is a write" — see AddrMode.AbsoluteX
                // below for why. (dp),Y has no read-modify-write opcode on this part, so the two
                // spellings pick the same micro-op here; it is written this way so all three
                // modes state one rule.
                ops.AddRange([
                    MicroOp.FetchDpOffset, MicroOp.DirectPagePenalty,
                    MicroOp.PtrReadLo816,
                    info.Access != Access.Read ? MicroOp.DpPtrReadHiYWrite : MicroOp.DpPtrReadHiY,
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
            // drives the former and installs the latter — selected at table-build time from
            // info.Access, the same mechanism (dp),Y above uses.
            //
            // The predicate is `!= Access.Read`, matching EmitAddressing's own predicate for the
            // five 8-bit cores, and the difference is load-bearing for exactly one family: an
            // indexed READ-MODIFY-WRITE pays the indexing cycle unconditionally, like a write and
            // unlike a read. Research document §13.1's row 6b puts NO note on that cycle — where
            // §9's read row 6a carries Note 4 there and skips it when x=1 without a page cross —
            // and Clark's flat 9-2*m for abs,X RMW (§13.3) has no p term and no x term, which
            // says the same thing independently; §13.6 lists it among the settled questions.
            // Spelled `== Access.Write`, ASL/LSR/ROL/ROR abs,X would take the skip and cost
            // 8-2m instead of 9-2m whenever x=1 and indexing did not cross a page.
            case AddrMode.AbsoluteX:
                ops.AddRange([
                    MicroOp.FetchAddrLo,
                    info.Access != Access.Read ? MicroOp.AbsHiIndexedXWrite : MicroOp.AbsHiIndexedX,
                    MicroOp.AbsIndexFixup,
                ]);
                break;

            // §9's "Absolute,Y — row 7": identical in shape to AbsoluteX, Y substituted for X.
            case AddrMode.AbsoluteY:
                ops.AddRange([
                    MicroOp.FetchAddrLo,
                    info.Access != Access.Read ? MicroOp.AbsHiIndexedYWrite : MicroOp.AbsHiIndexedY,
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

        // Code-review fix (phase 7b task 5), extended by phase 7b task 6 and again by phase 7c
        // task 7: only the three plain direct-page forms — dp, dp,X and dp,Y — and plain
        // stack-relative are bank-0-confined (their data access is 0,D+DO[+X|+Y] or 0,S+SO);
        // dp,Y joined the set with the mode itself in phase 7c task 7, since it is direct-page
        // addressing and confined exactly as dp,X is. Every other mode's final access goes
        // through DBR or the operand's own bank byte, and its "+1" must carry into the next bank
        // rather than wrap — Clark §5.2 Example 2, cited at Cpu.HighByteAddressCarry. (sr,S),Y is
        // NOT in the bank-0-confined set despite sharing sr,S's bank-0 pointer fetch: phase 7b
        // task 6 review found this mode's
        // *final* access, DBR,AA+Y (§9 row 24, cycles 7/7a), goes through DBR exactly like
        // (dp),Y's does — indistinguishable from (dp),Y's bank-carry requirement, and wrongly
        // grouped with plain sr,S here originally. Zero vector coverage: catching it needs
        // M=0 with the indexed pointer landing exactly on $xxFFFF, which no SingleStepTests
        // vector for $B3/$93 happens to hit across 10,000 tries each.
        var carry = info.Mode is not (AddrMode.DirectPage or AddrMode.DirectPageX
            or AddrMode.DirectPageY or AddrMode.StackRelative);

        switch (info.Access)
        {
            case Access.Write:
                ops.Add(MicroOp.ExecWrite816);
                ops.Add(carry ? MicroOp.ExecWriteHigh816Carry : MicroOp.ExecWriteHigh816);
                break;

            // Six slots, of which any one execution runs three (8-bit) or five (16-bit). The rest
            // are skipped by the preceding micro-op, the same conditional-slot idiom
            // DirectPagePenalty uses — which is what keeps every one of them statically
            // classified in MicroOps.IsWriteCycle, consulted on every tick of all six cores
            // because RDY must never halt a write. Datasheet Note 17 decides which of the two
            // middle forms runs, at run time, from E.
            case Access.ReadModifyWrite:
                ops.AddRange([
                    MicroOp.RmwRead816,
                    carry ? MicroOp.RmwReadHigh816Carry : MicroOp.RmwReadHigh816,
                    MicroOp.RmwModifyWrite816,
                    carry ? MicroOp.RmwModifyRead816Carry : MicroOp.RmwModifyRead816,
                    carry ? MicroOp.RmwWriteHigh816Carry : MicroOp.RmwWriteHigh816,
                    MicroOp.RmwWrite816,
                ]);
                break;

            default:
                ops.Add(MicroOp.ReadExec816);
                ops.Add(carry ? MicroOp.ReadExecHigh816Carry : MicroOp.ReadExecHigh816);
                break;
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
