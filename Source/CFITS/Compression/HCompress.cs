namespace CFITS;

/// <summary>
/// HCOMPRESS_1 tile decoder. The bitstream, quadtree, and inverse H-transform
/// follow the tiled-image convention used for FITS (White 1992).
/// </summary>
internal static class HCompress
{
    public static int[] Decompress(ReadOnlySpan<byte> source, int smooth)
    {
        var cursor = new Cursor(source.ToArray());
        if (cursor.Take() != 0xDD || cursor.Take() != 0x99)
        {
            throw new FitsException("An H-compress tile does not start with the expected marker.", new FitsLocation());
        }

        int width = cursor.Int32();
        int height = cursor.Int32();
        int scale = cursor.Int32();
        long sum = cursor.Int64();
        int plane0 = cursor.Take();
        int plane1 = cursor.Take();
        int plane2 = cursor.Take();
        if (width <= 0 || height <= 0 || width > int.MaxValue / height)
        {
            throw new FitsException("An H-compress tile has an invalid shape.", new FitsLocation());
        }

        var pixels = new int[width * height];
        int slowHalf = (width + 1) / 2;
        int fastHalf = (height + 1) / 2;
        DecodeQuadrant(cursor, pixels, 0, height, slowHalf, fastHalf, plane0);
        DecodeQuadrant(cursor, pixels, fastHalf, height, slowHalf, height / 2, plane1);
        DecodeQuadrant(cursor, pixels, height * slowHalf, height, width / 2, fastHalf, plane1);
        DecodeQuadrant(cursor, pixels, (height * slowHalf) + fastHalf, height, width / 2, height / 2, plane2);
        if (cursor.Nybble() != 0)
        {
            throw new FitsException("An H-compress tile ended before its bit planes were complete.", new FitsLocation());
        }

        cursor.Align();
        for (int index = 0; index < pixels.Length; index++)
        {
            if (pixels[index] != 0 && cursor.Bit() != 0)
            {
                pixels[index] = -pixels[index];
            }
        }

        pixels[0] = unchecked((int)sum);
        if (scale > 1)
        {
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] *= scale;
            }
        }

        Inverse(pixels, width, height, smooth, scale);
        return pixels;
    }

    private static void DecodeQuadrant(Cursor cursor, int[] pixels, int offset, int stride, int rows, int columns, int planes)
    {
        if (rows <= 0 || columns <= 0 || planes <= 0)
        {
            return;
        }

        int longest = Math.Max(rows, columns);
        int depth = (int)((Math.Log((float)longest) / Math.Log(2.0)) + 0.5);
        if (longest > (1 << depth))
        {
            depth++;
        }

        var codes = new byte[((rows + 1) / 2) * ((columns + 1) / 2)];
        for (int bit = planes - 1; bit >= 0; bit--)
        {
            int format = cursor.Nybble();
            if (format == 0)
            {
                for (int index = 0; index < codes.Length; index++)
                {
                    codes[index] = (byte)cursor.Nybble();
                }
            }
            else if (format != 0xF)
            {
                throw new FitsException("An H-compress bit plane has an unknown format code.", new FitsLocation());
            }
            else
            {
                Array.Clear(codes);
                codes[0] = (byte)cursor.Huffman();
                int rowCount = 1;
                int columnCount = 1;
                int rowRemainder = rows;
                int columnRemainder = columns;
                int step = 1 << depth;
                for (int level = 1; level < depth; level++)
                {
                    step >>= 1;
                    rowCount <<= 1;
                    columnCount <<= 1;
                    if (rowRemainder <= step)
                    {
                        rowCount--;
                    }
                    else
                    {
                        rowRemainder -= step;
                    }

                    if (columnRemainder <= step)
                    {
                        columnCount--;
                    }
                    else
                    {
                        columnRemainder -= step;
                    }

                    Expand(cursor, codes, rowCount, columnCount);
                }
            }

            InsertPlane(codes, rows, columns, pixels, offset, stride, bit);
        }
    }

    private static void Expand(Cursor cursor, byte[] codes, int rows, int columns)
    {
        var expanded = new byte[rows * columns];
        Scatter(codes, rows, columns, expanded);
        for (int index = expanded.Length - 1; index >= 0; index--)
        {
            if (expanded[index] != 0)
            {
                expanded[index] = (byte)cursor.Huffman();
            }
        }

        expanded.CopyTo(codes, 0);
    }

    private static void Scatter(byte[] source, int rows, int columns, byte[] destination)
    {
        int halfRows = (rows + 1) / 2;
        int halfColumns = (columns + 1) / 2;
        int sourceIndex = (halfColumns * (halfRows - 1)) + halfColumns - 1;
        for (int row = halfRows - 1; row >= 0; row--)
        {
            int destinationIndex = 2 * ((columns * row) + halfColumns - 1);
            for (int column = halfColumns - 1; column >= 0; column--)
            {
                destination[destinationIndex] = source[sourceIndex];
                sourceIndex--;
                destinationIndex -= 2;
            }
        }

        int rowIndex = 0;
        for (; rowIndex < rows - 1; rowIndex += 2)
        {
            int even = columns * rowIndex;
            int odd = even + columns;
            int column = 0;
            for (; column < columns - 1; column += 2)
            {
                int code = destination[even];
                destination[odd + 1] = (byte)(code & 1);
                destination[odd] = (byte)((code >> 1) & 1);
                destination[even + 1] = (byte)((code >> 2) & 1);
                destination[even] = (byte)((code >> 3) & 1);
                even += 2;
                odd += 2;
            }

            if (column < columns)
            {
                int code = destination[even];
                destination[odd] = (byte)((code >> 1) & 1);
                destination[even] = (byte)((code >> 3) & 1);
            }
        }

        if (rowIndex >= rows)
        {
            return;
        }

        int last = columns * rowIndex;
        int lastColumn = 0;
        for (; lastColumn < columns - 1; lastColumn += 2)
        {
            int code = destination[last];
            destination[last + 1] = (byte)((code >> 2) & 1);
            destination[last] = (byte)((code >> 3) & 1);
            last += 2;
        }

        if (lastColumn < columns)
        {
            destination[last] = (byte)((destination[last] >> 3) & 1);
        }
    }

    private static void InsertPlane(byte[] codes, int rows, int columns, int[] pixels, int offset, int stride, int bit)
    {
        int plane = 1 << bit;
        int codeIndex = 0;
        int row = 0;
        for (; row < rows - 1; row += 2)
        {
            int even = offset + (stride * row);
            int column = 0;
            for (; column < columns - 1; column += 2)
            {
                int code = codes[codeIndex++];
                Or(pixels, even + stride + 1, plane, code & 1);
                Or(pixels, even + stride, plane, (code >> 1) & 1);
                Or(pixels, even + 1, plane, (code >> 2) & 1);
                Or(pixels, even, plane, (code >> 3) & 1);
                even += 2;
            }

            if (column < columns)
            {
                int code = codes[codeIndex++];
                Or(pixels, even + stride, plane, (code >> 1) & 1);
                Or(pixels, even, plane, (code >> 3) & 1);
            }
        }

        if (row >= rows)
        {
            return;
        }

        int last = offset + (stride * row);
        int lastColumn = 0;
        for (; lastColumn < columns - 1; lastColumn += 2)
        {
            int code = codes[codeIndex++];
            Or(pixels, last + 1, plane, (code >> 2) & 1);
            Or(pixels, last, plane, (code >> 3) & 1);
            last += 2;
        }

        if (lastColumn < columns)
        {
            Or(pixels, last, plane, (codes[codeIndex] >> 3) & 1);
        }
    }

    private static void Or(int[] pixels, int index, int plane, int bit)
    {
        if (bit != 0)
        {
            pixels[index] |= plane;
        }
    }

    private static void Inverse(int[] pixels, int width, int height, int smooth, int scale)
    {
        int longest = Math.Max(width, height);
        int depth = (int)((Math.Log((float)longest) / Math.Log(2.0)) + 0.5);
        if (longest > (1 << depth))
        {
            depth++;
        }

        var scratch = new int[(longest + 1) / 2];
        int shift = 1;
        int bit0 = 1 << (depth - 1);
        int bit1 = bit0 << 1;
        int mask0 = -bit0;
        int mask1 = mask0 << 1;
        int mask2 = mask0 << 2;
        int round0 = bit0 >> 1;
        int round1 = bit1 >> 1;
        int round2 = bit1;
        int negative0 = round0 - 1;
        int negative1 = round1 - 1;
        int negative2 = round2 - 1;
        pixels[0] = (pixels[0] + (pixels[0] >= 0 ? round2 : negative2)) & mask2;

        int rows = 1;
        int columns = 1;
        int rowRemainder = width;
        int columnRemainder = height;
        int step = 1 << depth;
        for (int level = depth - 1; level >= 0; level--)
        {
            step >>= 1;
            rows <<= 1;
            columns <<= 1;
            if (rowRemainder <= step)
            {
                rows--;
            }
            else
            {
                rowRemainder -= step;
            }

            if (columnRemainder <= step)
            {
                columns--;
            }
            else
            {
                columnRemainder -= step;
            }

            if (level == 0)
            {
                negative0 = 0;
                shift = 2;
            }

            for (int slow = 0; slow < rows; slow++)
            {
                Unshuffle(pixels, height * slow, columns, 1, scratch);
            }

            for (int column = 0; column < columns; column++)
            {
                Unshuffle(pixels, column, rows, height, scratch);
            }

            if (smooth != 0)
            {
                Smooth(pixels, rows, columns, height, scale);
            }

            int oddRows = rows % 2;
            int oddColumns = columns % 2;
            int row = 0;
            for (; row < rows - oddRows; row += 2)
            {
                int even = height * row;
                int odd = even + height;
                int column = 0;
                for (; column < columns - oddColumns; column += 2)
                {
                    int sum = pixels[even];
                    int horizontal = Round(pixels[odd], round1, negative1, mask1);
                    int vertical = Round(pixels[even + 1], round1, negative1, mask1);
                    int diagonal = Round(pixels[odd + 1], round0, negative0, mask0);
                    int low = diagonal & bit0;
                    horizontal = horizontal >= 0 ? horizontal - low : horizontal + low;
                    vertical = vertical >= 0 ? vertical - low : vertical + low;
                    int mixed = (diagonal ^ horizontal ^ vertical) & bit1;
                    sum = sum >= 0
                        ? sum + low - mixed
                        : sum + (low == 0 ? mixed : low - mixed);
                    pixels[odd + 1] = (sum + horizontal + vertical + diagonal) >> shift;
                    pixels[odd] = (sum + horizontal - vertical - diagonal) >> shift;
                    pixels[even + 1] = (sum - horizontal + vertical - diagonal) >> shift;
                    pixels[even] = (sum - horizontal - vertical + diagonal) >> shift;
                    even += 2;
                    odd += 2;
                }

                if (oddColumns != 0)
                {
                    int sum = pixels[even];
                    int horizontal = Round(pixels[odd], round1, negative1, mask1);
                    int mixed = horizontal & bit1;
                    sum = sum >= 0 ? sum - mixed : sum + mixed;
                    pixels[odd] = (sum + horizontal) >> shift;
                    pixels[even] = (sum - horizontal) >> shift;
                }
            }

            if (oddRows != 0)
            {
                int even = height * row;
                int column = 0;
                for (; column < columns - oddColumns; column += 2)
                {
                    int sum = pixels[even];
                    int vertical = Round(pixels[even + 1], round1, negative1, mask1);
                    int mixed = vertical & bit1;
                    sum = sum >= 0 ? sum - mixed : sum + mixed;
                    pixels[even + 1] = (sum + vertical) >> shift;
                    pixels[even] = (sum - vertical) >> shift;
                    even += 2;
                }

                if (oddColumns != 0)
                {
                    pixels[even] >>= shift;
                }
            }

            bit1 = bit0;
            bit0 >>= 1;
            mask1 = mask0;
            mask0 >>= 1;
            round1 = round0;
            round0 >>= 1;
            negative1 = negative0;
            negative0 = round0 - 1;
        }
    }

    private static int Round(int value, int positive, int negative, int mask) =>
        (value + (value >= 0 ? positive : negative)) & mask;

    private static void Smooth(int[] pixels, int rows, int columns, int stride, int scale)
    {
        int limit = scale >> 1;
        if (limit <= 0)
        {
            return;
        }

        int stride2 = stride << 1;
        for (int row = 2; row < rows - 2; row += 2)
        {
            int even = stride * row;
            int odd = even + stride;
            for (int column = 0; column < columns; column += 2)
            {
                int previous = pixels[even - stride2];
                int current = pixels[even];
                int next = pixels[even + stride2];
                Adjust(pixels, odd, next - previous, next - current, current - previous, pixels[odd], 3, limit);
                even += 2;
                odd += 2;
            }
        }

        for (int row = 0; row < rows; row += 2)
        {
            int even = (stride * row) + 2;
            for (int column = 2; column < columns - 2; column += 2)
            {
                int previous = pixels[even - 2];
                int current = pixels[even];
                int next = pixels[even + 2];
                Adjust(pixels, even + 1, next - previous, next - current, current - previous, pixels[even + 1], 3, limit);
                even += 2;
            }
        }

        for (int row = 2; row < rows - 2; row += 2)
        {
            int even = (stride * row) + 2;
            int odd = even + stride;
            for (int column = 2; column < columns - 2; column += 2)
            {
                int lowerLeft = pixels[even - stride2 - 2];
                int lowerRight = pixels[even + stride2 - 2];
                int upperLeft = pixels[even - stride2 + 2];
                int upperRight = pixels[even + stride2 + 2];
                int current = pixels[even];
                int slopeX = pixels[odd] << 1;
                int slopeY = pixels[even + 1] << 1;
                int high = Math.Min(
                    Math.Min(Math.Max(upperRight - current, 0) - slopeX - slopeY, Math.Max(current - lowerRight, 0) + slopeX - slopeY),
                    Math.Min(Math.Max(current - upperLeft, 0) - slopeX + slopeY, Math.Max(lowerLeft - current, 0) + slopeX + slopeY)) << 4;
                int low = Math.Max(
                    Math.Max(Math.Min(upperRight - current, 0) - slopeX - slopeY, Math.Min(current - lowerRight, 0) + slopeX - slopeY),
                    Math.Max(Math.Min(current - upperLeft, 0) - slopeX + slopeY, Math.Min(lowerLeft - current, 0) + slopeX + slopeY)) << 4;
                if (low < high)
                {
                    int slope = Math.Clamp(upperRight + lowerLeft - upperLeft - lowerRight, low, high);
                    int change = slope - (pixels[odd + 1] << 6);
                    change = change >= 0 ? change >> 6 : (change + 63) >> 6;
                    pixels[odd + 1] += Math.Clamp(change, -limit, limit);
                }

                even += 2;
                odd += 2;
            }
        }
    }

    private static void Adjust(int[] pixels, int index, int slope, int rise, int fall, int current, int shift, int limit)
    {
        int high = Math.Max(Math.Min(rise, fall), 0) << 2;
        int low = Math.Min(Math.Max(rise, fall), 0) << 2;
        if (low >= high)
        {
            return;
        }

        int bounded = Math.Clamp(slope, low, high);
        int divisor = 1 << shift;
        int change = bounded - (current << shift);
        change = change >= 0 ? change >> shift : (change + divisor - 1) >> shift;
        pixels[index] += Math.Clamp(change, -limit, limit);
    }

    private static void Unshuffle(int[] values, int start, int count, int stride, int[] scratch)
    {
        int half = (count + 1) >> 1;
        int scratchCount = 0;
        int source = start + (stride * half);
        for (int index = half; index < count; index++)
        {
            scratch[scratchCount++] = values[source];
            source += stride;
        }

        int tail = start + (stride * (half - 1));
        int even = start + ((stride * (half - 1)) << 1);
        for (int index = half - 1; index >= 0; index--)
        {
            values[even] = values[tail];
            tail -= stride;
            even -= stride + stride;
        }

        scratchCount = 0;
        int odd = start + stride;
        for (int index = 1; index < count; index += 2)
        {
            values[odd] = scratch[scratchCount++];
            odd += stride + stride;
        }
    }

    private sealed class Cursor(byte[] source)
    {
        private int _next;
        private int _buffer;
        private int _bits;

        public void Align() => _bits = 0;

        public int Take()
        {
            if (_next >= source.Length)
            {
                throw new FitsException("An H-compress tile ended early.", new FitsLocation());
            }

            return source[_next++];
        }

        public int Int32()
        {
            int value = 0;
            for (int index = 0; index < 4; index++)
            {
                value = (value << 8) | Take();
            }

            return value;
        }

        public long Int64()
        {
            long value = 0;
            for (int index = 0; index < 8; index++)
            {
                value = (value << 8) | (uint)Take();
            }

            return value;
        }

        public int Bit()
        {
            if (_bits == 0)
            {
                _buffer = Take();
                _bits = 8;
            }

            _bits--;
            return (_buffer >> _bits) & 1;
        }

        public int Nybble() => Bits(4);

        public int Huffman()
        {
            int code = Bits(3);
            if (code < 4)
            {
                return 1 << code;
            }

            code = Bit() | (code << 1);
            if (code < 13)
            {
                return code switch
                {
                    8 => 3,
                    9 => 5,
                    10 => 10,
                    11 => 12,
                    12 => 15,
                    _ => throw new FitsException("An H-compress Huffman code is not recognized.", new FitsLocation()),
                };
            }

            code = Bit() | (code << 1);
            if (code < 31)
            {
                return code switch
                {
                    26 => 6,
                    27 => 7,
                    28 => 9,
                    29 => 11,
                    30 => 13,
                    _ => throw new FitsException("An H-compress Huffman code is not recognized.", new FitsLocation()),
                };
            }

            code = Bit() | (code << 1);
            return code == 62 ? 0 : 14;
        }

        private int Bits(int count)
        {
            if (_bits < count)
            {
                _buffer &= (1 << _bits) - 1;
                _buffer = (_buffer << 8) | Take();
                _bits += 8;
            }

            _bits -= count;
            return (_buffer >> _bits) & ((1 << count) - 1);
        }
    }
}
