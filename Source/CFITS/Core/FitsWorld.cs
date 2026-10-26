using System.Collections.Frozen;

namespace CFITS;

/// <summary>
/// Pixel-to-world conversion from the keywords in FITS section 8.2.
/// Celestial values use the zenithal and plate-carree projections named by CTYPE.
/// Pixels are 1-based, matching CRPIX.
/// </summary>
public sealed class FitsWorld
{
    private const int MaximumAxes = 999;
    private const int StackWorkspaceLimit = 64;
    private const double SingularMatrixTolerance = 1e-12;
    private const double DegreesPerRadian = 180 / Math.PI;
    private const double RadiansPerDegree = Math.PI / 180;
    private const double SqrtTwo = 1.4142135623730950488;

    private static readonly FrozenDictionary<string, Projection> ProjectionKinds =
        new Dictionary<string, Projection>(StringComparer.OrdinalIgnoreCase)
    {
        ["ARC"] = Projection.Arc,
        ["AIT"] = Projection.Ait,
        ["AZP"] = Projection.Azp,
        ["SZP"] = Projection.Szp,
        ["CAR"] = Projection.Car,
        ["CEA"] = Projection.Cea,
        ["CYP"] = Projection.Cyp,
        ["MER"] = Projection.Mer,
        ["SFL"] = Projection.Sfl,
        ["PAR"] = Projection.Par,
        ["MOL"] = Projection.Mol,
        ["COE"] = Projection.Coe,
        ["COD"] = Projection.Cod,
        ["COO"] = Projection.Coo,
        ["BON"] = Projection.Bon,
        ["HPX"] = Projection.Hpx,
        ["SIN"] = Projection.Sin,
        ["STG"] = Projection.Stg,
        ["TAN"] = Projection.Tan,
        ["ZEA"] = Projection.Zea,
        ["ZPN"] = Projection.Zpn,
        ["AIR"] = Projection.Air,
        ["COP"] = Projection.Cop,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly double[] _referencePixel;
    private readonly double[] _referenceValue;
    private readonly double[] _scale;
    private readonly double[] _matrix;
    private readonly bool _scaledMatrix;
    private readonly int _longitude;
    private readonly int _latitude;
    private readonly Projection _projectionKind;
    private readonly double[] _euler;
    private readonly double[] _pv;
    private readonly string?[] _axisAlgorithms;
    private readonly IReadOnlyList<WcsAxisMetadata> _axisMetadata;

    private FitsWorld(double[] referencePixel, double[] referenceValue, double[] scale, double[] matrix, bool scaledMatrix, int longitude, int latitude, string? projection, double[] euler, double[] pv, string?[] axisAlgorithms, IReadOnlyList<WcsAxisMetadata> axisMetadata)
    {
        _referencePixel = referencePixel;
        _referenceValue = referenceValue;
        _scale = scale;
        _matrix = matrix;
        _scaledMatrix = scaledMatrix;
        _longitude = longitude;
        _latitude = latitude;
        _projectionKind = ProjectionOf(projection);
        _euler = euler;
        _pv = pv;
        _axisAlgorithms = axisAlgorithms;
        _axisMetadata = axisMetadata;
    }

    public int Axes => _referencePixel.Length;

    public IReadOnlyList<WcsAxisMetadata> AxisMetadata => _axisMetadata;

    public static FitsWorld FromHeader(FitsHeader header, char version = ' ')
    {
        int count = AxisCount(header, version);
        var referencePixel = new double[count];
        var referenceValue = new double[count];
        var scale = new double[count];
        var type = new string[count];
        bool cd = false;
        bool explicitPc = false;
        for (int axis = 1; axis <= count; axis++)
        {
            referencePixel[axis - 1] = Read(header, $"CRPIX{axis}", version, 0);
            referenceValue[axis - 1] = Read(header, $"CRVAL{axis}", version, 0);
            EnsureFinite(referencePixel[axis - 1], $"CRPIX{axis}");
            EnsureFinite(referenceValue[axis - 1], $"CRVAL{axis}");
            scale[axis - 1] = Read(header, $"CDELT{axis}", version, 1);
            if (!double.IsFinite(scale[axis - 1]) || scale[axis - 1] == 0)
            {
                throw new FitsException("CDELT must be a finite, non-zero increment.", new FitsLocation(Keyword: $"CDELT{axis}"));
            }
            type[axis - 1] = Text(header, $"CTYPE{axis}", version);
            for (int other = 1; other <= count; other++)
            {
                if (header.TryGetCard(Name($"CD{axis}_{other}", version), out _))
                {
                    cd = true;
                }

                if (header.TryGetCard(Name($"PC{axis}_{other}", version), out _))
                {
                    explicitPc = true;
                }
            }
        }

        if (cd && explicitPc)
        {
            throw new FitsException("CDi_j and PCi_j must not appear in the same header.", new FitsLocation());
        }

        var matrix = new double[count * count];
        for (int axis = 0; axis < count; axis++)
        {
            for (int other = 0; other < count; other++)
            {
                string keyword = cd ? $"CD{axis + 1}_{other + 1}" : $"PC{axis + 1}_{other + 1}";
                double fallback = !cd && axis == other ? 1 : 0;
                matrix[(axis * count) + other] = Read(header, keyword, version, fallback);
                EnsureFinite(matrix[(axis * count) + other], keyword);
            }
        }

        foreach (var card in header.Cards)
        {
            if (card.Keyword.StartsWith("PV", StringComparison.OrdinalIgnoreCase)
                && (card.Kind is FitsValueKind.Real or FitsValueKind.Integer))
            {
                EnsureFinite(card.GetDouble(), card.Keyword);
            }
        }

        if (!cd && !explicitPc)
        {
            double rotation = Read(header, "CROTA2", version, 0);
            if (count >= 2 && rotation != 0)
            {
                double radians = rotation * RadiansPerDegree;
                matrix[0] = Math.Cos(radians);
                matrix[1] = -Math.Sin(radians);
                matrix[count] = Math.Sin(radians);
                matrix[count + 1] = Math.Cos(radians);
            }
        }

        int longitude = -1;
        int latitude = -1;
        string? projection = null;
        for (int axis = 0; axis < count; axis++)
        {
            if (!TryProjection(type[axis], out string coordinate, out string algorithm))
            {
                continue;
            }

            if (!IsLongitude(coordinate) && !IsLatitude(coordinate))
            {
                if ((IsSpectral(coordinate) || IsTime(coordinate)) && IsAxisAlgorithm(algorithm))
                {
                    continue;
                }

                throw new FitsException(
                    $"Projection or coordinate algorithm '{algorithm}' is only valid for a celestial longitude/latitude axis.",
                    new FitsLocation(Keyword: $"CTYPE{axis + 1}"));
            }

            if (projection is null)
            {
                projection = algorithm;
            }
            else if (!projection.Equals(algorithm, StringComparison.OrdinalIgnoreCase))
            {
                throw new FitsException("The celestial axes do not use the same projection.", new FitsLocation());
            }

            if (IsLongitude(coordinate))
            {
                if (!ProjectionKinds.ContainsKey(algorithm))
                {
                    throw new FitsException($"Projection '{algorithm}' is not implemented.", new FitsLocation());
                }

                if (longitude >= 0)
                {
                    throw new FitsException("A WCS cannot define multiple longitude axes.", new FitsLocation());
                }

                longitude = axis;
            }
            else if (IsLatitude(coordinate))
            {
                if (!ProjectionKinds.ContainsKey(algorithm))
                {
                    throw new FitsException($"Projection '{algorithm}' is not implemented.", new FitsLocation());
                }

                if (latitude >= 0)
                {
                    throw new FitsException("A WCS cannot define multiple latitude axes.", new FitsLocation());
                }

                latitude = axis;
            }
            else
            {
                throw new FitsException(
                    $"Projection '{algorithm}' is only valid for a celestial longitude/latitude axis.",
                    new FitsLocation(Keyword: $"CTYPE{axis + 1}"));
            }
        }

        if ((longitude >= 0) != (latitude >= 0))
        {
            throw new FitsException("Celestial WCS requires a longitude and latitude axis pair.", new FitsLocation());
        }

        double pole = header.TryGetCard(Name("LONPOLE", version), out _)
            ? Read(header, "LONPOLE", version, 0)
            : double.NaN;
        double latitudePole = header.TryGetCard(Name("LATPOLE", version), out _)
            ? Read(header, "LATPOLE", version, 90)
            : 999;
        string? active = longitude >= 0 && latitude >= 0 ? projection : null;
        double[] euler = active is null
            ? []
            : Euler(active, referenceValue[longitude], referenceValue[latitude], pole, latitudePole);
        var pv = new double[10];
        if (longitude >= 0)
        {
            for (int index = 0; index < pv.Length; index++)
            {
                int keywordParameter = active == "ZPN" ? index : index + 1;
                pv[index] = Read(header, $"PV{latitude + 1}_{keywordParameter}", version, ProjectionDefault(active, keywordParameter));
                EnsureFinite(pv[index], $"PV{latitude + 1}_{keywordParameter}");
            }

            ValidateProjectionParameters(active!, pv, latitude + 1);
        }
        var metadata = new WcsAxisMetadata[count];
        var axisAlgorithms = new string?[count];
        string? referenceSystem = Text(header, "SPECSYS", version);
        string? velocitySystem = Text(header, "SSYSOBS", version);
        double? restFrequency = OptionalFinite(header, "RESTFRQ", version);
        double? restWavelength = OptionalFinite(header, "RESTWAV", version);
        int? velocityReference = OptionalInteger(header, "VELREF", version);
        for (int axis = 0; axis < count; axis++)
        {
            string coordinate = type[axis];
            bool hasProjection = TryProjection(coordinate, out string coordinateName, out string algorithmName);
            bool celestial = hasProjection && (IsLongitude(coordinateName) || IsLatitude(coordinateName));
            string? axisAlgorithm = hasProjection && (IsSpectral(coordinateName) || IsTime(coordinateName))
                ? algorithmName
                : null;
            axisAlgorithms[axis] = axisAlgorithm;
            metadata[axis] = new WcsAxisMetadata(
                axis + 1,
                coordinate,
                Text(header, $"CUNIT{axis + 1}", version),
                celestial,
                IsSpectral(coordinate),
                IsTime(coordinate),
                celestial ? algorithmName : null,
                (IsSpectral(coordinate) || IsTime(coordinate)) && hasProjection ? algorithmName : null,
                Text(header, $"CNAME{axis + 1}", version),
                OptionalFinite(header, $"CRDER{axis + 1}", version),
                OptionalFinite(header, $"CSYER{axis + 1}", version),
                string.IsNullOrEmpty(referenceSystem) ? null : referenceSystem,
                restFrequency,
                restWavelength,
                string.IsNullOrEmpty(velocitySystem) ? null : velocitySystem,
                velocityReference);
        }

        return new FitsWorld(referencePixel, referenceValue, scale, matrix, cd, longitude, latitude, active, euler, pv, axisAlgorithms, metadata);
    }

    public double[] PixelToWorld(ReadOnlySpan<double> pixel)
    {
        var world = new double[Axes];
        PixelToWorld(pixel, world);
        return world;
    }

    public void PixelToWorld(ReadOnlySpan<double> pixel, Span<double> world)
    {
        if (pixel.Length != Axes)
        {
            throw new FitsException("The pixel coordinate does not have one value per axis.", new FitsLocation());
        }

        if (world.Length < Axes)
        {
            throw new FitsException("The world coordinate destination is too short.", new FitsLocation());
        }

        Span<double> source = stackalloc double[Axes];
        pixel.CopyTo(source);
        for (int axis = 0; axis < Axes; axis++)
        {
            double intermediate = 0;
            for (int other = 0; other < Axes; other++)
            {
                intermediate += _matrix[(axis * Axes) + other] * (source[other] - _referencePixel[other]);
            }

            if (!_scaledMatrix)
            {
                intermediate *= _scale[axis];
            }

            world[axis] = _referenceValue[axis] + intermediate;
            world[axis] = ApplySpectralForward(world[axis], axis);
        }

        if (_projectionKind == Projection.None)
        {
            return;
        }

        double planeX = world[_longitude] - _referenceValue[_longitude];
        double planeY = world[_latitude] - _referenceValue[_latitude];
        double phi;
        double theta;
        if (_projectionKind == Projection.Car)
        {
            phi = planeX;
            theta = planeY;
        }
        else
        {
            var native = Native(_projectionKind, planeX, planeY, _pv);
            EnsureProjectionResult(native.Phi, native.Theta, _projectionKind);
            phi = native.Phi;
            theta = native.Theta;
        }

        var celestial = Spherical(phi, theta, _euler);
        world[_longitude] = celestial.Longitude;
        world[_latitude] = celestial.Latitude;
    }

    public double[] WorldToPixel(ReadOnlySpan<double> world)
    {
        var pixel = new double[Axes];
        WorldToPixel(world, pixel);
        return pixel;
    }

    public void WorldToPixel(ReadOnlySpan<double> world, Span<double> pixel)
    {
        if (world.Length != Axes)
        {
            throw new FitsException("The world coordinate does not have one value per axis.", new FitsLocation());
        }

        if (pixel.Length < Axes)
        {
            throw new FitsException("The pixel coordinate destination is too short.", new FitsLocation());
        }

        Span<double> intermediate = stackalloc double[Axes];
        if (_projectionKind == Projection.None)
        {
            for (int axis = 0; axis < Axes; axis++)
            {
                intermediate[axis] = world[axis] - _referenceValue[axis];
            }
        }
        else
        {
            var native = InverseSpherical(world[_longitude], world[_latitude], _euler);
            var plane = Plane(_projectionKind, native.Phi, native.Theta, _pv);
            EnsureProjectionResult(plane.X, plane.Y, _projectionKind);
            for (int axis = 0; axis < Axes; axis++)
            {
                intermediate[axis] = axis == _longitude ? plane.X : axis == _latitude ? plane.Y : world[axis] - _referenceValue[axis];
            }
        }

        for (int axis = 0; axis < Axes; axis++)
        {
            if (axis != _longitude && axis != _latitude)
            {
                intermediate[axis] = ApplySpectralInverse(world[axis], axis) - _referenceValue[axis];
            }
        }

        if (!_scaledMatrix)
        {
            for (int axis = 0; axis < Axes; axis++)
            {
                intermediate[axis] /= _scale[axis];
            }
        }

        Solve(_matrix, intermediate, pixel);
        for (int axis = 0; axis < Axes; axis++)
        {
            pixel[axis] += _referencePixel[axis];
        }
    }

    private static (double Phi, double Theta) Native(Projection projection, double x, double y, double[] pv)
    {
        double pv1 = pv[0];
        double pv2 = pv[1];
        double radius = Math.Sqrt((x * x) + (y * y));
        ValidateNativeDomain(projection, x, y, radius, pv1);
        double phi = double.RadiansToDegrees(Math.Atan2(x, -y));
        double theta = projection switch
        {
            Projection.Tan => radius == 0 ? 90 : double.RadiansToDegrees(Math.Atan(DegreesPerRadian / radius)),
            Projection.Sin => radius * RadiansPerDegree > 1 ? double.NaN : double.RadiansToDegrees(Math.Acos(radius * RadiansPerDegree)),
            Projection.Arc => 90 - radius,
            Projection.Stg => 90 - (2 * double.RadiansToDegrees(Math.Atan(RadiansPerDegree * radius / 2))),
            Projection.Zea => 90 - (2 * double.RadiansToDegrees(Math.Asin(RadiansPerDegree * radius / 2))),
            Projection.Cea => double.RadiansToDegrees(Math.Asin(Math.Clamp(y * RadiansPerDegree * pv1, -1, 1))),
            Projection.Ait => AitNative(x, y).Theta,
            Projection.Azp => AzpNative(x, y, pv2, pv1).Theta,
            Projection.Szp => SlantNative(x, y, pv2, pv1).Theta,
            Projection.Zpn => ZpnNative(x, y, pv),
            Projection.Air => ZpnNative(x, y, new[] { 0d, 1d }),
            Projection.Hpx => double.RadiansToDegrees(Math.Asin(Math.Clamp(y / 67.5, -1, 1))),
            Projection.Mer => double.RadiansToDegrees(2 * Math.Atan(Math.Exp(y * RadiansPerDegree)) - Math.PI / 2),
            Projection.Sfl => y,
            Projection.Par => DegreesPerRadian * 3 * Math.Asin(Math.Clamp(y / 180, -1, 1)),
            Projection.Mol => MolNative(x, y).Theta,
            Projection.Cyp => CypNative(x, y, pv1, pv2).Theta,
            Projection.Coe or Projection.Cod or Projection.Coo or Projection.Bon or Projection.Cop => ConicNative(projection, x, y, pv1, pv2).Theta,
            Projection.Car => y,
            _ => throw new FitsException("Projection is not implemented.", new FitsLocation()),
        };
        if (projection == Projection.Car)
        {
            phi = x;
        }
        else if (projection == Projection.Azp)
        {
            phi = AzpNative(x, y, pv2, pv1).Phi;
        }
        else if (projection == Projection.Ait)
        {
            phi = AitNative(x, y).Phi;
        }
        else if (projection == Projection.Cea)
        {
            phi = x / pv1;
        }
        else if (projection == Projection.Hpx)
        {
            phi = x;
        }
        else if (projection == Projection.Mer)
        {
            phi = x;
        }
        else if (projection == Projection.Sfl)
        {
            phi = Math.Abs(theta) >= 90 ? 0 : x / Math.Cos(theta * RadiansPerDegree);
        }
        else if (projection == Projection.Par)
        {
            phi = x / (2 * Math.Cos(2 * theta * RadiansPerDegree / 3) - 1);
        }
        else if (projection == Projection.Mol)
        {
            phi = MolNative(x, y).Phi;
        }
        else if (projection == Projection.Cyp)
        {
            phi = CypNative(x, y, pv1, pv2).Phi;
        }
        else if (projection is Projection.Coe or Projection.Cod or Projection.Coo or Projection.Bon or Projection.Cop)
        {
            phi = ConicNative(projection, x, y, pv1, pv2).Phi;
        }

        return (phi, theta);
    }

    private static void ValidateNativeDomain(Projection projection, double x, double y, double radius, double pv1)
    {
        const double tolerance = 1e-12;
        double radialArgument = radius * RadiansPerDegree;
        bool invalid = projection switch
        {
            Projection.Sin => radialArgument > 1 + tolerance,
            Projection.Zea => radialArgument > 2 + tolerance,
            Projection.Ait => ((x * x) / (16 * DegreesPerRadian * DegreesPerRadian))
                + ((y * y) / (4 * DegreesPerRadian * DegreesPerRadian)) > 1 + tolerance,
            Projection.Cea => Math.Abs(y * RadiansPerDegree * pv1) > 1 + tolerance,
            Projection.Hpx => Math.Abs(y) > 67.5 + tolerance,
            _ => false,
        };

        if (invalid)
        {
            throw new FitsException(
                $"The {projection} projection coordinate is outside its mathematical domain.",
                new FitsLocation());
        }
    }

    private static (double Phi, double Theta) InverseSpherical(double longitude, double latitude, double[] euler)
    {
        if (euler[4] == 0)
        {
            if (euler[1] == 0)
            {
                return (NormalizeSigned(longitude + euler[2] - 180 - euler[0]), latitude);
            }

            return (NormalizeSigned(euler[2] + euler[0] - longitude), -latitude);
        }

        double deltaLongitude = longitude - euler[0];
        (double sinLatitude, double cosLatitude) = Math.SinCos(double.DegreesToRadians(latitude));
        (double sinLongitude, double cosLongitude) = Math.SinCos(double.DegreesToRadians(deltaLongitude));
        double x = (sinLatitude * euler[4]) - (cosLatitude * euler[3] * cosLongitude);
        if (Math.Abs(x) < 1e-5)
        {
            x = -Math.Cos(double.DegreesToRadians(latitude + euler[1])) + (cosLatitude * euler[3] * (1 - cosLongitude));
        }

        double y = -cosLatitude * sinLongitude;
        double deltaPhi = Math.Abs(x) < 1e-15 && Math.Abs(y) < 1e-15
            ? euler[1] < 90 ? deltaLongitude - 180 : -deltaLongitude
            : double.RadiansToDegrees(Math.Atan2(y, x));
        double theta = Math.Abs(deltaLongitude % 180) < 1e-12
            ? latitude + (cosLongitude * euler[1])
            : double.RadiansToDegrees(Math.Asin(Math.Clamp((sinLatitude * euler[3]) + (cosLatitude * euler[4] * cosLongitude), -1, 1)));
        if (theta > 90)
        {
            theta = 180 - theta;
        }
        else if (theta < -90)
        {
            theta = -180 - theta;
        }

        return (NormalizeSigned(euler[2] + deltaPhi), theta);
    }

    private static (double X, double Y) Plane(Projection projection, double phi, double theta, double[] pv)
    {
        double pv1 = pv[0];
        double pv2 = pv[1];
        if (projection == Projection.Car)
        {
            return (phi, theta);
        }

        double phiRadians = phi * RadiansPerDegree;
        double thetaRadians = theta * RadiansPerDegree;
        if (projection is Projection.Azp or Projection.Szp)
        {
            return SlantPlane(phi, theta, pv2, pv1);
        }

        (double sinPhi, double cosPhi) = Math.SinCos(phiRadians);
        double radius = projection switch
        {
            Projection.Tan => Math.Abs(theta - 90) < 1e-12 ? 0 : DegreesPerRadian / Math.Tan(thetaRadians),
            Projection.Sin => DegreesPerRadian * Math.Cos(thetaRadians),
            Projection.Arc => 90 - theta,
            Projection.Stg => 2 * DegreesPerRadian * Math.Tan((Math.PI / 2 - thetaRadians) / 2),
            Projection.Zea => 2 * DegreesPerRadian * Math.Sin((Math.PI / 2 - thetaRadians) / 2),
            Projection.Cea => Math.Sin(thetaRadians) / (RadiansPerDegree * pv1),
            Projection.Ait => 0,
            Projection.Zpn => ZpnRadius(theta, pv),
            Projection.Air => DegreesPerRadian / Math.Tan(thetaRadians),
            Projection.Hpx => 67.5 * Math.Sin(thetaRadians),
            Projection.Mer => DegreesPerRadian * Math.Log(Math.Tan(Math.PI / 4 + thetaRadians / 2)),
            Projection.Sfl => 0,
            Projection.Par => 180 * Math.Sin(thetaRadians / 3),
            Projection.Mol => 0,
            Projection.Cyp => 0,
            Projection.Coe or Projection.Cod or Projection.Coo or Projection.Bon or Projection.Cop => 0,
            _ => throw new FitsException("Projection is not implemented.", new FitsLocation()),
        };
        if (projection == Projection.Ait)
        {
            return AitPlane(phi, theta);
        }
        if (projection == Projection.Cea)
        {
            return (phi * pv1, Math.Sin(thetaRadians) / (RadiansPerDegree * pv1));
        }
        if (projection == Projection.Sfl)
        {
            return (phi * Math.Cos(thetaRadians), theta);
        }
        if (projection == Projection.Par)
        {
            return (phi * (2 * Math.Cos(2 * thetaRadians / 3) - 1), 180 * Math.Sin(thetaRadians / 3));
        }
        if (projection == Projection.Mer)
        {
            return (phi, DegreesPerRadian * Math.Log(Math.Tan(Math.PI / 4 + thetaRadians / 2)));
        }
        if (projection == Projection.Mol)
        {
            return MolPlane(phi, theta);
        }
        if (projection == Projection.Cyp)
        {
            return CypPlane(phi, theta, pv1, pv2);
        }
        if (projection is Projection.Coe or Projection.Cod or Projection.Coo or Projection.Bon or Projection.Cop)
        {
            return ConicPlane(projection, phi, theta, pv1, pv2);
        }
        return (radius * sinPhi, -radius * cosPhi);
    }

    private static (double Phi, double Theta) AitNative(double x, double y)
    {
        double radius = DegreesPerRadian;
        double z = Math.Sqrt(Math.Max(0, 1 - ((x * x) / (16 * radius * radius)) - ((y * y) / (4 * radius * radius))));
        double latitude = Math.Asin(Math.Clamp(z * y / radius, -1, 1));
        double longitude = 2 * Math.Atan2(z * x, 2 * radius * ((2 * z * z) - 1));
        return (double.RadiansToDegrees(longitude), double.RadiansToDegrees(latitude));
    }

    private static (double X, double Y) AitPlane(double phi, double theta)
    {
        double halfPhi = phi * RadiansPerDegree / 2;
        double latitude = theta * RadiansPerDegree;
        double denominator = Math.Sqrt(1 + Math.Cos(latitude) * Math.Cos(halfPhi));
        return (DegreesPerRadian * SqrtTwo * Math.Cos(latitude) * Math.Sin(halfPhi) / denominator,
            DegreesPerRadian * SqrtTwo * Math.Sin(latitude) / denominator);
    }

    private static (double Phi, double Theta) SlantNative(double x, double y, double gamma, double mu)
    {
        double phi = double.RadiansToDegrees(Math.Atan2(x, -y));
        double radius = Math.Sqrt((x * x) + (y * y));
        double normalizedRadius = radius / (DegreesPerRadian * (mu + 1));
        double theta = 90 - double.RadiansToDegrees(Math.Atan(normalizedRadius));
        for (int iteration = 0; iteration < 12; iteration++)
        {
            double thetaRadians = theta * RadiansPerDegree;
            (double sinTheta, double cosTheta) = Math.SinCos(thetaRadians);
            double value = normalizedRadius * (mu + sinTheta
                + (cosTheta * Math.Cos(phi * RadiansPerDegree) * Math.Tan(gamma * RadiansPerDegree)))
                - cosTheta;
            double derivative = normalizedRadius * (cosTheta
                - (sinTheta * Math.Cos(phi * RadiansPerDegree) * Math.Tan(gamma * RadiansPerDegree)))
                + sinTheta;
            double correction = value / derivative;
            theta -= correction * DegreesPerRadian;
            if (Math.Abs(correction) < 1e-14)
            {
                break;
            }
        }

        return (phi, theta);
    }

    private static (double Phi, double Theta) AzpNative(double x, double y, double gamma, double mu)
    {
        double gammaRadians = gamma * RadiansPerDegree;
        double cosGamma = Math.Cos(gammaRadians);
        double sinGamma = Math.Sin(gammaRadians);
        double phi = double.RadiansToDegrees(Math.Atan2(x, -y * cosGamma));
        double radius = Math.Sqrt((x * x) + ((y * cosGamma) * (y * cosGamma)));
        double rho = radius / (DegreesPerRadian * (mu + 1) + (y * sinGamma));
        double psi = Math.Atan2(rho, 1);
        double omega = Math.Asin(Math.Clamp(rho * mu / Math.Sqrt((rho * rho) + 1), -1, 1));
        double theta = 90 - double.RadiansToDegrees(psi + omega);
        return (phi, theta);
    }

    private static (double X, double Y) SlantPlane(double phi, double theta, double gamma, double mu)
    {
        double phiRadians = phi * RadiansPerDegree;
        double thetaRadians = theta * RadiansPerDegree;
        double sinTheta = Math.Sin(thetaRadians);
        double cosTheta = Math.Cos(thetaRadians);
        double sinPhi = Math.Sin(phiRadians);
        double cosPhi = Math.Cos(phiRadians);
        double denominator = mu + sinTheta;
        denominator += cosTheta * cosPhi * Math.Tan(gamma * RadiansPerDegree);
        double radius = DegreesPerRadian * (mu + 1) * cosTheta / denominator;
        return
        (
            radius * sinPhi,
            -radius * cosPhi / Math.Cos(gamma * RadiansPerDegree)
        );
    }

    private static double ZpnRadius(double theta, double[] pv)
    {
        double zenithDistance = (90 - theta) * RadiansPerDegree;
        double power = 1;
        double radius = 0;
        for (int index = 0; index < pv.Length; index++)
        {
            radius += pv[index] * power;
            power *= zenithDistance;
        }

        return DegreesPerRadian * radius;
    }

    private static double ZpnNative(double x, double y, double[] pv)
    {
        double radius = Math.Sqrt((x * x) + (y * y));
        double target = radius * RadiansPerDegree;
        double distance = Math.Max(target, 1e-12);
        for (int iteration = 0; iteration < 12; iteration++)
        {
            double value = 0;
            double derivative = 0;
            double power = 1;
            for (int index = 0; index < pv.Length; index++)
            {
                value += pv[index] * power;
                if (index > 0)
                {
                    derivative += index * pv[index] * (power / distance);
                }
                power *= distance;
            }

            if (Math.Abs(derivative) < 1e-14)
            {
                break;
            }

            double next = distance - (value - target) / derivative;
            if (Math.Abs(next - distance) < 1e-12)
            {
                distance = next;
                break;
            }
            distance = next;
        }

        return 90 - (distance * DegreesPerRadian);
    }

    private static (double X, double Y) MolPlane(double phi, double theta)
    {
        double latitude = theta * RadiansPerDegree;
        double beta = SolveMollweide(latitude);
        return (phi * Math.Cos(beta), DegreesPerRadian * Math.Sin(beta) * 1.4142135623730951);
    }

    private static (double Phi, double Theta) MolNative(double x, double y)
    {
        double beta = Math.Asin(Math.Clamp(y / (DegreesPerRadian * 1.4142135623730951), -1, 1));
        double latitude = Math.Asin(Math.Clamp((2 * beta + Math.Sin(2 * beta)) / Math.PI, -1, 1));
        double phi = Math.Abs(Math.Cos(beta)) < 1e-14 ? 0 : x / (Math.Cos(beta) * (2 * Math.Sqrt(2) / Math.PI));
        return (phi, latitude * DegreesPerRadian);
    }

    private static double SolveMollweide(double latitude)
    {
        double beta = latitude;
        for (int iteration = 0; iteration < 12; iteration++)
        {
            double delta = (2 * beta + Math.Sin(2 * beta) - Math.PI * Math.Sin(latitude))
                / (2 + 2 * Math.Cos(2 * beta));
            beta -= delta;
            if (Math.Abs(delta) < 1e-13)
            {
                break;
            }
        }
        return beta;
    }

    private static (double X, double Y) CypPlane(double phi, double theta, double mu, double lambda)
    {
        double thetaRadians = theta * RadiansPerDegree;
        double radius = DegreesPerRadian * (mu + lambda) * Math.Sin(thetaRadians) / (mu + Math.Cos(thetaRadians));
        return (lambda * phi, radius);
    }

    private static (double Phi, double Theta) CypNative(double x, double y, double mu, double lambda)
    {
        double radius = y;
        double q = radius / (DegreesPerRadian * (mu + lambda));
        double discriminant = Math.Sqrt(Math.Max(0, 1 + q * q * (1 - mu * mu)));
        double cosTheta = (-q * q * mu + discriminant) / (1 + q * q);
        double sinTheta = q * (mu + cosTheta);
        return (x / lambda, DegreesPerRadian * Math.Asin(Math.Clamp(sinTheta, -1, 1)));
    }

    private static (double X, double Y) ConicPlane(Projection projection, double phi, double theta, double p1, double p2)
    {
        double thetaRadians = theta * RadiansPerDegree;
        double phiRadians = phi * RadiansPerDegree;
        double n = projection == Projection.Coe ? (Math.Sin(p1 * RadiansPerDegree) + Math.Sin(p2 * RadiansPerDegree)) / 2
            : (Math.Cos(p1 * RadiansPerDegree) - Math.Cos(p2 * RadiansPerDegree)) / ((p2 - p1) * RadiansPerDegree);
        if (Math.Abs(n) < 1e-14)
        {
            n = Math.Sin(p1 * RadiansPerDegree);
        }
        double c = projection == Projection.Coe
            ? Math.Pow(Math.Cos(p1 * RadiansPerDegree), 2) + 2 * n * Math.Sin(p1 * RadiansPerDegree)
            : n * (p1 * RadiansPerDegree) + Math.Cos(p1 * RadiansPerDegree);
        double rho = projection == Projection.Coe
            ? Math.Sqrt(Math.Max(0, c - 2 * n * Math.Sin(thetaRadians))) / n
            : c - thetaRadians;
        double rho0 = projection == Projection.Coe
            ? Math.Sqrt(Math.Max(0, c - 2 * n * Math.Sin(0))) / n
            : c;
        return (DegreesPerRadian * rho * Math.Sin(n * phiRadians),
            DegreesPerRadian * (rho0 - rho * Math.Cos(n * phiRadians)));
    }

    private static (double Phi, double Theta) ConicNative(Projection projection, double x, double y, double p1, double p2)
    {
        double n = projection == Projection.Coe ? (Math.Sin(p1 * RadiansPerDegree) + Math.Sin(p2 * RadiansPerDegree)) / 2
            : (Math.Cos(p1 * RadiansPerDegree) - Math.Cos(p2 * RadiansPerDegree)) / ((p2 - p1) * RadiansPerDegree);
        if (Math.Abs(n) < 1e-14)
        {
            n = Math.Sin(p1 * RadiansPerDegree);
        }
        double c = projection == Projection.Coe
            ? Math.Pow(Math.Cos(p1 * RadiansPerDegree), 2) + 2 * n * Math.Sin(p1 * RadiansPerDegree)
            : n * (p1 * RadiansPerDegree) + Math.Cos(p1 * RadiansPerDegree);
        double rho0 = projection == Projection.Coe ? Math.Sqrt(Math.Max(0, c)) / n : c;
        double dx = x * RadiansPerDegree;
        double dy = rho0 - y * RadiansPerDegree;
        double rho = Math.CopySign(Math.Sqrt(dx * dx + dy * dy), n);
        double theta = projection == Projection.Coe
            ? Math.Asin(Math.Clamp((c - (rho * n) * (rho * n)) / (2 * n), -1, 1))
            : c - rho;
        return (DegreesPerRadian * Math.Atan2(dx, dy) / n, theta * DegreesPerRadian);
    }

    private static void Solve(ReadOnlySpan<double> matrix, ReadOnlySpan<double> value, Span<double> solution)
    {
        int size = value.Length;
        double matrixScale = 0;
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                matrixScale = Math.Max(matrixScale, Math.Abs(matrix[(i * size) + j]));
            }
        }

        Span<double> row = size <= StackWorkspaceLimit
            ? stackalloc double[size * (size + 1)]
            : new double[checked(size * (size + 1))];
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                row[(i * (size + 1)) + j] = matrix[(i * size) + j];
            }

            row[(i * (size + 1)) + size] = value[i];
        }

        for (int column = 0; column < size; column++)
        {
            int pivot = column;
            for (int candidate = column + 1; candidate < size; candidate++)
            {
                if (Math.Abs(row[(candidate * (size + 1)) + column]) > Math.Abs(row[(pivot * (size + 1)) + column]))
                {
                    pivot = candidate;
                }
            }

            double pivotValue = Math.Abs(row[(pivot * (size + 1)) + column]);
            if (!double.IsFinite(matrixScale) || matrixScale == 0 || pivotValue <= matrixScale * SingularMatrixTolerance)
            {
                throw new FitsException("The WCS transformation matrix is singular.", new FitsLocation());
            }

            if (pivot != column)
            {
                for (int j = column; j <= size; j++)
                {
                    (row[(column * (size + 1)) + j], row[(pivot * (size + 1)) + j]) =
                        (row[(pivot * (size + 1)) + j], row[(column * (size + 1)) + j]);
                }
            }

            double scale = row[(column * (size + 1)) + column];
            for (int j = column; j <= size; j++)
            {
                row[(column * (size + 1)) + j] /= scale;
            }

            for (int other = 0; other < size; other++)
            {
                if (other == column)
                {
                    continue;
                }

                double factor = row[(other * (size + 1)) + column];
                for (int j = column; j <= size; j++)
                {
                    row[(other * (size + 1)) + j] -= factor * row[(column * (size + 1)) + j];
                }
            }
        }

        for (int i = 0; i < size; i++)
        {
            solution[i] = row[(i * (size + 1)) + size];
        }
    }

    private static (double Longitude, double Latitude) Spherical(double phi, double theta, double[] euler)
    {
        if (euler[4] == 0)
        {
            double shifted = euler[1] == 0
                ? phi + euler[0] + 180 - euler[2]
                : euler[0] + euler[2] - phi;
            return (Normalize(shifted), euler[1] == 0 ? theta : -theta);
        }

        double deltaPhi = phi - euler[2];
        double sinTheta = Math.Sin(double.DegreesToRadians(theta));
        double cosTheta = Math.Cos(double.DegreesToRadians(theta));
        double sinPhi = Math.Sin(double.DegreesToRadians(deltaPhi));
        double cosPhi = Math.Cos(double.DegreesToRadians(deltaPhi));
        double x = (sinTheta * euler[4]) - (cosTheta * euler[3] * cosPhi);
        double y = -cosTheta * sinPhi;
        double delta = Math.Abs(x) < 1e-5
            ? -Math.Cos(double.DegreesToRadians(theta + euler[1])) + (cosTheta * euler[3] * (1 - cosPhi))
            : x;
        double longitude = Math.Abs(delta) < 1e-15 && Math.Abs(y) < 1e-15
            ? euler[0] + (euler[1] < 90 ? deltaPhi + 180 : -deltaPhi)
            : euler[0] + double.RadiansToDegrees(Math.Atan2(y, delta));
        double latitude = Math.Abs(deltaPhi % 180) < 1e-12
            ? theta + (cosPhi * euler[1])
            : double.RadiansToDegrees(Math.Asin(Math.Clamp((sinTheta * euler[3]) + (cosTheta * euler[4] * cosPhi), -1, 1)));
        if (latitude > 90)
        {
            latitude = 180 - latitude;
        }
        else if (latitude < -90)
        {
            latitude = -180 - latitude;
        }

        return (Normalize(longitude), latitude);
    }

    private static double[] Euler(string projection, double longitude0, double latitude0, double longitudeOfPole, double latitudeOfPole)
    {
        double theta0 = projection.Equals("CAR", StringComparison.OrdinalIgnoreCase)
            || projection.Equals("CEA", StringComparison.OrdinalIgnoreCase)
            || projection.Equals("HPX", StringComparison.OrdinalIgnoreCase)
            || projection.Equals("AIT", StringComparison.OrdinalIgnoreCase)
            || projection.Equals("MER", StringComparison.OrdinalIgnoreCase)
            || projection.Equals("SFL", StringComparison.OrdinalIgnoreCase)
            || projection.Equals("PAR", StringComparison.OrdinalIgnoreCase)
            || projection.Equals("MOL", StringComparison.OrdinalIgnoreCase)
            || projection.Equals("CYP", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 90;
        const double phi0 = 0;
        double phiP = double.IsNaN(longitudeOfPole) ? (latitude0 < theta0 ? 180 : 0) + phi0 : longitudeOfPole;
        double latitudeP;
        double longitudeP;
        if (theta0 == 90)
        {
            longitudeP = longitude0;
            latitudeP = latitude0;
        }
        else
        {
            double phiDelta = double.DegreesToRadians(phiP - phi0);
            double sinPhi = Math.Sin(phiDelta);
            double cosPhi = Math.Cos(phiDelta);
            double sinTheta = Math.Sin(double.DegreesToRadians(theta0));
            double cosTheta = Math.Cos(double.DegreesToRadians(theta0));
            double sinLatitude = Math.Sin(double.DegreesToRadians(latitude0));
            double cosLatitude = Math.Cos(double.DegreesToRadians(latitude0));
            double radius = Math.Sqrt((cosTheta * cosPhi * cosTheta * cosPhi) + (sinTheta * sinTheta));
            double u = double.RadiansToDegrees(Math.Atan2(sinTheta, cosTheta * cosPhi));
            double v = double.RadiansToDegrees(Math.Acos(Math.Clamp(sinLatitude / radius, -1, 1)));
            double first = NormalizeSigned(u + v);
            double second = NormalizeSigned(u - v);
            bool defaultLatitudePole = !double.IsFinite(latitudeOfPole) || latitudeOfPole == 999;
            latitudeP = defaultLatitudePole
                ? first
                : Math.Abs(latitudeOfPole - first) <= Math.Abs(latitudeOfPole - second) ? first : second;
            if (Math.Abs(latitudeP) > 90)
            {
                latitudeP = Math.Abs(first) <= 90 ? first : second;
            }

            double divisor = Math.Cos(double.DegreesToRadians(latitudeP)) * cosLatitude;
            double x = (sinTheta - (Math.Sin(double.DegreesToRadians(latitudeP)) * sinLatitude)) / divisor;
            double y = cosLatitude == 0 ? 0 : sinPhi * cosTheta / cosLatitude;
            longitudeP = longitude0 - double.RadiansToDegrees(Math.Atan2(y, x));
            if (longitude0 >= 0 && longitudeP < 0)
            {
                longitudeP += 360;
            }
        }

        double tilt = 90 - latitudeP;
        return
        [
            longitudeP,
            tilt,
            phiP,
            Math.Cos(double.DegreesToRadians(tilt)),
            Math.Sin(double.DegreesToRadians(tilt)),
        ];
    }

    private static double Normalize(double angle)
    {
        angle %= 360;
        if (angle < 0)
        {
            angle += 360;
        }

        return angle;
    }

    private static double NormalizeSigned(double angle)
    {
        angle = Normalize(angle);
        return angle > 180 ? angle - 360 : angle;
    }

    private static int AxisCount(FitsHeader header, char version)
    {
        if (header.TryGetCard(Name("WCSAXES", version), out var count))
        {
            return ValidAxisCount(count, Name("WCSAXES", version));
        }

        if (version != ' ' && header.TryGetCard("WCSAXES", out count))
        {
            return ValidAxisCount(count, "WCSAXES");
        }

        if (header.TryGetCard("NAXIS", out var axes))
        {
            return ValidAxisCount(axes, "NAXIS");
        }

        return 0;
    }

    private static int ValidAxisCount(FitsCard card, string keyword)
    {
        if (card.Kind != FitsValueKind.Integer || card.Integer is < 0 or > MaximumAxes)
        {
            throw new FitsException($"{keyword} must be from 0 through {MaximumAxes}.", new FitsLocation(Keyword: keyword));
        }

        return (int)card.Integer;
    }

    private static bool TryProjection(string type, out string coordinate, out string algorithm)
    {
        coordinate = "";
        algorithm = "";
        if (type.Length < 8 || type[4] != '-')
        {
            return false;
        }

        coordinate = type[..4];
        algorithm = type[5..8].Trim().ToUpperInvariant();
        return algorithm.Length > 0;
    }

    private static bool IsLongitude(string coordinate) =>
        coordinate.StartsWith("RA", StringComparison.OrdinalIgnoreCase) || coordinate.Contains("LON", StringComparison.OrdinalIgnoreCase);

    private static bool IsLatitude(string coordinate) =>
        coordinate.StartsWith("DEC", StringComparison.OrdinalIgnoreCase) || coordinate.Contains("LAT", StringComparison.OrdinalIgnoreCase);

    private static double Read(FitsHeader header, string keyword, char version, double fallback)
    {
        if (!header.TryGetCard(Name(keyword, version), out var card) &&
            (version == ' ' || !header.TryGetCard(keyword, out card)))
        {
            return fallback;
        }

        return card.Kind is FitsValueKind.Real or FitsValueKind.Integer ? card.GetDouble() : fallback;
    }

    private static string Text(FitsHeader header, string keyword, char version)
    {
        if (header.TryGetCard(Name(keyword, version), out var card) ||
            (version != ' ' && header.TryGetCard(keyword, out card)))
        {
            return card.GetString() ?? "";
        }

        return "";
    }

    private static double? OptionalFinite(FitsHeader header, string keyword, char version)
    {
        if (!header.TryGetCard(Name(keyword, version), out var card) &&
            (version == ' ' || !header.TryGetCard(keyword, out card)))
        {
            return null;
        }

        if (card.Kind is not (FitsValueKind.Real or FitsValueKind.Integer))
        {
            throw new FitsException($"{keyword} must be numeric.", new FitsLocation(Keyword: keyword));
        }

        double value = card.GetDouble();
        EnsureFinite(value, keyword);
        return value;
    }

    private static int? OptionalInteger(FitsHeader header, string keyword, char version)
    {
        if (!header.TryGetCard(Name(keyword, version), out var card) &&
            (version == ' ' || !header.TryGetCard(keyword, out card)))
        {
            return null;
        }

        if (card.Kind != FitsValueKind.Integer)
        {
            throw new FitsException($"{keyword} must be an integer.", new FitsLocation(Keyword: keyword));
        }

        return checked((int)card.Integer);
    }

    private static bool IsSpectral(string type) =>
        type.StartsWith("FREQ", StringComparison.OrdinalIgnoreCase)
        || type.StartsWith("ENER", StringComparison.OrdinalIgnoreCase)
        || type.StartsWith("WAVE", StringComparison.OrdinalIgnoreCase)
        || type.StartsWith("AWAV", StringComparison.OrdinalIgnoreCase)
        || type.StartsWith("VELO", StringComparison.OrdinalIgnoreCase)
        || type.StartsWith("VRAD", StringComparison.OrdinalIgnoreCase)
        || type.StartsWith("VOPT", StringComparison.OrdinalIgnoreCase)
        || type.StartsWith("BETA", StringComparison.OrdinalIgnoreCase)
        || type.StartsWith("ZOPT", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpectralAlgorithm(string algorithm) =>
        algorithm is "LOG" or "TAB" or "F2W" or "W2F" or "V2F" or "F2V"
            or "GRW" or "GRZ" or "GRA";

    private static bool IsAxisAlgorithm(string algorithm) =>
        IsSpectralAlgorithm(algorithm);

    private double ApplySpectralForward(double value, int axis)
    {
        if (_axisAlgorithms[axis] != "LOG")
        {
            return value;
        }

        double reference = _referenceValue[axis];
        if (reference == 0)
        {
            throw new FitsException("A logarithmic spectral or time axis requires a non-zero CRVAL.", new FitsLocation(Keyword: $"CRVAL{axis + 1}"));
        }

        double result = reference * Math.Exp((value - reference) / reference);
        EnsureFinite(result, $"CTYPE{axis + 1}");
        return result;
    }

    private double ApplySpectralInverse(double value, int axis)
    {
        if (_axisAlgorithms[axis] != "LOG")
        {
            return value;
        }

        double reference = _referenceValue[axis];
        if (reference == 0 || !double.IsFinite(value) || value / reference <= 0)
        {
            throw new FitsException("A logarithmic spectral or time coordinate must have the same sign as CRVAL.", new FitsLocation());
        }

        return reference + (reference * Math.Log(value / reference));
    }

    private static void ValidateProjectionParameters(string projection, double[] pv, int axis)
    {
        if (projection is "CEA" && pv[0] == 0)
        {
            throw new FitsException("PV" + axis + "_1 must be non-zero for CEA.", new FitsLocation(Keyword: "PV" + axis + "_1"));
        }

        if (projection is "CYP" && pv[1] == 0)
        {
            throw new FitsException("PV" + axis + "_2 must be non-zero for CYP.", new FitsLocation(Keyword: "PV" + axis + "_2"));
        }

        if (projection is "AZP" or "SZP" && Math.Abs(pv[1]) == 90)
        {
            throw new FitsException("The AZP/SZP gamma parameter must not be ±90 degrees.", new FitsLocation(Keyword: "PV" + axis + "_2"));
        }
    }

    private static bool IsTime(string type) =>
        type.StartsWith("TIME", StringComparison.OrdinalIgnoreCase);

    private static void EnsureFinite(double value, string keyword)
    {
        if (!double.IsFinite(value))
        {
            throw new FitsException($"{keyword} must be finite.", new FitsLocation(Keyword: keyword));
        }
    }

    private static void EnsureProjectionResult(double x, double y, Projection projection)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new FitsException(
                $"The {projection} projection is outside its mathematical domain.",
                new FitsLocation());
        }
    }

    private static string Name(string keyword, char version) => version == ' ' ? keyword : keyword + version;

    private static Projection ProjectionOf(string? projection) =>
        projection is not null && ProjectionKinds.TryGetValue(projection, out Projection kind)
            ? kind
            : Projection.None;

    private static double ProjectionDefault(string? projection, int parameter) =>
        projection?.ToUpperInvariant() switch
        {
            "CEA" when parameter == 1 => 1,
            "CYP" when parameter == 1 => 1,
            "CYP" when parameter == 2 => 1,
            "AZP" when parameter == 1 => 0,
            "SZP" when parameter == 1 => 0,
            "SZP" when parameter == 2 => 0,
            "HPX" when parameter == 1 => 4,
            "HPX" when parameter == 2 => 3,
            "ZPN" when parameter == 1 => 1,
            _ => 0,
        };

    private enum Projection
    {
        None,
        Arc,
        Ait,
        Azp,
        Szp,
        Car,
        Cea,
        Cyp,
        Mer,
        Sfl,
        Par,
        Mol,
        Coe,
        Cod,
        Coo,
        Bon,
        Hpx,
        Sin,
        Stg,
        Tan,
        Zea,
        Zpn,
        Air,
        Cop,
    }
}
