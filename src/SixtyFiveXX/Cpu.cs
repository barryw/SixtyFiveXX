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

    /// <summary>
    /// The four 65816 native-mode interrupt vectors, plus the one emulation-mode vector the
    /// eight-bit cores have no counterpart for. Research document §14.2, transcribed from
    /// datasheet Tables 5-2 and 5-3, which Clark §6.3.1.1 corroborates cell for cell — and
    /// measured: 10,000 of 10,000 vectors each for <c>$00</c>/<c>$02</c> in both modes.
    /// <para>
    /// There is no native <c>RESET</c> vector. Clark, verbatim: <em>"A RESET interrupt puts the
    /// 65C816 into emulation mode, thus there is no native mode RESET vector"</em>, and Table
    /// 5-3 has no row for it — so <see cref="ResetVector"/> serves both modes.
    /// <c>ABORT</c>'s pair (<c>$00FFE8</c> native, <c>$00FFF8</c> emulation) is deliberately
    /// absent: this core has no <c>ABORTB</c> pin and nothing can raise one.
    /// </para>
    /// <para>
    /// Private, unlike the three above: this phase adds no public API, and
    /// <see cref="Vector816"/> is the only reader. Emulation-mode <c>BRK</c> and <c>IRQ</c>
    /// share <see cref="IrqVector"/> and emulation-mode <c>NMI</c> is <see cref="NmiVector"/>,
    /// so only <c>COP</c> needs an emulation constant of its own.
    /// </para>
    /// </summary>
    private const int CopVector816 = 0xFFE4;

    /// <inheritdoc cref="CopVector816"/>
    private const int BrkVector816 = 0xFFE6;

    /// <inheritdoc cref="CopVector816"/>
    private const int NmiVector816 = 0xFFEA;

    /// <inheritdoc cref="CopVector816"/>
    private const int IrqVector816 = 0xFFEE;

    /// <inheritdoc cref="CopVector816"/>
    private const int CopVectorEmulation = 0xFFF4;

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
    /// <para>
    /// <see cref="A8"/>'s setter is the exception to that: on the 65816 it preserves A's high
    /// byte, which is the hidden B accumulator that <c>XBA</c> exchanges with. An 8-bit
    /// operation on A must not disturb it. The <c>TVariant.Variant</c> test is a compile-time
    /// constant per closed generic type, so for the five 8-bit cores this folds back to a plain
    /// <c>_s.A = value</c> and costs nothing — zeroing a high byte those cores have no
    /// architectural use for, exactly as the paragraph above describes. <c>LDA</c> is the reason
    /// that is worth stating: until phase 7c it was the one operation that preserved that byte on
    /// an 8-bit core, and only as an artifact of the hand-rolled <c>_s.A = (_s.A &amp; 0xFF00) | _data</c>
    /// it used to carry for the 65816's sake. Folding that onto this setter aligned it with the
    /// other thirteen A-writing operations, at the cost of one assertion in
    /// <c>UnusedFlagBitRegressionTests</c> — which is the only place in the repository that ever
    /// put anything in an 8-bit core's A high byte, and did so as a probe, not a requirement.
    /// <see cref="X8"/> and <see cref="Y8"/> deliberately do NOT get this treatment: there is no
    /// hidden high byte for the index registers to preserve, and their setters already zero the
    /// high byte on every 8-bit write — by assigning the whole 16-bit field from a <c>byte</c>, as
    /// the paragraph above describes, not by writing the low half in place. Hardware does force
    /// <c>XH</c>/<c>YH</c> to <c>$00</c> whenever <c>x</c> is set, so that zeroing setter is
    /// correct — but, unlike <see cref="S8"/>'s <c>SH</c>, this is not a continuously-held
    /// invariant of the core. <see cref="Op.Xce"/>, <see cref="Op.Rep"/> and <see cref="Op.Sep"/>
    /// force it at their own mode-transition points, and <see cref="FetchOpcode"/>'s own forcing
    /// block re-applies it once per instruction, but only while <see cref="CpuState.E"/> is set —
    /// its guard is <c>&amp;&amp; _s.E</c> — so in native mode with <c>x = 1</c> it does nothing.
    /// A state set directly, such as a conformance vector's <c>initial</c> loaded straight into
    /// <see cref="CpuState"/>, can therefore carry a nonzero high byte alongside <c>x = 1</c>
    /// without passing through any of those paths. That gap is exactly why <see cref="IndexX"/>
    /// and <see cref="IndexY"/> narrow at read time instead of trusting the field to already be
    /// zero.
    /// </para>
    /// </remarks>
    private byte A8
    {
        get => (byte)_s.A;
        set => _s.A = TVariant.Variant == CpuVariant.W65C816
            ? (ushort)((_s.A & 0xFF00) | value)
            : value;
    }

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

    /// <summary>
    /// The 65816's 16-bit access in progress. Holds the low byte, widened, once
    /// <see cref="MicroOp.ReadExec816"/> or <see cref="MicroOp.ExecWrite816"/> has run; holds
    /// the combined 16-bit value once <see cref="MicroOp.ReadExecHigh816"/> forms it or
    /// <see cref="Op.Sta"/> populates it directly from <c>A</c>. 65816 only — no 8-bit-core
    /// micro-op ever touches it.
    /// </summary>
    private ushort _data16;

    /// <summary>
    /// True when the instruction now executing takes a 16-bit operand. Resolved once, in
    /// <see cref="FetchOpcode"/>, from the opcode's <see cref="Width"/> and the matching status
    /// flag; the width-deciding micro-ops read it rather than testing <c>m</c> or <c>x</c>
    /// themselves.
    /// </summary>
    /// <remarks>
    /// <b>Latched at fetch, not sampled per cycle.</b> Nothing in phase 7c can change <c>m</c> or
    /// <c>x</c> part-way through an instruction, so the distinction is unobservable there — but
    /// it becomes observable in phase 7d, when <c>PLP</c> and <c>RTI</c> can rewrite <c>P</c>
    /// mid-sequence. Latching is deliberate and is what a decoder that resolves width once
    /// actually does: an instruction already committed to a 16-bit access does not become an
    /// 8-bit one halfway through. Do not "fix" this into a live read of <c>_s.M</c>.
    /// <para>
    /// Assigned only under a compile-time variant guard, so for the five 8-bit cores the
    /// assignment is never emitted and this stays <see langword="false"/> for the lifetime of the
    /// core — but that guard is defence-in-depth and a JIT-folding win, not what actually keeps an
    /// 8-bit core off the 16-bit path. An 8-bit core's opcode table never sets <see cref="Width"/>
    /// either, so <c>info.Width</c> is always <see cref="Width.None"/> there and this field would
    /// resolve to <see langword="false"/> even without the variant guard. A bare
    /// <c>_wide = !_s.M</c> is the one mutation that does set it <see langword="true"/> on an 8-bit
    /// core, and the read guards catch that one too — both results measured by mutation and
    /// recorded in <c>UnusedFlagBitRegressionTests</c>. What actually
    /// holds the line is the read guard on each width-deciding arm —
    /// <c>TVariant.Variant != CpuVariant.W65C816 ||</c>, see <see cref="Op.Lda"/>'s arm — which is
    /// why every read of this field in variant-shared code must still sit behind it, and which is
    /// also what keeps the field off an 8-bit core's hot path.
    /// </para>
    /// </remarks>
    private bool _wide;

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

    /// <summary>Set by <c>StpHold</c> or <c>StpHold816</c>; cleared only by <see cref="Reset"/>.</summary>
    private bool _stopped;

    /// <summary>
    /// Set by <c>WaiHold</c> or <c>WaiHold816</c>; cleared by any interrupt signal, or by
    /// <see cref="Reset"/>.
    /// </summary>
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
    /// True while <c>STP</c> has halted the processor. The WDC 65C02 and the 65816 only;
    /// nothing but <see cref="Reset"/> clears it.
    /// </summary>
    public bool IsStopped => _stopped;

    /// <summary>
    /// True while <c>WAI</c> is holding the processor. The WDC 65C02 and the 65816 only.
    /// Cleared by IRQ being asserted or an NMI latched, whether or not <c>I</c> allows the
    /// interrupt to be taken.
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
    /// write already in progress completes. A halted processor keeps re-driving the address
    /// bus every cycle rather than going silent, which is the basic shape of how a video chip
    /// steals cycles without disturbing the CPU's state — as a real bus read for every 8-bit
    /// core, where every cycle is a real access, and, on the 65816, as a real read for a halted
    /// read micro-op but as a no-access <see cref="IBus.Internal"/> cycle for a halted internal
    /// one (<c>MicroOps.IsInternalCycle</c>), matching what that cycle would have driven anyway.
    /// The address driven on a halted cycle is not guaranteed to be the address the pending
    /// micro-op would have used; see the <c>ponytail:</c> note at the halted read in
    /// <see cref="Tick"/>.
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
            // Halted: re-drive the address bus without advancing. One access, as always —
            // except a pending 65816 internal cycle (MicroOps.IsInternalCycle), which drives
            // the address through InternalCycle rather than ReadBus, because hardware performs
            // no memory access on that cycle at all; going through ReadBus there would turn a
            // no-access cycle into a real read, contradicting the None LastPins already reports
            // for it. A cycle skipped this way never reaches the poll below, so a halt mid-
            // instruction leaves _intPoll holding whatever the last live cycle computed —
            // exactly as if the clock itself had stopped, which is what RDY models.
            // ponytail: _addr is only the right address for the minority of read micro-ops
            // that actually read it (ReadExec, RmwRead, ReadPageCross, DummyReadFixup,
            // UnstableStoreFixup, ZpIndex*). Every other read micro-op — PC, stack, pointer
            // or vector reads — gets _addr's stale value here instead of its own, which is
            // a real hazard on a bus with read side effects. The 65816's high-byte micro-ops
            // widen that gap rather than change it: ReadExecHigh816*, RmwReadHigh816* and the
            // 16-bit form of RmwModifyRead816* all drive HighByteAddress*() when live but get
            // _addr here, so a halted cycle re-drives the LOW address of a 16-bit access. The
            // four RMW write-forms are immune, since RDY cannot halt a write at all. Upgrade
            // path: derive the
            // pending micro-op's true read address (a switch mirroring Execute) instead of
            // hard-coding _addr. WAI and STP are already handled below, because their holds
            // are unbounded — see MicroOps.HoldsAtPc. LastPins inherits the same hazard: it
            // reports the pending micro-op's classification (or the fetch pins, at a boundary)
            // rather than pins derived from the address actually redriven.
            _lastPins = _mpc < 0 ? OpcodeFetchPins : MicroOps.PinsFor(_ops[_mpc]);
            var haltedAddress = _mpc < 0 || MicroOps.HoldsAtPc(_ops[_mpc]) ? PcAddress() : _addr;
            if (_mpc >= 0 && MicroOps.IsInternalCycle(_ops[_mpc])) InternalCycle(haltedAddress);
            else ReadBus(haltedAddress);
            return;
        }

        if (_mpc < 0)
        {
            // A fetch cycle does no polling of its own: it consults whatever _intPoll
            // was left holding by the instruction that just finished (see below), the
            // same instant real hardware samples during phase 2 of the penultimate cycle.
            //
            // GAP CLOSED, phase 7d task 3: when _intPoll is set, FetchOpcode() below diverts
            // into the interrupt-entry sequence instead of fetching an opcode, and this comment
            // used to record that the resulting cycle's pins were unknown — research document
            // §9 has no interrupt rows. §14.2 supplies them: Table 5-7 row 22a's cycle 1 prints
            // VDA=1 VPA=1 at PBR,PC, which is OpcodeFetchPins exactly. The datasheet labels its
            // data bus "IO", meaning the byte is discarded, not that the address is invalid —
            // both address-valid pins really are asserted, so this needed no change beyond the
            // reading. Row 22a's cycle 2 is the internal cycle, and that one is a real IO with
            // neither pin: MicroOp.IntInternal816. Not vector-covered — the SingleStepTests set
            // carries no interrupt-line stimulus at all (§14.2's gap 1) — so this rests on the
            // row. The five 8-bit cores are unaffected: VDA/VPA do not exist there.
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
        // The 65816's own BRK/COP/IRQ/NMI sequences end on VectorHi816, not this micro-op,
        // and research document §14.2 records that omission as a deliberate decision — this
        // part has no such blackout after those four. Its reset sequence is the exception:
        // MicroOpTable's ResetEntry shares the 8-bit VectorLo/VectorHi pair on every variant,
        // so this line still fires for the 65816 after a reset.
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
    /// The address the next stack access drives, on the 65816. Native mode puts the stack
    /// anywhere in bank 0 and uses all sixteen bits of <c>S</c>; emulation mode confines it to
    /// page one, which <see cref="S8"/>'s setter and <see cref="FetchOpcode"/> hold as an
    /// invariant rather than something this method re-imposes. Both cases are therefore the
    /// same expression. Bank 0 either way: research document §14.1, Clark §5.1.2 and §5.22,
    /// plus every address cell in Table 5-7's rows 22a–22j, which write the bank as a literal
    /// <c>0</c>.
    /// </summary>
    /// <remarks>
    /// 65816 only. The five 8-bit cores keep their own <c>0x0100 + S8</c> in each stack
    /// micro-op — those micro-ops are correct for them and are not reached from any 65816
    /// sequence, which <c>W65C816ReachabilityTests</c> asserts.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int StackAddress816() => _s.S;

    /// <summary>
    /// True when the stack instruction now executing keeps its accesses inside page one in
    /// emulation mode. Bruce Clark, "65C816 Opcodes" §5.1.1: the page-one wrap applies
    /// <em>"only for 'old' instructions and addressing modes, i.e. instructions and addressing
    /// modes that are available on the 65C02."</em> <c>PHA</c>, <c>PHP</c>, <c>PHX</c>,
    /// <c>PHY</c>, <c>PLA</c>, <c>PLP</c>, <c>PLX</c> and <c>PLY</c> are old; <c>PHB</c>,
    /// <c>PHK</c>, <c>PHD</c>, <c>PLB</c> and <c>PLD</c> are new to the 65816 and do not wrap
    /// — the same exception research document §14.7 records for <c>PEA</c> and <c>PEI</c>, which
    /// Clark states in the same passage.
    /// </summary>
    /// <remarks>
    /// <b>Not in §14.1, originally.</b> That section answered the wrap question only from Clark
    /// §5.22's blanket <em>"In emulation mode, SL will be decremented N times"</em> and omitted
    /// §5.1.1's/§5.22's old/new (and interrupt) exception for the five new push/pull opcodes; it
    /// has since been amended, alongside this task's other fixes, to carry it. The vectors below
    /// settled it independently and are unambiguous: <c>ab e 75</c> starts at <c>S = $27FF</c>
    /// (forced to <c>$01FF</c>) and <c>PLB</c> reads at <c>$000200</c>, not <c>$000100</c>, while
    /// <c>28 e 311</c> starts at <c>$01FF</c> and <c>PLP</c> reads at <c>$000100</c>.
    /// <c>0b e 435</c> is the discriminating
    /// push: <c>PHD</c> from <c>S = $0100</c> writes at <c>$000100</c> and then <c>$0000FF</c>,
    /// below page one entirely. The rule as implemented here — old wraps, new does raw 16-bit
    /// arithmetic, and <see cref="StackSettle816"/> puts <c>SH</c> back to <c>$01</c> once the
    /// last access is done — reproduces the access addresses and the final <c>S</c> of all
    /// 260,000 vectors across the thirteen opcodes, in both modes, with no exceptions.
    /// <para>
    /// <b>Written as a list of operations, not as a general old/new predicate, deliberately.</b>
    /// Clark, "65C816 Opcodes" §5.22, settles the rule outright: <em>"For all interrupts and 'old'
    /// instructions, when the e flag is 1, the address of the data for an 8-bit push is 0,1,SL …
    /// Otherwise … 0,S."</em> The predicate is universal — interrupts and old instructions wrap
    /// within page one when <c>e = 1</c>; everything else uses raw 16-bit <c>0,S</c> — so
    /// <c>COP</c> was never a counter-example: it is new to the 65816, but it is also an
    /// interrupt, and §5.22 names interrupts in the same clause as "old" instructions. Measured
    /// across every stack opcode in the instruction set: <c>JSR</c> (old) and <c>BRK</c>/<c>COP</c>
    /// (interrupts) have zero out-of-page-one stack accesses in emulation mode, while <c>JSL</c>
    /// has 103, <c>RTL</c> 196, <c>PER</c> 34 and <c>PEA</c> 51 — for example <c>22 e 61</c> writes
    /// <c>$0000FF</c> from <c>S = $0100</c>. All new, all non-wrapping; all old or interrupt, all
    /// wrapping. The list below is that same predicate written as opcodes rather than as an
    /// <see cref="AddrMode"/> flag, because it is cheap and needs no addressing-mode plumbing: when
    /// a later task adds the remaining old instructions, this list gains <c>Op.Jsr</c>,
    /// <c>Op.Rts</c> and <c>Op.Rti</c>, and leaves <c>Op.Jsl</c>, <c>Op.Rtl</c>, <c>Op.Per</c>,
    /// <c>Op.Pea</c> and <c>Op.Pei</c> off.
    /// </para>
    /// <para>
    /// Phase 7d task 3 added the four interrupts — <c>Op.Brk</c> and <c>Op.Cop</c>, which are
    /// opcodes, and <c>Op.Irq</c> and <c>Op.Nmi</c>, which are not: <see cref="FetchOpcode"/>
    /// assigns those two when it diverts into the interrupt sequence, precisely so this predicate
    /// and <see cref="MicroOp.PushPInt816"/> can see them. Clark §5.22 names interrupts in the
    /// same clause as "old" instructions and draws no distinction among them, so all four are on
    /// the list; the two opcodes are measured at zero out-of-page-one accesses across their
    /// 20,000 emulation-mode vectors, and the two hardware interrupts have no vectors at all
    /// (§14.2's gap 1) and rest on the citation alone.
    /// </para>
    /// </remarks>
    private bool StackWrapsInPageOne() => _op is Op.Pha or Op.Php or Op.Phx or Op.Phy
                                                or Op.Pla or Op.Plp or Op.Plx or Op.Ply
                                                or Op.Brk or Op.Cop or Op.Irq or Op.Nmi;

    /// <summary>
    /// The vector an interrupt reads, on the 65816. Native mode has its own four addresses;
    /// emulation mode reuses the eight-bit cores' — except <c>COP</c>, which the eight-bit
    /// cores do not have. Research document §14.2 — every address here is transcribed from
    /// that table, not derived from the 6502's.
    /// </summary>
    /// <remarks>
    /// One selector rather than a test inside each interrupt micro-op: the choice depends only
    /// on <c>E</c> and on which interrupt is being taken, both of which are known before the
    /// sequence starts, and keeping it in one place is what makes the five constants above
    /// reviewable against §14.2 as a block.
    /// <para>
    /// Reached from exactly two places, one per kind of interrupt:
    /// <see cref="FetchOpcode"/> for a hardware interrupt, whose reason only the dispatcher
    /// knows, and <see cref="MicroOp.BrkPad816"/> for <c>BRK</c>/<c>COP</c>, whose reason is
    /// their opcode. <b>Which cycle commits the vector is unobservable on this part</b>, unlike
    /// on the NMOS cores where the P-push cycle commits it and a latched NMI may hijack it
    /// there — see <see cref="MicroOp.PushPInt816"/> for why there is no hijack here.
    /// </para>
    /// </remarks>
    private int Vector816(Op reason) => reason switch
    {
        Op.Cop => _s.E ? CopVectorEmulation : CopVector816,
        Op.Brk => _s.E ? IrqVector : BrkVector816,
        Op.Nmi => _s.E ? NmiVector : NmiVector816,
        Op.Irq => _s.E ? IrqVector : IrqVector816,
        _ => throw new InvalidOperationException($"{reason} is not an interrupt."),
    };

    /// <summary>Writes one byte at <see cref="StackAddress816"/>, then moves <c>S</c> down.</summary>
    private void PushStack816(byte value)
    {
        WriteBus(StackAddress816(), value);
        if (_s.E && StackWrapsInPageOne()) S8--; else _s.S--;
    }

    /// <summary>Moves <c>S</c> up, then reads one byte at <see cref="StackAddress816"/>.</summary>
    /// <remarks>
    /// The order is the hardware's and matters: a push writes before decrementing and a pull
    /// increments before reading, so <c>S</c> always points at the next free byte.
    /// <c>S8++</c> rather than <c>_s.S++</c> is what keeps an old instruction's wrap inside page
    /// one — <c>_s.S++</c> at <c>$01FF</c> would produce <c>$0200</c>, which is exactly what a
    /// new instruction does; see <see cref="StackWrapsInPageOne"/>.
    /// </remarks>
    private byte PullStack816()
    {
        if (_s.E && StackWrapsInPageOne()) S8++; else _s.S++;
        return ReadBus(StackAddress816());
    }

    /// <summary>
    /// Puts <c>SH</c> back to <c>$01</c> after a stack instruction's last access, for emulation
    /// mode. Emulation mode has no storage for <c>SH</c> at all (Eyes &amp; Lichty p. 71, quoted
    /// at <see cref="Op.Xce"/>) — but a new instruction's address adder still carries out of
    /// <c>SL</c>, which is how <c>PHD</c> at <c>S = $0100</c> writes its second byte at
    /// <c>$0000FF</c> and still ends with <c>S = $01FE</c>. Called from the last cycle of every
    /// push and pull rather than from <see cref="FetchOpcode"/>'s own forcing block, because the
    /// register is observable at the instruction boundary — every vector's <c>final.s</c> reads
    /// it there — and <see cref="FetchOpcode"/> does not run until the next instruction.
    /// A no-op for the old instructions, whose <see cref="S8"/> arithmetic never left page one.
    /// </summary>
    private void StackSettle816()
    {
        if (_s.E) S8 = (byte)_s.S;
    }

    /// <summary>
    /// True when the stack operation now executing moves sixteen bits. Read from the live flag
    /// rather than from <c>_wide</c>: these opcodes declare <see cref="Width.None"/>, because
    /// <c>Width</c> means "the operand fetched from memory is 16 bits" and that is what keeps
    /// <c>W65C816WidthTests</c>'s set equality meaningful.
    /// </summary>
    /// <remarks>
    /// 65816 only, like <see cref="IndexX"/> and for the same reason: the only micro-ops that
    /// call it — <see cref="MicroOp.StackPushInternal816"/> and
    /// <see cref="MicroOp.PullLow816"/> — appear in no 8-bit core's table, so no guard on
    /// <c>TVariant.Variant</c> is needed to keep <c>_s.M</c> and <c>_s.XFlag</c> (which alias
    /// the 8-bit cores' <c>U</c> and <c>B</c> bits) from being read there.
    /// </remarks>
    private bool StackIsWide() => _op switch
    {
        Op.Pha or Op.Pla => !_s.M,
        Op.Phx or Op.Plx or Op.Phy or Op.Ply => !_s.XFlag,
        Op.Phd or Op.Pld => true,
        _ => false,             // PHP, PHB, PHK, PLB — always one byte
    };

    /// <summary>
    /// The <c>X</c> index register, narrowed to 8 bits when the <c>x</c> flag selects that
    /// width. 65816 only — the five 8-bit cores have no <c>x</c> flag and no micro-op of theirs
    /// calls this. Read-time narrowing rather than a continuously-enforced invariant on
    /// <see cref="CpuState.X"/> itself, because a conformance vector's initial state is loaded
    /// directly into <see cref="CpuState"/> and can carry a nonzero high byte alongside
    /// <c>x = 1</c> without passing through any of the code paths — <c>XCE</c>, <c>REP</c>,
    /// <c>SEP</c> — that normally force it to <c>$00</c>.
    /// </summary>
    private ushort IndexX() => _s.XFlag ? (byte)_s.X : _s.X;

    /// <inheritdoc cref="IndexX"/>
    private ushort IndexY() => _s.XFlag ? (byte)_s.Y : _s.Y;

    /// <summary>
    /// The address one past <c>_addr</c> for the bank-0-confined families — plain direct page
    /// (<c>0,D+DO+1</c>) and the stack. Bank preserved, low 16 bits wrapped: since <c>_addr</c>'s
    /// bank is always 0 for these two families (Clark §5.1.2: both are "confined to" bank 0),
    /// this is the same thing as wrapping within bank 0.
    /// </summary>
    /// <remarks>
    /// Code-review fix: a single "+1" formula used to serve every 65816 addressing mode, direct
    /// and indirect alike. That is correct only for this bank-0-confined family; it is wrong for
    /// every DBR-relative and long mode, whose "+1" must carry into the next bank instead — see
    /// <see cref="HighByteAddressCarry"/>, which those modes use.
    /// </remarks>
    private int HighByteAddressBank0() => (_addr & 0xFF0000) | ((_addr + 1) & 0xFFFF);

    /// <summary>
    /// The address one past <c>_addr</c> for the DBR-relative and long families — every 65816
    /// addressing mode outside <see cref="HighByteAddressBank0"/>'s two. A plain 24-bit add, so a
    /// low-16-bit overflow carries into the next bank rather than wrapping within the current
    /// one.
    /// </summary>
    /// <remarks>
    /// Bruce Clark, "65C816 Opcodes" §5.2, Example 2, verbatim: "If the DBR is $12 and the m flag
    /// is 0, then LDA $FFFF loads the low byte of the data from address $12FFFF, and the high
    /// byte from address $130000" — and §5.1.2: "Otherwise, wrapping does not occur at bank
    /// boundaries." No SingleStepTests vector places the low access at <c>bank:$FFFF</c> with
    /// <c>m=0</c>, so this case has zero vector coverage; see
    /// <c>docs/superpowers/research/2026-08-03-65816-reference-sources.md</c> §7/§9 and the task
    /// 5 review that found the bank-preserving formula was being used here too.
    /// </remarks>
    private int HighByteAddressCarry() => (_addr + 1) & 0xFFFFFF;

    /// <summary>
    /// The address of a direct-page indirect pointer's own high byte — <c>ptr + 1</c>, confined
    /// to bank 0 (the pointer itself always lives in bank 0: <see cref="MicroOp.FetchDpOffset"/>
    /// forms <c>_ptr</c> as <c>(D + DO) &amp; 0xFFFF</c>). In emulation mode, when <c>D</c> is
    /// <c>$0000</c>, the read wraps within page zero instead of carrying into the next page.
    /// </summary>
    /// <remarks>
    /// Code-review fix. Clark's appendix, verbatim: "if the D register is $0000 (and the e flag
    /// is 1), then LDA ($FF) uses a pointer whose low byte is at $0000FF and whose high byte is
    /// at $000000 (like the 65C02), but PEI $FF pushes a 16-bit value whose low byte is at
    /// $0000FF and whose high byte is at $000100" — so the pointer read wraps, but a "new"
    /// instruction's does not. Clark §5.1.1 pins down which addressing modes this applies to:
    /// "only for 'old' instructions and addressing modes, i.e. instructions and addressing modes
    /// that are available on the 65C02." <c>(dp)</c>, <c>(dp,X)</c> and <c>(dp),Y</c> are old and
    /// call this (via <see cref="MicroOp.DpPtrReadHi"/> and <see cref="MicroOp.DpPtrReadHiY"/>);
    /// <c>[dp]</c> and <c>[dp],Y</c> are new to the 65816 and do not — their pointer reads
    /// (<see cref="MicroOp.LongPtrReadMid"/>, <see cref="MicroOp.LongPtrReadHi"/>) never wrap, at
    /// any byte of the three-byte pointer.
    /// <para>
    /// The condition is <c>D == $0000</c>, exactly as Clark words it — <b>not</b>
    /// <c>DL == $00</c>, which is what <see cref="MicroOp.DirectPageIndexX"/> uses for the index
    /// add and what this method used until phase 7c task 5. Measured: research document §12.7.
    /// Task 5's <c>SBC</c> vectors supplied the first vector in the corpus that discriminates the
    /// two — <c>e1 e 8669</c>, <c>D = $F400</c> (so <c>DL == $00</c> but <c>D != $0000</c>),
    /// pointer base <c>$F4FF</c> — and the hardware reads the high byte at <c>$F500</c>, without
    /// wrapping. The earlier "zero vector coverage" note this remark carried was true when it was
    /// written and is not any more. The index add's own <c>DL == $00</c> condition is confirmed,
    /// separately and abundantly: 320 emulation vectors wrap it with <c>D != $0000</c>.
    /// </para>
    /// </remarks>
    private int DirectPagePointerHighAddress() =>
        _s.E && _s.DP == 0
            ? (_ptr & 0xFF00) | ((_ptr + 1) & 0xFF)
            : (_ptr & 0xFF0000) | ((_ptr + 1) & 0xFFFF);

    /// <summary>
    /// Shared address formation for <c>(dp)</c> and <c>(dp),Y</c>'s pointer high byte: reads the
    /// byte at <see cref="DirectPagePointerHighAddress"/>, combines it with the low byte
    /// <see cref="MicroOp.PtrReadLo816"/> already read, and returns the resulting 16-bit
    /// <c>AAH:AAL</c> pair (unindexed). <see cref="MicroOp.DpPtrReadHi"/> uses the pair directly;
    /// <see cref="MicroOp.DpPtrReadHiY"/> and <see cref="MicroOp.DpPtrReadHiYWrite"/> index it by
    /// <c>Y</c> afterward.
    /// </summary>
    private int DirectPagePointerHigh()
    {
        var hi = ReadBus(DirectPagePointerHighAddress());
        return (hi << 8) | _tmp;
    }

    /// <summary>
    /// <c>(dp),Y</c>'s pointer high byte and mis-indexed intermediate address, shared by
    /// <see cref="MicroOp.DpPtrReadHiY"/> (read) and <see cref="MicroOp.DpPtrReadHiYWrite"/>
    /// (write) — everything except whether <see cref="MicroOp.IndexDirectPageIndirectY"/> can be
    /// skipped afterward, which differs between the two and so is left to each case in
    /// <see cref="Execute"/>.
    /// </summary>
    private void DirectPageIndirectYHigh()
    {
        var aa = DirectPagePointerHigh();
        var lo = (aa & 0xFF) + (IndexY() & 0xFF);
        _pageCross = lo > 0xFF;
        _addr = (_s.DBR << 16) | (aa & 0xFF00) | (lo & 0xFF);   // mis-indexed intermediate
        _ptr = aa;                                              // unindexed pointer, for the fixup
    }

    /// <summary>
    /// <c>abs,X</c>/<c>abs,Y</c>'s shared cycle 3 (research document §9's "Absolute,X — row 6a,
    /// and Absolute,Y — row 7"): reads <c>AAH</c> at the live program counter — the same bus
    /// access <see cref="MicroOp.FetchAddrHi"/> performs — then precomputes everything the
    /// following conditional cycle needs without spending one on it. <c>_addr</c> is left holding
    /// the mis-indexed intermediate <c>DBR,AAH,AAL+indexLow</c> (high byte un-carried, for
    /// <see cref="MicroOp.AbsIndexFixup"/> to drive if the cycle is taken); <c>_ptr</c> is left
    /// holding the real, possibly bank-carrying target <c>DBR,AA+index</c> (reused as scratch
    /// exactly as <see cref="DirectPageIndirectYHigh"/> reuses it for the unindexed pointer).
    /// Called with <see cref="IndexX"/> or <see cref="IndexY"/> depending on which register the
    /// opcode indexes by; the caller alone decides whether to skip <c>AbsIndexFixup</c>
    /// afterward, since a write never skips and a read skips only when datasheet Note 4's
    /// condition is not met.
    /// </summary>
    private void AbsIndexedHigh(ushort index)
    {
        var hi = ReadBus(PcAddress());
        _s.PC++;
        var aa = (hi << 8) | (_addr & 0xFF);
        var lo = (aa & 0xFF) + (index & 0xFF);
        _pageCross = lo > 0xFF;
        _addr = (_s.DBR << 16) | (aa & 0xFF00) | (lo & 0xFF);        // mis-indexed intermediate
        _ptr = (((_s.DBR << 16) | aa) + index) & 0xFFFFFF;           // real target, may carry a bank
    }

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
        // The 65816 emulation-mode stack pointer has no storage for its high byte at all — it
        // is hard-wired to $01 whenever E is set (Eyes & Lichty p. 71, quoted at Op.Xce), not
        // merely forced by specific writes. S8's setter (above) enforces this for every write
        // an instruction performs, and Reset()/XCE enforce it at their own mode-transition
        // points, but nothing previously enforced it independent of a write — which matters for
        // any caller that sets CpuState.S directly while E is already true (this project's own
        // conformance harness does exactly that, loading a vector's `initial` state as one
        // struct literal) and for any instruction, such as REP/SEP, that never touches S.
        // Measured against the SingleStepTests $C2/$E2 emulation-mode vectors: `initial.s`
        // deliberately carries a non-$01 high byte while e=1, and `final.s` still shows it
        // corrected to $01 even though REP/SEP's own operation never writes S — settling that
        // the correction is continuous, not write-triggered, and belongs at the instruction
        // boundary rather than inside Op.Rep/Op.Sep specifically. Applied here, once per
        // instruction and before this instruction's own sequence runs, which is early enough
        // that nothing downstream can observe the stale value.
        //
        // m, x, XH and YH get the identical treatment, for the identical reason. WDC datasheet
        // §2.8, verbatim: "The M and X flags are always equal to one in Emulation mode."
        // Research document §7 gives the full continuously-held invariant as m=1, x=1,
        // XH=YH=$00, SH=$01. Reset() and XCE already force it at their own mode-transition
        // points, and REP/SEP force it in Cpu.Exec.cs whenever their own operand would
        // otherwise clear a bit E pins — but, like SH before this fix, nothing forced it
        // independent of those specific writes: `State.E = true; State.M = false;` through the
        // public API produced a 16-bit LDA in emulation mode, which cannot happen on real
        // silicon, because nothing on that path ever ran XCE, REP or SEP.
        //
        // Folded into the same `if` as SH rather than a second one: same guard, same condition,
        // same instant. That guard is load-bearing, not defensive — Flag.M and Flag.X alias
        // Flag.U and Flag.B (see CpuState.Flag's remarks), so assigning `_s.M`/`_s.XFlag`
        // unconditionally would clear bit 5/bit 4 of P on every 8-bit core the moment this ran
        // there. It cannot run there: `TVariant.Variant == CpuVariant.W65C816` is a compile-time
        // constant per closed generic type, so the JIT sees `if (false)` and the whole block
        // folds away for the five 8-bit cores regardless of `_s.E` — the same bit-aliasing trap
        // Op.Lda/Op.Sta's own variant guard exists to avoid (Cpu.Exec.cs).
        if (TVariant.Variant == CpuVariant.W65C816 && _s.E)
        {
            _s.S = (ushort)((_s.S & 0x00FF) | 0x0100);
            _s.M = true;
            _s.XFlag = true;
            _s.X &= 0x00FF;
            _s.Y &= 0x00FF;
        }

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
                _op = Op.Nmi;
            }
            else
            {
                _op = Op.Irq;
            }

            // _op is assigned on every core but read only by the 65816, whose interrupt
            // sequence shares StackWrapsInPageOne, PushPInt816 and Vector816 with BRK and COP
            // and can tell them apart only by this. The five 8-bit cores' sequence reads none
            // of the three; nothing else in an interrupt sequence reads _op on any core.
            //
            // The vector likewise: the 65816 has two sets of four (research document §14.2),
            // so it goes through Vector816. The guard is a compile-time constant per closed
            // generic type, so for those five cores this folds back to exactly the
            // NmiVector/IrqVector choice that was here before.
            _vector = TVariant.Variant == CpuVariant.W65C816
                ? Vector816(_op)
                : _op == Op.Nmi ? NmiVector : IrqVector;
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

        // Resolve this instruction's operand width once, here, rather than per access cycle.
        // The guard is a compile-time constant per closed generic type, so the five 8-bit cores
        // emit nothing at all and _wide stays false for them regardless of what P holds — but that
        // guard is defence-in-depth and a JIT-folding win, not what actually keeps an 8-bit core
        // off the 16-bit path: their opcode tables never set Width either (Flag.M and Flag.X alias
        // Flag.U and Flag.B, so reading _s.M on a 6502 reads its always-set unused bit, and _wide
        // would still resolve to false even without this guard). What actually holds the line is
        // the read guard on every width-aware Exec arm (Cpu.Exec.cs). See the remarks on _wide
        // and UnusedFlagBitRegressionTests for the mutation testing that pins this.
        if (TVariant.Variant == CpuVariant.W65C816)
            _wide = info.Width switch
            {
                Width.M => !_s.M,
                Width.X => !_s.XFlag,
                _ => false,
            };

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

    /// <summary>
    /// One block-move iteration's register update, and the rewind — the whole of
    /// <c>MVN</c>/<c>MVP</c>'s operation, run on the last of the seven cycles. Research document
    /// §14.3.
    /// </summary>
    /// <remarks>
    /// Three rules, each from a source and each measured there:
    /// <list type="bullet">
    /// <item><description>
    /// <b>The count is the accumulator, is bytes-minus-one, and is sixteen bits regardless of
    /// <c>m</c>.</b> Clark §6.6 writes "(16-bit)" three times in two sentences and never
    /// qualifies it by <c>m</c>; §14.3 measured <c>54 n 1</c>, which has <c>m = 1</c> and takes
    /// <c>A</c> from <c>$EF9B</c> to <c>$EF8D</c> — a sixteen-bit <c>-14</c>. The <c>B</c>
    /// accumulator is clobbered, and that is correct.
    /// </description></item>
    /// <item><description>
    /// <b>The index registers move at the operative index width</b>, up for <c>MVN</c> and down
    /// for <c>MVP</c> (Table 5-7's own annotations "x,y Increment" and "x,y Decrement"). With
    /// <c>x = 1</c> they wrap inside the low byte: <c>54 n 63</c> reads <c>…$B300FF</c> then
    /// <c>$B30000</c> and ends with <c>X = $01</c> from <c>$F3</c>. <see cref="X8"/>'s setter
    /// zeroes the high byte, which agrees with hardware holding <c>XH</c>/<c>YH</c> at <c>$00</c>
    /// whenever <c>x</c> is set — the same rule <c>Op.Inx</c> already relies on. No vector in
    /// either file has <c>x = 1</c> with a nonzero index high byte, so nothing arbitrates that
    /// case; this follows the part rather than inventing a preserving formula for it.
    /// </description></item>
    /// <item><description>
    /// <b><c>PC</c> is rewound onto the opcode unless the count has run out.</b> Clark §6.6: "the
    /// program counter will be the address of the next instruction … if the accumulator is
    /// $FFFF, and the program counter will be the address of the the MVN or MVP if the
    /// accumulator is not $FFFF (i.e. the instruction jumps to itself if the accumulator is not
    /// $FFFF)." The test is on the decremented value, so a move with <c>A = $0000</c> — one byte
    /// — never rewinds at all. <c>PC</c> is sixteen bits and wraps within the bank without
    /// touching <c>PBR</c>, exactly as it does when advancing.
    /// </description></item>
    /// </list>
    /// No flag is written. <c>P</c> is identical in the initial and final state of all 20,000
    /// <c>$54</c>/<c>$44</c> native vectors.
    /// <para>
    /// 65816 only: reached from <see cref="MicroOp.BlockMoveNext"/>, which appears in no
    /// eight-bit sequence, so the reads of <c>_s.XFlag</c> here need no variant guard — the same
    /// position <see cref="StackIsWide"/> and <see cref="IndexX"/> are in.
    /// </para>
    /// </remarks>
    private void BlockMoveStep()
    {
        var delta = _op switch
        {
            Op.Mvn => +1,
            Op.Mvp => -1,
            _ => throw new InvalidOperationException($"{_op} is not a block move."),
        };

        if (_s.XFlag)
        {
            X8 = (byte)(X8 + delta);
            Y8 = (byte)(Y8 + delta);
        }
        else
        {
            _s.X = (ushort)(_s.X + delta);
            _s.Y = (ushort)(_s.Y + delta);
        }

        _s.A--;
        if (_s.A != 0xFFFF) _s.PC -= 3;
    }

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

            case MicroOp.ImpliedInternal816:
                // The same internal cycle, without the Exec() — XBA's extra one. Research
                // document §13.5, Table 5-7 row 19b: two IO cycles, both at PBR,PC+1.
                InternalCycle((_s.PBR << 16) | _s.PC);
                break;

            case MicroOp.StackPushInternal816:
                // Cycle 2 of every push: an internal cycle at PBR,PC+1 — research document
                // §14.1, Table 5-7 row 22c, measured against 08.n/08.e. The value is formed
                // here, before the first write, so both write cycles are pure.
                InternalCycle(PcAddress());
                Exec();
                if (!StackIsWide()) _mpc++;      // skip PushHigh816: one byte only
                break;

            case MicroOp.PushHigh816:
                PushStack816((byte)(_data16 >> 8));
                break;

            case MicroOp.PushLow816:
                PushStack816((byte)_data16);
                StackSettle816();
                break;

            case MicroOp.PullLow816:
                _data16 = PullStack816();
                if (StackIsWide()) break;
                StackSettle816();
                Exec();
                EndInstruction();
                break;

            case MicroOp.PullHigh816:
                _data16 |= (ushort)(PullStack816() << 8);
                StackSettle816();
                Exec();
                break;

            // Phase 7d task 3: the interrupts. Research document §14.2, Table 5-7 rows 22j
            // (BRK/COP) and 22a (IRQ/NMI/RESET/ABORT). The two rows share cycles 3 to 8
            // exactly, so the five micro-ops from PushPbr816 on are shared too; only the two
            // leading cycles differ, and each of those owns the emulation-mode skip of the
            // program-bank push.

            case MicroOp.BrkPad816:
                // Row 22j cycle 2. The signature byte IS fetched — VPA=1 with a real value on
                // the row, and every one of the 40,000 BRK/COP vectors shows a non-null value
                // with pin string "-p-r…" here. Read and discarded; PC advances past it, which
                // is why the pushed address is the instruction's own plus 2.
                ReadBus(PcAddress());
                _s.PC++;
                // The vector is chosen here rather than on the P push, where the eight-bit
                // cores commit it: that commit point exists to bound the NMI hijack, and this
                // part has none (see PushPInt816), so nothing between here and VectorLo816 can
                // change the answer. E cannot change mid-instruction either.
                _vector = Vector816(_op);
                if (_s.E) _mpc++;           // emulation pushes no program bank: skip PushPbr816
                break;

            case MicroOp.IntInternal816:
                // Row 22a cycle 2: an internal cycle at PBR,PC — VDA=0 VPA=0, no memory access.
                // PC is NOT advanced: a hardware interrupt consumes no opcode, so both of its
                // leading cycles drive the same address. Cycle 1 is FetchOpcode's own discarded
                // read, which the row prints as VDA=1 VPA=1.
                InternalCycle(PcAddress());
                if (_s.E) _mpc++;           // as BrkPad816: no program-bank push in emulation
                break;

            case MicroOp.PushPbr816:
                // Native only, both rows: "omitted when e = 1", datasheet §7.11.2 ("previous
                // contents of the PBR are not automatically saved") and Clark §6.3.1.1, which
                // lists no K push in the emulation sequence. Datasheet note 7 — "subtract 1
                // cycle for 6502 emulation mode" — is the same fact from the cycle-count side;
                // research document §14.2 records that row 22a prints that marker one line too
                // high and resolves it against row 22j, where it is unambiguous.
                PushStack816(_s.PBR);
                break;

            case MicroOp.PushPch816:
                PushStack816((byte)(_s.PC >> 8));
                break;

            case MicroOp.PushPcl816:
                PushStack816((byte)_s.PC);
                break;

            case MicroOp.PushPInt816:
                // The P push, then I set and D cleared — in that order. Clark §6.3.1: "The i
                // flag is set after pushing the P register; furthermore … the d flag is cleared
                // after pushing the P register", and measured: the pushed byte XORed against
                // the initial P is $00 across all 40,000 BRK/COP vectors, so both flag writes
                // land strictly after the push, while the final P has d = 0 and i = 1 in all
                // 40,000.
                //
                // Bit 4 is the one place the two rows differ. BRK and COP push P verbatim in
                // both modes (measured, above). A hardware interrupt pushes it verbatim in
                // native mode too — Table 8-1, "X=X on stack always", bit 4 simply being the x
                // flag — but forces it to 0 in emulation mode: datasheet note 11, "BRK bit 4
                // equals 0 in Emulation mode", printed on row 22a and NOT on row 22j, which is
                // the 6502's rule and the opposite of what the note's wording reads like on its
                // own. Flag.B and Flag.X are the same bit; the name used here is Flag.B because
                // what is being cleared is the break flag, not the index-width flag — and in
                // emulation mode x is forced to 1 anyway, so the two readings differ only in
                // what they mean, never in what they do.
                //
                // No NMI hijack, deliberately. The NMOS cores in this repository let a latched
                // NMI steal a BRK's vector on this very cycle; research document §14.2 records
                // that no 65816 source mentions the case at all and that no vector can settle
                // it, and §14.9's gap 2 says outright not to carry the NMOS behaviour over by
                // inference. It is not carried over. The general 6502-family sampling rule —
                // recognise at an instruction boundary — is what runs here instead, and it
                // needs no unsourced claim; the NMOS anomaly is a property of that die's ~VEC
                // transistor chain, which no source places on this one. Same reasoning applies
                // to the recognition blackout Tick performs on MicroOp.VectorHi: VectorHi816 is
                // deliberately NOT in it. See the task 3 report.
                PushStack816((_op is Op.Irq or Op.Nmi) && _s.E ? (byte)(_s.P & ~Flag.B) : _s.P);
                _s.I = true;
                _s.D = false;
                break;

            case MicroOp.VectorLo816:
                _tmp = ReadBus(_vector);
                break;

            case MicroOp.VectorHi816:
                // Both vector bytes come from bank 0 — both rows print "0,VA" and "0,VA+1" —
                // and _vector is already a bank-0 address, so no PBR is folded in. PBR itself
                // is cleared here, in both modes: datasheet §7.11.1 and §7.11.2 say so
                // separately for native and emulation, the rows' own "0,AAV" next-opcode cell
                // says it from the bus side, and the final PBR is $00 in all 40,000 vectors.
                _s.PC = (ushort)((ReadBus(_vector + 1) << 8) | _tmp);
                _s.PBR = 0;
                break;

            case MicroOp.StackDummyReadDec816:
                // The 65816's reset stack decrement. Same shape as StackDummyReadDec, but the
                // address comes from StackAddress816() rather than a hard-coded 0x0100 + S8.
                // Reset always enters emulation mode, where the two agree — this exists so the
                // formula is right because it is right, not because another invariant holds.
                ReadBus(StackAddress816());
                if (_s.E) S8--; else _s.S--;
                break;

            // Phase 7d task 4: the block moves. Research document §14.3, Table 5-7 rows 9a and
            // 9b — seven cycles per byte moved, the fetch plus these six, and one whole
            // instruction per byte. Every cycle below drives exactly _addr at the moment it
            // runs, which is why _addr is re-pointed at the destination on the read cycle: it
            // makes the RDY halt path in Tick() re-drive the right address on all four
            // non-fetch cycles, rather than the stale-address approximation that path notes as
            // a limitation for the rest of the core.

            case MicroOp.BlockMoveDestBank:
                // Cycle 2, DBA. Datasheet §7.18, verbatim in §14.3: "The MVN and MVP
                // instructions change the Data Bank Register to the value of the second byte of
                // the instruction (destination bank address)." The second byte of the
                // instruction is the one at PC+1 — the opcode is the first — so the register is
                // written here, on the fetch, and the write cycle reads it back from DBR.
                _s.DBR = ReadBus(PcAddress());
                _s.PC++;
                break;

            case MicroOp.BlockMoveSrcBank:
                // Cycle 3, SBA — into no register at all, only into this iteration's own source
                // address, which is why both operand bytes are re-fetched every iteration. The
                // index is folded in here rather than on the read cycle: X cannot change in
                // between (nothing but BlockMoveNext touches it), and it leaves _addr holding
                // the exact address cycle 4 will drive.
                _addr = (ReadBus(PcAddress()) << 16) | IndexX();
                _s.PC++;
                break;

            case MicroOp.BlockMoveRead:
                // Cycle 4, at SBA,X. _addr then becomes DBA,Y for the three cycles that follow:
                // DBR is already the destination bank (cycle 2) and Y is still this iteration's,
                // since BlockMoveNext has not run yet. Both addresses wrap inside their own
                // bank — Clark §5.1.2, "source,destination addressing … wraps at both the source
                // and destination bank boundaries" — which is what the 16-bit IndexX()/IndexY()
                // ORed under a bank byte gives, with no carry out of the low sixteen bits.
                _data = ReadBus(_addr);
                _addr = (_s.DBR << 16) | IndexY();
                break;

            case MicroOp.BlockMoveWrite:
                // Cycle 5, at DBA,Y.
                WriteBus(_addr, _data);
                break;

            case MicroOp.BlockMoveInternal:
                // Cycle 6: an internal cycle at the address just written, not at PBR,PC. See
                // MicroOp.BlockMoveInternal — the only internal cycles on this part whose
                // address is a data address.
                InternalCycle(_addr);
                break;

            case MicroOp.BlockMoveNext:
                // Cycle 7: the same internal cycle again, then the whole iteration's register
                // update and the rewind. The instruction ends here either way; only where PC
                // points differs, so a move that is not finished is re-entered by the next
                // fetch and an interrupt latched during this cycle is serviced first. Clark
                // §6.6: MVN/MVP "can be interrupted by IRQ and NMI before the move is complete
                // … however, they can only be interrupted every seventh cycle."
                InternalCycle(_addr);
                BlockMoveStep();
                break;

            case MicroOp.RepSepOperand:
                _data = ReadBus(PcAddress());
                _s.PC++;
                break;

            case MicroOp.RepSepExec:
                // Internal cycle at the operand's own address — PC+1 in datasheet Note 1's
                // terms, which is PC-1 from here since RepSepOperand already advanced past it.
                InternalCycle((_s.PBR << 16) | ((_s.PC - 1) & 0xFFFF));
                Exec();
                break;

            case MicroOp.FetchDpOffset:
            {
                var dpOffset = ReadBus(PcAddress());
                _s.PC++;
                _addr = (_s.DP + dpOffset) & 0xFFFF;
                if ((_s.DP & 0xFF) == 0) _mpc++;      // DL == $00: skip DirectPagePenalty
                break;
            }

            case MicroOp.DirectPagePenalty:
                InternalCycle((_s.PBR << 16) | ((_s.PC - 1) & 0xFFFF));
                break;

            case MicroOp.DirectPageIndexX:
                InternalCycle((_s.PBR << 16) | ((_s.PC - 1) & 0xFFFF));
                _addr = _s.E && (_s.DP & 0xFF) == 0
                    ? (_addr & 0xFF00) | ((_addr + IndexX()) & 0xFF)
                    : (_addr + IndexX()) & 0xFFFF;
                break;

            case MicroOp.DirectPageIndexY:
                InternalCycle((_s.PBR << 16) | ((_s.PC - 1) & 0xFFFF));
                _addr = _s.E && (_s.DP & 0xFF) == 0
                    ? (_addr & 0xFF00) | ((_addr + IndexY()) & 0xFF)
                    : (_addr + IndexY()) & 0xFFFF;
                break;

            case MicroOp.PtrReadLo816:
                _ptr = _addr;
                _tmp = ReadBus(_ptr);
                break;

            case MicroOp.DpPtrReadHi:
                _addr = (_s.DBR << 16) | DirectPagePointerHigh();
                break;

            case MicroOp.DpPtrReadHiY:
                DirectPageIndirectYHigh();
                // Read only: skip the indexing cycle when datasheet Note 4's condition is not
                // met. info.Access decided this micro-op at table-build time — see
                // MicroOp.DpPtrReadHiY's remarks — so no opcode comparison happens here.
                if (!_pageCross && _s.XFlag) _mpc++;
                break;

            case MicroOp.DpPtrReadHiYWrite:
                // A write pays the indexing cycle unconditionally ("or write") — never skips.
                DirectPageIndirectYHigh();
                break;

            case MicroOp.IndexDirectPageIndirectY:
                InternalCycle(_addr);
                _addr = (((_s.DBR << 16) | _ptr) + IndexY()) & 0xFFFFFF;
                break;

            case MicroOp.LongPtrReadMid:
            {
                var mid = ReadBus((_ptr & 0xFF0000) | ((_ptr + 1) & 0xFFFF));
                _addr = (mid << 8) | _tmp;
                break;
            }

            case MicroOp.LongPtrReadHi:
            {
                var bank = ReadBus((_ptr & 0xFF0000) | ((_ptr + 2) & 0xFFFF));
                _addr = (bank << 16) | _addr;
                break;
            }

            case MicroOp.LongPtrReadHiY:
            {
                var bank = ReadBus((_ptr & 0xFF0000) | ((_ptr + 2) & 0xFFFF));
                _addr = (((bank << 16) | _addr) + IndexY()) & 0xFFFFFF;
                break;
            }

            case MicroOp.ReadExec816:
                _data = ReadBus(_addr);
                if (!_wide) { Exec(); EndInstruction(); }
                break;

            case MicroOp.ReadExecHigh816:
            {
                var hi = ReadBus(HighByteAddressBank0());
                _data16 = (ushort)((hi << 8) | _data);
                Exec();
                break;
            }

            case MicroOp.ReadExecHigh816Carry:
            {
                var hi = ReadBus(HighByteAddressCarry());
                _data16 = (ushort)((hi << 8) | _data);
                Exec();
                break;
            }

            case MicroOp.ExecWrite816:
                Exec();
                if (!_wide) { WriteBus(_addr, _data); EndInstruction(); }
                else WriteBus(_addr, (byte)_data16);
                break;

            case MicroOp.ExecWriteHigh816:
                WriteBus(HighByteAddressBank0(), (byte)(_data16 >> 8));
                break;

            case MicroOp.ExecWriteHigh816Carry:
                WriteBus(HighByteAddressCarry(), (byte)(_data16 >> 8));
                break;

            // Phase 7c′ task 2: the read-modify-write access class. Six slots emitted, four run
            // at 8-bit width and five at 16 — the skips below, not a shorter emitted sequence,
            // are what keeps every slot statically classified in MicroOps.IsWriteCycle. See
            // MicroOp's own remarks on each member for the research document §13.1 row it is.

            case MicroOp.RmwRead816:
                _data = ReadBus(_addr);
                if (!_wide)
                {
                    _mpc++;                       // 8-bit: skip the high-byte read
                    if (!_s.E) _mpc++;            // native: skip the NMOS write-form too
                }
                break;

            case MicroOp.RmwReadHigh816:
                _data16 = (ushort)((ReadBus(HighByteAddressBank0()) << 8) | _data);
                _mpc++;                           // 16-bit is native-only, so always skip the write-form
                break;

            case MicroOp.RmwReadHigh816Carry:
                _data16 = (ushort)((ReadBus(HighByteAddressCarry()) << 8) | _data);
                _mpc++;
                break;

            case MicroOp.RmwModifyWrite816:
                // Datasheet Note 17: in emulation mode the middle cycle writes the UNMODIFIED
                // value back, as NMOS does. Emulation forces m=1, so this is always 8-bit.
                WriteBus(_addr, _data);
                Exec();
                _mpc += 2;                        // skip the read-form and the high-byte write
                break;

            // Native mode: an INTERNAL cycle, not a read — research document §13.1, Table 5-7
            // rows 1d/6b/10b/16b all print VDA=0 VPA=0, data bus IO, RWB=1. MLB is still
            // asserted, which is why these two carry BusPins.Mlb alone. The address driven is the
            // high one (AA+1) at 16 bits; §13.1 records the 8-bit case as a gap, so _addr is used
            // there and the vectors settle it.
            //
            // Two variants for the same reason the read and write halves have two: whether the
            // "+1" carries into the next bank is a property of the addressing mode, decided at
            // table-build time, not something a shared micro-op can test.
            case MicroOp.RmwModifyRead816:
                InternalCycle(_wide ? HighByteAddressBank0() : _addr);
                Exec();
                if (!_wide) _mpc++;               // 8-bit: skip the high-byte write
                break;

            case MicroOp.RmwModifyRead816Carry:
                InternalCycle(_wide ? HighByteAddressCarry() : _addr);
                Exec();
                if (!_wide) _mpc++;
                break;

            case MicroOp.RmwWriteHigh816:
                WriteBus(HighByteAddressBank0(), (byte)(_data16 >> 8));
                break;

            case MicroOp.RmwWriteHigh816Carry:
                WriteBus(HighByteAddressCarry(), (byte)(_data16 >> 8));
                break;

            case MicroOp.RmwWrite816:
                WriteBus(_addr, _wide ? (byte)_data16 : _data);
                break;

            // Task 6: absolute, long, stack-relative and immediate. See MicroOp's own remarks
            // on each member for the research document §9 row it comes from.

            case MicroOp.ImmExec816:
                _data = ReadBus(PcAddress());
                _s.PC++;
                if (!_wide) { Exec(); EndInstruction(); }
                break;

            case MicroOp.ImmExecHigh816:
            {
                var hi = ReadBus(PcAddress());
                _s.PC++;
                _data16 = (ushort)((hi << 8) | _data);
                Exec();
                break;
            }

            case MicroOp.AbsHi:
            {
                var hi = ReadBus(PcAddress());
                _s.PC++;
                _addr = (_s.DBR << 16) | (hi << 8) | (_addr & 0xFF);
                break;
            }

            case MicroOp.AbsHiIndexedX:
                AbsIndexedHigh(IndexX());
                if (!_pageCross && _s.XFlag) _mpc++;
                break;

            case MicroOp.AbsHiIndexedXWrite:
                AbsIndexedHigh(IndexX());
                break;

            case MicroOp.AbsHiIndexedY:
                AbsIndexedHigh(IndexY());
                if (!_pageCross && _s.XFlag) _mpc++;
                break;

            case MicroOp.AbsHiIndexedYWrite:
                AbsIndexedHigh(IndexY());
                break;

            case MicroOp.AbsIndexFixup:
                InternalCycle(_addr);   // the mis-indexed address AbsHiIndexed* formed
                _addr = _ptr;           // the real, precomputed (and possibly bank-carried) target
                break;

            case MicroOp.FetchAddrBank:
            {
                var bank = ReadBus(PcAddress());
                _s.PC++;
                _addr = (bank << 16) | _addr;
                break;
            }

            case MicroOp.FetchAddrBankX:
            {
                var bank = ReadBus(PcAddress());
                _s.PC++;
                _addr = (((bank << 16) | _addr) + IndexX()) & 0xFFFFFF;
                break;
            }

            case MicroOp.FetchSrOffset:
            {
                var so = ReadBus(PcAddress());
                _s.PC++;
                _addr = (_s.S + so) & 0xFFFF;
                break;
            }

            case MicroOp.StackRelativePenalty:
                InternalCycle((_s.PBR << 16) | ((_s.PC - 1) & 0xFFFF));
                break;

            case MicroOp.SrPtrReadHi:
            {
                var hi = ReadBus((_ptr + 1) & 0xFFFF);
                _addr = (hi << 8) | _tmp;
                break;
            }

            case MicroOp.IndexStackRelativeIndirectY:
                InternalCycle((_ptr + 1) & 0xFFFF);
                _addr = (((_s.DBR << 16) | _addr) + IndexY()) & 0xFFFFFF;
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

            case MicroOp.BranchFetch816:
                _data = ReadBus(PcAddress());
                _s.PC++;
                if (!IsBranchTaken()) EndInstruction();
                break;

            case MicroOp.BranchTaken816:
            {
                // The IO cycle drives the displacement byte's own address — research document
                // §14.5's row 20 writes cycles 2, 2a and 2b all as PBR,PC+1 — so it is PC-1 by
                // the time the fetch above has stepped over it. Stashed because cycle 2b drives
                // the same address after PC has moved to the destination.
                _addr = (_s.PBR << 16) | ((_s.PC - 1) & 0xFFFF);
                InternalCycle(_addr);

                // Wraps inside the program bank: PC is a ushort and PBR is untouched.
                var lo = (_s.PC & 0xFF) + (sbyte)_data;
                _s.PC = (ushort)(_s.PC + (sbyte)_data);

                // Datasheet Note 6: the page-cross cycle exists in emulation mode ONLY. Clark
                // §6.2.1.1's 2+t+t*e*p says the same by multiplying the term by e. A taken
                // branch in native mode is a flat three cycles wherever it lands, which is the
                // one place this core's branches differ from all five 8-bit cores here.
                if (!_s.E || lo is >= 0 and <= 0xFF) EndInstruction();
                break;
            }

            case MicroOp.BranchFixup816:
                InternalCycle(_addr);                 // row 20's cycle 2b, e = 1 only
                break;

            case MicroOp.BranchLong816:
                // Row 21's cycle 4, at PBR,PC+2 — the high displacement byte's own address, the
                // same "last operand byte" rule row 20 follows. _addr holds the signed 16-bit
                // displacement FetchAddrLo and FetchAddrHi assembled. Recomputed here rather than
                // stashed the way BranchTaken816 stashes the equivalent PBR,PC+1 above: BRL has
                // only this one internal cycle, so there is no second cycle to hand a stash to.
                InternalCycle((_s.PBR << 16) | ((_s.PC - 1) & 0xFFFF));
                _s.PC = (ushort)(_s.PC + (short)_addr);
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

            case MicroOp.WaiHold816:
                // The 65816's hold. Same wake rule as WaiHold — the interrupt SIGNAL, not the
                // poll — and the same _mpc-- that keeps the core off an instruction boundary
                // for as long as it holds. No bus access: research document §14.4 measured
                // every one of the 20,000 $CB vectors ending in [null, null, "--------"], with
                // no address and no read/write character at all. See MicroOp.WaiHold816.
                if (_nmiPending || _irqLine) _waiting = false;
                else { _waiting = true; _mpc--; }
                break;

            case MicroOp.StpHold816:
                _stopped = true;
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
