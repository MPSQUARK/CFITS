namespace CFITS;

/// <summary>
/// Requires the mapped value to be present when a document is read.
/// A missing keyword, column, image data, or HDU throws <see cref="FitsException"/>.
/// Without this attribute, a missing keyword, column, or optional HDU leaves the property at its default.
/// This attribute is a read constraint.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class FitsRequiredAttribute : Attribute;
