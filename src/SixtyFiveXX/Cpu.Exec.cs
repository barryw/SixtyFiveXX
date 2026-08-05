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

            // Loads. Lda is width-aware for the 65816: in 8-bit mode (true for every 8-bit
            // core as well as a native-mode 65816 with an 8-bit accumulator) it must touch
            // only the low byte, preserving whatever sits in the high byte — the "hidden B
            // accumulator" research document §2.4 describes, which a plain 16-bit assignment
            // would clobber to $00. A8's setter (Cpu.cs) is what preserves it, for this arm
            // and for every other 8-bit write to A, so no `& 0xFF00` appears here.
            //
            // The `TVariant.Variant != CpuVariant.W65C816 ||` guard is load-bearing, not
            // defensive dead code: `_wide` is derived in FetchOpcode from `_s.M`, and
            // Flag.M (0x20) is the same bit as Flag.U, the NMOS/CMOS "unused" flag, which
            // CpuState.P exposes publicly and which every core's own opcodes normally leave
            // set. Without the guard, a `_wide` an 8-bit core had somehow acquired would send
            // this down the 16-bit path — reading `_data16`, a field no 8-bit-core micro-op
            // ever writes — even though that core has no 16-bit mode at all. The guard is
            // `TVariant.Variant` first, a compile-time constant per closed generic type, so
            // for the five 8-bit cores it folds to `if (true)`, the whole test costs nothing
            // and `_wide` is never even loaded; only the 65816 ever reads it. Code-review
            // finding, task 5: 0 of 10,000 6502/a5 vectors and 0 of 10,000 wdc65c02/a5
            // vectors have bit 5 clear, so conformance could not see this. See
            // UnusedFlagBitRegressionTests.
            case Op.Lda:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { A8 = _data; SetZN(_data); }
                else { _s.A = _data16; SetZN16(_data16); }
                break;
            // Same variant guard as Op.Lda, variant test first for the same JIT-folding reason.
            // Unlike Lda, the 8-bit arm needs no hidden-high-byte care: there is no B register
            // behind X or Y, and whenever x is set hardware holds XH/YH at $00 — so X8's plain
            // whole-field setter, which zeroes the high byte, agrees with the part rather than
            // losing anything. See A8's remarks in Cpu.cs for why the two accessors differ.
            case Op.Ldx:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) { X8 = _data; SetZN(X8); }
                else { _s.X = _data16; SetZN16(_s.X); }
                break;

            case Op.Ldy:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) { Y8 = _data; SetZN(Y8); }
                else { _s.Y = _data16; SetZN16(_s.Y); }
                break;

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

            // 65816 mode control. XCE is the only instruction that changes E (research
            // document §2.2, §6.10.4). When the swap results in emulation mode, force the
            // invariants that mode holds continuously: m=1, x=1, XH/YH cleared (research
            // document §7). S8's setter (Cpu.cs) only re-forces SH on an actual write to S
            // — XCE writes E, not S, so that shim never fires here, and SH is forced
            // directly instead, the same way Reset() already does it.
            //
            // SH's own condition is not "new E == 1" the way M/X/XH/YH's is: research §7
            // states only that SH is forced "when the e flag is 1", without saying whether
            // that means before or after XCE's own swap, and no other source resolves it.
            // Measured exhaustively against all 20,000 SingleStepTests/65816 $FB vectors
            // (both .e and .n): SH is forced to $01 whenever EITHER the old or the new E is
            // 1, and passes through unforced only when both are 0 — 0 violations across
            // every (oldE, newE) combination and every SH value each one produces. Because C
            // and E are swapped, "old C or old E" and "new C or new E" are the same set, so
            // testing (post-swap) C || E is equivalent to testing either side and is what is
            // used below. SL is unaffected either way — confirmed unchanged across all
            // 20,000 vectors.
            case Op.Xce:
                (_s.C, _s.E) = (_s.E, _s.C);
                if (_s.C || _s.E) _s.S = (ushort)((_s.S & 0x00FF) | 0x0100);
                if (_s.E)
                {
                    _s.M = true;
                    _s.XFlag = true;
                    _s.X &= 0x00FF;
                    _s.Y &= 0x00FF;
                }
                break;

            // REP clears the P bits set in the operand (_data); SEP sets them. Neither can
            // touch E — only XCE does that — so only the m/x/XH/YH half of the asymmetry
            // research document §11 draws applies here; SH has no rule to inherit at all.
            //
            // In emulation mode m and x cannot be cleared: Clark §6.4.2, verbatim, "when the e
            // flag is 1, the m and x flag are forced to 1, so after the REP or SEP, both flags
            // will still be 1 no matter what the operand is." Applied unconditionally after the
            // operand's own effect, for both instructions — REP is the one that can actually
            // violate it (its operand can ask to clear either bit); SEP can only set bits it
            // would already be setting, so the force is a no-op there and kept only so the two
            // cases read the same way rather than trusting an invariant this method does not
            // itself maintain.
            //
            // Whenever x ends up set, XH and YH are forced to $00 — the same continuously-held
            // invariant CpuState.X's own doc comment states and XCE already enforces on an E
            // transition. Unconditional on "x is set" rather than "x just became set": harmless
            // when it was already set and already zero, and it is what catches SEP #$10 setting
            // x from a 16-bit-index start, which is the only case that actually loses data.
            case Op.Rep:
                _s.P &= (byte)~_data;
                if (_s.E) _s.P |= Flag.M | Flag.X;
                if (_s.XFlag) { _s.X &= 0x00FF; _s.Y &= 0x00FF; }
                break;

            case Op.Sep:
                _s.P |= _data;
                if (_s.E) _s.P |= Flag.M | Flag.X;
                if (_s.XFlag) { _s.X &= 0x00FF; _s.Y &= 0x00FF; }
                break;

            // Stores. The value lands in _data (or, 65816 16-bit mode, _data16), which the
            // writing micro-op then commits. Reading A8 for the 8-bit case needs no hidden-B
            // guard the way Lda's write does: A8 already reads only the low byte, regardless
            // of what the high byte holds.
            //
            // Same variant guard as Op.Lda, and for the same reason: a _wide an 8-bit core had
            // somehow acquired — _wide derives from Flag.M, which is Flag.U, bit 5 — would send
            // this into the else branch, which sets _data16 and leaves _data untouched, so the
            // write micro-op (ExecWrite, not the 65816's ExecWrite816) would commit whatever
            // _data last held instead of A's low byte. See UnusedFlagBitRegressionTests.
            case Op.Sta:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = A8;
                else _data16 = _s.A;
                break;
            case Op.Stx:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = X8;
                else _data16 = _s.X;
                break;

            case Op.Sty:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Y8;
                else _data16 = _s.Y;
                break;

            // Same variant guard as Op.Sta. STZ takes its width from m, not x: it stores an
            // accumulator-width zero despite naming no register, so _wide is already resolved
            // from Width.M for it and this arm needs no distinguishing test of its own.
            case Op.Stz:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = 0;
                else _data16 = 0;
                break;

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

            // Logic. Width-aware for the 65816, the same shape Op.Lda uses: the 8-bit path
            // writes through A8, which preserves A's high byte (the hidden B accumulator) on the
            // 816 and folds to a plain assignment on the five 8-bit cores; the 16-bit path
            // operates on the full accumulator and takes N from bit 15. The variant guard comes
            // first so those five cores never load _wide at all — see the remarks on _wide, and
            // Op.Lda's own comment for why the guard is load-bearing rather than defensive.
            case Op.And:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { A8 &= _data; SetZN(A8); }
                else { _s.A &= _data16; SetZN16(_s.A); }
                break;

            case Op.Ora:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { A8 |= _data; SetZN(A8); }
                else { _s.A |= _data16; SetZN16(_s.A); }
                break;

            case Op.Eor:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                { A8 ^= _data; SetZN(A8); }
                else { _s.A ^= _data16; SetZN16(_s.A); }
                break;

            // BIT takes N and V straight from the operand's top two bits, and Z from
            // the AND. The accumulator is not modified. At sixteen bits those top two bits are
            // 15 and 14, not 7 and 6.
            case Op.Bit:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide)
                {
                    _s.Z = (A8 & _data) == 0;
                    _s.N = (_data & 0x80) != 0;
                    _s.V = (_data & 0x40) != 0;
                }
                else
                {
                    _s.Z = (_s.A & _data16) == 0;
                    _s.N = (_data16 & 0x8000) != 0;
                    _s.V = (_data16 & 0x4000) != 0;
                }
                break;

            // BIT immediate sets Z alone — see Op.BitImm.
            case Op.BitImm:
                _s.Z = TVariant.Variant != CpuVariant.W65C816 || !_wide
                    ? (A8 & _data) == 0
                    : (_s.A & _data16) == 0;
                break;

            // Compares. CMP is sized by m; CPX and CPY by x. Which flag each one reads is not
            // decided here — _wide already holds the answer, resolved once at fetch from the
            // opcode's declared Width (Cpu.FetchOpcode). See the remarks on _wide.
            case Op.Cmp:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) Compare(A8);
                else Compare16(_s.A);
                break;

            case Op.Cpx:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) Compare(X8);
                else Compare16(_s.X);
                break;

            case Op.Cpy:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) Compare(Y8);
                else Compare16(_s.Y);
                break;

            // Shifts and rotates on memory. Width-aware for the 65816: _data carries the operand
            // at 8 bits, _data16 at 16. The variant guard comes first so the five 8-bit cores
            // never load _wide — see the remarks on _wide and Op.Lda's own comment.
            case Op.Asl:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Asl(_data);
                else _data16 = Asl16(_data16);
                break;

            case Op.Lsr:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Lsr(_data);
                else _data16 = Lsr16(_data16);
                break;

            case Op.Rol:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Rol(_data);
                else _data16 = Rol16(_data16);
                break;

            case Op.Ror:
                if (TVariant.Variant != CpuVariant.W65C816 || !_wide) _data = Ror(_data);
                else _data16 = Ror16(_data16);
                break;

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

            // 65816 arithmetic. Both widths and both modes live in one helper each, because the
            // operand arrives in _data or _data16 depending on width and the helper is the only
            // place that knows which. Never reached by an 8-bit core — Op.Adc816 appears in no
            // table but the 65816's — so these need no variant guard, unlike Op.Lda's arm.
            case Op.Adc816: Adc816(_data, _data16); break;
            case Op.Sbc816: Sbc816(_data, _data16); break;

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

    /// <summary>
    /// Sets Z and N from a 16-bit result — the 65816 native-mode counterpart of
    /// <see cref="SetZN"/>, used only when <c>M</c> selects a 16-bit accumulator.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetZN16(ushort value)
    {
        _s.Z = value == 0;
        _s.N = (value & 0x8000) != 0;
    }

    /// <summary>Compares a register against <c>_data</c>, setting C, Z and N.</summary>
    private void Compare(byte register)
    {
        var result = register - _data;
        _s.C = result >= 0;
        SetZN((byte)result);
    }

    /// <summary>
    /// Compares a 16-bit register against <c>_data16</c>, setting C, Z and N — the 65816
    /// native-mode counterpart of <see cref="Compare"/>. C is the absence of a borrow out of
    /// bit 16, so the subtraction is performed in <c>int</c> and tested before narrowing.
    /// </summary>
    private void Compare16(ushort register)
    {
        var result = register - _data16;
        _s.C = result >= 0;
        SetZN16((ushort)result);
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

    /// <summary>
    /// 16-bit <c>ASL</c>. The 65816 native-mode counterpart of <see cref="Asl"/>.
    /// </summary>
    /// <remarks>
    /// Carry comes from bit 15, which is what <c>m = 0</c> means. Clark §6.1.3's prose says the
    /// opposite — "the high bit (bit 15 when the m flag is one, bit 7 when the m flag is 0)" —
    /// and that sentence has its m-flag polarity inverted; he has it the right way round in
    /// §6.1.1.1 and §6.1.2.2, and his own cycle formula <c>8-2*m</c> reads correctly. Recorded in
    /// research document §13.3. Do not "correct" this toward that sentence.
    /// </remarks>
    private ushort Asl16(ushort value)
    {
        _s.C = (value & 0x8000) != 0;
        var result = (ushort)(value << 1);
        SetZN16(result);
        return result;
    }

    /// <summary>16-bit <c>LSR</c>. See <see cref="Asl16"/>.</summary>
    private ushort Lsr16(ushort value)
    {
        _s.C = (value & 0x0001) != 0;
        var result = (ushort)(value >> 1);
        SetZN16(result);
        return result;
    }

    /// <summary>16-bit <c>ROL</c>. See <see cref="Asl16"/>.</summary>
    private ushort Rol16(ushort value)
    {
        var carryIn = _s.C ? 1 : 0;
        _s.C = (value & 0x8000) != 0;
        var result = (ushort)((value << 1) | carryIn);
        SetZN16(result);
        return result;
    }

    /// <summary>16-bit <c>ROR</c>. See <see cref="Asl16"/>.</summary>
    private ushort Ror16(ushort value)
    {
        var carryIn = _s.C ? 0x8000 : 0x0000;
        _s.C = (value & 0x0001) != 0;
        var result = (ushort)((value >> 1) | carryIn);
        SetZN16(result);
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
    /// The 65816's add with carry, at whichever width <c>_wide</c> selects. No source describes
    /// the decimal correction at either width, so every decimal behaviour below is measured
    /// against the vectors rather than cited — research document §12.1's "Measured" block, which
    /// names the vectors. Decimal mode costs no extra cycle on this part (§12.5), so there is
    /// nothing here for the emitter to know about.
    /// </summary>
    private void Adc816(byte value8, ushort value16)
    {
        if (!_wide)
        {
            // Measured: the 65816's 8-bit decimal ADC is the 65C02's exactly, so this delegates
            // rather than restating it — 150,000 emulation-mode vectors across all fifteen ADC
            // opcodes, plus the 8-bit half of the native set, passed on the first run against
            // this delegation. Note that the matching delegation does NOT hold for SBC; see
            // Sbc816, where the vectors rejected it.
            if (_s.D) { AdcCmos(value8); return; }

            var carry8 = _s.C ? 1 : 0;
            var sum8 = A8 + value8 + carry8;
            _s.C = sum8 > 0xFF;
            _s.V = (~(A8 ^ value8) & (A8 ^ sum8) & 0x80) != 0;
            A8 = (byte)sum8;
            SetZN(A8);
            return;
        }

        if (_s.D)
        {
            // Measured, not cited: the four-digit generalisation of AdcCmos above — nibble-wise
            // correction, V from the partially corrected top nibble, C from the corrected top
            // nibble, N and Z from the final decimal result. All 150,000 native-mode vectors of
            // the fifteen ADC opcodes passed against this on the first run, which is what settles
            // §12.1's gap 5 (where decimal ADC's N comes from at 16 bits): the corrected result,
            // the same place SBC's comes from, though nothing in any source licensed assuming so.
            var dcarry = _s.C ? 1 : 0;
            var n0 = (_s.A & 0x000F) + (value16 & 0x000F) + dcarry;
            if (n0 > 0x09) n0 += 0x06;
            var n1 = ((_s.A >> 4) & 0x0F) + ((value16 >> 4) & 0x0F) + (n0 > 0x0F ? 1 : 0);
            if (n1 > 0x09) n1 += 0x06;
            var n2 = ((_s.A >> 8) & 0x0F) + ((value16 >> 8) & 0x0F) + (n1 > 0x0F ? 1 : 0);
            if (n2 > 0x09) n2 += 0x06;
            var n3 = ((_s.A >> 12) & 0x0F) + ((value16 >> 12) & 0x0F) + (n2 > 0x0F ? 1 : 0);

            _s.V = (~(_s.A ^ value16) & (_s.A ^ (n3 << 12)) & 0x8000) != 0;

            if (n3 > 0x09) n3 += 0x06;
            _s.C = n3 > 0x0F;
            _s.A = (ushort)(((n3 & 0x0F) << 12) | ((n2 & 0x0F) << 8) |
                            ((n1 & 0x0F) << 4) | (n0 & 0x0F));
            SetZN16(_s.A);
            return;
        }

        var carry = _s.C ? 1 : 0;
        var sum = _s.A + value16 + carry;
        _s.C = sum > 0xFFFF;
        _s.V = (~(_s.A ^ value16) & (_s.A ^ sum) & 0x8000) != 0;
        _s.A = (ushort)sum;
        SetZN16(_s.A);
    }

    /// <summary>
    /// The 65816's subtract with borrow. See <see cref="Adc816"/> for the measured-not-cited
    /// standing of everything decimal here.
    /// <para>
    /// Unlike <see cref="Adc816"/>, this cannot delegate its 8-bit decimal path to the certified
    /// CMOS helper, and the reason is a measured divergence from a source rather than a gap in
    /// one: Clark's §6 preamble says the 65816 "has the same behavior as 65C02" for 8-bit
    /// results, and for <c>SBC</c> that is not so. The 65816 corrects nibble-wise, the way NMOS
    /// does; <see cref="SbcCmos"/> adjusts the binary difference by $60 and $06. The two agree
    /// on every flag and on every valid-BCD input, and disagree on the accumulator as soon as a
    /// digit is $A-$F — which is why only the vectors could tell them apart. Research document
    /// §12.1's "Measured" block; vector <c>e9 e 15</c> is the hand-traced one.
    /// </para>
    /// <para>
    /// The flags then split: C and V come from the binary difference, N and Z from the corrected
    /// result. That is <see cref="SbcCmos"/>'s split rather than <see cref="Sbc"/>'s, at both
    /// widths, and at 16 bits the N half is the one thing about 65816 decimal arithmetic any
    /// source states — Clark's Example 2, §12.1, reproduced by
    /// <c>W65C816AluTests.Sbc_SixteenBitDecimal_MatchesClarksWorkedExample</c>.
    /// </para>
    /// </summary>
    private void Sbc816(byte value8, ushort value16)
    {
        if (!_wide)
        {
            var borrow8 = _s.C ? 0 : 1;
            var binary8 = A8 - value8 - borrow8;
            _s.C = binary8 >= 0;
            _s.V = ((A8 ^ value8) & (A8 ^ binary8) & 0x80) != 0;

            if (_s.D)
            {
                var d0 = (A8 & 0x0F) - (value8 & 0x0F) - borrow8;
                var d1 = (A8 >> 4) - (value8 >> 4);
                if ((d0 & 0x10) != 0) { d0 -= 0x06; d1--; }
                if ((d1 & 0x10) != 0) d1 -= 0x06;
                A8 = (byte)((d1 << 4) | (d0 & 0x0F));
            }
            else A8 = (byte)binary8;

            SetZN(A8);
            return;
        }

        var borrow = _s.C ? 0 : 1;
        var binary = _s.A - value16 - borrow;
        _s.C = binary >= 0;
        _s.V = ((_s.A ^ value16) & (_s.A ^ binary) & 0x8000) != 0;

        if (_s.D)
        {
            var n0 = (_s.A & 0x0F) - (value16 & 0x0F) - borrow;
            var n1 = ((_s.A >> 4) & 0x0F) - ((value16 >> 4) & 0x0F);
            var n2 = ((_s.A >> 8) & 0x0F) - ((value16 >> 8) & 0x0F);
            var n3 = ((_s.A >> 12) & 0x0F) - ((value16 >> 12) & 0x0F);

            if ((n0 & 0x10) != 0) { n0 -= 0x06; n1--; }
            if ((n1 & 0x10) != 0) { n1 -= 0x06; n2--; }
            if ((n2 & 0x10) != 0) { n2 -= 0x06; n3--; }
            if ((n3 & 0x10) != 0) n3 -= 0x06;

            _s.A = (ushort)(((n3 & 0x0F) << 12) | ((n2 & 0x0F) << 8) |
                            ((n1 & 0x0F) << 4) | (n0 & 0x0F));
        }
        else _s.A = (ushort)binary;

        SetZN16(_s.A);
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
