# Handover — phase 7d, the last of the 65816 phases

Written 2026-08-05, at the close of phase 7c′. Everything a cold session needs to start 7d without
re-deriving what the previous four phases established.

Committed rather than left in `.superpowers/sdd/progress.md`, because that ledger is gitignored and
`git clean -fdx` would destroy it. The ledger remains the detailed per-task record; this file is the
part that must survive.

## Where the repository stands

`main` is at merge commit `d506aa9`. Working tree clean.

| | net8.0 | net10.0 |
| --- | --- | --- |
| Unit (`--filter "Category!=Performance"`) | 528 | 528 |
| Conformance | 1,734 | 1,734 |
| Throughput gate | passes | passes |

Of the 1,734 conformance tests, **1,309 belong to the five 8-bit cores and must never move**. The
identity `1,309 + (implemented 65816 opcodes × 2) + 1 = total` has held every phase and is the
fastest check that nothing drifted.

**`main` is 42 commits ahead of `origin/main` and deliberately unpushed.** A non-skipped push to
`main` cuts a public nuget.org release. Both the 7c and 7c′ merge commits carry `[skip ci]`. Pushing
is the owner's decision, not a step in any phase.

The 65816 implements **212 of 256 opcodes**, certified per-cycle in both emulation and native mode
against 3,600,000 SingleStepTests vectors, with the full eight-character bus-qualifier pin string
asserted on every cycle.

## What phase 7d is

The remaining **44 opcodes**, and the last 65816 phase before the disassembler work in 7e.

| Group | Bytes |
| --- | --- |
| Branches | `$10` BPL, `$30` BMI, `$50` BVC, `$70` BVS, `$90` BCC, `$B0` BCS, `$D0` BNE, `$F0` BEQ, `$80` BRA, `$82` BRL |
| Jumps | `$4C` JMP abs, `$6C` JMP (abs), `$7C` JMP (abs,X), `$5C` JML long, `$DC` JML [abs] |
| Subroutine calls | `$20` JSR abs, `$FC` JSR (abs,X), `$22` JSL long |
| Returns | `$40` RTI, `$60` RTS, `$6B` RTL |
| Stack, register | `$48` PHA, `$08` PHP, `$DA` PHX, `$5A` PHY, `$68` PLA, `$28` PLP, `$FA` PLX, `$7A` PLY, `$8B` PHB, `$0B` PHD, `$4B` PHK, `$AB` PLB, `$2B` PLD |
| Stack, address | `$F4` PEA, `$D4` PEI, `$62` PER |
| Interrupts | `$00` BRK, `$02` COP, `$42` WDM |
| Block move | `$54` MVN, `$44` MVP |
| Halt | `$CB` WAI, `$DB` STP |

**Its gate is different from every phase before it: all 512 vector files — the full 5,120,000 —
not just its own 44 opcodes'.** `Harte816Tests.ExpectedImplementedOpcodes` reaches 256, at which
point the derived-versus-declared check stops being a drift alarm and becomes a proof that nothing is
missing.

Expect roughly 500 MB of further vector download.

## Three defects that MUST be fixed in 7d

These have been carried since phase 7b and deferred every phase because no opcode reached them. 7d
implements the stack and interrupts, so 7d reaches all three. **None is optional.**

1. **`MicroOp.PullP` masks `~Flag.B`, which is also `~Flag.X`.** On a native-mode 65816 that means
   `PLP` and `RTI` would clear the index-width flag as a side effect. The two flags share bit 4 —
   `B` in emulation mode, `x` in native — so the mask is right for the 8-bit cores and wrong for the
   65816 in native mode.
2. **`ImpliedExec`, `FetchAddrHiX`/`Y`, the branch micro-ops, `JmpAbs`, `BrkPad`, `IntDummy` and
   every stack micro-op still compute a bare 16-bit `PC` and/or `0x0100 + S8`.** The 65816 needs
   `PBR,PC` for program reads (there is a `PcAddress()` helper) and a full 16-bit `S` in native mode.
   Phase 7b's carry-forward note lists these by name.
3. **The 65816 IRQ sequence mutates `S` and memory before `Unimplemented816` throws.** The throw is
   deliberate; the mutation before it is not.

## Things established that would cost a day to rediscover

**Note 17's generalisation, measured in 7c′.** The read-modify-write middle cycle's native and
emulation forms are **pin-identical** — `MLB` asserted, neither address-valid pin — and differ only
in `RWB`. This was established by sixteen vector failures isolating a single character of the pin
string. Interrupt sequences in 7d should assume the same discipline: check Table 5-7's pin columns
rather than inferring them from a cycle's apparent purpose.

**`Sequences.NotYet816` must survive.** It looks like dead scaffolding and is not: `MicroOpTable`'s
constructor reads `seq.IntPushP` from it for *every* variant when building the shared `IrqEntry`
section. `Emit816` takes no `Sequences` argument at all, so the 65816's own paths never consult it —
but deleting the record breaks the table build. The 7c′ spec originally claimed that phase would
clear it; that claim was wrong and has been corrected.

**`W65C816StateTests.UnimplementedOpcode_Throws` now derives its probe byte** from the first
`Op.Undefined` entry in the table. Once 7d defines all 256 there is no undefined byte left, and the
test calls `Assert.Fail` with an explanatory message. **Delete it at that point**, and check whether
`UndefinedOpcodeException` still has a reachable caller.

**Research gaps still open**, recorded honestly rather than guessed:

- §12 (phase 7c): four decimal-mode gaps — the correction algorithm at 8 bits, decimal `V`, invalid
  BCD digits, and part of `Z`/`C` sourcing. 7d's `BRK`/`COP` may touch decimal mode.
- §13 (phase 7c′): the emulation middle cycle's written value is an NMOS inference rather than a
  citation, and emulation behaviour beyond forced `m`/`x` rests on Clark's "In general" hedge, which
  is treated as a hypothesis.

**Two places a source is documented as wrong**, both in research §3: Eyes & Lichty on the `m`/`x` bit
positions, and on indexed-read timing. Research §13 adds a third — Clark §6.1.3's prose has the
`m`-flag polarity inverted for `ASL`'s carry bit.

## Traps that have each cost real time

- **The width tripwires do not catch a uniformly-wrong `Width`.** Proven by experiment in 7c: setting
  all three `CPX` entries to `Width.M` left both tripwires green. They check that opcodes sharing an
  `Op` agree with *each other*, and a uniformly wrong group agrees with itself.
- **`Cpu`'s constructor never calls `Reset()`, so `P == $00` and both width flags read clear.** A
  test that sets only one width flag cannot distinguish `m` from `x`. **Any width-discriminating test
  must set both to opposed values explicitly.** This produced a real hole in 7c's own test design.
- **`Width` means "the operand fetched from memory is 16 bits"** — nothing else. Opcodes with no
  operand declare `Width.None` and test their flag inside their `Exec` arm. That precision is what
  lets the tripwire assert set equality. Most of 7d's opcodes are `Width.None`.
- **Every width test in variant-shared code reads `TVariant.Variant != CpuVariant.W65C816 || …`,
  variant test first.** `Flag.M` shares bit 5 with the 6502's always-set unused flag and `Flag.X`
  shares bit 4 with the break flag, so a missing guard sends an 8-bit core down a 16-bit path.
  Conformance cannot catch it: 0 of 10,000 `6502/a5` vectors have bit 5 clear. Phase 7b shipped this
  bug once; `UnusedFlagBitRegressionTests` exists for it. Members appearing in no 8-bit table need no
  guard.
- **The conformance suite needs an explicit 600000 ms timeout on the Bash call.** The default 120 s
  silently auto-backgrounds the run; that stalled one task with its whole tree uncommitted.
- **Commit before running any probe.** `git checkout -- <file>` to revert a mutation destroyed an
  implementer's uncommitted work once.
- **An instruction that runs one cycle short surfaces as
  `UndefinedOpcodeException: Undefined opcode $00 at $<garbage>`** — the harness ticks past the end
  and fetches whatever follows. Nothing in that message names the real defect. That is exactly how
  `XBA`'s three-cycle length was found. Suspect a cycle count, not a table entry.
- **The `task-brief` script writes a fixed filename** and will silently overwrite a previous phase's
  brief. Rename to a phase prefix (`p7d-task-N-brief.md`) immediately after extracting.
- **Give every task dispatch an explicit README opcode-count bump.** Skipping it once left the count
  doubly stale and cost a later task a correction.
- **Do not reintroduce a drifting count into a doc comment.** Three sites were deliberately made
  count-free after being wrong across successive tasks; `UndefinedOpcodeException`'s remarks and the
  README's support-matrix row are two of them.

## How to run the phase

The rhythm that produced four clean phases, in order:

1. **`superpowers:brainstorming`** — design 7d, then write it as a new `## Phase 7d` section in
   `docs/superpowers/specs/2026-08-03-65816-core-design.md`. That file already holds 7a, 7b, 7c and
   7c′, each with a **Verified** paragraph recording measured numbers. Mark 7d's phase-table row
   complete when it lands.
2. **`superpowers:writing-plans`** — produce
   `docs/superpowers/plans/2026-08-0X-phase7d-<name>.md`. **Task 1 must be research-only**, producing
   a new **§14** in `docs/superpowers/research/2026-08-03-65816-reference-sources.md`. Every phase
   that did this got its vectors green on the first run; the one time a plan guessed a hardware fact,
   the vectors disagreed. Note the research document already has §10–§13, so a plan that says "new
   §10" will be wrong — 7c's did, and needed correcting mid-phase.
3. **`superpowers:subagent-driven-development`** on a branch `phase7d-<name>`, forked from `main`.
   Fresh implementer per task, task reviewer after each, one whole-branch review at the end, then
   `superpowers:finishing-a-development-branch`.

**The research task's honesty rule is the single highest-value practice in this project:** where a
source is silent, record the silence in those words. Do not fill a gap from memory, do not infer
65816 behaviour from the 6502 or 65C02 without saying so, and do not write a number you cannot cite.
Recorded gaps have repeatedly turned out to be the interesting part.

**Search traps in Clark's document**, both of which cost a revision: he names flags by letter far
more often than by name — search `d flag`, not `decimal` — and the document is form-feed paginated,
so a literal phrase can split across a blank line and fail to match.

## Where everything is

| Path | What |
| --- | --- |
| `docs/superpowers/specs/2026-08-03-65816-core-design.md` | The 65816 design. Phases 7a–7c′ each have a Verified paragraph. |
| `docs/superpowers/research/2026-08-03-65816-reference-sources.md` | §1–§13. §9 is the per-cycle bus specification; §12 and §13 are 7c's and 7c′'s. |
| `docs/superpowers/plans/` | One plan per phase. |
| `.superpowers/sdd/progress.md` | The detailed ledger. **Gitignored** — every task's findings, defects and decisions. |
| `src/SixtyFiveXX/Opcodes65C816.cs` | The opcode table. 212 entries. |
| `src/SixtyFiveXX/MicroOpTable.cs` | `Emit816` and `EmitAddressed816` — the 65816's own emitter. |
| `tests/SixtyFiveXX.Conformance/Harte816Tests.cs` | The gate. `ExpectedImplementedOpcodes` is bumped per task. |

## One process observation worth carrying forward

Across 7c and 7c′, subagents caught **six** errors in material I had written — a spec bullet, two
brief premises, a prediction about which guard a test pinned, a test whose assertion could not fail,
and a dispatch that contradicted the research it cited. Every one was caught by an agent reading a
source or running an experiment rather than accepting the framing it was given.

The dispatches that produced this asked for measurement rather than agreement, said explicitly that
stopping was acceptable, and told implementers not to tune anything to make a vector pass. Keep all
three in 7d's dispatches. The instruction that mattered most, repeatedly, was: *if a vector fails, do
not tune anything — report the failing opcode, the vector name, and the expected-versus-actual line,
and stop.*
