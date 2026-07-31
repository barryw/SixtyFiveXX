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

    private byte _opcode;
    private Op _op;

    /// <summary>The value being read, written, or modified by the current instruction.</summary>
    private byte _data;

    // Scaffolding for the addressing-mode and control-flow micro-ops Tasks 6-12 add to
    // Execute(MicroOp); unread until those micro-op cases exist, so the compiler's
    // unused-field warnings are suppressed for just this block rather than deleting
    // fields the brief specifies for a shell task.
#pragma warning disable CS0169, CS0414

    /// <summary>Scratch for the low byte of a 16-bit quantity assembled across two cycles.</summary>
    private byte _tmp;

    /// <summary>The effective address.</summary>
    private int _addr;

    /// <summary>The indirect pointer address, for the (zp,X) and (zp),Y modes.</summary>
    private int _ptr;

    /// <summary>Set when indexing carried out of the low byte of the effective address.</summary>
    private bool _pageCross;

    /// <summary>+0x100 or -0x100, applied by <see cref="MicroOp.BranchFixup"/>.</summary>
    private int _branchFix;

    /// <summary>The vector the in-progress interrupt or BRK sequence will read.</summary>
    private int _vector = IrqVector;

#pragma warning restore CS0169, CS0414

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

    /// <summary>The bus. Exposed so a caller holding only the CPU can reach its memory.</summary>
    public ref TBus Bus => ref _bus;

    /// <summary>Advances the core by one clock cycle.</summary>
    public void Tick()
    {
        _cycles++;

        if (_mpc < 0)
        {
            FetchOpcode();
            return;
        }

        var micro = _ops[_mpc];
        _mpc++;
        Execute(micro);

        // A micro-op may have ended the instruction early (an untaken branch, or an
        // indexed read that did not cross a page). Otherwise the sequence ends when
        // the next slot is the terminator.
        if (_mpc >= 0 && _ops[_mpc] == MicroOp.End) _mpc = -1;
    }

    private void FetchOpcode()
    {
        var pc = _s.PC;
        _opcode = _bus.Read(pc);
        _s.PC++;

        var entry = _entry[_opcode];
        if (_ops[entry] == MicroOp.End) throw new UndefinedOpcodeException(_opcode, pc);

        _op = _table.Info[_opcode].Operation;
        _mpc = entry;
    }

    /// <summary>Ends the current instruction; the next tick fetches an opcode.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EndInstruction() => _mpc = -1;

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

            default:
                throw new NotImplementedException($"Micro-op {micro} is not implemented yet.");
        }
    }
}
