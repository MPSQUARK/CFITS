namespace CFITS;

[AttributeUsage(AttributeTargets.Property)]
public sealed class FitsColumnAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
