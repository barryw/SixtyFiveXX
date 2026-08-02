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
        CpuVariant.Mos6502 => Opcodes6502.Table,
        CpuVariant.Wdc65C02 or CpuVariant.Rockwell65C02 or CpuVariant.Synertek65C02 => Opcodes65C02.Table,
        _ => throw new NotSupportedException($"No opcode table for {variant} yet."),
    };

    /// <summary>
    /// Which P-push micro-op a variant's BRK and hardware-interrupt sequences use.
    /// </summary>
    /// <remarks>
    /// The NMOS and CMOS families differ on exactly one cycle of every interrupt entry —
    /// CMOS clears <c>D</c> and performs no NMI hijack — so the difference is resolved
    /// here, once per variant at table-build time, by emitting a different micro-op. The
    /// alternative, a variant test inside the micro-op itself, would put a branch on the
    /// interrupt path and defeat the reason the variant is a type parameter at all.
    /// </remarks>
    private static (MicroOp Brk, MicroOp Interrupt) InterruptPushesFor(CpuVariant variant) => variant switch
    {
        CpuVariant.Wdc65C02 or CpuVariant.Rockwell65C02 or CpuVariant.Synertek65C02 =>
            (MicroOp.PushPBrkCmos, MicroOp.PushPIntCmos),
        _ => (MicroOp.PushPBrk, MicroOp.PushPInt),
    };

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

        var pushes = InterruptPushesFor(variant);
        var ops = new List<MicroOp>(2048);

        for (var opcode = 0; opcode < 256; opcode++)
        {
            Entry[opcode] = (ushort)ops.Count;
            Emit(ops, info[opcode], pushes.Brk);
            ops.Add(MicroOp.End);
        }

        IrqEntry = (ushort)ops.Count;
        ops.AddRange([
            MicroOp.IntDummy,
            MicroOp.PushPch,
            MicroOp.PushPcl,
            pushes.Interrupt,
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

    private static void Emit(List<MicroOp> ops, OpcodeInfo info, MicroOp brkPushP)
    {
        if (info.Operation == Op.Undefined) return;

        // Hand-written sequences: control flow and stack instructions do not decompose
        // into an addressing phase plus an access phase.
        if (info.Mode == AddrMode.Stack)
        {
            EmitStack(ops, info.Operation, brkPushP);
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
            ops.Add(MicroOp.ImmExec);
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

        EmitAddressing(ops, info.Mode, info.Access);
        EmitAccess(ops, info.Mode, info.Access);
    }

    /// <summary>Emits the cycles that form the effective address, up to but excluding the access.</summary>
    private static void EmitAddressing(List<MicroOp> ops, AddrMode mode, Access access)
    {
        switch (mode)
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
                // Writes and read-modify-writes always pay the fixup cycle; reads only
                // pay it on an actual page cross, which ReadPageCross decides at run time.
                if (access != Access.Read) ops.Add(MicroOp.DummyReadFixup);
                break;

            case AddrMode.AbsoluteY:
                ops.AddRange([MicroOp.FetchAddrLo, MicroOp.FetchAddrHiY]);
                if (access != Access.Read) ops.Add(MicroOp.DummyReadFixup);
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
                if (access != Access.Read) ops.Add(MicroOp.DummyReadFixup);
                break;

            default:
                throw new InvalidOperationException($"{mode} has no addressing sequence.");
        }
    }

    /// <summary>Emits the cycles that read, write, or read-modify-write the effective address.</summary>
    private static void EmitAccess(List<MicroOp> ops, AddrMode mode, Access access)
    {
        var indexedRead = access == Access.Read &&
                          mode is AddrMode.AbsoluteX or AddrMode.AbsoluteY or AddrMode.IndirectIndexed;

        switch (access)
        {
            case Access.Read when indexedRead:
                ops.AddRange([MicroOp.ReadPageCross, MicroOp.ReadExec]);
                break;

            case Access.Read:
                ops.Add(MicroOp.ReadExec);
                break;

            case Access.Write:
                ops.Add(MicroOp.ExecWrite);
                break;

            case Access.ReadModifyWrite:
                ops.AddRange([MicroOp.RmwRead, MicroOp.RmwModifyWrite, MicroOp.RmwWrite]);
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
