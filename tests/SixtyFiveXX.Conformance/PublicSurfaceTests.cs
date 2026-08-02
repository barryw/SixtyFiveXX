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
        "SixtyFiveXX.Flag",
        "SixtyFiveXX.FlatBus",
        "SixtyFiveXX.IBus",
        "SixtyFiveXX.ICpuVariant",
        "SixtyFiveXX.RefBus",
        "SixtyFiveXX.UndefinedOpcodeException",
        "SixtyFiveXX.Variants.Mos6502Variant",
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
        private readonly Dictionary<string, string[]> _publicTypesByFramework;

        public IEnumerable<string> Frameworks => _publicTypesByFramework.Keys.Order();

        private PackedAssemblies(Dictionary<string, string[]> publicTypesByFramework) =>
            _publicTypesByFramework = publicTypesByFramework;

        public string[] PublicTypesFor(string targetFramework) =>
            _publicTypesByFramework.TryGetValue(targetFramework, out var types)
                ? types
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

                var byFramework = new Dictionary<string, string[]>();

                foreach (var entry in package.Entries)
                {
                    // lib/<tfm>/SixtyFiveXX.dll — the only files a consumer compiles against.
                    var parts = entry.FullName.Split('/');
                    if (parts is not ["lib", var tfm, "SixtyFiveXX.dll"]) continue;

                    byFramework[tfm] = ReadPublicTypes(entry);
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

        private static string Pack(string workspace)
        {
            var project = Path.Combine(RepositoryRoot(), "src", "SixtyFiveXX", "SixtyFiveXX.csproj");

            var pack = Process.Start(new ProcessStartInfo("dotnet")
            {
                ArgumentList = { "pack", project, "-c", "Release", "--artifacts-path", workspace },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("Could not start 'dotnet pack'.");

            // Read both pipes before waiting: a pack that fills one of them would otherwise
            // block on a full buffer and this test would hang rather than fail.
            var stdout = pack.StandardOutput.ReadToEndAsync();
            var stderr = pack.StandardError.ReadToEndAsync();
            pack.WaitForExit();

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
            // The test binary sits at tests/<project>/bin/<config>/<tfm>/, but that depth is
            // not worth hardcoding — walk up to the solution instead, which also survives
            // the artifacts-path layout used in CI.
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "SixtyFiveXX.sln")))
                    return dir.FullName;

            throw new InvalidOperationException(
                $"No SixtyFiveXX.sln above {AppContext.BaseDirectory}.");
        }

        private static string[] ReadPublicTypes(ZipArchiveEntry entry)
        {
            // PEReader needs a seekable stream; a zip entry stream is forward-only.
            using var dll = new MemoryStream();
            using (var compressed = entry.Open()) compressed.CopyTo(dll);
            dll.Position = 0;

            using var pe = new PEReader(dll);
            var metadata = pe.GetMetadataReader();

            return metadata.TypeDefinitions
                .Select(metadata.GetTypeDefinition)
                .Where(type => IsVisibleOutsideTheAssembly(metadata, type))
                .Select(type => FullName(metadata, type))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsVisibleOutsideTheAssembly(MetadataReader metadata, TypeDefinition type) =>
            (type.Attributes & TypeAttributes.VisibilityMask) switch
            {
                TypeAttributes.Public => true,

                // A nested type is only reachable if every type enclosing it is too — which
                // is what keeps MicroOpTable's private nested Cache<TVariant> out of the set
                // even though its own flags say nothing about MicroOpTable being internal.
                TypeAttributes.NestedPublic or
                    TypeAttributes.NestedFamily or
                    TypeAttributes.NestedFamORAssem =>
                    IsVisibleOutsideTheAssembly(metadata, metadata.GetTypeDefinition(type.GetDeclaringType())),

                // NotPublic (which is also how the synthetic <Module> type reads),
                // NestedPrivate, NestedAssembly, NestedFamANDAssem.
                _ => false,
            };

        private static string FullName(MetadataReader metadata, TypeDefinition type)
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
}
