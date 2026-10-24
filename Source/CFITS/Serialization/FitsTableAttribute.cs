namespace CFITS;

[AttributeUsage(AttributeTargets.Class)]
public sealed class FitsTableAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}
