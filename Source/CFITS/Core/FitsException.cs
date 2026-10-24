namespace CFITS;

/// <summary>A FITS file is not valid at the reported location.</summary>
public sealed class FitsException : Exception
{
    public FitsException(string message)
        : this(message, default, null)
    {
    }

    public FitsException(string message, FitsLocation location)
        : this(message, location, null)
    {
    }

    public FitsException(string message, FitsLocation location, Exception? inner)
        : base(message, inner)
    {
        Location = location;
    }

    public FitsLocation Location { get; }

    public int? HduIndex => Location.HduIndex;

    public string? Keyword => Location.Keyword;

    public string? Column => Location.Column;

    public long? Position => Location.Position;
}
