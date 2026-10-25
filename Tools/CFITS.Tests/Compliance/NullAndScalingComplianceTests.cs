namespace CFITS.Tests;

public sealed class NullAndScalingComplianceTests
{
    [Fact]
    public void RejectsByteNullMarkerOutsideColumnDomain()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().Add("VALUE", new byte[] { 1 }),
                    header => header.Integer("TNULL1", 256))
                .Build());
    }

    [Fact]
    public void RejectsNullMarkerOnUnsignedLogicalStorage()
    {
        Assert.Throws<FitsException>(() =>
            FitsDocumentBuilder.Create()
                .BinaryTable(
                    new BinaryTableBuilder().Add("FLAG", new[] { true }),
                    header => header.Integer("TNULL1", 0))
                .Build());
    }
}
