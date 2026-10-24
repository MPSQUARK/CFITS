using System.Numerics;

namespace CFITS;

public readonly record struct FitsComplex(double Real, double Imaginary)
{
    public static explicit operator Complex(FitsComplex value) => new(value.Real, value.Imaginary);

    public static explicit operator FitsComplex(Complex value) => new(value.Real, value.Imaginary);
}
