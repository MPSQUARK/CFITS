namespace CFITS;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property)]
public sealed class FitsTableAttribute : Attribute
{
    public FitsTableAttribute()
    {
    }

    public FitsTableAttribute(int hduIndex)
    {
        HduIndex = hduIndex;
    }

    public FitsTableAttribute(string name)
    {
        Name = name;
    }

    public FitsTableAttribute(int hduIndex, string name)
    {
        HduIndex = hduIndex;
        Name = name;
    }

    public string? Name { get; }

    public int HduIndex { get; init; } = -1;
}
