using System.Text;

namespace RecNetPatcher.Core;

internal static class PhotonPatcher
{
    private const int MaxIds = 2;

    public static bool TryPatch(
        ref byte[] data,
        string[] photonIds,
        string[] replacementIds,
        List<string> log
    )
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(photonIds);
        ArgumentNullException.ThrowIfNull(replacementIds);
        ArgumentNullException.ThrowIfNull(log);

        if (photonIds.Length > MaxIds)
            throw new ArgumentOutOfRangeException(
                nameof(photonIds),
                $"A maximum of {MaxIds} photon IDs is supported."
            );

        if (replacementIds.Length > MaxIds)
            throw new ArgumentOutOfRangeException(
                nameof(replacementIds),
                $"A maximum of {MaxIds} replacement IDs is supported."
            );

        if (photonIds.Length != replacementIds.Length)
            throw new ArgumentException(
                $"photonIds ({photonIds.Length}) and replacementIds ({replacementIds.Length}) must have the same number of elements."
            );

        bool patched = false;

        for (int i = 0; i < photonIds.Length; i++)
        {
            string id = photonIds[i];
            string replacement = replacementIds[i];

            byte[] idBytes = Encoding.UTF8.GetBytes(id);
            byte[] replacementBytes = Encoding.UTF8.GetBytes(replacement);

            if (idBytes.Length != replacementBytes.Length)
                throw new InvalidOperationException(
                    $"replacement must be the same length: old={idBytes.Length}, new={replacementBytes.Length}"
                );

            bool success = PatchId(data, idBytes, replacementBytes, replacement, log);
            if (success)
                Console.WriteLine($"patched: {id} -> {replacement}");

            patched |= success;
        }

        return patched;
    }

    private static bool PatchId(
        byte[] data,
        byte[] idBytes,
        byte[] replacementBytes,
        string replacement,
        List<string> log
    )
    {
        bool patched = false;

        Span<byte> dataSpan = data;
        ReadOnlySpan<byte> idSpan = idBytes;
        ReadOnlySpan<byte> replacementSpan = replacementBytes;

        int searchStart = 0;

        while (searchStart <= data.Length - idBytes.Length)
        {
            int relativeIndex = dataSpan[searchStart..].IndexOf(idSpan);

            if (relativeIndex < 0)
                break;

            int offset = searchStart + relativeIndex;

            replacementSpan.CopyTo(dataSpan.Slice(offset, replacementBytes.Length));

            log.Add($"{replacement}: in-place at 0x{offset:X}, length {replacementBytes.Length}");

            patched = true;

            searchStart = offset + idBytes.Length;
        }

        return patched;
    }
}
