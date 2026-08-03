# Phase 6a — The Disassembler

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A disassembler that decodes from any `IBus` for a given variant, driven by the **same opcode table the engine runs from**, so text and behaviour cannot drift.

**Architecture:** `OpcodeInfo` already carries an exact per-opcode mnemonic — `RMB0`, `WAI`, `BBS3`, `JAM` — and an `AddrMode`. The engine turns that row into a micro-op sequence; the disassembler turns the same row into text and a length. Neither reads a table of its own.

**Spec:** `docs/superpowers/specs/2026-08-02-variant-cores-design.md` §"Phase 6", and `docs/superpowers/specs/2026-07-31-sixtyfivexx-design.md` §5.

**Scope:** This is the first half of the spec's phase 6. The sim6502 adapter swap is **phase 6b**, planned separately and executed in the sim6502 repository. The disassembler is a prerequisite for it either way, its gate lives entirely in this repository, and the two halves have very different blast radii — sim6502 is at v4.0.1 with a live suite of its own.

## Global Constraints

- **No behaviour change.** The disassembler only reads. Every existing suite stays green on **both** `net8.0` and `net10.0`: 386 unit and 1,303 conformance.
- `src/SixtyFiveXX` keeps **zero** NuGet dependencies. `TreatWarningsAsErrors` is on; every public member needs an XML doc comment.
- **`OpcodeInfo` and `AddrMode` stay internal.** The design is explicit that the opcode descriptors are not public API. The disassembler's return type must therefore expose strings and an integer, nothing from the table.
- Conventional Commits. Work on a branch off `main`. **Do not push `main` without `[skip ci]` in the commit subject** — a push cuts a public nuget.org release.

## Established facts — verified this session, do not re-derive

- **64tass accepts every undocumented 6502 mnemonic this project uses, verbatim**, under `.cpu "6502i"`: `ALR ANC ANE ARR DCP ISC LAS LAX LXA RLA RRA SAX SBX SHA SHX SHY SLO SRE JAM`. Assembled clean, 51 bytes, no warnings. That makes a byte-exact reassembly round-trip a viable gate, and 64tass is **already a build prerequisite** for the Klaus interrupt test, so it costs no new tooling.
- **64tass accepts the WDC additions** under `.cpu "w65c02"`: `BRA PHX PHY PLX PLY STZ TRB TSB WAI STP`, `LDA ($12)`, `JMP ($1234,X)`, `BIT #$0F`, `INC A`, `DEC A`.
- ***** 64tass spells the Rockwell bit operations differently. ***** It rejects `RMB0 $12` outright ("general syntax") and wants the bit as a separate operand: `rmb 0,$12`, `smb 7,$12`, `bbr 0,$12,$1010`, `bbs 7,$12,$1010`. The **encodings are identical** — `07 12`, `f7 12`, `0f 12 09`, `ff 12 06` — so this is purely a naming convention, but a round-trip gate must translate those 32 opcodes or it will fail for reasons that have nothing to do with the disassembler. Both `RockwellTable` and `WdcTable` carry them; `WdcTable` is `RockwellTable` plus `WAI`/`STP`.
- **The bit number is fused into the mnemonic in this project's tables**, built as `$"RMB{bit}"` / `$"BBR{bit}"` in `Opcodes65C02.BuildRockwellTable`. That is the form the disassembler will emit, and it is the form a human reads. The 64tass spelling is an assembler detail belonging to the gate, not to the API.
- **sim6502 resolves a branch to its target address, not its displacement**: `Processor.Disassembly.cs` emits `$XXXX`. Its arithmetic looks wrong and is not — `movement = d > 127 ? d - 255 : d`, then `PC + movement + 1`, then a further `+1` only when `movement >= 0`. The missing 256 and the missing `+2` cancel exactly. **Do not "fix" it during 6b**; verify against it instead.
- **sim6502 has no `ZeroPageRelative` mode and no `BBR`/`BBS`.** Its trace line decorates the operand text with PC, `A`/`X`/`Y`/`SP` and flags. That decoration is the adapter's job in 6b, which is why this phase returns operand text rather than a formatted line.
- **`RunUntil` already exists** (`Cpu.cs:407`), so 6b's `ExecuteJsr` has the primitive it needs. Nothing in this phase touches it.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/SixtyFiveXX/Instruction.cs` | Create: the decoded result — mnemonic, operand text, length |
| `src/SixtyFiveXX/Disassembler.cs` | Create: decode one instruction from a bus for a variant |
| `tests/SixtyFiveXX.Tests/DisassemblerTests.cs` | Create: per-mode and per-variant decoding |
| `tests/SixtyFiveXX.Conformance/RoundTripTests.cs` | Create: the 64tass reassembly gate |
| `tests/SixtyFiveXX.Conformance/PublicSurfaceTests.cs` | Modify: add the two intended public types |

---

### Task 1: The decoder

**Files:** Create `Instruction.cs`, `Disassembler.cs`

- [ ] **Step 1: The result type.** A public `readonly record struct Instruction(string Mnemonic, string Operand, int Length)`. `Operand` is empty for implied. Nothing from `OpcodeInfo` or `AddrMode` leaks.
- [ ] **Step 2: The entry point.** Note the API sketch that was agreed is under-specified — `TBus` has to be a type parameter too, and `IBus.Read` takes an `int`:

      public static Instruction Decode<TBus, TVariant>(in TBus bus, int address)
          where TBus : struct, IBus
          where TVariant : struct, ICpuVariant

  Resolve the table from `TVariant.Variant` through the same path `MicroOpTable` uses, so a variant wired into one and forgotten in the other cannot decode as something else.
- [ ] **Step 3: Length and operand text per mode.** Every `AddrMode` gets an explicit arm. **A mode with no arm must throw, never fall through to a default** — phase 4's `SequencesFor` silently defaulted an unmapped variant to NMOS, and that is exactly the failure this avoids. Cover the plain modes here; Task 2 takes the awkward ones.
- [ ] **Step 4: Commit.**

---

### Task 2: The modes that are not a straight substitution

**Files:** Modify `Disassembler.cs`, create `DisassemblerTests.cs`

- [ ] **Step 1: `Relative`.** Resolve to the target address, `PC + 2 + (sbyte)displacement`, and emit `$XXXX`. This matches what sim6502 prints and what a reader wants. Test the boundaries: `$7F` forward, `$80` back, `$00`, and a wrap across `$0000`/`$FFFF`.
- [ ] **Step 2: `ZeroPageRelative`.** Two operands, three bytes: `$12,$1010`, with the branch target resolved from the **third** byte and a length of 3. Rockwell and WDC only.
- [ ] **Step 3: `Accumulator` and `Implied`.** `A` and empty respectively; both one byte.
- [ ] **Step 4: The CMOS NOP shapes.** `NopSingleCycle` is one byte, `NopAbsolute` and `NopAbsoluteExtra` are three. They are `NOP` with an operand that is fetched and discarded — emit the operand text anyway, because the bytes are there and a linear disassembly has to advance past them.
- [ ] **Step 5: `Undefined`.** `???`, one byte. A variant that does not implement an opcode still has to advance.
- [ ] **Step 6: Reading past `$FFFF`.** An operand fetch at `$FFFF` wraps, as the bus does. Assert it rather than discovering it.
- [ ] **Step 7: Commit.**

---

### Task 3: The 64tass round-trip gate

**Files:** Create `RoundTripTests.cs`, modify `PublicSurfaceTests.cs`

The gate is byte-exactness: disassemble an image linearly, reassemble the text, compare. Data regions round-trip too — they decode to garbage, but *faithful* garbage, which is the property being tested.

- [ ] **Step 1: Every opcode, per variant.** Synthesize an image containing all 256 opcodes with plausible operands, disassemble it, reassemble with the right `.cpu` directive, and compare bytes. Confirm first that `6502i` really does accept all 256 — the JAM opcodes and the `SHA`/`TAS` family are the ones to check.
- [ ] **Step 2: The Rockwell translation.** The 32 bit operations are emitted as `RMB0 $12` and must be rewritten to `rmb 0,$12` before 64tass sees them. Keep the translation in the **test**, not in the disassembler: it is an assembler's spelling, not the library's.
- [ ] **Step 3: A real corpus.** Round-trip a full Klaus image, which is already cached, as a check against operands the synthetic image happens not to produce.
- [ ] **Step 4: Prove the gate discriminates.** Break one operand format — swap `,X` for `,Y` on one mode — and confirm the round-trip **fails**. A gate that cannot fail looks like certification.
- [ ] **Step 5: `PublicSurfaceTests`.** It asserts an **exact** public type set and will fail the moment `Instruction` and `Disassembler` are added. Fix by adding the intended types, **never** by loosening the assertion.
- [ ] **Step 6: Commit.**

---

### Task 4: Close the phase

- [ ] **Step 1:** §10 phase 6 row — note that it covers 6a only and that 6b is the sim6502 swap. README if the disassembler is worth surfacing there.
- [ ] **Step 2:** Record for 6b what was learned about sim6502's trace format, so the adapter is written against verified facts rather than re-derived ones.
- [ ] **Step 3: Final whole-branch review, then merge.**

---

## Risks

- **A round-trip gate proves consistency, not correctness.** A disassembler that renders `LDA $1234,X` as `LDA $1234,Y` round-trips perfectly if the assembler agrees with the mistake — it does not, because the encoding differs, which is precisely why byte-exactness is the assertion and why Task 3 Step 4 exists. But the gate says nothing about *mnemonic* choice: emit `ISB` where the table says `ISC` and 64tass would simply reject it, which is a build failure rather than a silent wrong answer. That asymmetry is the gate's strength and its limit.
- **The Rockwell spelling divergence is a trap for 6b too.** Anything that feeds this disassembler's output to an assembler needs the same translation. It belongs in whatever does the feeding, not in the library.
- **Disassembly reads the bus.** On a flat memory map that is free; on a bus with side-effecting reads it is not. The library cannot know the difference, so this is documented, not defended against.
- **sim6502's disassembler is a second implementation of the same thing** and would be a useful differential oracle — but wiring this repository's tests to sim6502 inverts the dependency the swap is meant to establish. Left to 6b, where the comparison is a migration check rather than a gate.
