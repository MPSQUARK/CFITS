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

    private static readonly FrozenDictionary<string, Projection> ProjectionKinds =
        new Dictionary<string, Projection>(StringComparer.OrdinalIgnoreCase)
    {
        ["ARC"] = Projection.Arc,
        ["CAR"] = Projection.Car,
        ["SIN"] = Projection.Sin,
        ["STG"] = Projection.Stg,
        ["TAN"] = Projection.Tan,
        ["ZEA"] = Projection.Zea,
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

    private FitsWorld(double[] referencePixel, double[] referenceValue, double[] scale, double[] matrix, bool scaledMatrix, int longitude, int latitude, string? projection, double[] euler)
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
    }

    public int Axes => _referencePixel.Length;

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
            scale[axis - 1] = Read(header, $"CDELT{axis}", version, 1);
            if (!double.IsFinite(scale[axis - 1]) || scale[axis - 1] == 0)
            {
                throw new FitsException("CDELT must be a finite, non-zero increment.", new FitsLocation(Keyword: $"CDELT{axis}"));
            }
            type[axis - 1] = header.TryGetCard(Name($"CTYPE{axis}", version), out var kind) ? kind.GetString() ?? "" : "";
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
        return new FitsWorld(referencePixel, referenceValue, scale, matrix, cd, longitude, latitude, active, euler);
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
            var native = Native(_projectionKind, planeX, planeY);
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
            var plane = Plane(_projectionKind, native.Phi, native.Theta);
            for (int axis = 0; axis < Axes; axis++)
            {
                intermediate[axis] = axis == _longitude ? plane.X : axis == _latitude ? plane.Y : world[axis] - _referenceValue[axis];
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

    private static (double Phi, double Theta) Native(Projection projection, double x, double y)
    {
        double radius = Math.Sqrt((x * x) + (y * y));
        double phi = double.RadiansToDegrees(Math.Atan2(x, -y));
        double theta = projection switch
        {
            Projection.Tan => radius == 0 ? 90 : double.RadiansToDegrees(Math.Atan(DegreesPerRadian / radius)),
            Projection.Sin => radius * RadiansPerDegree > 1 ? double.NaN : double.RadiansToDegrees(Math.Acos(radius * RadiansPerDegree)),
            Projection.Arc => 90 - radius,
            Projection.Stg => 90 - (2 * double.RadiansToDegrees(Math.Atan(RadiansPerDegree * radius / 2))),
            Projection.Zea => 90 - (2 * double.RadiansToDegrees(Math.Asin(RadiansPerDegree * radius / 2))),
            Projection.Car => y,
            _ => throw new FitsException("Projection is not implemented.", new FitsLocation()),
        };
        if (projection == Projection.Car)
        {
            phi = x;
        }

        return (phi, theta);
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

    private static (double X, double Y) Plane(Projection projection, double phi, double theta)
    {
        if (projection == Projection.Car)
        {
            return (phi, theta);
        }

        double phiRadians = phi * RadiansPerDegree;
        double thetaRadians = theta * RadiansPerDegree;
        (double sinPhi, double cosPhi) = Math.SinCos(phiRadians);
        double radius = projection switch
        {
            Projection.Tan => Math.Abs(theta - 90) < 1e-12 ? 0 : DegreesPerRadian / Math.Tan(thetaRadians),
            Projection.Sin => DegreesPerRadian * Math.Cos(thetaRadians),
            Projection.Arc => 90 - theta,
            Projection.Stg => 2 * DegreesPerRadian * Math.Tan((Math.PI / 2 - thetaRadians) / 2),
            Projection.Zea => 2 * DegreesPerRadian * Math.Sin((Math.PI / 2 - thetaRadians) / 2),
            _ => throw new FitsException("Projection is not implemented.", new FitsLocation()),
        };
        return (radius * sinPhi, -radius * cosPhi);
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
        double theta0 = projection.Equals("CAR", StringComparison.OrdinalIgnoreCase) ? 0 : 90;
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
            latitudeP = Math.Abs(latitudeOfPole - first) <= Math.Abs(latitudeOfPole - second) ? first : second;
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

        if (version == ' ' && header.TryGetCard("NAXIS", out var axes))
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
        algorithm = type[5..8].Trim();
        return algorithm.Length > 0;
    }

    private static bool IsLongitude(string coordinate) =>
        coordinate.StartsWith("RA", StringComparison.OrdinalIgnoreCase) || coordinate.Contains("LON", StringComparison.OrdinalIgnoreCase);

    private static bool IsLatitude(string coordinate) =>
        coordinate.StartsWith("DEC", StringComparison.OrdinalIgnoreCase) || coordinate.Contains("LAT", StringComparison.OrdinalIgnoreCase);

    private static double Read(FitsHeader header, string keyword, char version, double fallback)
    {
        if (!header.TryGetCard(Name(keyword, version), out var card))
        {
            return fallback;
        }

        return card.Kind is FitsValueKind.Real or FitsValueKind.Integer ? card.GetDouble() : fallback;
    }

    private static string Name(string keyword, char version) => version == ' ' ? keyword : keyword + version;

    private static Projection ProjectionOf(string? projection) =>
        projection is not null && ProjectionKinds.TryGetValue(projection, out Projection kind)
            ? kind
            : Projection.None;

    private enum Projection
    {
        None,
        Arc,
        Car,
        Sin,
        Stg,
        Tan,
        Zea,
    }
}
