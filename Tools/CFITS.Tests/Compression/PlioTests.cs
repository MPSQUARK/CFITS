namespace CFITS.Tests;

public sealed class PlioTests
{
    [Fact]
    public void DecompressesFillDeltaSkipAndExtendedInstructionsExactly()
    {
        short[] words =
        [
            0, 0, 12,
            Instruction(1, 100), 0,
            Instruction(6, 0),
            Instruction(2, 3),
            Instruction(6, 0),
            Instruction(3, 2),
            Instruction(7, 0),
            Instruction(0, 2),
            Instruction(4, 2),
        ];

        int[] actual = Decompress(words, 7);

        Assert.Equal([100, 103, 101, 0, 0, 101, 101], actual);
    }

    [Fact]
    public void RejectsMalformedPlioInstructions()
    {
        Assert.Throws<FitsException>(() => Decompress([0, 0], 1));
        Assert.Throws<FitsException>(() => Decompress([0, 0, 4, Instruction(1, 1), 0], 1));
        Assert.Throws<FitsException>(() => Decompress([0, 0, 4, Instruction(8, 0), 0], 1));
        Assert.Throws<FitsException>(() => Decompress([0, 0, 4, Instruction(0, 2), 0], 3));
        Assert.Throws<FitsException>(() => Decompress([0, 0, 4, Instruction(0, 0), 0], 1));
    }

    private static int[] Decompress(short[] words, int pixels)
    {
        byte[] bytes = new byte[words.Length * sizeof(short)];
        for (int index = 0; index < words.Length; index++)
        {
            bytes[index * 2] = (byte)(words[index] >> 8);
            bytes[(index * 2) + 1] = (byte)words[index];
        }

        string payload = new(bytes.Select(static value => (char)value).ToArray());
        return ReadImage(payload, pixels);
    }

    private static int[] ReadImage(string payload, int pixels)
    {
        using var document = FitsDocumentBuilder.Create()
            .BinaryTable(
                new BinaryTableBuilder().AddVariable("COMPRESSED_DATA", [payload]),
                header => header
                    .Logical("ZIMAGE", true)
                    .Text("ZCMPTYPE", "PLIO_1")
                    .Integer("ZBITPIX", 32)
                    .Integer("ZNAXIS", 1)
                    .Integer("ZNAXIS1", pixels)
                    .Integer("ZTILE1", pixels))
            .Build();

        int[] result = new int[pixels];
        document.Hdus[1].Image!.CopyInteger(result);
        return result;
    }

    private static short Instruction(int opcode, int data) =>
        unchecked((short)((opcode << 12) | data));
}
