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
            case Op.NopRead: break;   // the read already happened; the value is discarded

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

            // Stores. The value lands in _data, which the writing micro-op then commits.
            case Op.Sta: _data = _s.A; break;
            case Op.Stx: _data = _s.X; break;
            case Op.Sty: _data = _s.Y; break;

            // Memory increment and decrement, operating on _data in place.
            case Op.Inc: _data = (byte)(_data + 1); SetZN(_data); break;
            case Op.Dec: _data = (byte)(_data - 1); SetZN(_data); break;

            // Register increment and decrement.
            case Op.Inx: _s.X = (byte)(_s.X + 1); SetZN(_s.X); break;
            case Op.Dex: _s.X = (byte)(_s.X - 1); SetZN(_s.X); break;
            case Op.Iny: _s.Y = (byte)(_s.Y + 1); SetZN(_s.Y); break;
            case Op.Dey: _s.Y = (byte)(_s.Y - 1); SetZN(_s.Y); break;

            // Stack. PHP and BRK are the only ways the B flag reaches memory.
            case Op.Pha: _data = _s.A; break;
            case Op.Php: _data = (byte)(_s.P | Flag.B | Flag.U); break;
            case Op.Pla: _s.A = _data; SetZN(_s.A); break;
            case Op.Plp: _s.P = (byte)((_data & ~Flag.B) | Flag.U); break;

            // Logic
            case Op.And: _s.A &= _data; SetZN(_s.A); break;
            case Op.Ora: _s.A |= _data; SetZN(_s.A); break;
            case Op.Eor: _s.A ^= _data; SetZN(_s.A); break;

            // BIT takes N and V straight from the operand's top two bits, and Z from
            // the AND. The accumulator is not modified.
            case Op.Bit:
                _s.Z = (_s.A & _data) == 0;
                _s.N = (_data & 0x80) != 0;
                _s.V = (_data & 0x40) != 0;
                break;

            // Compares
            case Op.Cmp: Compare(_s.A); break;
            case Op.Cpx: Compare(_s.X); break;
            case Op.Cpy: Compare(_s.Y); break;

            // Shifts and rotates on memory, operating on _data in place.
            case Op.Asl: _data = Asl(_data); break;
            case Op.Lsr: _data = Lsr(_data); break;
            case Op.Rol: _data = Rol(_data); break;
            case Op.Ror: _data = Ror(_data); break;

            // The same four on the accumulator.
            case Op.AslA: _s.A = Asl(_s.A); break;
            case Op.LsrA: _s.A = Lsr(_s.A); break;
            case Op.RolA: _s.A = Rol(_s.A); break;
            case Op.RorA: _s.A = Ror(_s.A); break;

            // Arithmetic
            case Op.Adc: Adc(_data); break;
            case Op.Sbc: Sbc(_data); break;

            // Undocumented combination read-modify-writes. Each performs a documented
            // memory operation and then a documented ALU operation on the result.
            // Rra and Isc inherit decimal-mode behaviour from Adc and Sbc.
            case Op.Slo: _data = Asl(_data); _s.A |= _data; SetZN(_s.A); break;
            case Op.Rla: _data = Rol(_data); _s.A &= _data; SetZN(_s.A); break;
            case Op.Sre: _data = Lsr(_data); _s.A ^= _data; SetZN(_s.A); break;
            case Op.Rra: _data = Ror(_data); Adc(_data); break;
            case Op.Dcp: _data = (byte)(_data - 1); Compare(_s.A); break;
            case Op.Isc: _data = (byte)(_data + 1); Sbc(_data); break;

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

    /// <summary>Compares a register against <c>_data</c>, setting C, Z and N.</summary>
    private void Compare(byte register)
    {
        var result = register - _data;
        _s.C = result >= 0;
        SetZN((byte)result);
    }

    private byte Asl(byte value)
    {
        _s.C = (value & 0x80) != 0;
        var result = (byte)(value << 1);
        SetZN(result);
        return result;
    }

    private byte Lsr(byte value)
    {
        _s.C = (value & 0x01) != 0;
        var result = (byte)(value >> 1);
        SetZN(result);
        return result;
    }

    private byte Rol(byte value)
    {
        var carryIn = _s.C ? 1 : 0;
        _s.C = (value & 0x80) != 0;
        var result = (byte)((value << 1) | carryIn);
        SetZN(result);
        return result;
    }

    private byte Ror(byte value)
    {
        var carryIn = _s.C ? 0x80 : 0x00;
        _s.C = (value & 0x01) != 0;
        var result = (byte)((value >> 1) | carryIn);
        SetZN(result);
        return result;
    }

    /// <summary>Add with carry, in binary or NMOS decimal mode.</summary>
    private void Adc(byte value)
    {
        var carry = _s.C ? 1 : 0;
        var binary = _s.A + value + carry;

        if (!_s.D)
        {
            _s.C = binary > 0xFF;
            _s.V = (~(_s.A ^ value) & (_s.A ^ binary) & 0x80) != 0;
            _s.A = (byte)binary;
            SetZN(_s.A);
            return;
        }

        // NMOS decimal mode. Z comes from the binary sum; N and V come from the
        // partially corrected high nibble. Those two are documented as undefined
        // precisely because they leak this intermediate — reproducing the leak is
        // what makes the per-cycle vectors pass.
        var lo = (_s.A & 0x0F) + (value & 0x0F) + carry;
        if (lo > 0x09) lo += 0x06;
        var hi = (_s.A >> 4) + (value >> 4) + (lo > 0x0F ? 1 : 0);

        _s.Z = (binary & 0xFF) == 0;
        _s.N = (hi & 0x08) != 0;
        _s.V = (~(_s.A ^ value) & (_s.A ^ (hi << 4)) & 0x80) != 0;

        if (hi > 0x09) hi += 0x06;
        _s.C = hi > 0x0F;
        _s.A = (byte)((hi << 4) | (lo & 0x0F));
    }

    /// <summary>Subtract with borrow, in binary or NMOS decimal mode.</summary>
    private void Sbc(byte value)
    {
        var borrow = _s.C ? 0 : 1;
        var binary = _s.A - value - borrow;

        // On NMOS parts every flag comes from the binary result, in both modes.
        // Only the accumulator differs.
        _s.C = binary >= 0;
        _s.V = ((_s.A ^ value) & (_s.A ^ binary) & 0x80) != 0;
        _s.Z = (binary & 0xFF) == 0;
        _s.N = (binary & 0x80) != 0;

        if (!_s.D)
        {
            _s.A = (byte)binary;
            return;
        }

        var lo = (_s.A & 0x0F) - (value & 0x0F) - borrow;
        var hi = (_s.A >> 4) - (value >> 4);
        if ((lo & 0x10) != 0)
        {
            lo -= 0x06;
            hi--;
        }
        if ((hi & 0x10) != 0) hi -= 0x06;

        _s.A = (byte)((hi << 4) | (lo & 0x0F));
    }
}
