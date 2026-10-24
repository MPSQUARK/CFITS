namespace CFITS;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class FitsSerializableAttribute : Attribute
{
    public FitsSerializableAttribute(Type type) => Type = type;

    public Type Type { get; }
}
