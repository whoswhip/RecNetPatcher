using System.Buffers.Binary;

namespace RecNetPatcher;

internal static class StandardMetadataPatcher
{
    public static bool TryPatch(ref byte[] data, byte[] oldBytes, byte[] newBytes, List<string> log)
    {
        uint tableOffset = ReadU32(data, 0x08);
        uint tableSize = ReadU32(data, 0x0C);
        uint dataOffset = ReadU32(data, 0x10);

        if (!IsValidStandardMetadata(data, tableOffset, tableSize, dataOffset))
            return false;

        int rowCount = (int)(tableSize / 8);
        bool patched = false;

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            int rowOffset = (int)tableOffset + rowIndex * 8;
            uint oldLength = ReadU32(data, rowOffset);
            uint oldRelativeOffset = ReadU32(data, rowOffset + 4);
            ulong oldAbsoluteOffset = (ulong)dataOffset + oldRelativeOffset;

            if (oldLength != oldBytes.Length)
                continue;

            if (oldAbsoluteOffset + oldLength > (ulong)data.Length)
                continue;

            if (!BytesEqual(data, (int)oldAbsoluteOffset, oldBytes))
                continue;

            PatchStringReference(
                ref data,
                rowOffset,
                dataOffset,
                oldAbsoluteOffset,
                oldLength,
                newBytes,
                log
            );

            patched = true;
        }

        return patched;
    }

    public static uint ReadU32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static bool IsValidStandardMetadata(
        byte[] data,
        uint tableOffset,
        uint tableSize,
        uint dataOffset
    )
    {
        return tableOffset >= 0x18
            && tableSize >= 8
            && tableSize % 8 == 0
            && (ulong)tableOffset + tableSize <= (ulong)data.Length
            && dataOffset < data.Length;
    }

    private static void PatchStringReference(
        ref byte[] data,
        int rowOffset,
        uint stringBlobBaseOffset,
        ulong oldAbsoluteOffset,
        uint oldLength,
        byte[] newBytes,
        List<string> log
    )
    {
        if (newBytes.Length <= oldLength)
        {
            Buffer.BlockCopy(newBytes, 0, data, (int)oldAbsoluteOffset, newBytes.Length);
            Array.Clear(
                data,
                (int)oldAbsoluteOffset + newBytes.Length,
                (int)oldLength - newBytes.Length
            );

            WriteU32(data, rowOffset, (uint)newBytes.Length);

            log.Add(
                $"standard row 0x{rowOffset:X}: in-place at 0x{oldAbsoluteOffset:X}, length {oldLength} -> {newBytes.Length}"
            );

            return;
        }

        int newAbsoluteOffset = data.Length;

        Array.Resize(ref data, data.Length + newBytes.Length);
        Buffer.BlockCopy(newBytes, 0, data, newAbsoluteOffset, newBytes.Length);

        uint newRelativeOffset = checked((uint)(newAbsoluteOffset - stringBlobBaseOffset));

        WriteU32(data, rowOffset, (uint)newBytes.Length);
        WriteU32(data, rowOffset + 4, newRelativeOffset);

        log.Add(
            $"standard row 0x{rowOffset:X}: append at 0x{newAbsoluteOffset:X}, rel 0x{newRelativeOffset:X}, length {newBytes.Length}"
        );
    }

    private static bool BytesEqual(byte[] data, int offset, byte[] expected)
    {
        if (offset < 0 || offset + expected.Length > data.Length)
            return false;

        return data.AsSpan(offset, expected.Length).SequenceEqual(expected);
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
    }
}
