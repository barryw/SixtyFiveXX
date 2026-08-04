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
        // Every named CpuVariant now maps to a table — phase 7b wired up the last one,
        // W65C816 — so this proves the catch-all instead, with an enum value outside the
        // named set. The exception surfaces wrapped: the mapping runs inside a generic
        // static field initialiser (Cache<TVariant>.Table), and the CLR wraps anything
        // thrown there in TypeInitializationException.
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
    /// A variant naming an enum value outside <see cref="CpuVariant"/>'s named members. Every
    /// named member now has a table — phase 7b wired up the last of them, W65C816 — so no
    /// real <see cref="CpuVariant"/> value is left to prove the catch-all with. C# enums are
    /// not closed to arbitrary underlying values, so an out-of-range cast still reaches it.
    /// </summary>
    private readonly struct StubUnsupportedVariant : ICpuVariant
    {
        public static CpuVariant Variant => (CpuVariant)99;
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
