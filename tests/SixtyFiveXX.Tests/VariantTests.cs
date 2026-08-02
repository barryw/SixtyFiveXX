using SixtyFiveXX;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

// xUnit's analyzer requires the test class itself to be public, but MicroOpTable and
// OpcodeInfo are internal — reachable here only via InternalsVisibleTo. None of these
// test methods put an internal type in a public signature (no parameters, void return),
// so CS0051 never comes up; it would if a helper method here took or returned MicroOp
// directly, and that helper would need to be declared internal instead.
public class VariantTests
{
    [Fact]
    public void For_Mos6502Variant_ResolvesToOpcodes6502Table()
    {
        OpcodeInfo[] expected = Opcodes6502.Table;
        OpcodeInfo[] actual = MicroOpTable.For<Mos6502Variant>().Info;

        Assert.Equal(256, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void For_ResolvesTheTableFromTVariantsVariant_NotSomethingHardcoded()
    {
        // Two variants that both resolve successfully, to different tables. This is the
        // direct form of the claim: resolution follows TVariant.Variant. It replaces an
        // earlier version that inferred the same thing from an unsupported variant throwing
        // — which held only while exactly one table existed, and silently stopped
        // discriminating the moment phase 4 wired up a second one.
        Assert.Same(Opcodes6502.Table, MicroOpTable.For<Mos6502Variant>().Info);
        Assert.Same(Opcodes65C02.Table, MicroOpTable.For<StubCmosVariant>().Info);
    }

    [Fact]
    public void For_ThrowsForAVariantWithNoTableYet()
    {
        // The 65816 is deferred indefinitely, so nothing maps it. The exception surfaces
        // wrapped: the mapping runs inside a generic static field initialiser
        // (Cache<TVariant>.Table), and the CLR wraps anything thrown there in
        // TypeInitializationException.
        var ex = Assert.Throws<TypeInitializationException>(
            () => MicroOpTable.For<StubUnsupportedVariant>());
        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Fact]
    public void Mos6502Variant_DeclaresItsCpuVariantValue()
    {
        // The public half of the variant contract. Phase 6's sim6502 adapter selects its
        // processor from a DSL at run time, so it needs the type -> enum direction pinned:
        // given Mos6502Variant, which CpuVariant is it. Asserting it here rather than
        // leaning on For_Mos6502Variant_ResolvesToOpcodes6502Table, which catches a wrong
        // value today only as a side effect of the internal table mapping having no entry
        // for any other variant — a shield that disappears the moment phase 4 wires one up.
        Assert.Equal(CpuVariant.Mos6502, Mos6502Variant.Variant);
    }

    [Fact]
    public void For_IsCachedPerVariant()
    {
        // A static field on a generic type is per-constructed-type, so two calls for the
        // same TVariant must yield the same instance rather than rebuilding the table.
        Assert.Same(MicroOpTable.For<Mos6502Variant>(), MicroOpTable.For<Mos6502Variant>());
    }

    /// <summary>
    /// A variant naming a real <see cref="CpuVariant"/> member that has no opcode table and
    /// is not scheduled to get one — the 65816 is out of scope for the variant-cores spec.
    /// Deliberately not a 65C02 or the 6510: those acquire tables in phases 4 and 5, which
    /// would quietly turn this test into a no-op.
    /// </summary>
    private readonly struct StubUnsupportedVariant : ICpuVariant
    {
        public static CpuVariant Variant => CpuVariant.W65C816;
    }

    /// <summary>
    /// A CMOS variant, used to prove table resolution follows <c>TVariant.Variant</c> by
    /// landing on a different table than the 6502 does. A test-local stub rather than a
    /// released variant struct, so this test does not depend on which 65C02 sub-variants
    /// happen to be public yet.
    /// </summary>
    private readonly struct StubCmosVariant : ICpuVariant
    {
        public static CpuVariant Variant => CpuVariant.Synertek65C02;
    }
}
