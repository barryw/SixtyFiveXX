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
/// <typeparam name="TVariant">
/// The member of the 65xx family this core models. A <c>struct</c> type parameter, like
/// <typeparamref name="TBus"/>, so <see cref="ICpuVariant"/>'s <c>static abstract</c>
/// members resolve at compile time with no virtual dispatch on the per-cycle path.
/// </typeparam>
public sealed partial class Cpu<TBus, TVariant> where TBus : struct, IBus where TVariant : struct, ICpuVariant
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

    /// <summary>
    /// The 8-bit view of the register file.
    /// </summary>
    /// <remarks>
    /// <see cref="CpuState"/> is sized for the 65816, so its registers are 16 bits wide on
    /// every variant. The five 8-bit cores read and write only the low byte, and these
    /// shims say so once rather than scattering casts across two hundred use sites.
    /// <para>
    /// The setters assign the whole 16-bit field, which is correct here because these cores
    /// never put anything in the high byte. The getters are what matter: <c>X8--</c> through
    /// this property wraps at 8 bits, as an 8-bit register must, whereas <c>_s.X--</c> on
    /// the raw field takes $00 to $FFFF instead.
    /// </para>
    /// <para>
    /// Named with an explicit <c>8</c> suffix, rather than the bare register letter, so that
    /// 65816 code sharing this <c>partial class</c> cannot assign a 16-bit value here by
    /// accident: <c>A = someUshortValue</c> would silently truncate to 8 bits with no
    /// compile error if this property were named <c>A</c>. The suffix makes that a
    /// deliberate, visible choice instead of a typo.
    /// </para>
    /// <para>
    /// <see cref="S8"/> shares this shape but is documented on its own rather than through
    /// <c>&lt;inheritdoc cref="A8"/&gt;</c>: its setter also enforces a 65816-only invariant,
    /// and a sibling <c>remarks</c> tag on an inheriting member replaces the inherited one
    /// instead of adding to it, which would otherwise silently drop this paragraph from
    /// <see cref="S8"/>'s documentation.
    /// </para>
    /// </remarks>
    private byte A8 { get => (byte)_s.A; set => _s.A = value; }

    /// <inheritdoc cref="A8"/>
    private byte X8 { get => (byte)_s.X; set => _s.X = value; }

    /// <inheritdoc cref="A8"/>
    private byte Y8 { get => (byte)_s.Y; set => _s.Y = value; }

    /// <summary>
    /// The 8-bit view of the stack pointer, used by every core.
    /// </summary>
    /// <remarks>
    /// As <see cref="A8"/>: <see cref="CpuState.S"/> is 16 bits wide on every variant, and
    /// the 8-bit cores read and write only the low byte. <c>S8--</c> through this property
    /// wraps at 8 bits, as a 6502 stack pointer must, whereas <c>_s.S--</c> on the raw field
    /// takes $00 to $FFFF and pushes to the wrong address.
    /// <para>
    /// On the 65816 in emulation mode, <c>SH</c> is not merely initialised to $01 at reset —
    /// it is a continuously held invariant; hardware forces it on every write to S for as
    /// long as <c>E</c> is set (research document §7). This setter is the one place every
    /// core narrows a 16-bit write to 8 bits, so it is also the one place that invariant can
    /// be enforced for every caller — including the reset sequence's own dummy stack reads —
    /// without a guard at each call site. Folds away for every other core, the same way
    /// <see cref="ReadBus"/> does for the 6510's port.
    /// </para>
    /// </remarks>
    private byte S8
    {
        get => (byte)_s.S;
        set => _s.S = TVariant.Variant == CpuVariant.W65C816 && _s.E ? (ushort)(0x0100 | value) : value;
    }

    /// <summary>
    /// The 6510's on-chip registers. Unused by every other variant, where the accesses
    /// that would reach it are folded away at JIT time.
    /// </summary>
    private CpuPort _port;
    private long _cycles;

    /// <summary>Index into <see cref="_ops"/>; negative means the next tick fetches an opcode.</summary>
    private int _mpc = -1;

    private Op _op;

    /// <summary>
    /// The opcode currently executing. Only the Rockwell bit operations consult it, to
    /// recover the bit index the hardware decodes from bits 4-6 rather than carrying
    /// thirty-two near-identical operations.
    /// </summary>
    private byte _opcode;

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

    /// <summary>Set by <c>StpHold</c>; cleared only by <see cref="Reset"/>.</summary>
    private bool _stopped;

    /// <summary>Set by <c>WaiHold</c>; cleared by any interrupt signal, or by <see cref="Reset"/>.</summary>
    private bool _waiting;

    /// <summary>Current level on the IRQ pin. Level-sensitive, not latched.</summary>
    private bool _irqLine;

    /// <summary>Current level on the NMI pin. Tracked to detect a rising edge and for the public <see cref="NmiAsserted"/> readback.</summary>
    private bool _nmiLine;

    /// <summary>
    /// Latched by a rising edge on NMI and cleared when the interrupt is serviced — by
    /// <see cref="FetchOpcode"/> dispatching it at an instruction boundary, by
    /// <c>MicroOp.PushPBrk</c> or <c>MicroOp.PushPInt</c> hijacking a BRK or IRQ sequence
    /// already in flight, or by <see cref="Reset"/>. NMI is edge-triggered, so this
    /// survives the line going low again and holding it high does not produce a second
    /// interrupt.
    /// </summary>
    private bool _nmiPending;

    /// <summary>Level on the RDY pin. Low halts the processor on read cycles.</summary>
    private bool _rdy = true;

    /// <summary>Bus-qualifier pins the most recently completed cycle asserted. See <see cref="LastPins"/>.</summary>
    private BusPins _lastPins;

    /// <summary>Address the most recently completed cycle drove. See <see cref="LastAddress"/>.</summary>
    private int _lastAddress;

    /// <summary>
    /// Interrupt poll result, recomputed at the start of every cycle that continues an
    /// in-progress instruction — except a fetch cycle, which only reads this, and a cycle
    /// held by RDY, which returns before reaching the assignment; both leave the prior
    /// value untouched. At an instruction boundary this therefore holds the value from the
    /// start of the final cycle, the same instant a real 6502 samples during phase 2 of the
    /// penultimate cycle. True when either an NMI is latched or IRQ is asserted with
    /// <c>I</c> clear; NMI is never blocked by <c>I</c>. Forced false on
    /// <c>MicroOp.VectorHi</c>, which is the recognition blackout at the end of an
    /// interrupt sequence — see <see cref="Tick"/>.
    /// </summary>
    private bool _intPoll;

    /// <summary>Creates a core over the given bus.</summary>
    public Cpu(TBus bus)
    {
        _bus = bus;
        _table = MicroOpTable.For<TVariant>();
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

    /// <summary>
    /// True while <c>STP</c> has halted the processor. WDC only; nothing but
    /// <see cref="Reset"/> clears it.
    /// </summary>
    public bool IsStopped => _stopped;

    /// <summary>
    /// True while <c>WAI</c> is holding the processor. WDC only. Cleared by IRQ being
    /// asserted or an NMI latched, whether or not <c>I</c> allows the interrupt to be taken.
    /// </summary>
    public bool IsWaiting => _waiting;

    /// <summary>The bus. Exposed so a caller holding only the CPU can reach its memory.</summary>
    public ref TBus Bus => ref _bus;

    /// <summary>True while the IRQ pin is asserted. Level-sensitive: reflects the current pin level.</summary>
    public bool IrqAsserted => _irqLine;

    /// <summary>
    /// Drives the IRQ pin. The line is level-sensitive: while it is held asserted and the
    /// interrupt-disable flag is clear, an interrupt is taken at each instruction boundary.
    /// </summary>
    public void SetIrq(bool asserted) => _irqLine = asserted;

    /// <summary>
    /// True while the NMI pin is asserted. This reflects the current line level, not the
    /// edge-triggered pending latch — <see cref="NmiAsserted"/> returning <c>false</c> does
    /// not mean no NMI is pending.
    /// </summary>
    public bool NmiAsserted => _nmiLine;

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
    /// write already in progress completes. A halted processor keeps performing one bus
    /// read per cycle rather than going silent, which is the basic shape of how a video
    /// chip steals cycles without disturbing the CPU's state — but the address driven on a
    /// halted cycle is not guaranteed to be the address the pending micro-op would have
    /// used; see the <c>ponytail:</c> note at the halted read in <see cref="Tick"/>.
    /// </summary>
    public void SetRdy(bool ready) => _rdy = ready;

    /// <summary>
    /// The <see cref="BusPins"/> the current or most recently completed cycle asserted. Set
    /// by every <see cref="Tick"/>, including one halted by RDY, so a conformance harness
    /// reading this after each call never misses a cycle. Internal rather than public: every
    /// test project has <c>InternalsVisibleTo</c>, and this is readback for the harness, not
    /// library surface a consumer needs.
    /// </summary>
    internal BusPins LastPins => _lastPins;

    /// <summary>
    /// The address the current or most recently completed cycle drove. Companion to
    /// <see cref="LastPins"/>, set on the same cycles.
    /// </summary>
    internal int LastAddress => _lastAddress;

    /// <summary>
    /// Pulses the SO pin, setting the overflow flag. Nothing clears it but an instruction
    /// that writes V.
    /// </summary>
    public void SetSo() => _s.V = true;

    /// <summary>
    /// The pins an opcode-fetch cycle asserts. Not a <see cref="MicroOp"/> classification —
    /// the fetch is performed by this loop, not by a sequence member — so <see cref="Tick"/>
    /// assigns it directly rather than through <see cref="MicroOps.PinsFor"/>.
    /// </summary>
    private const BusPins OpcodeFetchPins = BusPins.Vda | BusPins.Vpa;

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
            // ponytail: _addr is only the right address for the minority of read micro-ops
            // that actually read it (ReadExec, RmwRead, ReadPageCross, DummyReadFixup,
            // UnstableStoreFixup, ZpIndex*). Every other read micro-op — PC, stack, pointer
            // or vector reads — gets _addr's stale value here instead of its own, which is
            // a real hazard on a bus with read side effects. Upgrade path: derive the
            // pending micro-op's true read address (a switch mirroring Execute) instead of
            // hard-coding _addr. WAI and STP are already handled below, because their holds
            // are unbounded — see MicroOps.HoldsAtPc. LastPins inherits the same hazard: it
            // reports the pending micro-op's classification (or the fetch pins, at a boundary)
            // rather than pins derived from the address actually redriven.
            _lastPins = _mpc < 0 ? OpcodeFetchPins : MicroOps.PinsFor(_ops[_mpc]);
            ReadBus(_mpc < 0 || MicroOps.HoldsAtPc(_ops[_mpc]) ? PcAddress() : _addr);
            return;
        }

        if (_mpc < 0)
        {
            // A fetch cycle does no polling of its own: it consults whatever _intPoll
            // was left holding by the instruction that just finished (see below), the
            // same instant real hardware samples during phase 2 of the penultimate cycle.
            //
            // KNOWN GAP, 65816 only: when _intPoll is set, FetchOpcode() below diverts into
            // the interrupt-entry sequence instead of fetching an opcode, and that cycle is
            // actually a discarded read at PC — VDA and VPA should not both be asserted
            // there, unlike a real opcode fetch. Research document §9 covers only phase 7b's
            // addressing-mode slice and has no interrupt rows, so the correct pin pair cannot
            // be established from it today. Left as OpcodeFetchPins until phase 7d implements
            // 65816 interrupts and can pin the right value down. The five 8-bit cores are
            // unaffected: VDA/VPA do not exist there, and their opcode-fetch-vs-interrupt-entry
            // pin behaviour has no equivalent gap.
            _lastPins = OpcodeFetchPins;
            FetchOpcode();
            return;
        }

        var micro = _ops[_mpc];
        _lastPins = MicroOps.PinsFor(micro);

        // Poll before this cycle's own work. When this is the last cycle of the
        // instruction, the value computed here — using register state from before this
        // cycle's Execute — is what the next fetch cycle above will act on. This is what
        // makes CLI delay a pending IRQ by one instruction while SEI fails to block one,
        // with no special case for either: the flag write and the poll simply land in the
        // same cycle for CLI/SEI, and the poll happens first. NMI is never blocked by I.
        _intPoll = _nmiPending || (_irqLine && !_s.I);

        // Interrupt-recognition blackout. VectorHi is the final cycle of every BRK,
        // IRQ, NMI and reset sequence, and hardware cannot recognise a new interrupt
        // there: node 1368 is held grounded from T5 phase 2 through T0 phase 1, so
        // stage-1 recognition is deferred to T1 phase 1 of the handler's first
        // instruction. The guarantee that falls out of it is the visible one — at least
        // one handler instruction always executes before another interrupt is serviced.
        if (micro == MicroOp.VectorHi) _intPoll = false;

        _mpc++;
        Execute(micro);

        // A micro-op may have ended the instruction early (an untaken branch, or an
        // indexed read that did not cross a page). Otherwise the sequence ends when
        // the next slot is the terminator.
        if (_mpc >= 0 && _ops[_mpc] == MicroOp.End) _mpc = -1;
    }

    /// <summary>
    /// Every read the core performs. On the 6510 the on-chip port answers <c>$00</c> and
    /// <c>$01</c> itself and the access never reaches the bus.
    /// </summary>
    /// <remarks>
    /// The variant test is a compile-time constant for each closed generic type, so for
    /// every core without the port the JIT sees <c>if (false)</c> and the check costs
    /// nothing. That is the reason it is written against <c>TVariant.Variant</c> rather
    /// than a field.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadBus(int address)
    {
        _lastAddress = address;

        if (TVariant.Variant == CpuVariant.Mos6510 && (uint)address <= 1)
            return _port.Read(address);

        return _bus.Read(address);
    }

    /// <summary>Every write the core performs. See <see cref="ReadBus"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteBus(int address, byte value)
    {
        _lastAddress = address;

        if (TVariant.Variant == CpuVariant.Mos6510 && (uint)address <= 1)
        {
            _port.Write(address, value);
            return;
        }

        _bus.Write(address, value);
    }

    /// <summary>
    /// An internal-operation cycle: drives an address but performs no read or write. Only the
    /// 65816 has these — research document §9's <c>IO</c> rows, modelled by
    /// <see cref="IBus.Internal"/> — so every earlier core's <see cref="MicroOp"/> sequence
    /// never reaches this method at all.
    /// </summary>
    /// <remarks>
    /// The <c>_bus.Internal</c> call is guarded by a compile-time variant test, the same
    /// technique <see cref="ReadBus"/> uses to fold away the 6510's port for every other core:
    /// <see cref="IBus.Internal"/> is a default interface method, and a call through it on a
    /// <c>struct</c> that does not override it is a constrained call that boxes — unacceptable
    /// on this per-cycle path for the five 8-bit cores that never take it. Guarding with
    /// <c>TVariant.Variant</c> lets the JIT see <c>if (false)</c> for those cores and emit
    /// nothing, exactly as it already does for the port check above.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InternalCycle(int address)
    {
        _lastAddress = address;
        if (TVariant.Variant == CpuVariant.W65C816) _bus.Internal(address);
    }

    /// <summary>True when the cycle about to run is a write. RDY cannot halt a write.</summary>
    private bool IsWriteCycleNext() => _mpc >= 0 && MicroOps.IsWriteCycle(_ops[_mpc]);

    /// <summary>
    /// The program-bank-qualified address of the live program counter: research document §9's
    /// <c>PBR,PC</c> family of addresses. Every read of the program stream — the opcode fetch,
    /// an operand fetch, or a dummy read that rereads live PC without advancing it — goes
    /// through this rather than repeating the shift at each call site.
    /// </summary>
    /// <remarks>
    /// <see cref="CpuState.PC"/> itself needs no change here: it stays a <c>ushort</c> and rolls
    /// <c>$FFFF</c> to <c>$0000</c> without touching <see cref="CpuState.PBR"/> (research
    /// document §2.2/§2.4) — this only adds the bank on top of whatever <c>PC</c> already holds.
    /// Guarded by the same compile-time <c>TVariant.Variant</c> test <see cref="ReadBus"/> uses
    /// for the 6510's port, so the JIT sees <c>if (false)</c> and folds straight to the bare
    /// <c>PC</c> for the five 8-bit cores on this per-cycle path — the same technique
    /// <see cref="InternalCycle"/> uses.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int PcAddress() => TVariant.Variant == CpuVariant.W65C816 ? (_s.PBR << 16) | _s.PC : _s.PC;

    /// <summary>
    /// Begins a hardware reset. The sequence takes seven cycles; drive it with
    /// <see cref="Step"/> or seven calls to <see cref="Tick"/>.
    /// </summary>
    /// <remarks>
    /// Reset does not clear the registers. Real hardware leaves A, X, Y and most of P
    /// undisturbed; it sets I, decrements S three times, and loads PC from $FFFC. On the
    /// 65816 this still holds for N, V, Z and A — the datasheet's reset initialisation table
    /// (p. 15, §2.25) marks them "not initialized" alongside SL, XL and YL — but D is one of
    /// the few flags the table does pin down, to 0, so <see cref="Reset"/> clears it
    /// explicitly for that variant (see below). The table's last P-register column is
    /// labelled <c>C/E</c> with value 1, ambiguous between "C is set" and "E is set" — and E
    /// is already listed as 1 under Signals, so this implementation leaves C untouched. No
    /// conformance vector covers reset, so nothing can arbitrate the ambiguity; research
    /// document §10 records it.
    /// <para>
    /// It does discard a pending NMI. A reset runs a BRK on this die — RESG high
    /// substitutes BRK into the instruction register — and that BRK clears NMI stage 1
    /// (<c>~NMIG</c>) at T0 phase 1 unconditionally. The NMI <em>line level</em> is left
    /// alone: the edge detector compares against it, so clearing it would re-arm on a pin
    /// that never moved and manufacture a phantom interrupt.
    /// </para>
    /// <para>
    /// This implementation clears the latch at the moment <c>Reset()</c> is called, not at
    /// the reset sequence's seventh cycle where hardware actually clears it. The two differ
    /// only for an NMI edge the host asserts during the seven cycles the reset sequence is
    /// running.
    /// </para>
    /// </remarks>
    public void Reset()
    {
        _s.I = true;

        // The 65816 resets into emulation mode, not native — CpuState.E defaults to false,
        // which is native. Emulation mode forces m and x, clears XH and YH, and forces SH to
        // $01 (research document §7); DBR, PBR and DP are not part of that invariant, but
        // reset clears them too. SH's forcing here is belt-and-braces: S8's setter (above)
        // re-forces it on every write for as long as E is set, which is what makes it survive
        // the reset sequence's own dummy stack decrements below. D is cleared because the
        // datasheet's reset table pins it to 0 (research document §10) — unlike N, V, Z and
        // A, which the same table marks "not initialized" and which this deliberately leaves
        // alone.
        if (TVariant.Variant == CpuVariant.W65C816)
        {
            _s.E = true;
            _s.M = true;
            _s.XFlag = true;
            _s.D = false;
            _s.X &= 0x00FF;
            _s.Y &= 0x00FF;
            _s.S = (ushort)((_s.S & 0x00FF) | 0x0100);
            _s.DBR = 0;
            _s.PBR = 0;
            _s.DP = 0;
        }

        // ponytail: hardware clears ~NMIG at T0 phase 1 — the reset's seventh cycle — not
        // when RES is first pulled. The two differ only for an NMI edge the host asserts
        // during those seven cycles: hardware discards or defers it, this keeps it. That
        // needs a reset-only micro-op to model, since VectorHi is shared with BRK/IRQ/NMI
        // where an unconditional clear would change other behaviour. Not worth it for a
        // seven-cycle window that requires moving the NMI pin mid-reset.
        _nmiPending = false;
        _vector = ResetVector;
        _mpc = _table.ResetEntry;
        _jammed = false;
        _jamPhase = 0;
        _stopped = false;
        _waiting = false;
        _port.Reset();
    }

    /// <summary>
    /// Runs to the next instruction boundary, or returns early if the processor jams.
    /// </summary>
    /// <remarks>
    /// Nothing inside this loop can release a held RDY line, so it never returns while
    /// RDY is expected to stay low on a read cycle. A caller driving RDY must step the
    /// processor with <see cref="Tick"/> or <see cref="Run"/> instead. For the same reason
    /// it returns while <c>WAI</c> is holding: only the host can assert the interrupt that
    /// releases it, so looping here would never terminate.
    /// </remarks>
    /// <returns>The number of cycles consumed.</returns>
    public long Step()
    {
        var before = _cycles;
        do
        {
            Tick();
        }
        while (_mpc >= 0 && !_jammed && !_stopped && !_waiting);

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
    public long RunUntil(Func<Cpu<TBus, TVariant>, bool> stop, long maxCycles = long.MaxValue)
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
            ReadBus(PcAddress());
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

        // Bank-qualified via PcAddress(): research document §9 shows every opcode fetch at
        // "PBR,PC", not PC alone.
        var opcode = ReadBus(PcAddress());
        _s.PC++;

        var entry = _entry[opcode];
        var info = _table.Info[opcode];

        // Keyed off the descriptor rather than an empty sequence: the CMOS single-cycle
        // NOPs are defined opcodes that emit no micro-ops at all, so "no micro-ops" and
        // "not implemented" are no longer the same thing.
        if (info.Operation == Op.Undefined)
            throw TVariant.Variant == CpuVariant.W65C816
                ? new UndefinedOpcodeException(opcode, pc, _s.PBR)
                : new UndefinedOpcodeException(opcode, pc);

        _op = info.Operation;
        _opcode = opcode;
        _mpc = _ops[entry] == MicroOp.End ? -1 : entry;
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
        Op.Bra => true,
        _ => throw new InvalidOperationException($"{_op} is not a branch."),
    };

    /// <summary>The value an unstable store will write, before the address fold-in.</summary>
    private byte UnstableStoreValue() => _op switch
    {
        Op.Sha => (byte)(A8 & X8 & _storeHigh),
        Op.Shx => (byte)(X8 & _storeHigh),
        Op.Shy => (byte)(Y8 & _storeHigh),
        Op.Tas => (byte)(A8 & X8 & _storeHigh),
        _ => throw new InvalidOperationException($"{_op} is not an unstable store."),
    };

    private void Execute(MicroOp micro)
    {
        switch (micro)
        {
            case MicroOp.ImpliedExec:
                ReadBus(_s.PC);
                Exec();
                break;

            case MicroOp.ImmExec:
                _data = ReadBus(PcAddress());
                _s.PC++;
                Exec();
                break;

            case MicroOp.ImpliedDummy:
                ReadBus(PcAddress());
                break;

            case MicroOp.ImpliedExec816:
                // Internal cycle at PBR,PC — no memory access — then run the operation.
                // PC already reflects the opcode fetch's increment, so no further adjustment
                // is needed to reach research document §9's "PC+1".
                InternalCycle((_s.PBR << 16) | _s.PC);
                Exec();
                break;

            case MicroOp.FetchAddrLo:
                _addr = ReadBus(PcAddress());
                _s.PC++;
                break;

            case MicroOp.FetchAddrHi:
                _addr |= ReadBus(PcAddress()) << 8;
                _s.PC++;
                break;

            case MicroOp.ReadExec:
                _data = ReadBus(_addr);
                Exec();
                break;

            case MicroOp.ExecWrite:
                Exec();
                WriteBus(_addr, _data);
                break;

            case MicroOp.RmwRead:
                _data = ReadBus(_addr);
                break;

            case MicroOp.RmwModifyWrite:
                // NMOS parts write the unmodified value back before writing the result.
                WriteBus(_addr, _data);
                Exec();
                break;

            case MicroOp.ReadExecCmosArith:
                _data = ReadBus(_addr);
                if (_s.D) break;                 // decimal costs one more cycle: BcdExtra
                Exec();
                EndInstruction();
                break;

            case MicroOp.ImmExecCmosArith:
                _data = ReadBus(PcAddress());
                _s.PC++;
                if (_s.D) break;
                Exec();
                EndInstruction();
                break;

            case MicroOp.BcdExtra:
                // ponytail: re-reads the effective address, which is what every memory
                // addressing mode's vectors show. Immediate mode has no effective address
                // and its vectors expect a fixed per-opcode constant instead — see
                // CmosArithmeticTests. _addr is stale here for that mode.
                ReadBus(_addr);
                Exec();
                break;

            case MicroOp.ReadPageCrossCmosArith:
                if (_pageCross)
                {
                    ReadBus((_s.PC - 1) & 0xFFFF);
                    _addr = (_addr + 0x100) & 0xFFFF;
                    break;
                }
                _data = ReadBus(_addr);
                if (_s.D) { _mpc++; break; }      // skip the read, land on BcdExtra
                Exec();
                EndInstruction();
                break;

            case MicroOp.RmwModifyRead:
                // CMOS parts read instead. Same cycle, opposite direction — which matters
                // to any bus with read or write side effects, not just to a cycle count.
                ReadBus(_addr);
                Exec();
                break;

            case MicroOp.RmwWrite:
                WriteBus(_addr, _data);
                break;

            case MicroOp.FetchAddrHiX:
            {
                var hi = ReadBus(_s.PC);
                _s.PC++;
                var lo = (_addr & 0xFF) + X8;
                _pageCross = lo > 0xFF;
                _addr = (hi << 8) | (lo & 0xFF);
                break;
            }

            case MicroOp.FetchAddrHiY:
            {
                var hi = ReadBus(_s.PC);
                _s.PC++;
                var lo = (_addr & 0xFF) + Y8;
                _pageCross = lo > 0xFF;
                _addr = (hi << 8) | (lo & 0xFF);
                break;
            }

            case MicroOp.ZpIndexX:
                ReadBus(_addr);                      // dummy read at the unindexed address
                _addr = (_addr + X8) & 0xFF;         // page zero indexing wraps within the page
                break;

            case MicroOp.ZpIndexY:
                ReadBus(_addr);
                _addr = (_addr + Y8) & 0xFF;
                break;

            case MicroOp.ReadPageCross:
                // The read happens either way. Without a page cross it is the real read
                // and the instruction is done; with one it was a read of the wrong
                // address and the next micro-op re-reads the corrected one.
                _data = ReadBus(_addr);
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
                ReadBus(_addr);
                if (_pageCross) _addr = (_addr + 0x100) & 0xFFFF;
                break;

            // The CMOS indexing fixups all read the last operand byte rather than the
            // mis-indexed address. PC has advanced past the operand bytes by now, so that
            // is PC - 1: $nnnn's high byte for the absolute-indexed modes, and the
            // zero-page operand for (zp),Y.
            case MicroOp.IndexFixupCmos:
                ReadBus((_s.PC - 1) & 0xFFFF);
                if (_pageCross) _addr = (_addr + 0x100) & 0xFFFF;
                break;

            case MicroOp.ReadPageCrossCmos:
                if (_pageCross)
                {
                    ReadBus((_s.PC - 1) & 0xFFFF);
                    _addr = (_addr + 0x100) & 0xFFFF;
                }
                else
                {
                    // No cross: this cycle is the real read, exactly as ReadPageCross does
                    // it. The saving is that CMOS never reads the wrong address first.
                    _data = ReadBus(_addr);
                    Exec();
                    EndInstruction();
                }
                break;

            case MicroOp.RmwPageCrossCmos:
                if (_pageCross)
                {
                    ReadBus((_s.PC - 1) & 0xFFFF);
                    _addr = (_addr + 0x100) & 0xFFFF;
                }
                else
                {
                    // No cross: perform the RMW's own read here and skip the RmwRead that
                    // follows, which is what makes these six cycles rather than seven.
                    _data = ReadBus(_addr);
                    _mpc++;
                }
                break;

            case MicroOp.UnstableStoreFixup:
                ReadBus(_addr);
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
                _tmp = ReadBus(_ptr);
                break;

            case MicroOp.PtrReadHi:
                // The pointer's high byte wraps within page zero.
                _addr = (ReadBus((_ptr + 1) & 0xFF) << 8) | _tmp;
                break;

            case MicroOp.PtrReadHiY:
            {
                var hi = ReadBus((_ptr + 1) & 0xFF);
                var lo = _tmp + Y8;
                _pageCross = lo > 0xFF;
                _addr = (hi << 8) | (lo & 0xFF);
                break;
            }

            case MicroOp.BranchFetch:
                _data = ReadBus(_s.PC);
                _s.PC++;
                if (!IsBranchTaken()) EndInstruction();
                break;

            case MicroOp.BranchTaken:
            {
                ReadBus(_s.PC);                       // dummy read at the byte after the branch
                var lo = (_s.PC & 0xFF) + (sbyte)_data;
                _branchFix = lo < 0 ? -0x100 : lo > 0xFF ? 0x100 : 0;
                _s.PC = (ushort)((_s.PC & 0xFF00) | (lo & 0xFF));
                if (_branchFix == 0) EndInstruction();
                break;
            }

            case MicroOp.BranchFixup:
                ReadBus(_s.PC);                       // dummy read at the un-fixed PC
                _s.PC = (ushort)(_s.PC + _branchFix);
                break;

            case MicroOp.StackDummyRead:
                ReadBus(0x0100 + S8);
                break;

            case MicroOp.StackDummyReadInc:
                ReadBus(0x0100 + S8);
                S8++;
                break;

            case MicroOp.PushPch:
                WriteBus(0x0100 + S8, (byte)(_s.PC >> 8));
                S8--;
                break;

            case MicroOp.PushPcl:
                WriteBus(0x0100 + S8, (byte)_s.PC);
                S8--;
                break;

            // JMP absolute and JSR both finish by reading the high byte at PC and
            // combining it with the low byte already in _addr.
            case MicroOp.JmpAbs:
            case MicroOp.JsrFinish:
                _s.PC = (ushort)((ReadBus(_s.PC) << 8) | _addr);
                break;

            case MicroOp.JmpIndLo:
                _ptr = _addr;
                _tmp = ReadBus(_ptr);
                break;

            case MicroOp.JmpIndHi:
                // NMOS bug: the vector's high byte is fetched from the same page, so
                // JMP ($xxFF) reads its high byte from $xx00.
                _s.PC = (ushort)((ReadBus((_ptr & 0xFF00) | ((_ptr + 1) & 0xFF)) << 8) | _tmp);
                break;

            case MicroOp.BitBranchDummy:
                ReadBus(_addr);
                break;

            case MicroOp.BitBranchFixup:
                // Reads the address stashed by BitBranchFetch rather than the half-corrected
                // PC an ordinary branch uses.
                ReadBus(_addr);
                _s.PC = (ushort)(_s.PC + _branchFix);
                break;

            case MicroOp.BitBranchFetch:
            {
                var tested = _data;
                _data = ReadBus(_s.PC);
                _s.PC++;
                // The byte after the displacement, which both remaining cycles read. _addr
                // held the zero-page address, which nothing needs from here on.
                _addr = _s.PC;
                var isSet = (tested & (1 << ((_opcode >> 4) & 7))) != 0;
                if (_op == Op.Bbr ? isSet : !isSet) EndInstruction();
                break;
            }

            case MicroOp.NopAbsExtraRead:
                // The fourth cycle of the three-byte, four-cycle CMOS NOPs: a discarded
                // re-read of the high operand byte, which PC has already passed.
                ReadBus((_s.PC - 1) & 0xFFFF);
                break;

            case MicroOp.JmpIndBugDummy:
                // Read and discard, at exactly the address JmpIndHi would have used. When
                // the pointer's low byte is not $FF this is the same address the next
                // micro-op reads, which is why the non-wrapping case shows two adjacent
                // reads of one location rather than an obviously wasted cycle.
                ReadBus((_ptr & 0xFF00) | ((_ptr + 1) & 0xFF));
                break;

            case MicroOp.PtrJmpHi:
                _s.PC = (ushort)((ReadBus((_ptr + 1) & 0xFFFF) << 8) | _tmp);
                break;

            case MicroOp.JmpAbsXDummy:
                // The discarded read is at the first operand byte. PC has already advanced
                // past both operand bytes, so that is PC - 2. Its address does not depend
                // on the indexing, so there is no page-cross penalty to account for.
                ReadBus((_s.PC - 2) & 0xFFFF);
                _addr = (_addr + X8) & 0xFFFF;
                break;

            case MicroOp.PullPcl:
                _tmp = ReadBus(0x0100 + S8);
                S8++;
                break;

            case MicroOp.PullPch:
                _s.PC = (ushort)((ReadBus(0x0100 + S8) << 8) | _tmp);
                break;

            case MicroOp.RtsFinish:
                ReadBus(_s.PC);
                _s.PC++;
                break;

            case MicroOp.PullP:
                // B exists only in pushed copies of P; U always reads as set.
                _s.P = (byte)((ReadBus(0x0100 + S8) & ~Flag.B) | Flag.U);
                S8++;
                break;

            case MicroOp.Push:
                Exec();
                WriteBus(0x0100 + S8, _data);
                S8--;
                break;

            case MicroOp.Pull:
                _data = ReadBus(0x0100 + S8);
                Exec();
                break;

            case MicroOp.BrkPad:
                ReadBus(_s.PC);      // BRK's signature byte, fetched and discarded
                _s.PC++;
                break;

            case MicroOp.IntDummy:
                ReadBus(_s.PC);
                break;

            case MicroOp.PushPBrk:
                // BRK's own vector, chosen before the hijack test below can override it.
                _vector = IrqVector;
                // The vector is committed here, on cycle 5 of the sequence — real silicon
                // decides at T5 phase 1, because this cycle *forms* the vector-low address
                // that appears on the pins in cycle 6, and a dedicated transistor chain
                // (~VEC, pipe~VEC, 1578, 1368) then blocks NMI recognition through the rest
                // of the sequence so no mixed $FFFE/$FFFA vector can occur. A latched NMI
                // therefore hijacks the BRK in progress: the pushes already happened with
                // BRK's own B flag set, but control lands in the NMI handler.
                //
                // No vector guard is needed here, unlike PushPInt: this micro-op is emitted
                // only by BRK, so _vector was just set to IrqVector above, and neither the
                // reset sequence nor a hardware-interrupt sequence ever executes it.
                //
                // Testing before the write matters: it reads the latch as of the start of
                // the cycle, so a bus-side-effect NMI raised by this very write cannot be
                // seen — the same reason _intPoll is computed before Execute.
                //
                // CMOS note (for the future CpuVariant work): the 65C02 family removed this
                // anomaly entirely — no hijack at all. Keeping the decision inside the
                // P-push micro-op lets a CMOS table emit a different one without touching
                // Tick, VectorLo or the sequence layout.
                if (_nmiPending)
                {
                    _nmiPending = false;
                    _vector = NmiVector;
                }
                WriteBus(0x0100 + S8, (byte)(_s.P | Flag.B | Flag.U));
                S8--;
                _s.I = true;
                break;

            case MicroOp.PushPInt:
                // Deliberately does not set _vector: only the dispatcher knows whether
                // this is an IRQ or an NMI, and they use different vectors. FetchOpcode
                // sets it before entering this sequence.
                //
                // Same cycle-5 vector commit as PushPBrk — see there for the timing and the
                // CMOS note. Only an IRQ-vectored sequence can be hijacked: the guard keeps
                // an NMI sequence from consuming a second latch that arrived during its own
                // run, and refuses a reset sequence for the same reason it always did,
                // should one ever be routed through this micro-op.
                if (_nmiPending && _vector == IrqVector)
                {
                    _nmiPending = false;
                    _vector = NmiVector;
                }
                WriteBus(0x0100 + S8, (byte)((_s.P | Flag.U) & ~Flag.B));
                S8--;
                _s.I = true;
                break;

            case MicroOp.PushPBrkCmos:
                // The CMOS BRK push. The vector is still committed on this cycle, but the
                // hijack the NMOS form performs here is simply absent — the 65C02 removed
                // the anomaly, so a latched NMI stays latched and is taken after the
                // handler's first instruction instead of stealing BRK's vector.
                //
                // The push happens before D is cleared, and that order is the behaviour:
                // the byte on the stack carries D as the interrupted code left it, so RTI
                // restores it, while the handler itself runs with D clear. Clearing first
                // would silently corrupt the restored flag on every CMOS BRK.
                _vector = IrqVector;
                WriteBus(0x0100 + S8, (byte)(_s.P | Flag.B | Flag.U));
                S8--;
                _s.I = true;
                _s.D = false;
                break;

            case MicroOp.PushPIntCmos:
                // The CMOS hardware-interrupt push. As PushPBrkCmos: no hijack, and D
                // cleared after the push. _vector is left alone for the same reason
                // PushPInt leaves it — only the dispatcher knows IRQ from NMI.
                WriteBus(0x0100 + S8, (byte)((_s.P | Flag.U) & ~Flag.B));
                S8--;
                _s.I = true;
                _s.D = false;
                break;

            case MicroOp.VectorLo:
                // No hijack test here: by this cycle the vector-low address has already
                // been formed (see PushPBrk). This is the plain read.
                _tmp = ReadBus(_vector);
                break;

            case MicroOp.VectorHi:
                _s.PC = (ushort)((ReadBus(_vector + 1) << 8) | _tmp);
                break;

            case MicroOp.StackDummyReadDec:
                ReadBus(0x0100 + S8);
                S8--;
                break;

            case MicroOp.WaiHold:
                // The wake condition is the interrupt SIGNAL, not the poll: WAI resumes even
                // with I set, and the instruction after WAI then runs instead of a handler.
                ReadBus(_s.PC);
                if (_nmiPending || _irqLine) _waiting = false;
                else { _waiting = true; _mpc--; }
                break;

            case MicroOp.StpHold:
                _stopped = true;
                ReadBus(_s.PC);
                _mpc--;                 // hold position: only Reset escapes
                break;

            case MicroOp.JamHold:
                // The address bus cycles $FFFF, $FFFE, $FFFE, then $FFFF forever.
                // ponytail: RDY-low during a jam is unmodelled — JamHold isn't a write, so
                // Tick's halt branch intercepts it first and re-reads _addr instead of
                // advancing this pattern, freezing _jamPhase instead of continuing the real
                // bus cycling. No test pins the halted address yet. If that ever matters,
                // give the halt branch a jammed-aware read (or let JamHold run through RDY).
                _jammed = true;
                ReadBus(_jamPhase switch
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
