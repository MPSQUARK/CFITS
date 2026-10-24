namespace CFITS;

[AttributeUsage(AttributeTargets.Class)]
public sealed class FitsTableAttribute : Attribute
{
    public FitsTableAttribute()
    {
    }

    public FitsTableAttribute(string name) => Name = name;

    public string? Name { get; }
}
