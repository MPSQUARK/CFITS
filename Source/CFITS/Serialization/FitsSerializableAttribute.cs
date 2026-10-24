namespace CFITS;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class FitsSerializableAttribute(Type type) : Attribute
{
    public Type Type { get; } = type;
}
