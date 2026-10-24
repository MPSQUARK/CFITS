namespace CFITS;

[AttributeUsage(AttributeTargets.Property)]
public sealed class FitsColumnAttribute : Attribute
{
    public FitsColumnAttribute(string name) => Name = name;

    public string Name { get; }
}
