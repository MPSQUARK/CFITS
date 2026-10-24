using System.Globalization;

namespace CFITS;

/// <summary>Ones-complement checksum from FITS section 4.4.2.7.</summary>
public static class FitsChecksum
{
    private static readonly int[] ExcludedCharacters = [0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60];

    public static uint Sum(ReadOnlySpan<byte> bytes)
    {
        ulong high = 0;
        ulong low = 0;
        int aligned = bytes.Length - (bytes.Length % 4);
        for (int index = 0; index < aligned; index += 4)
        {
            high += (uint)((bytes[index] << 8) | bytes[index + 1]);
            low += (uint)((bytes[index + 2] << 8) | bytes[index + 3]);
        }

        if (aligned != bytes.Length)
        {
            Span<byte> tail = stackalloc byte[4];
            tail.Clear();
            bytes[aligned..].CopyTo(tail);
            high += (uint)((tail[0] << 8) | tail[1]);
            low += (uint)((tail[2] << 8) | tail[3]);
        }

        return Fold(high, low);
    }

    public static uint Add(uint left, uint right)
    {
        ulong high = (left >> 16) + (right >> 16);
        ulong low = (left & 0xFFFF) + (right & 0xFFFF);
        return Fold(high, low);
    }

    public static string Encode(uint sum)
    {
        uint value = 0xFFFFFFFF - sum;
        Span<char> assembled = stackalloc char[16];
        Span<int> encoded = stackalloc int[4];
        for (int byteIndex = 0; byteIndex < 4; byteIndex++)
        {
            int piece = (int)((value >> (24 - (8 * byteIndex))) & 0xFF);
            int quotient = (piece / 4) + 0x30;
            for (int index = 0; index < 4; index++)
            {
                encoded[index] = quotient;
            }

            encoded[0] += piece % 4;
            int pending = 1;
            while (pending != 0)
            {
                pending = 0;
                for (int excludedIndex = 0; excludedIndex < ExcludedCharacters.Length; excludedIndex++)
                {
                    for (int index = 0; index < 4; index += 2)
                    {
                        if (encoded[index] == ExcludedCharacters[excludedIndex] || encoded[index + 1] == ExcludedCharacters[excludedIndex])
                        {
                            encoded[index]++;
                            encoded[index + 1]--;
                            pending++;
                        }
                    }
                }
            }

            for (int index = 0; index < 4; index++)
            {
                assembled[(4 * index) + byteIndex] = (char)encoded[index];
            }
        }

        Span<char> characters = stackalloc char[16];
        for (int index = 0; index < characters.Length; index++)
        {
            characters[index] = assembled[(index + 15) % 16];
        }

        return new string(characters);
    }

    public static byte[] Stamp(byte[] hdu)
    {
        int end = FindEnd(hdu);
        int headerLength = (int)FitsBlock.Pad(end + FitsBlock.CardSize);
        int dataLength = hdu.Length - headerLength;
        if (dataLength < 0)
        {
            throw new FitsException("The header does not fit in the HDU.", new FitsLocation());
        }

        int datasumAt = FindKeyword(hdu, end, "DATASUM ");
        int checksumAt = FindKeyword(hdu, end, "CHECKSUM");
        if (datasumAt < 0 || checksumAt < 0)
        {
            hdu = InsertMissing(hdu, end, headerLength, dataLength, datasumAt < 0, checksumAt < 0);
            end = FindEnd(hdu);
            headerLength = (int)FitsBlock.Pad(end + FitsBlock.CardSize);
            datasumAt = FindKeyword(hdu, end, "DATASUM ");
            checksumAt = FindKeyword(hdu, end, "CHECKSUM");
        }

        uint dataSum = Sum(hdu.AsSpan(headerLength));
        WriteCard(hdu, datasumAt, "DATASUM", dataSum.ToString(CultureInfo.InvariantCulture), "data unit checksum");
        WriteCard(hdu, checksumAt, "CHECKSUM", "0000000000000000", "HDU checksum");
        uint total = Add(dataSum, Sum(hdu.AsSpan(0, headerLength)));
        string encoded = Encode(total);
        for (int index = 0; index < encoded.Length; index++)
        {
            hdu[checksumAt + 11 + index] = (byte)encoded[index];
        }

        return hdu;
    }

    internal static FitsIntegrity Inspect(ReadOnlySpan<byte> header, ReadOnlySpan<byte> data, FitsHeader cards, ReadOnlySpan<byte> uncompressed, bool checkUncompressed)
    {
        uint dataSum = Sum(data);
        uint total = Add(dataSum, Sum(header));
        bool? checksum = cards.TryGetCard("CHECKSUM", out _)
            ? total == 0 || total == 0xFFFFFFFF
            : null;
        bool? datasum = null;
        if (cards.TryGetCard("DATASUM", out var stored) && TryUnsigned(stored, out uint expected))
        {
            datasum = expected == dataSum;
        }

        bool? uncompressedSum = null;
        if (checkUncompressed && cards.TryGetCard("ZDATASUM", out var compressed) && TryUnsigned(compressed, out uint expectedUncompressed))
        {
            uncompressedSum = Sum(uncompressed) == expectedUncompressed;
        }

        return new FitsIntegrity(checksum, datasum, uncompressedSum, dataSum);
    }

    private static byte[] InsertMissing(byte[] hdu, int end, int headerLength, int dataLength, bool datasum, bool checksum)
    {
        int inserted = (datasum ? 1 : 0) + (checksum ? 1 : 0);
        int newHeaderLength = (int)FitsBlock.Pad(end + ((inserted + 1) * FitsBlock.CardSize));
        var rewritten = new byte[newHeaderLength + dataLength];
        hdu.AsSpan(0, end).CopyTo(rewritten);
        int cursor = end;
        if (datasum)
        {
            WriteCard(rewritten, cursor, "DATASUM", "0", "data unit checksum");
            cursor += FitsBlock.CardSize;
        }

        if (checksum)
        {
            WriteCard(rewritten, cursor, "CHECKSUM", "0000000000000000", "HDU checksum");
            cursor += FitsBlock.CardSize;
        }

        WriteEnd(rewritten.AsSpan(cursor, FitsBlock.CardSize));
        rewritten.AsSpan(cursor + FitsBlock.CardSize, newHeaderLength - cursor - FitsBlock.CardSize).Fill((byte)' ');
        hdu.AsSpan(headerLength, dataLength).CopyTo(rewritten.AsSpan(newHeaderLength));
        return rewritten;
    }

    private static void WriteCard(byte[] hdu, int offset, string keyword, string value, string comment)
    {
        byte[] image = CardImages.Format(new FitsCard(keyword, FitsValueKind.String, false, 0, 0, 0, value, comment, null));
        image.CopyTo(hdu.AsSpan(offset));
        if (keyword == "CHECKSUM" && (hdu[offset + 10] != (byte)'\'' || hdu[offset + 27] != (byte)'\''))
        {
            throw new FitsException("CHECKSUM must occupy columns 12 through 27.", new FitsLocation(Keyword: "CHECKSUM"));
        }
    }

    private static void WriteEnd(Span<byte> card)
    {
        card.Fill((byte)' ');
        card[0] = (byte)'E';
        card[1] = (byte)'N';
        card[2] = (byte)'D';
    }

    private static int FindEnd(byte[] hdu)
    {
        for (int offset = 0; offset + FitsBlock.CardSize <= hdu.Length; offset += FitsBlock.CardSize)
        {
            if (hdu[offset] == (byte)'E' && hdu[offset + 1] == (byte)'N' && hdu[offset + 2] == (byte)'D' && hdu[offset + 3] == (byte)' ')
            {
                return offset;
            }
        }

        throw new FitsException("The header ended before the END record.", new FitsLocation());
    }

    private static int FindKeyword(byte[] hdu, int end, string keyword)
    {
        for (int offset = 0; offset < end; offset += FitsBlock.CardSize)
        {
            bool match = true;
            for (int index = 0; index < keyword.Length; index++)
            {
                if (hdu[offset + index] != (byte)keyword[index])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return offset;
            }
        }

        return -1;
    }

    private static bool TryUnsigned(FitsCard card, out uint value)
    {
        if (card.Kind == FitsValueKind.Integer && card.Integer >= 0 && card.Integer <= uint.MaxValue)
        {
            value = (uint)card.Integer;
            return true;
        }

        if (card.Kind == FitsValueKind.String && uint.TryParse(card.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (card.Kind == FitsValueKind.Real && card.Real >= 0 && card.Real <= uint.MaxValue && card.Real == Math.Truncate(card.Real))
        {
            value = (uint)card.Real;
            return true;
        }

        value = 0;
        return false;
    }

    private static uint Fold(ulong high, ulong low)
    {
        while (high > 0xFFFF || low > 0xFFFF)
        {
            ulong highCarry = high >> 16;
            ulong lowCarry = low >> 16;
            high = (high & 0xFFFF) + lowCarry;
            low = (low & 0xFFFF) + highCarry;
        }

        return (uint)((high << 16) + low);
    }
}
