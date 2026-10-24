namespace CFITS;

[AttributeUsage(AttributeTargets.Property)]
public sealed class FitsKeywordAttribute : Attribute
{
    public FitsKeywordAttribute(string name) => Name = name;

    public string Name { get; }
}
