namespace CFITS;

[AttributeUsage(AttributeTargets.Class)]
public sealed class FitsImageAttribute : Attribute
{
    public FitsImageAttribute()
    {
    }

    public FitsImageAttribute(string name) => Name = name;

    public string? Name { get; }
}
