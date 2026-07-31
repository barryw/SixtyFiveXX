using System.Text.Json;
using System.Text.Json.Serialization;

namespace SixtyFiveXX.Conformance;

/// <summary>A processor state as recorded in a SingleStepTests vector.</summary>
/// <remarks>
/// <c>ram</c> is an array of two-element <c>[address, value]</c> arrays. Only the bytes
/// a vector actually cares about are listed; everything else is unspecified.
/// </remarks>
public sealed record HarteState(
    [property: JsonPropertyName("pc")] ushort Pc,
    [property: JsonPropertyName("s")] byte S,
    [property: JsonPropertyName("a")] byte A,
    [property: JsonPropertyName("x")] byte X,
    [property: JsonPropertyName("y")] byte Y,
    [property: JsonPropertyName("p")] byte P,
    [property: JsonPropertyName("ram")] int[][] Ram);

/// <summary>One SingleStepTests vector: initial state, expected final state, and every bus cycle.</summary>
/// <remarks>
/// Each entry of <see cref="Cycles"/> is a three-element array of
/// <c>[address, value, "read" | "write"]</c>, which is mixed-type and so is kept as raw
/// <see cref="JsonElement"/> values.
/// </remarks>
public sealed record HarteCase(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("initial")] HarteState Initial,
    [property: JsonPropertyName("final")] HarteState Final,
    [property: JsonPropertyName("cycles")] JsonElement[][] Cycles);
