namespace SixtyFiveXX.Conformance;

/// <summary>
/// Downloads and caches Klaus Dormann's prebuilt 6502 test binaries from
/// <c>github.com/Klaus2m5/6502_65C02_functional_tests</c>.
/// </summary>
/// <remarks>
/// The binaries are GPL-licensed test programs. They are executed, never linked or
/// derived from, so their licence does not reach this project's source. They are
/// fetched rather than committed for the same reason as the Harte vectors.
/// </remarks>
public static class KlausCache
{
    private const string BaseUrl =
        "https://raw.githubusercontent.com/Klaus2m5/6502_65C02_functional_tests/master/bin_files";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>Where the binaries are cached.</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("SIXTYFIVEXX_KLAUS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".klaus-cache");

    /// <summary>Loads a 64 KB test image, downloading and caching it on first use.</summary>
    /// <param name="name">File name, for example <c>6502_functional_test.bin</c>.</param>
    public static byte[] Load(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Root, name));

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Download($"{BaseUrl}/{name}", path);
        }

        var image = File.ReadAllBytes(path);
        if (image.Length != 0x10000)
            throw new InvalidOperationException($"{name} is {image.Length} bytes; expected 65536.");

        return image;
    }

    private static void Download(string url, string destination)
    {
        try
        {
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

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
                $"Could not fetch {url}. Either allow network access, or clone " +
                $"https://github.com/Klaus2m5/6502_65C02_functional_tests and point " +
                $"SIXTYFIVEXX_KLAUS_DIR at its bin_files directory.", ex);
        }
    }
}
