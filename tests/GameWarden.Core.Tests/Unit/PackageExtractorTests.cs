using System.Buffers.Binary;
using System.IO.Compression;
using GameWarden.Core.Mods;

namespace GameWarden.Core.Tests.Unit;

/// <summary>The size cap of a set build (docs/13 §4.5): by the headers first, then by the bytes actually written.</summary>
[Trait("Category", "Unit")]
public sealed class PackageExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gamewarden-tests", "extract-" + Guid.NewGuid().ToString("N")[..10]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void A_package_under_the_limit_is_unpacked_where_the_loader_places_it()
    {
        using var zip = Open(Zip(("manifest.json", 10), ("plugins/A.dll", 500)));

        var files = PackageExtractor.Extract(zip, "A-B-1.0.0", _root, entry => entry == "manifest.json" ? null : $"BepInEx/plugins/A-B/{Path.GetFileName(entry)}", maxBytes: 510);

        Assert.Equal(1, files);
        Assert.Equal(500, new FileInfo(Path.Combine(_root, "BepInEx", "plugins", "A-B", "A.dll")).Length);
    }

    [Fact]
    public void A_package_whose_headers_say_it_is_too_large_is_refused_before_anything_is_written()
    {
        using var zip = Open(Zip(("a.dll", 600), ("b.dll", 600)));

        var error = Assert.Throws<ModSyncException>(() => PackageExtractor.Extract(zip, "A-B-1.0.0", _root, entry => entry, maxBytes: 1000));

        Assert.StartsWith("A-B-1.0.0: unpacks to more than 1000 bytes (at least 1200)", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void A_zip_whose_headers_lie_unpacks_no_more_than_they_say()
    {
        // A zip bomb's trick: 100 000 bytes that the local and central headers call 10.
        var bytes = Zip(("bomb.bin", 100_000));
        PatchUncompressedSize(bytes, 10);
        using var zip = Open(bytes);
        Assert.Equal(10, Assert.Single(zip.Entries).Length);

        try
        {
            PackageExtractor.Extract(zip, "A-B-1.0.0", _root, entry => entry, maxBytes: 1000);
        }
        catch (Exception ex) when (ex is ModSyncException or InvalidDataException)
        {
            // Refused by the count, or by .NET for not matching its header: either way nothing past the cap.
        }

        var written = Path.Combine(_root, "bomb.bin");
        Assert.True(!File.Exists(written) || new FileInfo(written).Length <= 1000);
    }

    [Fact]
    public void An_entry_that_gives_more_bytes_than_its_header_says_is_refused_by_the_bytes_written()
    {
        PackageEntry[] entries =
        [
            new("plugins/A.dll", 10, () => new MemoryStream(new byte[100])),
            new("plugins/Bomb.dll", 10, () => new MemoryStream(new byte[200_000])),
        ];

        var error = Assert.Throws<ModSyncException>(() => PackageExtractor.Extract(entries, "A-B-1.0.0", _root, entry => entry, maxBytes: 1000));

        Assert.StartsWith("A-B-1.0.0: unpacks to more than 1000 bytes", error.Message, StringComparison.Ordinal);
        Assert.Equal(100, new FileInfo(Path.Combine(_root, "plugins", "A.dll")).Length);
        Assert.True(new FileInfo(Path.Combine(_root, "plugins", "Bomb.dll")).Length <= 1000);
    }

    private static ZipArchive Open(byte[] bytes) => new(new MemoryStream(bytes), ZipArchiveMode.Read);

    /// <summary>A zip of entries filled with zeros, which compress to almost nothing.</summary>
    private static byte[] Zip(params (string Name, int Size)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, size) in entries)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(new byte[size]);
            }
        }

        return memory.ToArray();
    }

    /// <summary>Writes <paramref name="size"/> as the uncompressed size in every local and central header.</summary>
    private static void PatchUncompressedSize(byte[] zip, uint size)
    {
        for (var offset = 0; offset + 4 <= zip.Length; offset++)
        {
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(offset));
            if (signature == 0x04034b50)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(offset + 22), size);
            }
            else if (signature == 0x02014b50)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(offset + 24), size);
            }
        }
    }
}
