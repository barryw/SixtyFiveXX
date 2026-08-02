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
        // StubUnsupportedVariant reports CpuVariant.Wdc65C02, which the internal
        // variant-to-table mapping has no entry for yet (only Mos6502 does today). If
        // For<TVariant> ignored TVariant.Variant — e.g. always resolved the Mos6502 table
        // regardless of the type argument — this call would silently succeed instead of
        // throwing, proving nothing. It throws instead, so resolution genuinely depends on
        // TVariant.Variant rather than something hardcoded to the 6502. The exception
        // surfaces wrapped: the mapping runs inside a generic static field initialiser
        // (Cache<TVariant>.Table), and the CLR wraps any exception thrown there in
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
    /// A variant that names a real <see cref="CpuVariant"/> member with no opcode table
    /// wired up yet, used only to prove <see cref="MicroOpTable.For{TVariant}"/> resolves
    /// its table by consulting <c>TVariant.Variant</c> rather than something hardcoded to
    /// the 6502.
    /// </summary>
    private readonly struct StubUnsupportedVariant : ICpuVariant
    {
        public static CpuVariant Variant => CpuVariant.Wdc65C02;
    }
}
