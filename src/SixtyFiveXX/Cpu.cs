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
                _bus.Write(0x0100 + _s.S, (byte)((_s.P | Flag.U) & ~Flag.B));
                _s.S--;
                _s.I = true;
                break;

            case MicroOp.VectorLo:
                _tmp = _bus.Read(_vector);
                break;

            case MicroOp.VectorHi:
                _s.PC = (ushort)((_bus.Read(_vector + 1) << 8) | _tmp);
                break;

            default:
                throw new NotImplementedException($"Micro-op {micro} is not implemented yet.");
        }
    }
}
