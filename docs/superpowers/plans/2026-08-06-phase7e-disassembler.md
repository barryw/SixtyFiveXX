# Phase 7e — the 65816 disassembler

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach `Disassembler` every 65816 addressing mode, certified by a 64tass round-trip run once per `m`/`x` combination — the last phase of the 65816 work.

**Architecture:** `Disassembler.Decode` is a pure function of bytes, address and — new in this phase — the two operand-width flags, which a 65816 disassembler cannot get from the byte stream. Its address mask becomes variant-dependent so the 65816 decodes across 16 MB while the five 8-bit cores fold to exactly the code that is there today. The gate is the same external-assembler oracle phase 6a established: an assembler is the only thing that can say the notation is *right* rather than merely self-consistent.

**Tech Stack:** C# 13, .NET 8 and .NET 10 (both must pass), xUnit, 64tass 1.60+, no NuGet dependencies in `src/`.

**Spec:** `docs/superpowers/specs/2026-08-03-65816-core-design.md` §"Phase 7e".
**Research:** `docs/superpowers/research/2026-08-03-65816-reference-sources.md` — §1–§14 exist. **Task 1 adds §15.** A plan that says "new §10" is wrong; phase 7c's did and needed correcting mid-phase.

**Scope:** `Disassembler`, its tests, the round-trip harness, and documentation. Explicitly **not** in scope: any change to `Cpu`, `MicroOpTable`, the opcode tables or the micro-op sequences. The 65816 core is complete and certified at 256 of 256; this phase reads its tables and must not touch them.

## Global Constraints

- **The five 8-bit cores must not change.** 1,309 of the conformance tests are theirs and must stay at 1,309 passing on both TFMs. Their five round-trip tests must keep their pinned covered counts — 213, 213, 177, 210, 212 — unchanged.
- **Baselines on this branch's fork point:** unit **718** with `--filter "Category!=Performance"`, conformance **1822**.
- `src/SixtyFiveXX` keeps **zero** NuGet dependencies. `TreatWarningsAsErrors` is on with documentation generation, so **every public member needs an XML doc comment**.
- Both target frameworks must pass. Iterate with `-f net10.0`; run both before declaring a task done.
- **When you run the conformance suite, pass an explicit 600000 ms timeout on the Bash call.** The default is 120 seconds and the suite takes 3–6 minutes per framework; that default once silently auto-backgrounded a run and stalled a task with everything uncommitted.
- **Commit before running any probe or deliberate mutation.** Restore probe files with `git checkout --`, never `mv file.bak file` — the latter preserves an old mtime and defeats MSBuild's staleness detection.
- **This phase adds public API** — one `Decode` overload — unlike 7a through 7d. `PublicSurfaceTests` compares *type* names only and will not notice; task 1 confirms that reading.
- **If the round-trip fails, do not tune the renderer until it passes.** Report the opcode, the rendered text, the expected bytes and the assembled bytes, and stop. You may diagnose a failure to a rule stated by a named source or measured from 64tass, implement it, and validate it across the whole gate — but then you **must amend research §15 in the same task** and report the deviation.
- Conventional Commits. Branch `phase7e-disassembler`, forked from `main`. **Main is pushed now** — earlier phases were deliberately local, that is no longer true, so treat any push as public.

## Established facts — verified against 64tass while the spec was drafted, do not re-derive

Every one of these was measured with `64tass -a f.asm -o f.bin` and `xxd`, on 64tass 1.60.3243.

- **`.cpu "65816"` is the dialect.** 64tass also accepts `--m65816`.
- **`.as`/`.al` set the accumulator width and `.xs`/`.xl` the index width.** Under `.as`, `LDA #$34` is `a9 34`; under `.al`, `LDA #$1234` is `a9 34 12`. `REP #$30` and `SEP #$30` are `c2 30` and `e2 30` under both — they are `AddrMode.ImmediateByte` and never widen.
- **64tass picks the shortest encoding that fits the value.** `LDA $0012` → `a5 12` (direct page), `LDA $001234` → `ad 34 12` (absolute). The forcing prefixes are `@b`, `@w`, `@l`: `LDA @w $0012` → `ad 12 00`, `LDA @l $001234` → `af 34 12 00`.
- **`MVN $12,$34` assembles to `54 34 12`.** The assembly text is `src,dst`; the byte stream is `dst,src`. Research §14.3 established the destination bank byte comes first in the stream.
- **`PER $1234` assembles to `62 0d 02`** — a displacement, so `PER` renders as a target address like a branch. `BRL $1234` → `82 0a 02`, likewise.
- **`COP #$12` → `02 12`; `WDM #$12` → `42 12`.**
- Confirmed syntax for the rest: `LDA [$12]` → `a7 12`, `LDA [$12],Y` → `b7 12`, `LDA $12,S` → `a3 12`, `LDA ($12,S),Y` → `b3 12`, `JML [$1234]` → `dc 34 12`, `JSL $123456` → `22 56 34 12`, `PEA $1234` → `f4 34 12`, `PEI ($12)` → `d4 12`, `JMP ($1234,X)` → `7c 34 12`.

## Established facts about this codebase

- **`Disassembler.Decode` is driven by the same `MicroOpTable` the engine runs from**, so text and behaviour cannot drift. Adding an opcode makes it decodable in the same commit that makes it executable — for the 8-bit cores. The 65816 does not have that property until this phase.
- **`Instruction` is a `readonly record struct` of `(string Mnemonic, string Operand, int Length)`** and its `Length` doc says "1 to 3". The 65816 has four-byte instructions.
- **`RoundTripTests` lays every unambiguously-encodable opcode into a 64 KB array**, decodes linearly, emits `.cpu` and `*=$1000`, assembles, and requires the bytes back. It pins a covered count per variant and names every excluded opcode in the output.
- **`RoundTripTests.TheGateDiscriminates_AnAlteredOperandChangesTheBytes`** exists because a round-trip can pass while doing nothing load-bearing. The 65816 needs its own equivalent.
- **`PublicSurfaceTests` compares `ExpectedPublicTypes` (type names), a `MustStayInternal` list, and the shipped target frameworks.** No member-level assertion.
- **`AddrMode.Stack` is the mode for hand-written sequences** and does not fix a length; `DecodeStack` recovers it from the `Op`. Phase 7d made `Op.Cop`, `Op.Pea`, `Op.Pei` and `Op.Per` throw there rather than half-decode.
- **`FlatBus` is `public readonly struct FlatBus : IBus` in `src/SixtyFiveXX/IBus.cs`** and wraps at 64 KB.

## File Structure

| File | Responsibility |
| --- | --- |
| `docs/superpowers/research/2026-08-03-65816-reference-sources.md` | Modify (task 1). New §15. |
| `src/SixtyFiveXX/Disassembler.cs` | Modify. The overload, the variant-masked address path, every 65816 arm. |
| `src/SixtyFiveXX/Instruction.cs` | Modify (task 2). `Length` documented 1–4. |
| `tests/SixtyFiveXX.Tests/DisassemblerTests.cs` | Modify. A case per 65816 mode. |
| `tests/SixtyFiveXX.Conformance/RoundTripTests.cs` | Modify (task 4). The 65816 round-trip and its discriminator. |
| `README.md` | Modify (task 5). |

## The modes this phase must render

Listed once, here, so every task can be checked against one place. **Notation column measured against 64tass**, not recalled.

| `AddrMode` | Notation | Length | Note |
| --- | --- | --- | --- |
| `ImmediateByte` | `#$12` | 2 | `REP`/`SEP`/`WDM`; never widens |
| `Immediate` | `#$12` or `#$1234` | 2 or 3 | **Width-dependent** — `m` for the accumulator ops, `x` for the index ops |
| `DirectPage` | `$12` | 2 | |
| `DirectPageX` | `$12,X` | 2 | |
| `DirectPageY` | `$12,Y` | 2 | |
| `DirectPageIndirect` | `($12)` | 2 | |
| `DirectPageIndirectY` | `($12),Y` | 2 | |
| `DirectPageIndexedIndirectX` | `($12,X)` | 2 | |
| `DirectPageIndirectLong` | `[$12]` | 2 | |
| `DirectPageIndirectLongY` | `[$12],Y` | 2 | |
| `AbsoluteLong` | `$123456` | 4 | `@l` when the value is under `$10000` |
| `AbsoluteLongX` | `$123456,X` | 4 | `@l` when the value is under `$10000` |
| `StackRelative` | `$12,S` | 2 | |
| `StackRelativeIndirectY` | `($12,S),Y` | 2 | |
| `RelativeLong` | `$1234` (target) | 3 | `BRL` |
| `BlockMove` | `$12,$34` | 3 | **Operands reversed** — text `src,dst`, bytes `dst,src` |
| `AbsoluteIndirectLong` | `[$1234]` | 3 | `JML [abs]` |

Already rendered, but needing a 65816 change: **`Absolute`, `AbsoluteX`, `AbsoluteY`** gain `@w` when the operand is under `$100`. `AddrMode.Stack` gains sub-arms for `Op.Cop` (`#$12`, 2), `Op.Pea` (`$1234`, 3), `Op.Pei` (`($12)`, 2), `Op.Per` (target, 3) and `Op.Rtl` (empty, 1).

`JSL` and `JML long` take `AddrMode.AbsoluteLong` in the table, so they need no `Stack` arm. `JMP (abs)` takes `AddrMode.Indirect` and `JMP`/`JSR (abs,X)` take `AddrMode.AbsoluteIndexedIndirect`; both arms already exist and are already correct.

---

### Task 1: Research §15 — probe 64tass, and settle two questions the spec deliberately left open

**No code.** Every phase that did its research first got its gate green on the first run; the one time a phase guessed a hardware fact, the vectors disagreed.

**Files:**
- Modify: `docs/superpowers/research/2026-08-03-65816-reference-sources.md` (append §15)

**Interfaces:**
- Produces: research §15, cited by section number from tasks 2–5. §15.1 the per-mode notation table, §15.2 the shortest-encoding rule and the forcing prefixes, §15.3 the ambiguity set and the pinned covered count, §15.4 the two open questions below.

- [ ] **Step 1: Read §9 and §14 so the new section matches their notation, and confirm the section number**

The research document has §1 through §14. **Your section is §15.** Check the last heading in the file before writing.

- [ ] **Step 2: Reproduce every measurement in this plan's "Established facts" block**

Do not take them on trust — they were measured while the spec was drafted and belong in §15 with your own confirmation. Write each as a measured result in the style §12, §13 and §14 use: the source text, the assembled bytes, and the 64tass version.

```bash
64tass --version
```

- [ ] **Step 3: Settle §15.1 — a notation row for every mode in the plan's mode table**

For each, assemble a line and record the bytes. Include the modes the plan lists as already-rendered, so the table is complete rather than a delta.

- [ ] **Step 4: Settle §15.2 — the shortest-encoding rule, and exactly when a prefix is needed**

The spec states the rule as: force `@w` when an absolute-family operand is under `$100`, force `@l` when a long-family operand is under `$10000`. **Verify both boundaries** — assemble `LDA $00FF`, `LDA $0100`, `LDA $00FFFF` and `LDA $010000` and record which collapse. State the rule as a predicate a reader can implement without re-measuring.

Also check whether the direct-page modes can collapse *further* — is there any encoding shorter than two bytes for `LDA $12`? — and whether `@b` is ever needed.

- [ ] **Step 5: Settle §15.3 — the ambiguity set and the covered count**

Phase 6a excludes opcodes that render as text some other opcode also renders, names them in the output, and pins the covered count per variant. Determine the 65816's set: which of the 256 render identically to another, under each `m`/`x` combination.

**Note that this may differ per width combination** — if `LDA #$12` and some other opcode collide only at one width, the set is not constant. Say so if it happens; the harness has to handle whatever you find.

- [ ] **Step 6: Settle §15.4 — the two open questions, both of which change what later tasks may do**

**Question 1: are the five 8-bit cores affected by the shortest-encoding collapse?** Assemble `LDA $0012` under `.cpu "6502i"` and under `.cpu "w65c02"` and record the bytes. If they collapse, then `Disassembler` has been rendering ambiguous text for those cores since phase 6a, and phase 6a's round-trip cannot see it because its operand bytes make every absolute operand `$1234`. **Record the finding and stop there — do not decide whether to fix it.** That decision changes certified cores' rendered output and belongs to the phase owner.

**Question 2: does `PublicSurfaceTests` see a new public method?** Read `tests/SixtyFiveXX.Conformance/PublicSurfaceTests.cs` and state whether `ExpectedPublicTypes`, `MustStayInternal` or any other assertion in that file would notice a new overload on `Disassembler`. The spec claims it would not. If the spec is wrong, task 2 must declare the overload there.

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/research/2026-08-03-65816-reference-sources.md
git commit -m "docs: research §15, the 64tass notation phase 7e is built on"
```

**Gate:** `git diff --stat main -- src tests` empty. Every notation row carries assembled bytes. §15.4's two questions are each answered outright, and question 1 is answered without acting on it.

---

### Task 2: The address path, the width overload, and `Length` 1–4

**No new addressing modes.** This task touches code five certified cores depend on, and its gate is the absence of change — the same argument that gave phase 7a its own phase.

**Files:**
- Modify: `src/SixtyFiveXX/Disassembler.cs`, `src/SixtyFiveXX/Instruction.cs`
- Modify: `tests/SixtyFiveXX.Tests/DisassemblerTests.cs`

**Interfaces:**
- Produces, and tasks 3–5 consume:
  - `public static Instruction Decode<TBus, TVariant>(in TBus bus, int address)` — unchanged signature, delegates to the overload with both widths false.
  - `public static Instruction Decode<TBus, TVariant>(in TBus bus, int address, bool wideAccumulator, bool wideIndex)` — the new entry point.
  - `private static int OperandAddress<TVariant>(int address, int offset)` — variant-correct operand wrapping.

- [ ] **Step 1: Write the failing tests**

Append to `tests/SixtyFiveXX.Tests/DisassemblerTests.cs`:

```csharp
    /// <summary>
    /// The 65816 fetches from PBR:PC across 16 MB. Decoding at $12C000 must read the opcode
    /// at $12C000, not at $00C000 — the mask Decode applied before this task.
    /// </summary>
    [Fact]
    public void W65C816_DecodesInTheBankItIsGiven()
    {
        var ram = new BankedBus();
        ram[0x12C000] = 0xEA;           // NOP in bank $12
        ram[0x00C000] = 0xA9;           // LDA # in bank 0, to prove the bank is not masked away

        var decoded = Disassembler.Decode<RefBus, W65C816Variant>(new RefBus(ram), 0x12C000);

        Assert.Equal("NOP", decoded.Mnemonic);
    }

    /// <summary>
    /// An operand fetch wraps within the program bank, never into the next one: PC rolls
    /// $FFFF to $0000 without touching PBR (research document §2.2/§2.4).
    /// </summary>
    [Fact]
    public void W65C816_OperandFetchWrapsInsideTheBank()
    {
        var ram = new BankedBus();
        ram[0x12FFFF] = 0x4C;           // JMP abs, with its operand wrapping to $120000
        ram[0x120000] = 0x34;
        ram[0x120001] = 0x12;

        var decoded = Disassembler.Decode<RefBus, W65C816Variant>(new RefBus(ram), 0x12FFFF);

        Assert.Equal("$1234", decoded.Operand);
    }

    /// <summary>
    /// The width-carrying overload exists and the old signature still compiles unchanged,
    /// defaulting to eight-bit widths. This is the compatibility promise the spec makes.
    /// </summary>
    [Fact]
    public void TheWidthlessOverload_StillCompilesAndMeansEightBit()
    {
        var ram = new byte[0x10000];
        ram[0x1000] = 0xA9;             // LDA #
        ram[0x1001] = 0x34;

        var implicitWidths = Disassembler.Decode<FlatBus, Mos6502Variant>(new FlatBus(ram), 0x1000);
        var explicitWidths = Disassembler.Decode<FlatBus, Mos6502Variant>(
            new FlatBus(ram), 0x1000, wideAccumulator: false, wideIndex: false);

        Assert.Equal(implicitWidths, explicitWidths);
    }
```

`BankedBus` lives in `tests/SixtyFiveXX.Tests/Banked816TestMachine.cs` and is already used by the 65816 unit tests — read it before using it.

- [ ] **Step 2: Run them and watch them fail**

```bash
dotnet test tests/SixtyFiveXX.Tests -f net10.0 --filter "FullyQualifiedName~DisassemblerTests"
```

Expected: **FAIL**. The bank tests decode the wrong byte; the overload test does not compile until the overload exists.

- [ ] **Step 3: Add the overload and the variant-aware address helpers**

In `src/SixtyFiveXX/Disassembler.cs`, keep the existing method as a delegating overload so every current caller is source- and binary-compatible:

```csharp
    /// <inheritdoc cref="Decode{TBus, TVariant}(in TBus, int, bool, bool)"/>
    /// <remarks>
    /// Both operand widths are taken as eight-bit. That is exactly right for the five 8-bit
    /// cores, which have no width flags at all, and is the 65816's reset state — but on that
    /// part it is an assumption, not a fact, and a caller decoding native-mode code with
    /// 16-bit registers must use the overload that says so.
    /// </remarks>
    public static Instruction Decode<TBus, TVariant>(in TBus bus, int address)
        where TBus : struct, IBus
        where TVariant : struct, ICpuVariant =>
        Decode<TBus, TVariant>(bus, address, wideAccumulator: false, wideIndex: false);
```

and give the real method the two flags:

```csharp
    /// <param name="wideAccumulator">
    /// True when the accumulator and memory are 16 bits — the 65816's <c>m = 0</c>. Decides
    /// the length of <c>LDA #</c> and its siblings, which the byte stream does not encode.
    /// Named for the width rather than for the flag because <c>m = 1</c> means <em>eight</em>
    /// bits, and a parameter called <c>m</c> would invert under the reader.
    /// </param>
    /// <param name="wideIndex">
    /// True when X and Y are 16 bits — the 65816's <c>x = 0</c>. Decides the length of
    /// <c>LDX #</c>, <c>LDY #</c>, <c>CPX #</c> and <c>CPY #</c>.
    /// </param>
    public static Instruction Decode<TBus, TVariant>(
        in TBus bus, int address, bool wideAccumulator, bool wideIndex)
        where TBus : struct, IBus
        where TVariant : struct, ICpuVariant
    {
        var info = MicroOpTable.For<TVariant>().Info[bus.Read(address & AddressMask<TVariant>())];
        ...
    }
```

Add the two helpers, both folded away for the 8-bit cores by the same compile-time test `Cpu.PcAddress()` uses:

```csharp
    /// <summary>
    /// How much of the decode address is significant: 16 bits for the eight-bit cores, 24 for
    /// the 65816, which fetches from <c>PBR:PC</c> across 16 MB.
    /// </summary>
    private static int AddressMask<TVariant>() where TVariant : struct, ICpuVariant =>
        TVariant.Variant == CpuVariant.W65C816 ? 0xFFFFFF : 0xFFFF;

    /// <summary>
    /// Where an operand byte lives. On the 65816 the program counter wraps within the program
    /// bank and never carries into the next one (research document §2.2/§2.4), so the bank is
    /// preserved and only the low 16 bits advance. On the eight-bit cores it is a plain 16-bit
    /// wrap, which is what this method reduces to there.
    /// </summary>
    private static int OperandAddress<TVariant>(int address, int offset)
        where TVariant : struct, ICpuVariant =>
        TVariant.Variant == CpuVariant.W65C816
            ? (address & 0xFF0000) | ((address + offset) & 0xFFFF)
            : (address + offset) & 0xFFFF;
```

`Operand8` and `Operand16` gain the `TVariant` type parameter and route through `OperandAddress`. Every call site in the file changes with them; the compiler finds them all.

- [ ] **Step 4: Document `Instruction.Length` as 1 to 4**

In `src/SixtyFiveXX/Instruction.cs`:

```csharp
/// <param name="Length">
/// Bytes consumed, 1 to 4. This is what the <em>processor</em> consumes, which is why
/// <c>BRK</c> is 2: the byte after the opcode is fetched and discarded, and a caller walking
/// memory by <see cref="Length"/> has to skip it the same way. Four occurs only on the
/// 65816 — <c>LDA $123456,X</c> and <c>JSL</c> — and on that part the length of an immediate
/// instruction also depends on the width flags passed to
/// <see cref="Disassembler.Decode{TBus, TVariant}(in TBus, int, bool, bool)"/>.
/// </param>
```

- [ ] **Step 5: Run everything, both TFMs, commit**

```bash
dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"
dotnet test tests/SixtyFiveXX.Conformance
```

Expected: unit **721** (718 + 3), conformance **1822**, unchanged. **The five round-trip tests must still pass with their pinned counts** — 213, 213, 177, 210, 212. If any moves, this task changed 8-bit behaviour and is wrong.

```bash
git commit -m "feat: variant-masked decode address and the width-carrying Decode overload"
```

**Gate:** conformance **1822** with all five round-trips at their pinned counts, unit **721**, both TFMs, zero warnings.

---

### Task 3: Every 65816 operand format

**Files:**
- Modify: `src/SixtyFiveXX/Disassembler.cs`
- Modify: `tests/SixtyFiveXX.Tests/DisassemblerTests.cs`

**Interfaces:**
- Consumes: `OperandAddress<TVariant>`, `AddressMask<TVariant>`, and the two width parameters from task 2.

- [ ] **Step 1: Read research §15.1 and §15.2 and check this plan's mode table against them**

**Where §15 and this plan disagree, §15 governs** — it was measured against the assembler and this table was written before it. Record any deviation in the task report.

- [ ] **Step 2: Write the failing tests — one per mode**

Append to `tests/SixtyFiveXX.Tests/DisassemblerTests.cs`. Two that carry the phase's real traps, in full:

```csharp
    /// <summary>
    /// MVN and MVP reverse their operands. The byte stream is opcode, destination bank,
    /// source bank (research document §14.3); assemblers write MVN source,destination.
    /// Rendering them in byte order produces text that reassembles into a different
    /// instruction — measured against 64tass: MVN $12,$34 assembles to 54 34 12.
    /// </summary>
    [Theory]
    [InlineData(0x54, "MVN")]
    [InlineData(0x44, "MVP")]
    public void BlockMove_RendersSourceThenDestination(byte opcode, string mnemonic)
    {
        var ram = new BankedBus();
        ram[0xC000] = opcode;
        ram[0xC001] = 0x34;             // destination bank, first in the stream
        ram[0xC002] = 0x12;             // source bank

        var decoded = Disassembler.Decode<RefBus, W65C816Variant>(new RefBus(ram), 0xC000);

        Assert.Equal(mnemonic, decoded.Mnemonic);
        Assert.Equal("$12,$34", decoded.Operand);
        Assert.Equal(3, decoded.Length);
    }

    /// <summary>
    /// An absolute operand under $100 must be forced to a word, or 64tass assembles it as
    /// direct page and the bytes change: LDA $0012 is a5 12, not ad 12 00. Research §15.2.
    /// </summary>
    [Fact]
    public void Absolute_ForcesAWordWhenTheOperandWouldCollapseToDirectPage()
    {
        var ram = new BankedBus();
        ram[0xC000] = 0xAD;             // LDA abs
        ram[0xC001] = 0x12;
        ram[0xC002] = 0x00;

        var decoded = Disassembler.Decode<RefBus, W65C816Variant>(new RefBus(ram), 0xC000);

        Assert.Equal("@w $0012", decoded.Operand);
    }
```

Then one case per remaining mode in **the plan's mode table above**, which carries the exact notation and length each must assert — that table is the source, so no expected value needs inventing. Follow the shape of the two cases above: lay the opcode and its operand bytes into a `BankedBus`, decode, and assert mnemonic, operand text and length. Use `$12`/`$34`/`$56` for the operand bytes so a transposition is visible in the failure message.

**Both immediate widths must be covered**, and a test that discriminates them must pass **opposed** width flags — `wideAccumulator: true, wideIndex: false` and the reverse — or an implementation reading the wrong flag passes anyway. This is the same trap that produced a real hole in phase 7c's test design.

- [ ] **Step 3: Run them and watch them fail**

Expected: **FAIL** with `NotSupportedException` naming each mode.

- [ ] **Step 4: Add the arms**

Every 65816 mode from the plan's table, plus the `Stack` sub-arms for `Op.Cop`, `Op.Pea`, `Op.Pei`, `Op.Per` and `Op.Rtl` — replacing the `throw` phase 7d put there. The immediate arm becomes width-aware:

```csharp
            // The one mode whose length is not a property of the encoding. Which flag sizes it
            // is a property of the operation, not the mode: the accumulator operations follow
            // m and the index operations follow x.
            AddrMode.Immediate when WidensWithAccumulator(info.Operation) && wideAccumulator =>
                new Instruction(info.Mnemonic, $"#${Operand16<TVariant>(bus, address):X4}", 3),
            AddrMode.Immediate when WidensWithIndex(info.Operation) && wideIndex =>
                new Instruction(info.Mnemonic, $"#${Operand16<TVariant>(bus, address):X4}", 3),
            AddrMode.Immediate =>
                new Instruction(info.Mnemonic, $"#${Operand8<TVariant>(bus, address, 1):X2}", 2),
```

`WidensWithAccumulator` and `WidensWithIndex` are two small predicates over `Op`. **Derive their membership from the opcode table rather than writing a list from memory:** the operations declaring `Width.M` and `Width.X` are exactly the ones that widen, and `Opcodes65C816.cs` is the source of truth. Say in a comment that the eight-bit cores never reach these because their tables declare no `Width`.

- [ ] **Step 5: Run everything, both TFMs, commit**

Expected: conformance **1822**, five round-trips at their pinned counts, unit up by the number of cases added.

```bash
git commit -m "feat: 65816 operand formats for every addressing mode"
```

**Gate:** every mode rendered, no `NotSupportedException` reachable for a defined 65816 opcode, the five 8-bit round-trips unmoved.

---

### Task 4: The 65816 round-trip

**Files:**
- Modify: `tests/SixtyFiveXX.Conformance/RoundTripTests.cs`

**Interfaces:**
- Consumes: everything tasks 2 and 3 produced.

- [ ] **Step 1: Read research §15.3 and take its ambiguity set and covered count**

If §15.3 records that the set differs by width combination, the harness must carry a set per combination rather than one shared set. **Do not pin a count you have not read from §15.3.**

- [ ] **Step 2: Write the failing test**

```csharp
    /// <summary>
    /// The 65816 round-trips under every combination of the two width flags. Four runs rather
    /// than one because m and x size different opcodes, and a single setting would leave one
    /// flag's effect unexercised — the length of LDA # is the whole substance of the m/x
    /// problem, and it is invisible unless both settings are assembled.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void W65C816_RoundTripsThroughTheAssembler(bool wideAccumulator, bool wideIndex) =>
        RoundTrip816(wideAccumulator, wideIndex);
```

- [ ] **Step 3: Run it and watch it fail**

Expected: **FAIL** — the method does not exist.

- [ ] **Step 4: Build the 65816 harness**

Add two methods named exactly as step 5's discriminator expects: `RoundTrip816(bool wideAccumulator, bool wideIndex)` and `(string Source, byte[] Expected) Build816(bool wideAccumulator, bool wideIndex)`. They mirror the existing `RoundTrip<TVariant>` and `Build<TVariant>`, differing in three ways and otherwise reusing them:

1. **The source declares the widths**, emitting `.as` or `.al` and `.xs` or `.xl` after the `.cpu` line, matching the flags the disassembly was produced with. Without this 64tass assembles every immediate at its own default and the lengths disagree.
2. **The layout uses the same widths as the disassembly.** Both the write loop and the read loop pass the same two flags; a mismatch silently lays out one length and decodes another.
3. **Long operands need a third operand byte.** `Build`'s existing loop writes at most two. Extend it to write a third when the decoded length is 4.

Assemble with `.cpu "65816"`.

- [ ] **Step 5: Add the discriminator**

The 8-bit gate has `TheGateDiscriminates_AnAlteredOperandChangesTheBytes` because a round-trip can pass while doing nothing load-bearing. The 65816 needs one that would fail if the width plumbing were inert — corrupt the emitted `.al` to `.as` in a 16-bit run and require the assembled bytes to change:

```csharp
    /// <summary>
    /// The width plumbing has to be load-bearing. Assembling a 16-bit disassembly under the
    /// 8-bit directive must change the bytes; if it does not, the round-trip is passing
    /// without the widths mattering and would pass with them ignored entirely.
    /// </summary>
    [Fact]
    public void TheGateDiscriminates_TheWidthDirectivesChangeTheBytes()
    {
        var (source, expected) = Build816(wideAccumulator: true, wideIndex: true);
        var narrowed = source.Replace("\t.al\n", "\t.as\n").Replace("\t.xl\n", "\t.xs\n");

        Assert.NotEqual(source, narrowed);
        Assert.NotEqual(expected, Assemble(narrowed, "65816"));
    }
```

Match the real line endings `Build816` emits — `AppendLine` uses the platform's, so use `Environment.NewLine` or compare on the exact string the builder produced.

- [ ] **Step 6: Run everything, both TFMs, commit**

Expected: conformance **1827** (1822 + the four theory cases + the discriminator), five 8-bit round-trips unmoved.

```bash
git commit -m "test: 65816 round-trip through 64tass under every width combination"
```

**Gate:** all four width combinations reproduce their bytes, the discriminator fails when the widths are neutered, and the five 8-bit round-trips keep their pinned counts.

---

### Task 5: Documentation

**Files:**
- Modify: `README.md`, `src/SixtyFiveXX/Disassembler.cs` (class remarks)

- [ ] **Step 1: Rewrite the `Disassembler` class remarks**

The current text says most 65816 modes throw and that `AddrMode.Immediate` decodes at a fixed length ignoring `m`. Both stop being true in this phase. Say instead what the widths mean, that they are a caller input because the encoding does not carry them, and that the address is bank-qualified on the 65816. **Keep it count-free** — the existing remark says so explicitly and gives the reason.

- [ ] **Step 2: Update the README's disassembler section**

It currently warns that 65816 support is phase 7e's job. Replace with what the disassembler now does, including the width parameters and a one-line example. Leave the support-matrix row count-free.

- [ ] **Step 3: Commit**

```bash
git commit -m "docs: the disassembler covers the 65816"
```

**Gate:** no statement in either file that the code contradicts; no count that will drift.

---

### Task 6: Whole-branch review and the full gate

**Files:** whatever the review finds, plus the spec's Phase 7e section.

- [ ] **Step 1: Produce the branch diff**

```bash
git diff main...HEAD > .superpowers/sdd/p7e-review.diff
```

- [ ] **Step 2: Review against this checklist**

- **The five 8-bit cores' rendered text is byte-identical to `main`.** Decode every opcode of every 8-bit variant on both revisions and diff the strings. This is the task-2 gate restated, and it is the one thing in this phase that could break certified behaviour.
- **Every 65816 mode has an arm**, and no defined 65816 opcode can reach a `NotSupportedException`.
- **The forcing prefixes appear exactly when research §15.2 says and never otherwise** — a renderer that emits `@w` unconditionally would pass the round-trip while making every listing ugly.
- **`MVN`/`MVP` operands are reversed**, and a test would fail if they were not.
- **Both width flags are load-bearing**, proven by the discriminator rather than asserted.
- **No count in a doc comment that will drift.**
- **`PublicSurfaceTests` matches whatever research §15.4 question 2 established.**

- [ ] **Step 3: Fix Critical and Important findings, each as its own commit**

Minor findings are fixed or recorded in the ledger with the reason. Do not silently drop one.

- [ ] **Step 4: Add the Verified paragraph to the spec and mark the phase-table row**

Under §"Phase 7e", in the shape 7a through 7d already use: the measured counts, both TFMs, and anything certified by unit test alone. Then mark the phase-split table's **7e** row complete.

- [ ] **Step 5: Run the full gate on an idle machine**

```bash
uptime
dotnet test tests/SixtyFiveXX.Tests --filter "Category!=Performance"
dotnet test tests/SixtyFiveXX.Conformance
dotnet test tests/SixtyFiveXX.Tests -c Release --filter "Category=Performance"
```

Pass an explicit 600000 ms timeout on the conformance call. If the throughput gate fails, check `uptime` before believing it.

- [ ] **Step 6: Record the phase in the ledger**

Append a phase-7e section to `.superpowers/sdd/progress.md`: per-task commits, what each gate measured, every defect the round-trip found that review did not, every defect review found that the round-trip could not, and what phase 7e leaves open.

**Gate:** zero Critical findings, every suite green on both TFMs, build zero warnings, working tree clean.

---

## What this phase does not close

- **Research §12's four decimal-mode gaps and §14's open rows remain open.** Nothing here touches them.
- **Whether the five 8-bit cores should also force their absolute operands** is task 1 question 1, and is deliberately left to the phase owner rather than decided inside a task.
- **`PublicSurfaceTests` has no member-level assertion**, so a new public method on an existing type is invisible to it. Recorded in the spec; extending the gate is its own piece of work.
