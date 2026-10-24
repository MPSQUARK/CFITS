namespace CFITS;

internal static class Plio
{
    public static int[] Decompress(ReadOnlySpan<byte> source, int pixels)
    {
        if (pixels < 0 || (source.Length & 1) != 0)
        {
            throw new FitsException("A PLIO tile has an invalid size.", new FitsLocation());
        }

        if (source.Length < 10)
        {
            throw new FitsException("A PLIO tile is shorter than its header.", new FitsLocation());
        }

        var words = new int[(source.Length / 2) + 1];
        for (int index = 0; index < source.Length / 2; index++)
        {
            words[index + 1] = (short)((source[index * 2] << 8) | source[(index * 2) + 1]);
        }

        int length;
        int first;
        if (words[3] > 0)
        {
            length = words[3];
            first = 4;
        }
        else
        {
            length = (words[5] << 15) + words[4];
            first = words[2] + 1;
        }

        var output = new int[pixels];
        int x = 1;
        int outputIndex = 0;
        int high = 1;
        bool skipNext = false;
        if (first < 1 || length < first || length >= words.Length)
        {
            throw new FitsException("A PLIO tile has an invalid instruction range.", new FitsLocation());
        }

        for (int instruction = first; instruction <= length; instruction++)
        {
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            int opcode = words[instruction] / 4096;
            int data = words[instruction] & 4095;
            switch (opcode)
            {
                case 0:
                case 4:
                case 5:
                    int end = x + data - 1;
                    int from = Math.Max(x, 1);
                    int to = Math.Min(end, pixels);
                    int count = to - from + 1;
                    if (count > 0)
                    {
                        int fill = opcode == 4 ? high : 0;
                        for (int index = 0; index < count; index++)
                        {
                            output[outputIndex + index] = fill;
                        }

                        if (opcode == 5 && to == end)
                        {
                            output[outputIndex + count - 1] = high;
                        }

                        outputIndex += count;
                    }

                    x = end + 1;
                    break;
                case 1:
                    if (instruction + 1 > length)
                    {
                        throw new FitsException("A PLIO absolute instruction is missing its operand.", new FitsLocation());
                    }

                    high = (words[instruction + 1] << 12) + data;
                    skipNext = true;
                    break;
                case 2:
                    high += data;
                    break;
                case 3:
                    high -= data;
                    break;
                case 6:
                    high += data;
                    WritePixel(output, ref outputIndex, ref x, pixels, high);
                    break;
                case 7:
                    high -= data;
                    WritePixel(output, ref outputIndex, ref x, pixels, high);
                    break;
                default:
                    throw new FitsException($"A PLIO tile contains unknown opcode {opcode}.", new FitsLocation());
            }

            if (x > pixels)
            {
                break;
            }
        }

        if (outputIndex != pixels)
        {
            throw new FitsException("A PLIO tile did not produce the requested number of pixels.", new FitsLocation());
        }

        return output;
    }

    private static void WritePixel(int[] output, ref int outputIndex, ref int x, int pixels, int value)
    {
        if (x >= 1 && x <= pixels)
        {
            output[outputIndex++] = value;
        }

        x++;
    }
}
