namespace CFITS;

[AttributeUsage(AttributeTargets.Class)]
public sealed class FitsImageAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}
