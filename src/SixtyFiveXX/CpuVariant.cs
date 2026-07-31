namespace SixtyFiveXX;

/// <summary>Members of the 65xx family that a core can be configured as.</summary>
public enum CpuVariant
{
    /// <summary>MOS 6502. NMOS baseline, including undocumented opcodes.</summary>
    Mos6502 = 0,

    /// <summary>MOS 6510. A 6502 with on-chip I/O port registers at $00 and $01.</summary>
    Mos6510 = 1,

    /// <summary>WDC 65C02. CMOS, with WAI and STP.</summary>
    Wdc65C02 = 2,

    /// <summary>Rockwell 65C02. CMOS, with RMB/SMB/BBR/BBS but no WAI or STP.</summary>
    Rockwell65C02 = 3,

    /// <summary>Synertek 65C02. CMOS base instruction set only.</summary>
    Synertek65C02 = 4,

    /// <summary>WDC 65C816. 16-bit registers, 24-bit addressing, emulation and native modes.</summary>
    W65C816 = 5,
}
