using System.Runtime.CompilerServices;

namespace SixtyFiveXX;

/// <summary>
/// A cycle-stepped 65xx core. Each <see cref="Tick"/> advances the CPU by exactly one
/// clock cycle and performs at most one bus access.
/// </summary>
/// <typeparam name="TBus">
/// The bus this core talks to. A <c>struct</c> type parameter so the JIT specializes
/// the core and inlines every memory access; use <see cref="RefBus"/> to adapt a bus
/// chosen at run time.
/// </typeparam>
public sealed partial class Cpu<TBus> where TBus : struct, IBus
{
    /// <summary>Address of the NMI vector.</summary>
    public const int NmiVector = 0xFFFA;

    /// <summary>Address of the RESET vector.</summary>
    public const int ResetVector = 0xFFFC;

    /// <summary>Address of the IRQ and BRK vector.</summary>
    public const int IrqVector = 0xFFFE;

    private TBus _bus;
    private readonly MicroOpTable _table;
    private readonly MicroOp[] _ops;
    private readonly ushort[] _entry;

    private CpuState _s;
    private long _cycles;

    /// <summary>Index into <see cref="_ops"/>; negative means the next tick fetches an opcode.</summary>
    private int _mpc = -1;

    private Op _op;

    /// <summary>The value being read, written, or modified by the current instruction.</summary>
    private byte _data;

    /// <summary>The effective address.</summary>
    private int _addr;

    /// <summary>Set when indexing carried out of the low byte of the effective address.</summary>
    private bool _pageCross;

    /// <summary>Scratch for the low byte of a 16-bit quantity assembled across two cycles.</summary>
    private byte _tmp;

    /// <summary>The indirect pointer address, for the (zp,X) and (zp),Y modes.</summary>
    private int _ptr;

    /// <summary>+0x100 or -0x100, applied by <see cref="MicroOp.BranchFixup"/>.</summary>
    private int _branchFix;

    /// <summary>The vector the in-progress interrupt or BRK sequence will read.</summary>
    private int _vector = IrqVector;

    /// <summary>High byte of an unstable store's target address, plus one.</summary>
    private byte _storeHigh;

    /// <summary>Position within the jammed address-bus pattern.</summary>
    private int _jamPhase;

    /// <summary>Set by <c>JamHold</c>; cleared only by <see cref="Reset"/>.</summary>
    private bool _jammed;

    /// <summary>Current level on the IRQ pin. Level-sensitive, not latched.</summary>
    private bool _irqLine;

    /// <summary>Current level on the NMI pin, tracked only to detect a rising edge.</summary>
    private bool _nmiLine;

    /// <summary>
    /// Latched by a rising edge on NMI and cleared when the interrupt is serviced — either
    /// by <see cref="FetchOpcode"/> dispatching it at an instruction boundary, or by
    /// <c>MicroOp.VectorLo</c> hijacking a BRK or IRQ sequence already in flight. NMI is
    /// edge-triggered, so this survives the line going low again and holding it high does
    /// not produce a second interrupt.
    /// </summary>
    private bool _nmiPending;

    /// <summary>Level on the RDY pin. Low halts the processor on read cycles.</summary>
    private bool _rdy = true;

    /// <summary>
    /// Interrupt poll result, recomputed at the start of every cycle that continues an
    /// in-progress instruction — except a fetch cycle, which only reads this, and a cycle
    /// held by RDY, which returns before reaching the assignment; both leave the prior
    /// value untouched. At an instruction boundary this therefore holds the value from the
    /// start of the final cycle, the same instant a real 6502 samples during phase 2 of the
    /// penultimate cycle. True when either an NMI is latched or IRQ is asserted with
    /// <c>I</c> clear; NMI is never blocked by <c>I</c>.
    /// </summary>
    private bool _intPoll;

    /// <summary>Creates a core over the given bus.</summary>
    public Cpu(TBus bus)
    {
        _bus = bus;
        _table = MicroOpTable.Mos6502;
        _ops = _table.Ops;
        _entry = _table.Entry;
    }

    /// <summary>The register file. Mutable by reference so tests and debuggers can set state directly.</summary>
    public ref CpuState State => ref _s;

    /// <summary>Total cycles executed since construction or the last <see cref="ResetCycleCount"/>.</summary>
    public long Cycles => _cycles;

    /// <summary>Zeroes the cycle counter without disturbing CPU state.</summary>
    public void ResetCycleCount() => _cycles = 0;

    /// <summary>True when the next <see cref="Tick"/> will fetch an opcode.</summary>
    public bool AtInstructionBoundary => _mpc < 0;

    /// <summary>
    /// True once a JAM opcode has halted the processor. Only <see cref="Reset"/> clears
    /// it. A jammed core keeps driving the address bus if ticked, exactly as the silicon
    /// does, but never executes another instruction.
    /// </summary>
    public bool IsJammed => _jammed;

    /// <summary>The bus. Exposed so a caller holding only the CPU can reach its memory.</summary>
    public ref TBus Bus => ref _bus;

    /// <summary>The current level on the IRQ pin.</summary>
    public bool IrqLine => _irqLine;

    /// <summary>
    /// Drives the IRQ pin. The line is level-sensitive: while it is held asserted and the
    /// interrupt-disable flag is clear, an interrupt is taken at each instruction boundary.
    /// </summary>
    public void SetIrq(bool asserted) => _irqLine = asserted;

    /// <summary>The current level on the NMI pin.</summary>
    public bool NmiLine => _nmiLine;

    /// <summary>
    /// Drives the NMI pin. NMI is edge-triggered: only a low-to-high transition latches an
    /// interrupt, and that latch survives the line being released. Holding the line high
    /// produces exactly one interrupt, not a stream of them.
    /// </summary>
    public void SetNmi(bool asserted)
    {
        if (asserted && !_nmiLine) _nmiPending = true;
        _nmiLine = asserted;
    }

    /// <summary>The current level on the RDY pin. True means the processor runs freely.</summary>
    public bool Ready => _rdy;

    /// <summary>
    /// Drives the RDY pin. Pulling it low halts the processor on its next read cycle; a
    /// write already in progress completes. A halted processor keeps driving the address
    /// bus, which is how a video chip steals cycles without disturbing the CPU's state.
    /// </summary>
    public void SetRdy(bool ready) => _rdy = ready;

    /// <summary>
    /// Pulses the SO pin, setting the overflow flag. Nothing clears it but an instruction
    /// that writes P.
    /// </summary>
    public void SetSo() => _s.V = true;

    /// <summary>
    /// Advances the core by one clock cycle — or, while RDY is held low on a read cycle,
    /// re-drives the address bus without otherwise advancing.
    /// </summary>
    public void Tick()
    {
        _cycles++;

        if (!_rdy && !IsWriteCycleNext())
        {
            // Halted: re-drive the address bus without advancing. One access, as always.
            // A cycle skipped this way never reaches the poll below, so a halt mid-
            // instruction leaves _intPoll holding whatever the last live cycle computed —
            // exactly as if the clock itself had stopped, which is what RDY models.
            _bus.Read(_mpc < 0 ? _s.PC : _addr);
            return;
        }

        if (_mpc < 0)
        {
            // A fetch cycle does no polling of its own: it consults whatever _intPoll
            // was left holding by the instruction that just finished (see below), the
            // same instant real hardware samples during phase 2 of the penultimate cycle.
            FetchOpcode();
            return;
        }

        // Poll before this cycle's own work. When this is the last cycle of the
        // instruction, the value computed here — using register state from before this
        // cycle's Execute — is what the next fetch cycle above will act on. This is what
        // makes CLI delay a pending IRQ by one instruction while SEI fails to block one,
        // with no special case for either: the flag write and the poll simply land in the
        // same cycle for CLI/SEI, and the poll happens first. NMI is never blocked by I.
        _intPoll = _nmiPending || (_irqLine && !_s.I);

        var micro = _ops[_mpc];
        _mpc++;
        Execute(micro);

        // A micro-op may have ended the instruction early (an untaken branch, or an
        // indexed read that did not cross a page). Otherwise the sequence ends when
        // the next slot is the terminator.
        if (_mpc >= 0 && _ops[_mpc] == MicroOp.End) _mpc = -1;
    }

    /// <summary>True when the cycle about to run is a write. RDY cannot halt a write.</summary>
    private bool IsWriteCycleNext() => _mpc >= 0 && MicroOps.IsWriteCycle(_ops[_mpc]);

    /// <summary>
    /// Begins a hardware reset. The sequence takes seven cycles; drive it with
    /// <see cref="Step"/> or seven calls to <see cref="Tick"/>.
    /// </summary>
    /// <remarks>
    /// Reset does not clear the registers. Real hardware leaves A, X, Y and most of P
    /// undisturbed; it sets I, decrements S three times, and loads PC from $FFFC.
    /// </remarks>
    public void Reset()
    {
        _s.I = true;
        _vector = ResetVector;
        _mpc = _table.ResetEntry;
        _jammed = false;
        _jamPhase = 0;
    }

    /// <summary>
    /// Runs to the next instruction boundary, or returns early if the processor jams.
    /// </summary>
    /// <remarks>
    /// Nothing inside this loop can release a held RDY line, so it never returns while
    /// RDY is expected to stay low on a read cycle. A caller driving RDY must step the
    /// processor with <see cref="Tick"/> or <see cref="Run"/> instead.
    /// </remarks>
    /// <returns>The number of cycles consumed.</returns>
    public long Step()
    {
        var before = _cycles;
        do
        {
            Tick();
        }
        while (_mpc >= 0 && !_jammed);

        return _cycles - before;
    }

    /// <summary>
    /// Runs for at least <paramref name="cycles"/> cycles, stopping mid-instruction if
    /// the budget runs out.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Step"/> and <see cref="RunUntil"/>, this keeps ticking even
    /// once the processor has jammed: a real clock keeps running, and the jammed core
    /// keeps driving the address bus, so there is no early exit here.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cycles"/> is negative.</exception>
    /// <returns>The total cycle count after running.</returns>
    public long Run(long cycles)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cycles);

        var target = _cycles + cycles;
        while (_cycles < target) Tick();
        return _cycles;
    }

    /// <summary>
    /// Runs whole instructions until <paramref name="stop"/> returns true at an
    /// instruction boundary, until <paramref name="maxCycles"/> is exhausted, or until
    /// the processor jams.
    /// </summary>
    /// <param name="stop">Evaluated at each instruction boundary, never mid-instruction.</param>
    /// <param name="maxCycles">A ceiling, so a runaway program cannot hang the caller.</param>
    /// <remarks>
    /// Calls <see cref="Step"/> internally and so inherits the same hazard: nothing in
    /// here can release a held RDY line, so it never returns while RDY is expected to
    /// stay low on a read cycle. A caller driving RDY must step the processor with
    /// <see cref="Tick"/> or <see cref="Run"/> instead.
    /// </remarks>
    /// <returns>The number of cycles consumed.</returns>
    public long RunUntil(Func<Cpu<TBus>, bool> stop, long maxCycles = long.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(stop);

        var before = _cycles;
        while (_cycles - before < maxCycles)
        {
            Step();
            if (_jammed) break;
            if (stop(this)) break;
        }

        return _cycles - before;
    }

    private void FetchOpcode()
    {
        if (_intPoll)
        {
            // Take the interrupt instead of an instruction. Hardware spends two cycles
            // reading (and discarding) PC before the pushes begin, mirroring BRK's opcode
            // fetch plus its signature-byte pad; this read supplies the first of the two,
            // and IrqEntry's own IntDummy supplies the second, so entry is at the
            // sequence's start, not past it. Reset() has no opcode to fetch and so cannot
            // rely on this free read — see MicroOpTable.ResetEntry, which spells out both
            // dummy reads itself.
            _bus.Read(_s.PC);
            // NMI outranks IRQ, and servicing it consumes the latch. IRQ is level-sensitive
            // and so needs no clearing — it fires again next boundary if still asserted.
            if (_nmiPending)
            {
                _nmiPending = false;
                _vector = NmiVector;
            }
            else
            {
                _vector = IrqVector;
            }
            _mpc = _table.IrqEntry;
            return;
        }

        var pc = _s.PC;
        var opcode = _bus.Read(pc);
        _s.PC++;

        var entry = _entry[opcode];
        if (_ops[entry] == MicroOp.End) throw new UndefinedOpcodeException(opcode, pc);

        _op = _table.Info[opcode].Operation;
        _mpc = entry;
    }

    /// <summary>Ends the current instruction; the next tick fetches an opcode.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EndInstruction() => _mpc = -1;

    /// <summary>Evaluates the current branch instruction's condition.</summary>
    private bool IsBranchTaken() => _op switch
    {
        Op.Bcc => !_s.C,
        Op.Bcs => _s.C,
        Op.Bne => !_s.Z,
        Op.Beq => _s.Z,
        Op.Bpl => !_s.N,
        Op.Bmi => _s.N,
        Op.Bvc => !_s.V,
        Op.Bvs => _s.V,
        _ => throw new InvalidOperationException($"{_op} is not a branch."),
    };

    /// <summary>The value an unstable store will write, before the address fold-in.</summary>
    private byte UnstableStoreValue() => _op switch
    {
        Op.Sha => (byte)(_s.A & _s.X & _storeHigh),
        Op.Shx => (byte)(_s.X & _storeHigh),
        Op.Shy => (byte)(_s.Y & _storeHigh),
        Op.Tas => (byte)(_s.A & _s.X & _storeHigh),
        _ => throw new InvalidOperationException($"{_op} is not an unstable store."),
    };

    private void Execute(MicroOp micro)
    {
        switch (micro)
        {
            case MicroOp.ImpliedExec:
                _bus.Read(_s.PC);
                Exec();
                break;

            case MicroOp.ImmExec:
                _data = _bus.Read(_s.PC);
                _s.PC++;
                Exec();
                break;

            case MicroOp.ImpliedDummy:
                _bus.Read(_s.PC);
                break;

            case MicroOp.FetchAddrLo:
                _addr = _bus.Read(_s.PC);
                _s.PC++;
                break;

            case MicroOp.FetchAddrHi:
                _addr |= _bus.Read(_s.PC) << 8;
                _s.PC++;
                break;

            case MicroOp.ReadExec:
                _data = _bus.Read(_addr);
                Exec();
                break;

            case MicroOp.ExecWrite:
                Exec();
                _bus.Write(_addr, _data);
                break;

            case MicroOp.RmwRead:
                _data = _bus.Read(_addr);
                break;

            case MicroOp.RmwModifyWrite:
                // NMOS parts write the unmodified value back before writing the result.
                _bus.Write(_addr, _data);
                Exec();
                break;

            case MicroOp.RmwWrite:
                _bus.Write(_addr, _data);
                break;

            case MicroOp.FetchAddrHiX:
            {
                var hi = _bus.Read(_s.PC);
                _s.PC++;
                var lo = (_addr & 0xFF) + _s.X;
                _pageCross = lo > 0xFF;
                _addr = (hi << 8) | (lo & 0xFF);
                break;
            }

            case MicroOp.FetchAddrHiY:
            {
                var hi = _bus.Read(_s.PC);
                _s.PC++;
                var lo = (_addr & 0xFF) + _s.Y;
                _pageCross = lo > 0xFF;
                _addr = (hi << 8) | (lo & 0xFF);
                break;
            }

            case MicroOp.ZpIndexX:
                _bus.Read(_addr);                      // dummy read at the unindexed address
                _addr = (_addr + _s.X) & 0xFF;         // page zero indexing wraps within the page
                break;

            case MicroOp.ZpIndexY:
                _bus.Read(_addr);
                _addr = (_addr + _s.Y) & 0xFF;
                break;

            case MicroOp.ReadPageCross:
                // The read happens either way. Without a page cross it is the real read
                // and the instruction is done; with one it was a read of the wrong
                // address and the next micro-op re-reads the corrected one.
                _data = _bus.Read(_addr);
                if (_pageCross)
                {
                    _addr = (_addr + 0x100) & 0xFFFF;
                }
                else
                {
                    Exec();
                    EndInstruction();
                }
                break;

            case MicroOp.DummyReadFixup:
                _bus.Read(_addr);
                if (_pageCross) _addr = (_addr + 0x100) & 0xFFFF;
                break;

            case MicroOp.UnstableStoreFixup:
                _bus.Read(_addr);
                // The value these instructions store is ANDed with the target's high
                // byte plus one. On a page cross the AND result also becomes the high
                // byte, so the write lands somewhere other than the nominal address.
                _storeHigh = (byte)(((_addr >> 8) & 0xFF) + 1);
                if (_pageCross)
                {
                    _addr = (_addr & 0x00FF) | (UnstableStoreValue() << 8);
                }
                break;

            case MicroOp.PtrReadLo:
                _ptr = _addr;
                _tmp = _bus.Read(_ptr);
                break;

            case MicroOp.PtrReadHi:
                // The pointer's high byte wraps within page zero.
                _addr = (_bus.Read((_ptr + 1) & 0xFF) << 8) | _tmp;
                break;

            case MicroOp.PtrReadHiY:
            {
                var hi = _bus.Read((_ptr + 1) & 0xFF);
                var lo = _tmp + _s.Y;
                _pageCross = lo > 0xFF;
                _addr = (hi << 8) | (lo & 0xFF);
                break;
            }

            case MicroOp.BranchFetch:
                _data = _bus.Read(_s.PC);
                _s.PC++;
                if (!IsBranchTaken()) EndInstruction();
                break;

            case MicroOp.BranchTaken:
            {
                _bus.Read(_s.PC);                       // dummy read at the byte after the branch
                var lo = (_s.PC & 0xFF) + (sbyte)_data;
                _branchFix = lo < 0 ? -0x100 : lo > 0xFF ? 0x100 : 0;
                _s.PC = (ushort)((_s.PC & 0xFF00) | (lo & 0xFF));
                if (_branchFix == 0) EndInstruction();
                break;
            }

            case MicroOp.BranchFixup:
                _bus.Read(_s.PC);                       // dummy read at the un-fixed PC
                _s.PC = (ushort)(_s.PC + _branchFix);
                break;

            case MicroOp.StackDummyRead:
                _bus.Read(0x0100 + _s.S);
                break;

            case MicroOp.StackDummyReadInc:
                _bus.Read(0x0100 + _s.S);
                _s.S++;
                break;

            case MicroOp.PushPch:
                _bus.Write(0x0100 + _s.S, (byte)(_s.PC >> 8));
                _s.S--;
                break;

            case MicroOp.PushPcl:
                _bus.Write(0x0100 + _s.S, (byte)_s.PC);
                _s.S--;
                break;

            // JMP absolute and JSR both finish by reading the high byte at PC and
            // combining it with the low byte already in _addr.
            case MicroOp.JmpAbs:
            case MicroOp.JsrFinish:
                _s.PC = (ushort)((_bus.Read(_s.PC) << 8) | _addr);
                break;

            case MicroOp.JmpIndLo:
                _ptr = _addr;
                _tmp = _bus.Read(_ptr);
                break;

            case MicroOp.JmpIndHi:
                // NMOS bug: the vector's high byte is fetched from the same page, so
                // JMP ($xxFF) reads its high byte from $xx00.
                _s.PC = (ushort)((_bus.Read((_ptr & 0xFF00) | ((_ptr + 1) & 0xFF)) << 8) | _tmp);
                break;

            case MicroOp.PullPcl:
                _tmp = _bus.Read(0x0100 + _s.S);
                _s.S++;
                break;

            case MicroOp.PullPch:
                _s.PC = (ushort)((_bus.Read(0x0100 + _s.S) << 8) | _tmp);
                break;

            case MicroOp.RtsFinish:
                _bus.Read(_s.PC);
                _s.PC++;
                break;

            case MicroOp.PullP:
                // B exists only in pushed copies of P; U always reads as set.
                _s.P = (byte)((_bus.Read(0x0100 + _s.S) & ~Flag.B) | Flag.U);
                _s.S++;
                break;

            case MicroOp.Push:
                Exec();
                _bus.Write(0x0100 + _s.S, _data);
                _s.S--;
                break;

            case MicroOp.Pull:
                _data = _bus.Read(0x0100 + _s.S);
                Exec();
                break;

            case MicroOp.BrkPad:
                _bus.Read(_s.PC);      // BRK's signature byte, fetched and discarded
                _s.PC++;
                break;

            case MicroOp.IntDummy:
                _bus.Read(_s.PC);
                break;

            case MicroOp.PushPBrk:
                _bus.Write(0x0100 + _s.S, (byte)(_s.P | Flag.B | Flag.U));
                _s.S--;
                _s.I = true;
                _vector = IrqVector;
                break;

            case MicroOp.PushPInt:
                // Deliberately does not set _vector: only the dispatcher knows whether
                // this is an IRQ or an NMI, and they use different vectors. FetchOpcode
                // sets it before entering this sequence.
                _bus.Write(0x0100 + _s.S, (byte)((_s.P | Flag.U) & ~Flag.B));
                _s.S--;
                _s.I = true;
                break;

            case MicroOp.VectorLo:
                // An NMI that arrives before the vector is read hijacks the BRK or IRQ
                // sequence in progress: the pushes already happened with whatever B flag
                // the original interrupt used, but control lands in the NMI handler. Only
                // an IRQ-vectored sequence can be hijacked — reset outranks NMI on real
                // hardware, and an NMI already in progress must leave a later latch alone
                // so it can fire again.
                if (_nmiPending && _vector == IrqVector)
                {
                    _nmiPending = false;
                    _vector = NmiVector;
                }
                _tmp = _bus.Read(_vector);
                break;

            case MicroOp.VectorHi:
                _s.PC = (ushort)((_bus.Read(_vector + 1) << 8) | _tmp);
                break;

            case MicroOp.StackDummyReadDec:
                _bus.Read(0x0100 + _s.S);
                _s.S--;
                break;

            case MicroOp.JamHold:
                // The address bus cycles $FFFF, $FFFE, $FFFE, then $FFFF forever.
                // ponytail: RDY-low during a jam is unmodelled — JamHold isn't a write, so
                // Tick's halt branch intercepts it first and re-reads _addr instead of
                // advancing this pattern, freezing _jamPhase instead of continuing the real
                // bus cycling. No test pins the halted address yet. If that ever matters,
                // give the halt branch a jammed-aware read (or let JamHold run through RDY).
                _jammed = true;
                _bus.Read(_jamPhase switch
                {
                    0 => 0xFFFF,
                    1 => 0xFFFE,
                    2 => 0xFFFE,
                    _ => 0xFFFF,
                });
                if (_jamPhase < 3) _jamPhase++;
                _mpc--;             // hold position: this micro-op repeats forever
                break;

            default:
                throw new NotImplementedException($"Micro-op {micro} is not implemented yet.");
        }
    }
}
