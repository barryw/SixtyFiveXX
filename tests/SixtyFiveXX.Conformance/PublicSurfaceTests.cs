using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Gates the public API surface of the artifact a consumer actually receives.
/// </summary>
/// <remarks>
/// <para>
/// This packs the library and reflects over the DLLs inside the <c>.nupkg</c>, rather than
/// over the library as this test project sees it. Every test project in this solution has
/// <c>InternalsVisibleTo</c>, so from inside the suite an internal type is indistinguishable
/// from a public one — which is exactly how the phase 3 refactor once made <c>Cpu</c>
/// itself internal with all 569 tests still green. The published package would have
/// shipped with its only entry point invisible to every consumer. No behavioural suite can
/// catch that; only one that looks at the packed artifact can.
/// </para>
/// <para>
/// It is deliberately an <em>exact set</em> comparison, not a pair of contains/does-not-contain
/// checks. Accidental widening is the failure mode a named-type checklist misses: a
/// descriptor type that becomes public silently becomes API this project owes compatibility
/// to, and keeping <c>OpcodeInfo</c> internal is precisely what lets phase 4 reshape it.
/// A new public type is therefore a deliberate act that must edit <see cref="ExpectedPublicTypes"/>.
/// </para>
/// <para>
/// Metadata is read with <see cref="MetadataReader"/> rather than loaded with
/// <see cref="Assembly.Load(byte[])"/>. Loading a packed DLL into this process could bind to
/// the project reference already loaded here instead of the packed bytes, and the test would
/// pass while proving nothing. <c>MetadataReader</c> never resolves or executes anything, and
/// needs no reference assemblies — which also means one code path reads both TFMs.
/// </para>
/// <para>
/// Scope: types <em>and</em> their members. The type-level list alone was blind to a whole
/// class of change — phase 7e added a public <c>Disassembler.Decode</c> overload and every
/// assertion here stayed green, because <c>Disassembler</c> was already on the list and
/// nothing read a <c>MethodDefinition</c>. A new public method is API this package owes
/// compatibility to exactly as much as a new public type is, so
/// <see cref="ExpectedPublicMembers"/> pins it the same way.
/// </para>
/// <para>
/// The member list is what it is rather than what it could be, for reasons worth stating.
/// Property and event accessors appear under their generated names — <c>get_Mnemonic()</c>,
/// not <c>Mnemonic { get; }</c> — because the accessor is the thing a consumer actually
/// calls, and reading them straight off <c>GetMethods</c> needs no second metadata table to
/// stay in sync with. An <c>init</c> setter therefore shows as its real encoding,
/// <c>modreq(IsExternalInit) void</c>: init-versus-set is a source-breaking difference and
/// a prettier rendering would have hidden it. By-ref parameters and returns are qualified
/// <c>in</c>/<c>out</c>/<c>ref</c> from the parameter rows rather than left as a bare
/// <c>&amp;</c>, for the same reason. Generic parameters render by name, because the list is
/// read by a human deciding whether a diff was intended and <c>!0</c> does not help them.
/// </para>
/// <para>
/// One list covers both frameworks. The library has no <c>#if</c> and the two packed DLLs
/// were measured to produce byte-identical member sets; if that ever stops being true, the
/// per-TFM theory is what says so, and the list splits then rather than now.
/// </para>
/// </remarks>
public class PublicSurfaceTests
{
    /// <summary>
    /// Every type a consumer of the package can see. Types are namespace-qualified and
    /// carry CLR arity suffixes, so <c>Cpu&lt;TBus, TVariant&gt;</c> appears as
    /// <c>SixtyFiveXX.Cpu`2</c> — a change in type-parameter count is itself a breaking
    /// change and this list is meant to notice it.
    /// </summary>
    private static readonly string[] ExpectedPublicTypes =
    [
        "SixtyFiveXX.Cpu`2",
        "SixtyFiveXX.CpuState",
        "SixtyFiveXX.CpuVariant",
        "SixtyFiveXX.Disassembler",
        "SixtyFiveXX.Flag",
        "SixtyFiveXX.FlatBus",
        "SixtyFiveXX.IBus",
        "SixtyFiveXX.ICpuVariant",
        "SixtyFiveXX.Instruction",
        "SixtyFiveXX.RefBus",
        "SixtyFiveXX.UndefinedOpcodeException",
        "SixtyFiveXX.Variants.Mos6502Variant",
        "SixtyFiveXX.Variants.Mos6510Variant",
        "SixtyFiveXX.Variants.Rockwell65C02Variant",
        "SixtyFiveXX.Variants.Synertek65C02Variant",
        "SixtyFiveXX.Variants.W65C816Variant",
        "SixtyFiveXX.Variants.Wdc65C02Variant",
    ];

    /// <summary>
    /// Every member a consumer of the package can call, in the form
    /// <c>[static ][const ]Owner.Name[&lt;T…&gt;]([params]) : returnType</c>. Sorted
    /// ordinally, which is why the <c>static</c> entries collect at the end.
    /// </summary>
    private static readonly string[] ExpectedPublicMembers =
    [
        "SixtyFiveXX.CpuState.A : ushort",
        "SixtyFiveXX.CpuState.DBR : byte",
        "SixtyFiveXX.CpuState.DP : ushort",
        "SixtyFiveXX.CpuState.E : bool",
        "SixtyFiveXX.CpuState.P : byte",
        "SixtyFiveXX.CpuState.PBR : byte",
        "SixtyFiveXX.CpuState.PC : ushort",
        "SixtyFiveXX.CpuState.S : ushort",
        "SixtyFiveXX.CpuState.ToString() : string",
        "SixtyFiveXX.CpuState.X : ushort",
        "SixtyFiveXX.CpuState.Y : ushort",
        "SixtyFiveXX.CpuState.get_C() : bool",
        "SixtyFiveXX.CpuState.get_D() : bool",
        "SixtyFiveXX.CpuState.get_I() : bool",
        "SixtyFiveXX.CpuState.get_M() : bool",
        "SixtyFiveXX.CpuState.get_N() : bool",
        "SixtyFiveXX.CpuState.get_V() : bool",
        "SixtyFiveXX.CpuState.get_XFlag() : bool",
        "SixtyFiveXX.CpuState.get_Z() : bool",
        "SixtyFiveXX.CpuState.set_C(bool) : void",
        "SixtyFiveXX.CpuState.set_D(bool) : void",
        "SixtyFiveXX.CpuState.set_I(bool) : void",
        "SixtyFiveXX.CpuState.set_M(bool) : void",
        "SixtyFiveXX.CpuState.set_N(bool) : void",
        "SixtyFiveXX.CpuState.set_V(bool) : void",
        "SixtyFiveXX.CpuState.set_XFlag(bool) : void",
        "SixtyFiveXX.CpuState.set_Z(bool) : void",

        // The enum's backing field. Not something anyone names in source, but it is where
        // the underlying type lives, and narrowing CpuVariant to a byte would be a silent
        // breaking change that no other row here would notice.
        "SixtyFiveXX.CpuVariant.value__ : int",

        "SixtyFiveXX.Cpu`2..ctor(TBus) : void",
        "SixtyFiveXX.Cpu`2.Reset() : void",
        "SixtyFiveXX.Cpu`2.ResetCycleCount() : void",
        "SixtyFiveXX.Cpu`2.Run(long) : long",
        "SixtyFiveXX.Cpu`2.RunUntil(System.Func`2<SixtyFiveXX.Cpu`2<TBus,TVariant>,bool>, long) : long",
        "SixtyFiveXX.Cpu`2.SetIrq(bool) : void",
        "SixtyFiveXX.Cpu`2.SetNmi(bool) : void",
        "SixtyFiveXX.Cpu`2.SetRdy(bool) : void",
        "SixtyFiveXX.Cpu`2.SetSo() : void",
        "SixtyFiveXX.Cpu`2.Step() : long",
        "SixtyFiveXX.Cpu`2.Tick() : void",
        "SixtyFiveXX.Cpu`2.get_AtInstructionBoundary() : bool",
        "SixtyFiveXX.Cpu`2.get_Bus() : ref TBus",
        "SixtyFiveXX.Cpu`2.get_Cycles() : long",
        "SixtyFiveXX.Cpu`2.get_IrqAsserted() : bool",
        "SixtyFiveXX.Cpu`2.get_IsJammed() : bool",
        "SixtyFiveXX.Cpu`2.get_IsStopped() : bool",
        "SixtyFiveXX.Cpu`2.get_IsWaiting() : bool",
        "SixtyFiveXX.Cpu`2.get_NmiAsserted() : bool",
        "SixtyFiveXX.Cpu`2.get_Ready() : bool",
        "SixtyFiveXX.Cpu`2.get_State() : ref SixtyFiveXX.CpuState",
        "SixtyFiveXX.FlatBus..ctor(byte[]) : void",
        "SixtyFiveXX.FlatBus.Internal(int) : void",
        "SixtyFiveXX.FlatBus.Read(int) : byte",
        "SixtyFiveXX.FlatBus.Write(int, byte) : void",
        "SixtyFiveXX.FlatBus.get_Ram() : byte[]",
        "SixtyFiveXX.IBus.Internal(int) : void",
        "SixtyFiveXX.IBus.Read(int) : byte",
        "SixtyFiveXX.IBus.Write(int, byte) : void",
        "SixtyFiveXX.Instruction..ctor(string, string, int) : void",
        "SixtyFiveXX.Instruction.Deconstruct(out string, out string, out int) : void",
        "SixtyFiveXX.Instruction.Equals(SixtyFiveXX.Instruction) : bool",
        "SixtyFiveXX.Instruction.Equals(object) : bool",
        "SixtyFiveXX.Instruction.GetHashCode() : int",
        "SixtyFiveXX.Instruction.ToString() : string",
        "SixtyFiveXX.Instruction.get_Length() : int",
        "SixtyFiveXX.Instruction.get_Mnemonic() : string",
        "SixtyFiveXX.Instruction.get_Operand() : string",

        // init, not set. The modreq is the encoding of that distinction and turning an init
        // into a set is source-breaking for every caller using an object initialiser.
        "SixtyFiveXX.Instruction.set_Length(int) : modreq(System.Runtime.CompilerServices.IsExternalInit) void",
        "SixtyFiveXX.Instruction.set_Mnemonic(string) : modreq(System.Runtime.CompilerServices.IsExternalInit) void",
        "SixtyFiveXX.Instruction.set_Operand(string) : modreq(System.Runtime.CompilerServices.IsExternalInit) void",

        "SixtyFiveXX.RefBus..ctor(SixtyFiveXX.IBus) : void",
        "SixtyFiveXX.RefBus.Internal(int) : void",
        "SixtyFiveXX.RefBus.Read(int) : byte",
        "SixtyFiveXX.RefBus.Write(int, byte) : void",
        "SixtyFiveXX.UndefinedOpcodeException..ctor(byte, ushort) : void",
        "SixtyFiveXX.UndefinedOpcodeException..ctor(byte, ushort, byte) : void",
        "SixtyFiveXX.UndefinedOpcodeException.get_Address() : ushort",
        "SixtyFiveXX.UndefinedOpcodeException.get_Bank() : System.Nullable`1<byte>",
        "SixtyFiveXX.UndefinedOpcodeException.get_Opcode() : byte",

        // The two overloads this whole member-level gate exists for. The four-argument one
        // is what phase 7e added and what the type-level list could not see.
        "static SixtyFiveXX.Disassembler.Decode<TBus,TVariant>(in TBus, int) : SixtyFiveXX.Instruction",
        "static SixtyFiveXX.Disassembler.Decode<TBus,TVariant>(in TBus, int, bool, bool) : SixtyFiveXX.Instruction",

        "static SixtyFiveXX.ICpuVariant.get_Variant() : SixtyFiveXX.CpuVariant",
        "static SixtyFiveXX.Instruction.op_Equality(SixtyFiveXX.Instruction, SixtyFiveXX.Instruction) : bool",
        "static SixtyFiveXX.Instruction.op_Inequality(SixtyFiveXX.Instruction, SixtyFiveXX.Instruction) : bool",
        "static SixtyFiveXX.Variants.Mos6502Variant.get_Variant() : SixtyFiveXX.CpuVariant",
        "static SixtyFiveXX.Variants.Mos6510Variant.get_Variant() : SixtyFiveXX.CpuVariant",
        "static SixtyFiveXX.Variants.Rockwell65C02Variant.get_Variant() : SixtyFiveXX.CpuVariant",
        "static SixtyFiveXX.Variants.Synertek65C02Variant.get_Variant() : SixtyFiveXX.CpuVariant",
        "static SixtyFiveXX.Variants.W65C816Variant.get_Variant() : SixtyFiveXX.CpuVariant",
        "static SixtyFiveXX.Variants.Wdc65C02Variant.get_Variant() : SixtyFiveXX.CpuVariant",
        "static const SixtyFiveXX.CpuVariant.Mos6502 : SixtyFiveXX.CpuVariant",
        "static const SixtyFiveXX.CpuVariant.Mos6510 : SixtyFiveXX.CpuVariant",
        "static const SixtyFiveXX.CpuVariant.Rockwell65C02 : SixtyFiveXX.CpuVariant",
        "static const SixtyFiveXX.CpuVariant.Synertek65C02 : SixtyFiveXX.CpuVariant",
        "static const SixtyFiveXX.CpuVariant.W65C816 : SixtyFiveXX.CpuVariant",
        "static const SixtyFiveXX.CpuVariant.Wdc65C02 : SixtyFiveXX.CpuVariant",
        "static const SixtyFiveXX.Cpu`2.IrqVector : int",
        "static const SixtyFiveXX.Cpu`2.NmiVector : int",
        "static const SixtyFiveXX.Cpu`2.ResetVector : int",
        "static const SixtyFiveXX.Flag.B : byte",
        "static const SixtyFiveXX.Flag.C : byte",
        "static const SixtyFiveXX.Flag.D : byte",
        "static const SixtyFiveXX.Flag.I : byte",
        "static const SixtyFiveXX.Flag.M : byte",
        "static const SixtyFiveXX.Flag.N : byte",
        "static const SixtyFiveXX.Flag.U : byte",
        "static const SixtyFiveXX.Flag.V : byte",
        "static const SixtyFiveXX.Flag.X : byte",
        "static const SixtyFiveXX.Flag.Z : byte",
    ];

    /// <summary>
    /// The internal model. Implied by the exact-set assertion, but named explicitly so a
    /// leak reports itself as "this descriptor escaped" rather than as a set diff, and so
    /// the intent survives someone regenerating <see cref="ExpectedPublicTypes"/> from a
    /// failing run.
    /// </summary>
    private static readonly string[] MustStayInternal =
    [
        "SixtyFiveXX.OpcodeInfo",
        "SixtyFiveXX.AddrMode",
        "SixtyFiveXX.Op",
        "SixtyFiveXX.Access",
        "SixtyFiveXX.MicroOp",
        "SixtyFiveXX.MicroOpTable",
        "SixtyFiveXX.MicroOps",
        "SixtyFiveXX.Opcodes6502",
    ];

    /// <summary>
    /// The frameworks the package must ship. Kept in sync by hand with the library's
    /// <c>&lt;TargetFrameworks&gt;</c>; <see cref="Package_ShipsEveryDeclaredTargetFramework"/>
    /// is what fails if they drift.
    /// </summary>
    private static readonly string[] TargetFrameworks = ["net8.0", "net10.0"];

    /// <summary>Each framework gated separately — a type can differ per TFM.</summary>
    public static TheoryData<string> PackagedFrameworks => [.. TargetFrameworks];

    [Theory]
    [MemberData(nameof(PackagedFrameworks))]
    public void PackagedAssembly_ExposesExactlyTheIntendedPublicSurface(string targetFramework)
    {
        var actual = PackedPackage.Value.PublicTypesFor(targetFramework);

        // Both sides ordered by the same comparer so the assertion reports a real surface
        // difference rather than a sort artefact: ordinal puts '`' (0x60) after the
        // uppercase letters, so Cpu`2 sorts *after* CpuState while the literal list above
        // is written the way a human reads it.
        Assert.Equal(ExpectedPublicTypes.Order(StringComparer.Ordinal), actual);
    }

    [Theory]
    [MemberData(nameof(PackagedFrameworks))]
    public void PackagedAssembly_ExposesExactlyTheIntendedPublicMembers(string targetFramework)
    {
        var actual = PackedPackage.Value.PublicMembersFor(targetFramework);

        // Exact set, for the same reason the type assertion is: a member that appears
        // without anyone editing this list is a compatibility obligation nobody chose.
        Assert.Equal(ExpectedPublicMembers.Order(StringComparer.Ordinal), actual);
    }

    [Theory]
    [MemberData(nameof(PackagedFrameworks))]
    public void PackagedAssembly_KeepsTheDescriptorModelInvisible(string targetFramework)
    {
        var actual = PackedPackage.Value.PublicTypesFor(targetFramework);

        var leaked = MustStayInternal.Where(actual.Contains).ToArray();

        Assert.Empty(leaked);
    }

    [Fact]
    public void Package_ShipsEveryDeclaredTargetFramework()
    {
        // Guards the case the per-TFM theories cannot see: a csproj that quietly stops
        // multi-targeting still passes every assertion for the TFM it does ship, because
        // MemberData for the missing one would just be absent from the package. Asserting
        // the lib/ layout directly is what makes "certified on net8.0" a claim about the
        // package rather than about the build tree.
        Assert.Equal(TargetFrameworks.Order(), PackedPackage.Value.Frameworks);
    }

    /// <summary>
    /// Packs once per test process. xUnit runs collections in parallel by default and this
    /// project sets no <c>CollectionBehavior</c>, so the four tests above can start
    /// concurrently; <see cref="Lazy{T}"/> with the default thread-safety mode serialises
    /// the pack and lets the rest share it.
    /// </summary>
    private static readonly Lazy<PackedAssemblies> PackedPackage = new(PackedAssemblies.Create);

    private sealed class PackedAssemblies
    {
        /// <summary>What one packed <c>lib/&lt;tfm&gt;/SixtyFiveXX.dll</c> exposes.</summary>
        private sealed record Surface(string[] Types, string[] Members);

        private readonly Dictionary<string, Surface> _surfaceByFramework;

        public IEnumerable<string> Frameworks => _surfaceByFramework.Keys.Order();

        private PackedAssemblies(Dictionary<string, Surface> surfaceByFramework) =>
            _surfaceByFramework = surfaceByFramework;

        public string[] PublicTypesFor(string targetFramework) => For(targetFramework).Types;

        public string[] PublicMembersFor(string targetFramework) => For(targetFramework).Members;

        private Surface For(string targetFramework) =>
            _surfaceByFramework.TryGetValue(targetFramework, out var surface)
                ? surface
                : throw new InvalidOperationException(
                    $"The package ships no lib/{targetFramework}. It has: " +
                    $"{string.Join(", ", Frameworks)}.");

        public static PackedAssemblies Create()
        {
            // --artifacts-path redirects both the intermediate and the output directory, so
            // this pack shares no obj/ or bin/ with the conformance run that is executing
            // it — including the run for the *other* TFM, which VSTest starts concurrently.
            // Without it, two MSBuild processes would contend on src/SixtyFiveXX/obj.
            var workspace = Directory.CreateTempSubdirectory("sixtyfivexx-surface-");

            try
            {
                var nupkg = Pack(workspace.FullName);

                using var package = ZipFile.OpenRead(nupkg);

                var byFramework = new Dictionary<string, Surface>();

                foreach (var entry in package.Entries)
                {
                    // lib/<tfm>/SixtyFiveXX.dll — the only files a consumer compiles against.
                    var parts = entry.FullName.Split('/');
                    if (parts is not ["lib", var tfm, "SixtyFiveXX.dll"]) continue;

                    byFramework[tfm] = ReadSurface(entry);
                }

                if (byFramework.Count == 0)
                    throw new InvalidOperationException(
                        $"No lib/<tfm>/SixtyFiveXX.dll in {nupkg}. Entries: " +
                        $"{string.Join(", ", package.Entries.Select(e => e.FullName))}.");

                return new PackedAssemblies(byFramework);
            }
            finally
            {
                workspace.Delete(recursive: true);
            }
        }

        /// <summary>
        /// Generous enough that a cold CI restore is not mistaken for a hang, short enough
        /// that a real hang still surfaces as a failed test rather than a stuck pipeline.
        /// </summary>
        private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(5);

        private static string Pack(string workspace)
        {
            var project = Path.Combine(RepositoryRoot(), "src", "SixtyFiveXX", "SixtyFiveXX.csproj");

            using var pack = Process.Start(new ProcessStartInfo("dotnet")
            {
                ArgumentList = { "pack", project, "-c", "Release", "--artifacts-path", workspace },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("Could not start 'dotnet pack'.");

            // Read both pipes before waiting: a pack that fills one of them would otherwise
            // block on a full buffer and this test would hang rather than fail.
            var stdout = pack.StandardOutput.ReadToEndAsync();
            var stderr = pack.StandardError.ReadToEndAsync();

            // Bounded wait. Draining the pipes rules out a deadlock on our side, but not a
            // pack that never finishes on its own — a stalled NuGet restore, a blocked feed,
            // a global MSBuild lock held elsewhere. An unbounded wait would take the whole
            // conformance run down with it (a minute of Harte and Klaus coverage in the same
            // assembly), leaving recovery to whatever timeout CI happens to impose.
            if (!pack.WaitForExit((int)PackTimeout.TotalMilliseconds))
            {
                pack.Kill(entireProcessTree: true);
                throw new TimeoutException(
                    $"'dotnet pack' did not finish within {PackTimeout} and was killed. " +
                    "A restore that cannot reach its feed is the usual cause.");
            }

            if (pack.ExitCode != 0)
                throw new InvalidOperationException(
                    $"'dotnet pack' failed with exit code {pack.ExitCode}.\n" +
                    $"{stdout.GetAwaiter().GetResult()}\n{stderr.GetAwaiter().GetResult()}");

            return Directory.EnumerateFiles(workspace, "*.nupkg", SearchOption.AllDirectories)
                       .FirstOrDefault()
                   ?? throw new InvalidOperationException(
                       $"'dotnet pack' reported success but produced no .nupkg under {workspace}.");
        }

        private static string RepositoryRoot()
        {
            // Stamped by the csproj at build time rather than discovered by walking up from
            // AppContext.BaseDirectory. The walk looks obviously correct and is: the test
            // binary really does sit under the repo today. But it silently assumes the
            // output directory stays *inside* the checkout, and .NET's UseArtifactsOutput
            // can move it out — at which point the walk finds no .sln and takes the whole
            // conformance assembly down, not just this class. MSBuild already knows the
            // answer for free, so there is nothing to assume.
            var root = typeof(PublicSurfaceTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;

            if (string.IsNullOrEmpty(root))
                throw new InvalidOperationException(
                    "No RepositoryRoot assembly metadata. It is set by an <AssemblyMetadata> " +
                    "item in SixtyFiveXX.Conformance.csproj; the build stamps it.");

            if (!File.Exists(Path.Combine(root, "SixtyFiveXX.sln")))
                throw new InvalidOperationException(
                    $"RepositoryRoot metadata points at '{root}', which holds no SixtyFiveXX.sln.");

            return root;
        }

        private static Surface ReadSurface(ZipArchiveEntry entry)
        {
            // PEReader needs a seekable stream; a zip entry stream is forward-only.
            using var dll = new MemoryStream();
            using (var compressed = entry.Open()) compressed.CopyTo(dll);
            dll.Position = 0;

            using var pe = new PEReader(dll);
            var metadata = pe.GetMetadataReader();

            var visible = metadata.TypeDefinitions
                .Select(metadata.GetTypeDefinition)
                .Where(type => IsVisibleOutsideTheAssembly(metadata, type))
                .ToArray();

            return new Surface(
                visible.Select(type => FullName(metadata, type))
                    .Order(StringComparer.Ordinal).ToArray(),
                visible.SelectMany(type => VisibleMembers(metadata, type))
                    .Order(StringComparer.Ordinal).ToArray());
        }

        /// <summary>
        /// Every field and method on <paramref name="type"/> that survives outside the
        /// assembly, rendered as one line each.
        /// </summary>
        /// <remarks>
        /// Properties and events are not read from their own tables. Their accessors are
        /// already here as methods, carrying the same type information, and a member that
        /// exists in the property table but whose accessor is private would be reported by
        /// a property-level walk as callable when it is not.
        /// </remarks>
        private static IEnumerable<string> VisibleMembers(MetadataReader metadata, TypeDefinition type)
        {
            var owner = FullName(metadata, type);
            var typeParameters = GenericParameterNames(metadata, type.GetGenericParameters());

            foreach (var handle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(handle);
                if (!IsVisibleOutsideTheAssembly(field.Attributes)) continue;

                var context = new GenericNames(typeParameters, []);

                yield return
                    (field.Attributes.HasFlag(FieldAttributes.Static) ? "static " : "") +
                    (field.Attributes.HasFlag(FieldAttributes.Literal) ? "const " : "") +
                    $"{owner}.{metadata.GetString(field.Name)} : " +
                    field.DecodeSignature(new SignatureText(), context);
            }

            foreach (var handle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(handle);
                if (!IsVisibleOutsideTheAssembly(method.Attributes)) continue;

                var methodParameters = GenericParameterNames(metadata, method.GetGenericParameters());
                var context = new GenericNames(typeParameters, methodParameters);
                var signature = method.DecodeSignature(new SignatureText(), context);

                // The signature blob says "by ref" and stops there. in/out/ref live in the
                // parameter rows — ParameterAttributes.Out, and IsReadOnlyAttribute for in —
                // and each pair of them is a source-breaking swap for callers, so a bare '&'
                // would let one through. Sequence number 0 is the return value's row, which
                // is where a `ref readonly` return records itself.
                var byRef = ByRefKinds(metadata, method);

                var parameters = signature.ParameterTypes
                    .Select((type, index) => QualifyByRef(type, byRef.GetValueOrDefault(index + 1, "ref")));

                var generics = methodParameters.Length == 0
                    ? ""
                    : $"<{string.Join(",", methodParameters)}>";

                yield return
                    (method.Attributes.HasFlag(MethodAttributes.Static) ? "static " : "") +
                    $"{owner}.{metadata.GetString(method.Name)}{generics}" +
                    $"({string.Join(", ", parameters)}) : " +
                    QualifyByRef(signature.ReturnType, byRef.GetValueOrDefault(0, "ref"));
            }
        }

        private static ImmutableArray<string> GenericParameterNames(
            MetadataReader metadata, GenericParameterHandleCollection handles) =>
            [.. handles.Select(h => metadata.GetString(metadata.GetGenericParameter(h).Name))];

        private static string QualifyByRef(string type, string keyword) =>
            type.EndsWith('&') ? $"{keyword} {type[..^1]}" : type;

        /// <summary>By-ref kind per parameter sequence number; 0 is the return value.</summary>
        private static Dictionary<int, string> ByRefKinds(MetadataReader metadata, MethodDefinition method)
        {
            var kinds = new Dictionary<int, string>();

            foreach (var handle in method.GetParameters())
            {
                var parameter = metadata.GetParameter(handle);

                var readOnly = parameter.GetCustomAttributes()
                    .Select(metadata.GetCustomAttribute)
                    .Any(attribute => AttributeTypeName(metadata, attribute) == "IsReadOnlyAttribute");

                kinds[parameter.SequenceNumber] =
                    parameter.Attributes.HasFlag(ParameterAttributes.Out) ? "out"
                    : readOnly ? "in"
                    : "ref";
            }

            return kinds;
        }

        /// <summary>
        /// The unqualified name of an attribute's type, reached through its constructor.
        /// Compilers emit <c>IsReadOnlyAttribute</c> into the assembly that needs it, so it
        /// can arrive as either a reference or a definition depending on the target
        /// framework; both are handled rather than assumed.
        /// </summary>
        private static string AttributeTypeName(MetadataReader metadata, CustomAttribute attribute) =>
            attribute.Constructor.Kind switch
            {
                HandleKind.MemberReference =>
                    metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)
                        metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent).Name),

                HandleKind.MethodDefinition =>
                    metadata.GetString(metadata.GetTypeDefinition(
                        metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor)
                            .GetDeclaringType()).Name),

                _ => "",
            };

        private static bool IsVisibleOutsideTheAssembly(FieldAttributes attributes) =>
            (attributes & FieldAttributes.FieldAccessMask)
            is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;

        private static bool IsVisibleOutsideTheAssembly(MethodAttributes attributes) =>
            (attributes & MethodAttributes.MemberAccessMask)
            is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

        private static bool IsVisibleOutsideTheAssembly(MetadataReader metadata, TypeDefinition type) =>
            (type.Attributes & TypeAttributes.VisibilityMask) switch
            {
                TypeAttributes.Public => true,

                // A nested type is only reachable if every type enclosing it is too: its own
                // flags say nothing about whether the type declaring it escapes the assembly.
                // No type in the library reaches this branch today — MicroOpTable's
                // Cache<TVariant> is NestedPrivate and stops at the default case below — so
                // the first public or protected nested type anyone adds is its first live
                // exercise.
                TypeAttributes.NestedPublic or
                    TypeAttributes.NestedFamily or
                    TypeAttributes.NestedFamORAssem =>
                    IsVisibleOutsideTheAssembly(metadata, metadata.GetTypeDefinition(type.GetDeclaringType())),

                // NotPublic (which is also how the synthetic <Module> type reads),
                // NestedPrivate, NestedAssembly, NestedFamANDAssem.
                _ => false,
            };

        /// <remarks>
        /// Not private: <see cref="SignatureText"/> renders type names through this too, so
        /// a name means the same thing whether it arrived as a type row or as a signature
        /// operand. The enclosing class is itself private, so this widens nothing.
        /// </remarks>
        internal static string FullName(MetadataReader metadata, TypeDefinition type)
        {
            var name = metadata.GetString(type.Name);

            if (!type.IsNested)
            {
                var ns = metadata.GetString(type.Namespace);
                return ns.Length == 0 ? name : $"{ns}.{name}";
            }

            return $"{FullName(metadata, metadata.GetTypeDefinition(type.GetDeclaringType()))}+{name}";
        }
    }

    /// <summary>
    /// The generic parameter names in scope while a signature is decoded. A signature blob
    /// carries only indices — <c>!0</c> for the declaring type's, <c>!!0</c> for the
    /// method's — and the list this test pins is read by a human deciding whether a diff was
    /// deliberate, for whom <c>!0</c> is worse than useless.
    /// </summary>
    private readonly record struct GenericNames(
        ImmutableArray<string> OfType, ImmutableArray<string> OfMethod);

    /// <summary>
    /// Renders a signature blob as C#-shaped text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately literal. Custom modifiers render as <c>modreq(…)</c>/<c>modopt(…)</c>
    /// rather than being folded back into the syntax that produced them, because an earlier
    /// draft that did fold them silently erased the <c>init</c> on every
    /// <see cref="Instruction"/> setter — <c>init</c> is <c>modreq(IsExternalInit)</c> and
    /// nothing else — and would have let an <c>init</c> quietly become a <c>set</c>.
    /// </para>
    /// <para>
    /// Type names come out namespace-qualified with CLR arity suffixes, matching
    /// <see cref="ExpectedPublicTypes"/>, so the same name means the same thing in both lists.
    /// </para>
    /// </remarks>
    private sealed class SignatureText : ISignatureTypeProvider<string, GenericNames>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.UIntPtr => "nuint",

            // TypedReference, and whatever a future ECMA-335 adds. Named rather than thrown
            // on: an unrecognised primitive on the public surface should fail as a visible
            // diff in the pinned list, not as an exception that takes the assertion with it.
            _ => typeCode.ToString(),
        };

        public string GetTypeFromDefinition(
            MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            PackedAssemblies.FullName(reader, reader.GetTypeDefinition(handle));

        public string GetTypeFromReference(
            MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            var ns = reader.GetString(reference.Namespace);
            var name = reader.GetString(reference.Name);

            return ns.Length == 0 ? name : $"{ns}.{name}";
        }

        public string GetTypeFromSpecification(
            MetadataReader reader, GenericNames context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetArrayType(string elementType, ArrayShape shape) =>
            $"{elementType}[{new string(',', shape.Rank - 1)}]";

        public string GetByReferenceType(string elementType) => $"{elementType}&";

        public string GetPointerType(string elementType) => $"{elementType}*";

        public string GetPinnedType(string elementType) => $"{elementType} pinned";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            $"{genericType}<{string.Join(",", typeArguments)}>";

        public string GetGenericTypeParameter(GenericNames context, int index) =>
            index < context.OfType.Length ? context.OfType[index] : $"!{index}";

        public string GetGenericMethodParameter(GenericNames context, int index) =>
            index < context.OfMethod.Length ? context.OfMethod[index] : $"!!{index}";

        public string GetFunctionPointerType(MethodSignature<string> signature) =>
            $"delegate*<{string.Join(",", signature.ParameterTypes.Append(signature.ReturnType))}>";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
            $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
    }
}
