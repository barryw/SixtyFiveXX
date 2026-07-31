using System.Runtime.CompilerServices;

namespace SixtyFiveXX;

public sealed partial class Cpu<TBus>
{
    /// <summary>
    /// Applies the current instruction's operation. Called once per instruction, from
    /// whichever micro-op performs the final bus access, so this switch is off the
    /// per-cycle path.
    /// </summary>
    private void Exec()
    {
        switch (_op)
        {
            case Op.Nop: break;

            // Loads
            case Op.Lda: _s.A = _data; SetZN(_s.A); break;
            case Op.Ldx: _s.X = _data; SetZN(_s.X); break;
            case Op.Ldy: _s.Y = _data; SetZN(_s.Y); break;

            // Transfers. TXS is the only one that leaves flags alone.
            case Op.Tax: _s.X = _s.A; SetZN(_s.X); break;
            case Op.Tay: _s.Y = _s.A; SetZN(_s.Y); break;
            case Op.Tsx: _s.X = _s.S; SetZN(_s.X); break;
            case Op.Txa: _s.A = _s.X; SetZN(_s.A); break;
            case Op.Tya: _s.A = _s.Y; SetZN(_s.A); break;
            case Op.Txs: _s.S = _s.X; break;

            // Flags
            case Op.Clc: _s.C = false; break;
            case Op.Sec: _s.C = true;  break;
            case Op.Cld: _s.D = false; break;
            case Op.Sed: _s.D = true;  break;
            case Op.Cli: _s.I = false; break;
            case Op.Sei: _s.I = true;  break;
            case Op.Clv: _s.V = false; break;

            default:
                throw new NotImplementedException($"Operation {_op} is not implemented yet.");
        }
    }

    /// <summary>Sets Z and N from a result byte. Every 6502 operation that touches them does it this way.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetZN(byte value)
    {
        _s.Z = value == 0;
        _s.N = (value & 0x80) != 0;
    }
}
