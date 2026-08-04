namespace SixtyFiveXX.Conformance;

/// <summary>
/// Downloads and caches VICE's CPU test programs from the project's Subversion repository
/// at <c>svn.code.sf.net/p/vice-emu</c>.
/// </summary>
/// <remarks>
/// <para>
/// These come from SourceForge rather than the <c>VICE-Team/svn-mirror</c> GitHub mirror
/// because the mirror carries only the <c>vice/</c> subtree — <c>testprogs/</c> is absent
/// from it entirely. The SVN repository serves file content over plain HTTPS, so no
/// Subversion client is needed.
/// </para>
/// <para>
/// Like the Klaus binaries these are GPL-licensed programs that are executed, never linked
/// or derived from, so their licence does not reach this project's source.
/// </para>
/// </remarks>
public static class ViceCache
{
    private const string BaseUrl = "https://svn.code.sf.net/p/vice-emu/code/testprogs";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>Where the test programs are cached.</summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("SIXTYFIVEXX_VICE_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".vice-cache");

    /// <summary>
    /// Loads a Commodore <c>.prg</c> into a fresh 64 KB image, downloading and caching it on
    /// first use. A <c>.prg</c> is a two-byte little-endian load address followed by the
    /// bytes to place there, so the file is not itself an image and cannot be used as one.
    /// </summary>
    /// <param name="path">Path below <c>testprogs</c>, for example <c>CPU/cpuport/test1.prg</c>.</param>
    /// <returns>
    /// The 64 KB image with the program placed at its load address, and that load address.
    /// Memory outside the program is <c>$FF</c> rather than <c>$00</c> so that a test reading
    /// a result byte the program never wrote sees a value it can recognise as absent.
    /// </returns>
    public static (byte[] Ram, ushort LoadAddress) LoadProgram(string path)
    {
        var file = Download(path);

        if (file.Length < 3)
            throw new InvalidOperationException(
                $"{path} is {file.Length} bytes; a .prg needs a two-byte load address and a payload.");

        var loadAddress = (ushort)(file[0] | (file[1] << 8));
        var payload = file.Length - 2;

        if (loadAddress + payload > 0x10000)
            throw new InvalidOperationException(
                $"{path} loads {payload} bytes at ${loadAddress:X4}, which runs past the end of memory.");

        var ram = new byte[0x10000];
        Array.Fill(ram, (byte)0xFF);
        Array.Copy(file, 2, ram, loadAddress, payload);

        return (ram, loadAddress);
    }

    private static byte[] Download(string path)
    {
        var cached = Path.GetFullPath(Path.Combine(Root, path));
        if (File.Exists(cached)) return File.ReadAllBytes(cached);

        var url = $"{BaseUrl}/{path}";
        try
        {
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);

            // Unique per writer — see HarteCache and KlausCache. The frameworks of a
            // multi-targeted `dotnet test` run concurrently against one cache directory,
            // and a fixed temp name makes them race for it.
            var temp = $"{cached}.{Environment.ProcessId}-{Environment.CurrentManagedThreadId}.partial";
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, cached, overwrite: true);
            }
            catch
            {
                if (File.Exists(temp)) File.Delete(temp);
                throw;
            }

            return bytes;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not fetch {url}. Either allow network access, or check out " +
                $"https://svn.code.sf.net/p/vice-emu/code/testprogs and point " +
                $"SIXTYFIVEXX_VICE_DIR at it.", ex);
        }
    }
}
