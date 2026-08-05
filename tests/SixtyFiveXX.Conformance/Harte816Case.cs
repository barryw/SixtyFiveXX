using System.Text.Json;
using System.Text.Json.Serialization;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// A processor state as recorded in a SingleStepTests <c>65816</c> vector.
/// </summary>
/// <remarks>
/// Unlike <see cref="HarteState"/>, <c>a</c>, <c>x</c>, <c>y</c>, <c>s</c> and <c>d</c> are
/// 16-bit, and the state carries the bank registers (<c>dbr</c>, <c>pbr</c>) and the
/// emulation-mode flag (<c>e</c>) research document §2.3 documents as new for this set.
/// <c>e</c> is deserialised as a plain byte, not <c>bool</c>: the JSON encodes it as the
/// number <c>0</c> or <c>1</c>, which <c>System.Text.Json</c> does not implicitly convert to
/// a boolean. <see cref="Ram"/> is an array of two-element <c>[address, value]</c> arrays,
/// with 24-bit addresses; only the bytes a vector actually cares about are listed.
/// </remarks>
public sealed record Harte816State(
    [property: JsonPropertyName("pc")] ushort Pc,
    [property: JsonPropertyName("s")] ushort S,
    [property: JsonPropertyName("a")] ushort A,
    [property: JsonPropertyName("x")] ushort X,
    [property: JsonPropertyName("y")] ushort Y,
    [property: JsonPropertyName("p")] byte P,
    [property: JsonPropertyName("dbr")] byte Dbr,
    [property: JsonPropertyName("pbr")] byte Pbr,
    [property: JsonPropertyName("d")] ushort D,
    [property: JsonPropertyName("e")] byte E,
    [property: JsonPropertyName("ram")] int[][] Ram);

/// <summary>One SingleStepTests <c>65816</c> vector: initial state, expected final state, and every bus cycle.</summary>
/// <remarks>
/// Each entry of <see cref="Cycles"/> is a three-element array of
/// <c>[address, value, pinstring]</c>. <c>value</c> is <c>null</c> on an internal cycle —
/// research document §2.3, quoting the vector set's own README verbatim — so it is kept as a
/// raw <see cref="JsonElement"/> like the rest of the row, rather than typed as <c>byte</c>,
/// which would throw deserializing every such cycle.
/// </remarks>
public sealed record Harte816Case(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("initial")] Harte816State Initial,
    [property: JsonPropertyName("final")] Harte816State Final,
    [property: JsonPropertyName("cycles")] JsonElement[][] Cycles);
