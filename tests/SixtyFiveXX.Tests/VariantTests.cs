using SixtyFiveXX;
using SixtyFiveXX.Variants;
using Xunit;

namespace SixtyFiveXX.Tests;

// xUnit's analyzer requires the test class itself to be public, but MicroOpTable,
// OpcodeInfo and Mos6502Variant are all internal — reachable here only via
// InternalsVisibleTo. None of these test methods put an internal type in a public
// signature (no parameters, void return), so CS0051 never comes up; it would if a
// helper method here took or returned MicroOp directly, and that helper would need to
// be declared internal instead.
public class VariantTests
{
    [Fact]
    public void Mos6502Variant_SuppliesTheSame256DescriptorsAsOpcodes6502()
    {
        OpcodeInfo[] expected = Opcodes6502.Table;
        OpcodeInfo[] actual = Mos6502Variant.OpcodeTable;

        Assert.Equal(256, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void For_Mos6502Variant_IsStructurallyIdenticalToTheLegacyMos6502Table()
    {
        MicroOpTable variantTable = MicroOpTable.For<Mos6502Variant>();
        MicroOpTable legacyTable = MicroOpTable.Mos6502;

        Assert.Equal(legacyTable.Info, variantTable.Info);
        Assert.Equal(legacyTable.Ops, variantTable.Ops);
        Assert.Equal(legacyTable.Entry, variantTable.Entry);
        Assert.Equal(legacyTable.IrqEntry, variantTable.IrqEntry);
        Assert.Equal(legacyTable.ResetEntry, variantTable.ResetEntry);
    }

    [Fact]
    public void For_IsCachedPerVariant()
    {
        // A static field on a generic type is per-constructed-type, so two calls for the
        // same TVariant must yield the same instance rather than rebuilding the table.
        Assert.Same(MicroOpTable.For<Mos6502Variant>(), MicroOpTable.For<Mos6502Variant>());
    }
}
