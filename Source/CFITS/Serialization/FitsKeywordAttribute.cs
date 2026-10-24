namespace CFITS;

[AttributeUsage(AttributeTargets.Property)]
public sealed class FitsKeywordAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
