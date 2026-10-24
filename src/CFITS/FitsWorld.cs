namespace CFITS;

/// <summary>
/// Pixel-to-world conversion from the keywords in FITS section 8.2.
/// Celestial values use the zenithal and plate-carree projections named by CTYPE.
/// Pixels are 1-based, matching CRPIX.
/// </summary>
public sealed class FitsWorld
{
    private readonly double[] _referencePixel;
    private readonly double[] _referenceValue;
    private readonly double[] _scale;
    private readonly double[,] _matrix;
    private readonly bool _scaledMatrix;
    private readonly int _longitude;
    private readonly int _latitude;
    private readonly string? _projection;
    private readonly double[] _euler;

    private FitsWorld(double[] referencePixel, double[] referenceValue, double[] scale, double[,] matrix, bool scaledMatrix, int longitude, int latitude, string? projection, double[] euler)
    {
        _referencePixel = referencePixel;
        _referenceValue = referenceValue;
        _scale = scale;
        _matrix = matrix;
        _scaledMatrix = scaledMatrix;
        _longitude = longitude;
        _latitude = latitude;
        _projection = projection;
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

        var matrix = new double[count, count];
        for (int axis = 0; axis < count; axis++)
        {
            for (int other = 0; other < count; other++)
            {
                string keyword = cd ? $"CD{axis + 1}_{other + 1}" : $"PC{axis + 1}_{other + 1}";
                double fallback = !cd && axis == other ? 1 : 0;
                matrix[axis, other] = Read(header, keyword, version, fallback);
            }
        }

        if (!cd && !explicitPc)
        {
            double rotation = Read(header, "CROTA2", version, 0);
            if (count >= 2 && rotation != 0)
            {
                double radians = rotation * Math.PI / 180;
                matrix[0, 0] = Math.Cos(radians);
                matrix[0, 1] = -Math.Sin(radians);
                matrix[1, 0] = Math.Sin(radians);
                matrix[1, 1] = Math.Cos(radians);
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
                longitude = axis;
            }
            else if (IsLatitude(coordinate))
            {
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
        if (pixel.Length != Axes)
        {
            throw new FitsException("The pixel coordinate does not have one value per axis.", new FitsLocation());
        }

        var world = new double[Axes];
        for (int axis = 0; axis < Axes; axis++)
        {
            double intermediate = 0;
            for (int other = 0; other < Axes; other++)
            {
                intermediate += _matrix[axis, other] * (pixel[other] - _referencePixel[other]);
            }

            if (!_scaledMatrix)
            {
                intermediate *= _scale[axis];
            }

            world[axis] = _referenceValue[axis] + intermediate;
        }

        if (_projection is null)
        {
            return world;
        }

        double planeX = world[_longitude] - _referenceValue[_longitude];
        double planeY = world[_latitude] - _referenceValue[_latitude];
        double phi;
        double theta;
        if (_projection.Equals("CAR", StringComparison.OrdinalIgnoreCase))
        {
            phi = planeX;
            theta = planeY;
        }
        else
        {
            var native = Native(_projection, planeX, planeY);
            phi = native.Phi;
            theta = native.Theta;
        }

        var celestial = Spherical(phi, theta, _euler);
        world[_longitude] = celestial.Longitude;
        world[_latitude] = celestial.Latitude;
        return world;
    }

    public double[] WorldToPixel(ReadOnlySpan<double> world)
    {
        if (world.Length != Axes)
        {
            throw new FitsException("The world coordinate does not have one value per axis.", new FitsLocation());
        }

        var intermediate = new double[Axes];
        if (_projection is null)
        {
            for (int axis = 0; axis < Axes; axis++)
            {
                intermediate[axis] = world[axis] - _referenceValue[axis];
            }
        }
        else
        {
            var native = InverseSpherical(world[_longitude], world[_latitude], _euler);
            var plane = Plane(_projection, native.Phi, native.Theta);
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

        double[] offset = Solve(_matrix, intermediate);
        var pixel = new double[Axes];
        for (int axis = 0; axis < Axes; axis++)
        {
            pixel[axis] = offset[axis] + _referencePixel[axis];
        }

        return pixel;
    }

    private static (double Phi, double Theta) Native(string projection, double x, double y)
    {
        double radius = Math.Sqrt((x * x) + (y * y));
        double phi = double.RadiansToDegrees(Math.Atan2(x, -y));
        double theta = projection.ToUpperInvariant() switch
        {
            "TAN" => radius == 0 ? 90 : double.RadiansToDegrees(Math.Atan((180 / Math.PI) / radius)),
            "SIN" => radius * Math.PI / 180 > 1 ? double.NaN : double.RadiansToDegrees(Math.Acos(radius * Math.PI / 180)),
            "ARC" => 90 - radius,
            "STG" => 90 - (2 * double.RadiansToDegrees(Math.Atan((Math.PI / 180) * radius / 2))),
            "ZEA" => 90 - (2 * double.RadiansToDegrees(Math.Asin((Math.PI / 180) * radius / 2))),
            "CAR" => y,
            _ => throw new FitsException($"Projection '{projection}' is not implemented.", new FitsLocation()),
        };
        if (projection.Equals("CAR", StringComparison.OrdinalIgnoreCase))
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
        double sinLatitude = Math.Sin(double.DegreesToRadians(latitude));
        double cosLatitude = Math.Cos(double.DegreesToRadians(latitude));
        double sinLongitude = Math.Sin(double.DegreesToRadians(deltaLongitude));
        double cosLongitude = Math.Cos(double.DegreesToRadians(deltaLongitude));
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

    private static (double X, double Y) Plane(string projection, double phi, double theta)
    {
        if (projection.Equals("CAR", StringComparison.OrdinalIgnoreCase))
        {
            return (phi, theta);
        }

        double phiRadians = double.DegreesToRadians(phi);
        double thetaRadians = double.DegreesToRadians(theta);
        double radius = projection.ToUpperInvariant() switch
        {
            "TAN" => Math.Abs(theta - 90) < 1e-12 ? 0 : (180 / Math.PI) / Math.Tan(thetaRadians),
            "SIN" => (180 / Math.PI) * Math.Cos(thetaRadians),
            "ARC" => 90 - theta,
            "STG" => 2 * (180 / Math.PI) * Math.Tan((Math.PI / 2 - thetaRadians) / 2),
            "ZEA" => 2 * (180 / Math.PI) * Math.Sin((Math.PI / 2 - thetaRadians) / 2),
            _ => throw new FitsException($"Projection '{projection}' is not implemented.", new FitsLocation()),
        };
        return (radius * Math.Sin(phiRadians), -radius * Math.Cos(phiRadians));
    }

    private static double[] Solve(double[,] matrix, double[] value)
    {
        int size = value.Length;
        var row = new double[size, size + 1];
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                row[i, j] = matrix[i, j];
            }

            row[i, size] = value[i];
        }

        for (int column = 0; column < size; column++)
        {
            int pivot = column;
            for (int candidate = column + 1; candidate < size; candidate++)
            {
                if (Math.Abs(row[candidate, column]) > Math.Abs(row[pivot, column]))
                {
                    pivot = candidate;
                }
            }

            if (Math.Abs(row[pivot, column]) < 1e-12)
            {
                throw new FitsException("The WCS transformation matrix is singular.", new FitsLocation());
            }

            if (pivot != column)
            {
                for (int j = column; j <= size; j++)
                {
                    (row[column, j], row[pivot, j]) = (row[pivot, j], row[column, j]);
                }
            }

            double scale = row[column, column];
            for (int j = column; j <= size; j++)
            {
                row[column, j] /= scale;
            }

            for (int other = 0; other < size; other++)
            {
                if (other == column)
                {
                    continue;
                }

                double factor = row[other, column];
                for (int j = column; j <= size; j++)
                {
                    row[other, j] -= factor * row[column, j];
                }
            }
        }

        var solution = new double[size];
        for (int i = 0; i < size; i++)
        {
            solution[i] = row[i, size];
        }

        return solution;
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
            return checked((int)count.GetInt64());
        }

        if (version == ' ' && header.TryGetCard("NAXIS", out var axes))
        {
            return checked((int)axes.GetInt64());
        }

        return 0;
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
}
