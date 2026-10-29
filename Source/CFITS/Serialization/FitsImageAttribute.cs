namespace CFITS;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property)]
public sealed class FitsImageAttribute : Attribute
{
    public FitsImageAttribute()
    {
    }

    public FitsImageAttribute(int hduIndex)
    {
        HduIndex = hduIndex;
    }

    public FitsImageAttribute(string name)
    {
        Name = name;
    }

    public FitsImageAttribute(int hduIndex, string name)
    {
        HduIndex = hduIndex;
        Name = name;
    }

    public string? Name { get; }

    public int HduIndex { get; init; } = -1;
}
