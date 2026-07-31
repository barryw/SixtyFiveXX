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
    /// <summary>The MOS 6502 table. Built once, on first use.</summary>
    public static readonly MicroOpTable Mos6502 = new(Opcodes6502.Table);

    /// <summary>Every opcode's micro-op sequence, concatenated, each terminated by <see cref="MicroOp.End"/>.</summary>
    public readonly MicroOp[] Ops;

    /// <summary>Opcode byte to its first micro-op's index in <see cref="Ops"/>.</summary>
    public readonly ushort[] Entry;

    /// <summary>The descriptors this table was built from.</summary>
    public readonly OpcodeInfo[] Info;

    /// <summary>
    /// Index of the hardware interrupt sequence in <see cref="Ops"/>. The caller must set
    /// the CPU's vector field to <c>NmiVector</c> or <c>IrqVector</c> before entering this
    /// sequence — the sequence itself never sets it, since only the dispatcher knows
    /// which interrupt is being serviced.
    /// </summary>
    public readonly ushort IrqEntry;

    /// <summary>Index of the reset sequence in <see cref="Ops"/>.</summary>
    public readonly ushort ResetEntry;

    private MicroOpTable(OpcodeInfo[] info)
    {
        Info = info;
        Entry = new ushort[256];

        var ops = new List<MicroOp>(2048);

        for (var opcode = 0; opcode < 256; opcode++)
        {
            Entry[opcode] = (ushort)ops.Count;
            Emit(ops, info[opcode]);
            ops.Add(MicroOp.End);
        }

        IrqEntry = (ushort)ops.Count;
        ops.AddRange([
            MicroOp.IntDummy,
            MicroOp.PushPch,
            MicroOp.PushPcl,
            MicroOp.PushPInt,
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

    private static void Emit(List<MicroOp> ops, OpcodeInfo info)
    {
        if (info.Operation == Op.Undefined) return;

        // Hand-written sequences: control flow and stack instructions do not decompose
        // into an addressing phase plus an access phase.
        if (info.Mode == AddrMode.Stack)
        {
            EmitStack(ops, info.Operation);
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

    private static void EmitStack(List<MicroOp> ops, Op op)
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
                    MicroOp.PushPBrk, MicroOp.VectorLo, MicroOp.VectorHi,
                ]);
                break;

            case Op.Pha or Op.Php:
                ops.AddRange([MicroOp.ImpliedDummy, MicroOp.Push]);
                break;

            case Op.Pla or Op.Plp:
                ops.AddRange([MicroOp.ImpliedDummy, MicroOp.StackDummyReadInc, MicroOp.Pull]);
                break;

            default:
                throw new InvalidOperationException($"{op} has no stack sequence.");
        }
    }
}
