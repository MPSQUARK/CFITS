namespace CFITS;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class FitsImageAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;

    public int HduIndex { get; init; } = -1;
}
