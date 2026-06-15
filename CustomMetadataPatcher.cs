using System.Buffers.Binary;

namespace RecNetPatcher;

internal static class CustomMetadataPatcher
{
    public static bool TryPatch(ref byte[] data, byte[] oldBytes, byte[] newBytes, List<string> log)
    {
        foreach (int oldUrlAbsoluteOffset in FindAll(data, oldBytes))
        {
            foreach (
                CustomStringReference stringReference in FindPossibleStringReferences(
                    data,
                    oldUrlAbsoluteOffset,
                    oldBytes
                )
            )
            {
                PatchStringReference(ref data, stringReference, newBytes, log);

                log.Add($"custom old offset: 0x{oldUrlAbsoluteOffset:X}");
                log.Add($"custom blob base:  0x{stringReference.BlobBaseOffset:X}");
                log.Add($"custom first row:  0x{stringReference.Rows[0].TableOffset:X}");
                log.Add($"custom rows:       {stringReference.Rows.Count}");

                return true;
            }
        }

        return false;
    }

    private static IEnumerable<CustomStringReference> FindPossibleStringReferences(
        byte[] data,
        int oldUrlAbsoluteOffset,
        byte[] expectedUrl
    )
    {
        for (
            int candidateRowOffset = 0;
            candidateRowOffset <= data.Length - CustomStringRow.Size;
            candidateRowOffset += 4
        )
        {
            if (
                !TryReadStringRow(
                    data,
                    blobBaseOffset: 0,
                    tableOffset: candidateRowOffset,
                    validatePrintable: false,
                    out CustomStringRow firstRow
                )
            )
            {
                continue;
            }

            int inferredBlobBaseOffset = oldUrlAbsoluteOffset - firstRow.BlobRelativeOffset;

            if (!IsValidBlobBase(inferredBlobBaseOffset, data.Length))
                continue;

            if (
                !TryReadStringReferenceChain(
                    data,
                    inferredBlobBaseOffset,
                    candidateRowOffset,
                    expectedUrl,
                    out List<CustomStringRow> rows
                )
            )
            {
                continue;
            }

            yield return new CustomStringReference(
                oldUrlAbsoluteOffset,
                inferredBlobBaseOffset,
                rows
            );
        }
    }

    private static bool TryReadStringReferenceChain(
        byte[] data,
        int blobBaseOffset,
        int firstRowTableOffset,
        byte[] expectedBytes,
        out List<CustomStringRow> rows
    )
    {
        rows = [];

        int consumed = 0;
        int expectedBlobRelativeOffset = -1;
        int currentRowTableOffset = firstRowTableOffset;

        while (consumed < expectedBytes.Length)
        {
            if (
                !TryReadStringRow(
                    data,
                    blobBaseOffset,
                    currentRowTableOffset,
                    validatePrintable: true,
                    out CustomStringRow row
                )
            )
            {
                return false;
            }

            expectedBlobRelativeOffset =
                expectedBlobRelativeOffset < 0
                    ? row.BlobRelativeOffset
                    : expectedBlobRelativeOffset;

            if (row.BlobRelativeOffset != expectedBlobRelativeOffset)
                return false;

            if (consumed + row.ByteLength > expectedBytes.Length)
                return false;

            int absoluteSliceOffset = blobBaseOffset + row.BlobRelativeOffset;
            ReadOnlySpan<byte> expectedSlice = expectedBytes.AsSpan(consumed, row.ByteLength);

            if (!BytesEqual(data, absoluteSliceOffset, expectedSlice))
                return false;

            rows.Add(row);

            consumed += row.ByteLength;
            expectedBlobRelativeOffset += row.ByteLength;
            currentRowTableOffset += CustomStringRow.Size;
        }

        return consumed == expectedBytes.Length;
    }

    private static void PatchStringReference(
        ref byte[] data,
        CustomStringReference stringReference,
        byte[] newBytes,
        List<string> log
    )
    {
        int newAbsoluteOffset = data.Length;

        Array.Resize(ref data, data.Length + newBytes.Length);
        Buffer.BlockCopy(newBytes, 0, data, newAbsoluteOffset, newBytes.Length);

        uint newBlobRelativeOffset = checked(
            (uint)(newAbsoluteOffset - stringReference.BlobBaseOffset)
        );

        int consumed = 0;
        int rowsRemaining = stringReference.Rows.Count;

        foreach (CustomStringRow row in stringReference.Rows)
        {
            int newSliceLength = GetSliceLength(newBytes.Length, consumed, rowsRemaining);
            uint newSliceRelativeOffset = newBlobRelativeOffset + (uint)consumed;

            WriteU32(data, row.TableOffset + 4, newSliceRelativeOffset);
            WriteU32(data, row.TableOffset + 8, (uint)newSliceLength);

            log.Add(
                $"custom row 0x{row.TableOffset:X}: rel 0x{row.BlobRelativeOffset:X}/len {row.ByteLength} -> rel 0x{newSliceRelativeOffset:X}/len {newSliceLength}"
            );

            consumed += newSliceLength;
            rowsRemaining--;
        }

        log.Add($"custom append at:  0x{newAbsoluteOffset:X}");
        log.Add($"custom new rel:    0x{newBlobRelativeOffset:X}");
    }

    private static int GetSliceLength(int totalLength, int consumed, int rowsRemaining)
    {
        int bytesRemaining = totalLength - consumed;

        if (bytesRemaining <= 0)
            return 0;

        if (totalLength < rowsRemaining)
            return consumed == 0 ? totalLength : 0;

        return (int)Math.Ceiling(bytesRemaining / (double)rowsRemaining);
    }

    private static bool TryReadStringRow(
        byte[] data,
        int blobBaseOffset,
        int tableOffset,
        bool validatePrintable,
        out CustomStringRow row
    )
    {
        row = default;

        if (tableOffset < 0 || tableOffset + CustomStringRow.Size > data.Length)
            return false;

        uint token = ReadU32(data, tableOffset);
        uint blobRelativeOffset = ReadU32(data, tableOffset + 4);
        uint byteLength = ReadU32(data, tableOffset + 8);

        if (!IsTokenLike(token))
            return false;

        if (byteLength == 0 || byteLength > 256 || blobRelativeOffset > int.MaxValue)
            return false;

        ulong absoluteSliceOffset = (ulong)blobBaseOffset + blobRelativeOffset;

        if (absoluteSliceOffset + byteLength > (ulong)data.Length)
            return false;

        if (validatePrintable && !IsPrintable(data, (int)absoluteSliceOffset, (int)byteLength))
            return false;

        row = new CustomStringRow(tableOffset, token, (int)blobRelativeOffset, (int)byteLength);

        return true;
    }

    private static bool IsValidBlobBase(int blobBaseOffset, int fileLength)
    {
        return blobBaseOffset > 0 && blobBaseOffset < fileLength;
    }

    private static List<int> FindAll(byte[] data, byte[] needle)
    {
        List<int> hits = [];

        for (int offset = 0; offset <= data.Length - needle.Length; offset++)
        {
            if (BytesEqual(data, offset, needle))
                hits.Add(offset);
        }

        return hits;
    }

    private static bool IsTokenLike(uint value)
    {
        uint high = value >> 24;

        return high is 0x02 or 0x04 or 0x06 or 0x08 or 0x0A or 0x17 or 0x70;
    }

    private static bool IsPrintable(byte[] data, int offset, int length)
    {
        for (int i = 0; i < length; i++)
        {
            byte value = data[offset + i];

            if (value < 0x20 || value > 0x7E)
                return false;
        }

        return true;
    }

    private static bool BytesEqual(byte[] data, int offset, byte[] expected)
    {
        return BytesEqual(data, offset, expected.AsSpan());
    }

    private static bool BytesEqual(byte[] data, int offset, ReadOnlySpan<byte> expected)
    {
        if (offset < 0 || offset + expected.Length > data.Length)
            return false;

        return data.AsSpan(offset, expected.Length).SequenceEqual(expected);
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
    }

    private readonly record struct CustomStringReference(
        int OriginalStringAbsoluteOffset,
        int BlobBaseOffset,
        IReadOnlyList<CustomStringRow> Rows
    );

    private readonly record struct CustomStringRow(
        int TableOffset,
        uint Token,
        int BlobRelativeOffset,
        int ByteLength
    )
    {
        public const int Size = 12;
    }
}
