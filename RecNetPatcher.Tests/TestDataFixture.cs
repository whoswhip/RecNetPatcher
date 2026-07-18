using System.IO.Compression;

namespace RecNetPatcher.Tests;

public sealed class TestDataFixture
{
    private static readonly Lock DecompressionLock = new();

    public TestDataFixture()
    {
        TestDataDirectory = Path.Combine(AppContext.BaseDirectory, "TestData");
        NormalMetadataPath = EnsureDecompressed("normal-global-metadata.dat.gz");
        CustomMetadataPath = EnsureDecompressed("custom-global-metadata.dat.gz");
        ResourcesPath = EnsureDecompressed("resources.assets.gz");
    }

    public string TestDataDirectory { get; }
    public string NormalMetadataPath { get; }
    public string CustomMetadataPath { get; }
    public string ResourcesPath { get; }

    public string EnsureDecompressed(string gzipFileName)
    {
        string compressedPath = Path.Combine(TestDataDirectory, gzipFileName);
        string decompressedPath = Path.Combine(
            TestDataDirectory,
            Path.GetFileNameWithoutExtension(gzipFileName)
        );

        lock (DecompressionLock)
        {
            if (File.Exists(decompressedPath))
                return decompressedPath;

            if (!File.Exists(compressedPath))
                throw new FileNotFoundException("Compressed test fixture was not found.", compressedPath);

            using FileStream source = File.OpenRead(compressedPath);
            using GZipStream gzip = new(source, CompressionMode.Decompress);
            using FileStream destination = new(decompressedPath, FileMode.CreateNew, FileAccess.Write);
            gzip.CopyTo(destination);
        }

        return decompressedPath;
    }
}
