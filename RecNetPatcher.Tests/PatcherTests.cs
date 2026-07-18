using System.Text;
using RecNetPatcher.Core;

namespace RecNetPatcher.Tests;

public sealed class PatcherTests(TestDataFixture testData) : IClassFixture<TestDataFixture>
{
    [Fact]
    public void PatchMetadata_PatchesCustomFile()
    {
        string inputPath = testData.CustomMetadataPath;
        string outputPath = CreateOutputPath(Path.GetExtension(inputPath));
        const string replacementUrl = "https://example.test";

        try
        {
            IReadOnlyList<string> log = Patcher.PatchMetadata(
                inputPath,
                outputPath,
                replacementUrl
            );

            Assert.NotEmpty(log);
            Assert.True(File.Exists(outputPath));
            AssertFileContains(outputPath, replacementUrl);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void PatchMetadata_RejectsMissingUrl()
    {
        string outputPath = CreateOutputPath(Path.GetExtension(testData.NormalMetadataPath));

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                Patcher.PatchMetadata(
                    testData.NormalMetadataPath,
                    outputPath,
                    "https://example.test"
                )
            );

            Assert.Contains("Could not find a patchable", exception.Message);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
    [Fact]
    public void PatchPhoton_PatchesIds()
    {
        string outputPath = CreateOutputPath(Path.GetExtension(testData.ResourcesPath));
        string[] replacements =
        [
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
        ];

        try
        {
            IReadOnlyList<string> log = Patcher.PatchPhoton(
                testData.ResourcesPath,
                outputPath,
                replacements
            );

            Assert.NotEmpty(log);
            Assert.True(File.Exists(outputPath));
            AssertFileContains(outputPath, replacements[0]);
            AssertFileContains(outputPath, replacements[1]);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void Decompress_ReusesExistingFile()
    {
        DateTime lastWriteTime = File.GetLastWriteTimeUtc(testData.NormalMetadataPath);

        string secondPath = testData.EnsureDecompressed("normal-global-metadata.dat.gz");

        Assert.Equal(testData.NormalMetadataPath, secondPath);
        Assert.Equal(lastWriteTime, File.GetLastWriteTimeUtc(secondPath));
    }

    private static string CreateOutputPath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"RecNetPatcher-{Guid.NewGuid():N}{extension}");
    }

    private static void AssertFileContains(string path, string expected)
    {
        byte[] fileBytes = File.ReadAllBytes(path);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);

        Assert.True(
            fileBytes.AsSpan().IndexOf(expectedBytes) >= 0,
            $"Expected {Path.GetFileName(path)} to contain '{expected}'."
        );
    }
}
