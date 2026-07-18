using System.Text;

namespace RecNetPatcher.Core;

public static class Patcher
{
    public static IReadOnlyList<string> PatchMetadata(
        string inputPath,
        string outputPath,
        string replacementUrl
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        byte[] data = ReadAllBytesShared(inputPath);
        byte[] oldBytes = Encoding.UTF8.GetBytes(PatcherConstants.MetadataUrl);
        byte[] newBytes = Encoding.UTF8.GetBytes(replacementUrl.Trim());

        if (newBytes.Length == 0)
            throw new InvalidOperationException("Replacement URL cannot be empty.");

        List<string> log = [];
        bool isStandardMetadata =
            StandardMetadataPatcher.ReadU32(data, 0) == PatcherConstants.StandardMetadataMagic;
        bool patched = isStandardMetadata
            ? StandardMetadataPatcher.TryPatch(ref data, oldBytes, newBytes, log)
            : CustomMetadataPatcher.TryPatch(ref data, oldBytes, newBytes, log);

        if (!patched)
            throw new InvalidOperationException(
                $"Could not find a patchable {PatcherConstants.MetadataUrl} entry."
            );

        WriteAllBytes(outputPath, data);
        return log;
    }

    public static IReadOnlyList<string> PatchDll(
        string inputPath,
        string outputPath,
        string replacementUrl
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        List<string> log = [];
        bool patched = DllPatcher.TryPatch(inputPath, outputPath, replacementUrl.Trim(), log);

        if (!patched)
            throw new InvalidOperationException(
                "Could not find a patchable ns.rec.net or recroom.againstgrav.com string."
            );

        return log;
    }

    public static IReadOnlyList<string> PatchPhoton(
        string inputPath,
        string outputPath,
        IReadOnlyList<string> replacementIds,
        IReadOnlyList<string>? originalIds = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(replacementIds);

        string[] replacements = [.. replacementIds.Select(id => id.Trim())];
        string[] originals = originalIds is null
            ? PatcherConstants.RecRoomPhotonIds
            : [.. originalIds.Select(id => id.Trim())];

        if (replacements.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("All replacement Photon IDs are required.");

        byte[] data = ReadAllBytesShared(inputPath);
        List<string> log = [];
        bool patched = PhotonPatcher.TryPatch(ref data, originals, replacements, log);

        if (!patched)
            throw new InvalidOperationException("Could not find patchable Photon IDs.");

        WriteAllBytes(outputPath, data);
        return log;
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );

        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }

    private static void WriteAllBytes(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, data);
    }
}
