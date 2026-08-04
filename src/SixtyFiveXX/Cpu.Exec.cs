using System.Runtime.CompilerServices;

namespace SixtyFiveXX;

public sealed partial class Cpu<TBus, TVariant>
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
            case Op.Lda: A8 = _data; SetZN(A8); break;
            case Op.Ldx: X8 = _data; SetZN(X8); break;
            case Op.Ldy: Y8 = _data; SetZN(Y8); break;

            // Transfers. TXS is the only one that leaves flags alone.
            case Op.Tax: X8 = A8; SetZN(X8); break;
            case Op.Tay: Y8 = A8; SetZN(Y8); break;
            case Op.Tsx: X8 = S8; SetZN(X8); break;
            case Op.Txa: A8 = X8; SetZN(A8); break;
            case Op.Tya: A8 = Y8; SetZN(A8); break;
            case Op.Txs: S8 = X8; break;

            // Flags
            case Op.Clc: _s.C = false; break;
            case Op.Sec: _s.C = true;  break;
            case Op.Cld: _s.D = false; break;
            case Op.Sed: _s.D = true;  break;
            case Op.Cli: _s.I = false; break;
            case Op.Sei: _s.I = true;  break;
            case Op.Clv: _s.V = false; break;

            // Stores. The value lands in _data, which the writing micro-op then commits.
            case Op.Sta: _data = A8; break;
            case Op.Stx: _data = X8; break;
            case Op.Sty: _data = Y8; break;
            case Op.Stz: _data = 0; break;

            // Rockwell's bit set and reset, read-modify-writes over data. The bit index
            // comes from the opcode, as it does in hardware. Neither touches any flag.
            case Op.Rmb: _data = (byte)(_data & ~(1 << ((_opcode >> 4) & 7))); break;
            case Op.Smb: _data = (byte)(_data | (1 << ((_opcode >> 4) & 7))); break;

            // BBR and BBS do their work in BitBranchFetch; nothing remains for Exec.
            case Op.Bbr:
            case Op.Bbs:
                break;

            // Test-and-modify. Z comes from the AND, as for BIT, but N and V are left
            // alone — unlike BIT, which takes them from the operand's top two bits.
            case Op.Trb: _s.Z = (A8 & _data) == 0; _data = (byte)(_data & ~A8); break;
            case Op.Tsb: _s.Z = (A8 & _data) == 0; _data = (byte)(_data | A8); break;

            // Memory increment and decrement, operating on _data in place.
            case Op.Inc: _data = (byte)(_data + 1); SetZN(_data); break;
            case Op.Dec: _data = (byte)(_data - 1); SetZN(_data); break;

            // Register increment and decrement.
            case Op.IncA: A8 = (byte)(A8 + 1); SetZN(A8); break;
            case Op.DecA: A8 = (byte)(A8 - 1); SetZN(A8); break;
            case Op.Inx: X8 = (byte)(X8 + 1); SetZN(X8); break;
            case Op.Dex: X8 = (byte)(X8 - 1); SetZN(X8); break;
            case Op.Iny: Y8 = (byte)(Y8 + 1); SetZN(Y8); break;
            case Op.Dey: Y8 = (byte)(Y8 - 1); SetZN(Y8); break;

            // Stack. PHP and BRK are the only ways the B flag reaches memory.
            case Op.Pha: _data = A8; break;
            case Op.Php: _data = (byte)(_s.P | Flag.B | Flag.U); break;
            case Op.Pla: A8 = _data; SetZN(A8); break;
            case Op.Plp: _s.P = (byte)((_data & ~Flag.B) | Flag.U); break;
            case Op.Phx: _data = X8; break;
            case Op.Phy: _data = Y8; break;
            case Op.Plx: X8 = _data; SetZN(X8); break;
            case Op.Ply: Y8 = _data; SetZN(Y8); break;

            // Logic
            case Op.And: A8 &= _data; SetZN(A8); break;
            case Op.Ora: A8 |= _data; SetZN(A8); break;
            case Op.Eor: A8 ^= _data; SetZN(A8); break;

            // BIT takes N and V straight from the operand's top two bits, and Z from
            // the AND. The accumulator is not modified.
            case Op.Bit:
                _s.Z = (A8 & _data) == 0;
                _s.N = (_data & 0x80) != 0;
                _s.V = (_data & 0x40) != 0;
                break;

            // BIT immediate sets Z alone — see Op.BitImm.
            case Op.BitImm: _s.Z = (A8 & _data) == 0; break;

            // Compares
            case Op.Cmp: Compare(A8); break;
            case Op.Cpx: Compare(X8); break;
            case Op.Cpy: Compare(Y8); break;

            // Shifts and rotates on memory, operating on _data in place.
            case Op.Asl: _data = Asl(_data); break;
            case Op.Lsr: _data = Lsr(_data); break;
            case Op.Rol: _data = Rol(_data); break;
            case Op.Ror: _data = Ror(_data); break;

            // The same four on the accumulator.
            case Op.AslA: A8 = Asl(A8); break;
            case Op.LsrA: A8 = Lsr(A8); break;
            case Op.RolA: A8 = Rol(A8); break;
            case Op.RorA: A8 = Ror(A8); break;

            // Arithmetic
            case Op.Adc: Adc(_data); break;
            case Op.Sbc: Sbc(_data); break;
            case Op.AdcCmos: AdcCmos(_data); break;
            case Op.SbcCmos: SbcCmos(_data); break;

            // Undocumented combination read-modify-writes. Each performs a documented
            // memory operation and then a documented ALU operation on the result.
            // Rra and Isc inherit decimal-mode behaviour from Adc and Sbc.
            case Op.Slo: _data = Asl(_data); A8 |= _data; SetZN(A8); break;
            case Op.Rla: _data = Rol(_data); A8 &= _data; SetZN(A8); break;
            case Op.Sre: _data = Lsr(_data); A8 ^= _data; SetZN(A8); break;
            case Op.Rra: _data = Ror(_data); Adc(_data); break;
            case Op.Dcp: _data = (byte)(_data - 1); Compare(A8); break;
            case Op.Isc: _data = (byte)(_data + 1); Sbc(_data); break;

            // Undocumented. LAX loads both registers from one read; SAX stores the
            // AND of A and X and is the only store on the part that sets no flags.
            case Op.Lax: A8 = _data; X8 = _data; SetZN(_data); break;
            case Op.Sax: _data = (byte)(A8 & X8); break;

            // Undocumented immediate-mode instructions.
            case Op.Anc:
                A8 &= _data;
                SetZN(A8);
                _s.C = _s.N;                    // carry mirrors bit 7 of the result
                break;

            case Op.Alr:
                A8 &= _data;
                A8 = Lsr(A8);                   // Lsr sets C from bit 0 and Z/N from the result
                break;

            case Op.Arr:
                Arr(_data);
                break;

            case Op.Sbx:
            {
                // X = (A & X) - imm, always binary, never affected by decimal mode.
                var result = (A8 & X8) - _data;
                _s.C = result >= 0;
                X8 = (byte)result;
                SetZN(X8);
                break;
            }

            // Undocumented and unstable. The magic constant $EE was determined
            // empirically from the SingleStepTests vectors, not chosen: for $8B with
            // A=$E4, X=$E2, imm=$23, only ($E4 | $EE) & $E2 & $23 yields the expected $22.
            case Op.Ane: A8 = (byte)((A8 | AneMagic) & X8 & _data); SetZN(A8); break;
            case Op.Lxa: A8 = X8 = (byte)((A8 | AneMagic) & _data); SetZN(A8); break;

            case Op.Las:
                A8 = X8 = S8 = (byte)(_data & S8);
                SetZN(A8);
                break;

            // The unstable stores set no flags. UnstableStoreFixup has already computed
            // the high byte these AND against.
            case Op.Sha:
            case Op.Shx:
            case Op.Shy:
                _data = UnstableStoreValue();
                break;

            case Op.Tas:
                S8 = (byte)(A8 & X8);
                _data = UnstableStoreValue();
                break;

            default:
                throw new NotImplementedException($"Operation {_op} is not implemented yet.");
        }
    }

    /// <summary>
    /// The "magic" constant ANE and LXA mix into the accumulator. On real silicon this
    /// is the decaying value of an internal bus and varies by chip and temperature;
    /// $EE is what the SingleStepTests vectors encode and what most parts produce.
    /// </summary>
    private const byte AneMagic = 0xEE;

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
        var binary = A8 + value + carry;

        if (!_s.D)
        {
            _s.C = binary > 0xFF;
            _s.V = (~(A8 ^ value) & (A8 ^ binary) & 0x80) != 0;
            A8 = (byte)binary;
            SetZN(A8);
            return;
        }

        // NMOS decimal mode. Z comes from the binary sum; N and V come from the
        // partially corrected high nibble. Those two are documented as undefined
        // precisely because they leak this intermediate — reproducing the leak is
        // what makes the per-cycle vectors pass.
        var lo = (A8 & 0x0F) + (value & 0x0F) + carry;
        if (lo > 0x09) lo += 0x06;
        var hi = (A8 >> 4) + (value >> 4) + (lo > 0x0F ? 1 : 0);

        _s.Z = (binary & 0xFF) == 0;
        _s.N = (hi & 0x08) != 0;
        _s.V = (~(A8 ^ value) & (A8 ^ (hi << 4)) & 0x80) != 0;

        if (hi > 0x09) hi += 0x06;
        _s.C = hi > 0x0F;
        A8 = (byte)((hi << 4) | (lo & 0x0F));
    }

    /// <summary>
    /// CMOS ADC. Binary mode is identical to NMOS. Decimal mode keeps NMOS's C and V — V
    /// still comes from the partially corrected high nibble, not from the binary sum — but
    /// takes N and Z from the final decimal result.
    /// </summary>
    private void AdcCmos(byte value)
    {
        if (!_s.D) { Adc(value); return; }

        var carry = _s.C ? 1 : 0;
        var lo = (A8 & 0x0F) + (value & 0x0F) + carry;
        if (lo > 0x09) lo += 0x06;
        var hi = (A8 >> 4) + (value >> 4) + (lo > 0x0F ? 1 : 0);

        _s.V = (~(A8 ^ value) & (A8 ^ (hi << 4)) & 0x80) != 0;

        if (hi > 0x09) hi += 0x06;
        _s.C = hi > 0x0F;
        A8 = (byte)((hi << 4) | (lo & 0x0F));
        SetZN(A8);
    }

    /// <summary>
    /// CMOS SBC. The decimal correction itself differs from NMOS, not only the flags: the
    /// binary difference is adjusted by $60 and $06 rather than nibble-wise, which is what
    /// makes invalid BCD inputs land differently. C and V stay binary, as on NMOS.
    /// </summary>
    private void SbcCmos(byte value)
    {
        if (!_s.D) { Sbc(value); return; }

        var borrow = _s.C ? 0 : 1;
        var binary = A8 - value - borrow;

        _s.C = binary >= 0;
        _s.V = ((A8 ^ value) & (A8 ^ binary) & 0x80) != 0;

        var result = binary;
        if (result < 0) result -= 0x60;
        if (((A8 & 0x0F) - (value & 0x0F) - borrow) < 0) result -= 0x06;

        A8 = (byte)result;
        SetZN(A8);
    }

    /// <summary>Subtract with borrow, in binary or NMOS decimal mode.</summary>
    private void Sbc(byte value)
    {
        var borrow = _s.C ? 0 : 1;
        var binary = A8 - value - borrow;

        // On NMOS parts every flag comes from the binary result, in both modes.
        // Only the accumulator differs.
        _s.C = binary >= 0;
        _s.V = ((A8 ^ value) & (A8 ^ binary) & 0x80) != 0;
        _s.Z = (binary & 0xFF) == 0;
        _s.N = (binary & 0x80) != 0;

        if (!_s.D)
        {
            A8 = (byte)binary;
            return;
        }

        var lo = (A8 & 0x0F) - (value & 0x0F) - borrow;
        var hi = (A8 >> 4) - (value >> 4);
        if ((lo & 0x10) != 0)
        {
            lo -= 0x06;
            hi--;
        }
        if ((hi & 0x10) != 0) hi -= 0x06;

        A8 = (byte)((hi << 4) | (lo & 0x0F));
    }

    /// <summary>
    /// Undocumented ARR: AND with the operand, then a rotate-right whose flags do not
    /// match any documented instruction. Carry comes from bit 6 of the result and
    /// overflow from bit 6 XOR bit 5.
    /// </summary>
    private void Arr(byte value)
    {
        var anded = (byte)(A8 & value);

        if (!_s.D)
        {
            var result = (byte)((anded >> 1) | (_s.C ? 0x80 : 0x00));
            A8 = result;
            SetZN(result);
            _s.C = (result & 0x40) != 0;
            _s.V = (((result >> 6) ^ (result >> 5)) & 0x01) != 0;
            return;
        }

        // Decimal mode. N comes from the carry that was shifted in, Z from the shifted
        // result, and V from a comparison of the pre-shift and post-shift bit 6. The
        // accumulator then gets the BCD nibble corrections applied independently.
        var shifted = (byte)((anded >> 1) | (_s.C ? 0x80 : 0x00));
        _s.N = _s.C;
        _s.Z = shifted == 0;
        _s.V = ((shifted ^ anded) & 0x40) != 0;

        var lo = anded & 0x0F;
        var hi = anded & 0xF0;
        var adjusted = shifted;

        if (lo + (lo & 0x01) > 0x05) adjusted = (byte)((adjusted & 0xF0) | ((adjusted + 0x06) & 0x0F));

        if (hi + (hi & 0x10) > 0x50)
        {
            adjusted = (byte)((adjusted + 0x60) & 0xFF);
            _s.C = true;
        }
        else
        {
            _s.C = false;
        }

        A8 = adjusted;
    }
}
