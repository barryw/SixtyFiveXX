namespace SixtyFiveXX;

/// <summary>What an instruction does with its effective address.</summary>
internal enum Access : byte
{
    /// <summary>No effective address is formed, or the sequence is hand-written.</summary>
    None,

    /// <summary>Reads the effective address. Indexed forms may skip the page-cross fixup.</summary>
    Read,

    /// <summary>Writes the effective address. Indexed forms always pay the fixup cycle.</summary>
    Write,

    /// <summary>Read, dummy write of the original value, then write the modified value.</summary>
    ReadModifyWrite,
}
