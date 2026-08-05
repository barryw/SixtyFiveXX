using System.Text.Json;

namespace SixtyFiveXX.Conformance;

/// <summary>
/// Downloads and caches SingleStepTests vectors from
/// <c>https://github.com/SingleStepTests/65816</c> (MIT) — a different repository from the
/// <c>65x02</c> set <see cref="HarteCache"/> fetches, with a different vector shape (see
/// <see cref="Harte816Case"/>). Two files per opcode, <c>v1/{opcode:x2}.e.json</c> (emulation
/// mode) and <c>v1/{opcode:x2}.n.json</c> (native mode), 10,000 vectors each — research
/// document §2.3.
/// </summary>
/// <remarks>
/// A sibling to <see cref="HarteCache"/> rather than an edit of it — see research document
/// §2.3 for why the two sets do not share a loader. The download/cache/offline-override shape
/// is deliberately the same, down to the atomic-write-via-temp-file technique.
/// </remarks>
public static class Harte816Cache
{
    private const string BaseUrl = "https://raw.githubusercontent.com/SingleStepTests/65816/main";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Where vectors are cached, or read from if the user supplied a checkout.</summary>
    /// <remarks>
    /// The same environment variable as <see cref="HarteCache.Root"/>:
    /// <c>SIXTYFIVEXX_HARTE_DIR</c> points at one checkout directory that holds every set, and
    /// this one gets its own <c>65816</c> subdirectory below it — the same role
    /// <see cref="HarteCache.Load"/>'s <c>set</c> parameter plays for the 65x02 sets.
    /// </remarks>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("SIXTYFIVEXX_HARTE_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".harte-cache");

    /// <summary>
    /// Loads every vector for one opcode in one mode, downloading and caching it on first use.
    /// </summary>
    /// <param name="opcode">The opcode byte.</param>
    /// <param name="mode"><c>'e'</c> for emulation mode, <c>'n'</c> for native mode.</param>
    public static Harte816Case[] Load(byte opcode, char mode)
    {
        if (mode is not ('e' or 'n'))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Expected 'e' or 'n'.");

        var relative = Path.Combine("65816", "v1", $"{opcode:x2}.{mode}.json");
        var path = Path.GetFullPath(Path.Combine(Root, relative));

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Download($"{BaseUrl}/v1/{opcode:x2}.{mode}.json", path);
        }

        using var stream = File.OpenRead(path);

        if (stream.Length == 0) return [];

        return JsonSerializer.Deserialize<Harte816Case[]>(stream, Json)
               ?? throw new InvalidOperationException($"{path} deserialized to null.");
    }

    private static void Download(string url, string destination)
    {
        try
        {
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            // Write via a temporary file so a cancelled run cannot leave a truncated cache
            // entry that later passes File.Exists. The name is unique per writer, not a fixed
            // ".partial" — see HarteCache.Download for why: a multi-targeted `dotnet test`
            // runs its frameworks concurrently against one cache directory.
            var temp = $"{destination}.{Environment.ProcessId}-{Environment.CurrentManagedThreadId}.partial";
            try
            {
                using (var file = File.Create(temp))
                {
                    response.Content.CopyToAsync(file).GetAwaiter().GetResult();
                }
                File.Move(temp, destination, overwrite: true);
            }
            catch
            {
                if (File.Exists(temp)) File.Delete(temp);
                throw;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not fetch {url}. Conformance vectors are required and are never " +
                $"committed. Either allow network access, or clone " +
                $"https://github.com/SingleStepTests/65816 into a directory named '65816' " +
                $"and point SIXTYFIVEXX_HARTE_DIR at its parent — that checkout's v1/ sits " +
                $"at its own root, so SIXTYFIVEXX_HARTE_DIR must not point at the checkout " +
                $"itself.", ex);
        }
    }
}
