using System.Text.Json;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Downloads and caches SingleStepTests vectors from
/// <c>https://github.com/SingleStepTests/65x02</c> (MIT).
/// </summary>
/// <remarks>
/// The vectors are roughly a gigabyte across all sets, so they are cached to a
/// gitignored directory rather than committed. Set <c>SIXTYFIVEXX_HARTE_DIR</c> to
/// point at an existing checkout to run without network access.
/// </remarks>
public static class HarteCache
{
    private const string BaseUrl = "https://raw.githubusercontent.com/SingleStepTests/65x02/main";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Where vectors are cached, or read from if the user supplied a checkout.</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("SIXTYFIVEXX_HARTE_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".harte-cache");

    /// <summary>
    /// Loads every vector for one opcode, downloading and caching it on first use. Returns
    /// an empty array for opcodes the set deliberately has no vectors for.
    /// </summary>
    /// <param name="set">The test set directory, for example <c>6502</c> or <c>wdc65c02</c>.</param>
    /// <param name="opcode">The opcode byte.</param>
    public static HarteCase[] Load(string set, byte opcode)
    {
        var relative = Path.Combine(set, "v1", $"{opcode:x2}.json");
        var path = Path.GetFullPath(Path.Combine(Root, relative));

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Download($"{BaseUrl}/{set}/v1/{opcode:x2}.json", path);
        }

        using var stream = File.OpenRead(path);

        // Upstream ships genuinely empty files for opcodes it has no vectors for — WDC's
        // WAI and STP, which halt and so cannot be expressed as a before-and-after pair.
        // That is a meaningful answer, not a corrupt download, so it is reported as "no
        // cases" rather than thrown. HarteTests requires any such opcode to be declared.
        if (stream.Length == 0) return [];

        return JsonSerializer.Deserialize<HarteCase[]>(stream, Json)
               ?? throw new InvalidOperationException($"{path} deserialized to null.");
    }

    private static void Download(string url, string destination)
    {
        try
        {
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            // Write via a temporary file so a cancelled run cannot leave a truncated
            // cache entry that later passes File.Exists.
            var temp = destination + ".partial";
            using (var file = File.Create(temp))
            {
                response.Content.CopyToAsync(file).GetAwaiter().GetResult();
            }
            File.Move(temp, destination, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not fetch {url}. Conformance vectors are required and are never " +
                $"committed. Either allow network access, or clone " +
                $"https://github.com/SingleStepTests/65x02 and point " +
                $"SIXTYFIVEXX_HARTE_DIR at it.", ex);
        }
    }
}
