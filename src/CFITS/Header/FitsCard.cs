namespace CFITS;

/// <summary>One logical header keyword, with commentary kept in file order.</summary>
public readonly record struct FitsCard(
    string Keyword,
    FitsValueKind Kind,
    bool Logical,
    long Integer,
    double Real,
    double Imaginary,
    string? Text,
    string? Comment,
    string? Unit)
{
    public bool GetBoolean()
    {
        if (Kind != FitsValueKind.Logical)
        {
            throw new FitsException($"Keyword '{Keyword}' is not a logical value.", new FitsLocation(Keyword: Keyword));
        }

        return Logical;
    }

    public long GetInt64()
    {
        if (Kind != FitsValueKind.Integer)
        {
            throw new FitsException($"Keyword '{Keyword}' is not an integer value.", new FitsLocation(Keyword: Keyword));
        }

        return Integer;
    }

    public double GetDouble()
    {
        return Kind switch
        {
            FitsValueKind.Real => Real,
            FitsValueKind.Integer => Integer,
            _ => throw new FitsException($"Keyword '{Keyword}' is not a numeric value.", new FitsLocation(Keyword: Keyword)),
        };
    }

    public string? GetString()
    {
        return Kind switch
        {
            FitsValueKind.String or FitsValueKind.Commentary or FitsValueKind.Raw => Text,
            FitsValueKind.Undefined => null,
            _ => throw new FitsException($"Keyword '{Keyword}' is not a string value.", new FitsLocation(Keyword: Keyword)),
        };
    }

    public FitsComplex GetComplex()
    {
        if (Kind != FitsValueKind.Complex)
        {
            throw new FitsException($"Keyword '{Keyword}' is not a complex value.", new FitsLocation(Keyword: Keyword));
        }

        return new FitsComplex(Real, Imaginary);
    }
}

public readonly record struct FitsComplex(double Real, double Imaginary);
