using System;

namespace PolishOpenData.Geo;

/// <summary>
/// PL-1992 (EPSG:2180) to WGS-84: Transverse Mercator on GRS80, central meridian 19°E, scale 0.9993,
/// false easting 500 000, false northing −5 300 000. Krüger n-series (Karney 2011) to order n^6;
/// with n = 1.7e-3 the truncation error is about n^7 (1e-20 relative), far below the 1e-9° the tests demand.
/// GRS80 and WGS-84 differ by less than 0.1 mm, which is ignored.
/// </summary>
internal static class Pl1992Projection
{
    private const double A = 6378137.0;
    private const double F = 1 / 298.257222101;
    private const double K0 = 0.9993;
    private const double FalseEasting = 500_000;
    private const double FalseNorthing = -5_300_000;
    private const double CentralMeridian = 19 * Math.PI / 180;

    private static readonly double N = F / (2 - F);
    private static readonly double Rect = A / (1 + N) * (1 + N * N / 4 + Math.Pow(N, 4) / 64 + Math.Pow(N, 6) / 256);

    // Row j-1 holds the coefficients of n^1..n^6 for the j-th term of each series.
    private static readonly double[] Alpha = Series(
        [1.0 / 2, -2.0 / 3, 5.0 / 16, 41.0 / 180, -127.0 / 288, 7891.0 / 37800],
        [0, 13.0 / 48, -3.0 / 5, 557.0 / 1440, 281.0 / 630, -1983433.0 / 1935360],
        [0, 0, 61.0 / 240, -103.0 / 140, 15061.0 / 26880, 167603.0 / 181440],
        [0, 0, 0, 49561.0 / 161280, -179.0 / 168, 6601661.0 / 7257600],
        [0, 0, 0, 0, 34729.0 / 80640, -3418889.0 / 1995840],
        [0, 0, 0, 0, 0, 212378941.0 / 319334400]);

    private static readonly double[] Beta = Series(
        [1.0 / 2, -2.0 / 3, 37.0 / 96, -1.0 / 360, -81.0 / 512, 96199.0 / 604800],
        [0, 1.0 / 48, 1.0 / 15, -437.0 / 1440, 46.0 / 105, -1118711.0 / 3870720],
        [0, 0, 17.0 / 480, -37.0 / 840, -209.0 / 4480, 5569.0 / 90720],
        [0, 0, 0, 4397.0 / 161280, -11.0 / 504, -830251.0 / 7257600],
        [0, 0, 0, 0, 4583.0 / 161280, -108847.0 / 3991680],
        [0, 0, 0, 0, 0, 20648693.0 / 638668800]);

    private static readonly double[] Delta = Series(
        [2, -2.0 / 3, -2, 116.0 / 45, 26.0 / 45, -2854.0 / 675],
        [0, 7.0 / 3, -8.0 / 5, -227.0 / 45, 2704.0 / 315, 2323.0 / 945],
        [0, 0, 56.0 / 15, -136.0 / 35, -1262.0 / 105, 73814.0 / 2835],
        [0, 0, 0, 4279.0 / 630, -332.0 / 35, -399572.0 / 14175],
        [0, 0, 0, 0, 4174.0 / 315, -144838.0 / 6237],
        [0, 0, 0, 0, 0, 601676.0 / 22275]);

    public static Pl1992Point ToPl1992(Wgs84Point p)
    {
        var phi = p.Latitude * Math.PI / 180;
        var dLambda = p.Longitude * Math.PI / 180 - CentralMeridian;
        var e = 2 * Math.Sqrt(N) / (1 + N);
        var sinPhi = Math.Sin(phi);
        var t = Math.Sinh(Atanh(sinPhi) - e * Atanh(e * sinPhi));
        var xi0 = Math.Atan2(t, Math.Cos(dLambda));
        var eta0 = Atanh(Math.Sin(dLambda) / Math.Sqrt(1 + t * t));

        var xi = xi0;
        var eta = eta0;
        for (var j = 1; j <= 6; j++)
        {
            xi += Alpha[j - 1] * Math.Sin(2 * j * xi0) * Math.Cosh(2 * j * eta0);
            eta += Alpha[j - 1] * Math.Cos(2 * j * xi0) * Math.Sinh(2 * j * eta0);
        }

        return new Pl1992Point(FalseEasting + K0 * Rect * eta, FalseNorthing + K0 * Rect * xi);
    }

    public static Wgs84Point ToWgs84(Pl1992Point p)
    {
        var xi = (p.Northing - FalseNorthing) / (K0 * Rect);
        var eta = (p.Easting - FalseEasting) / (K0 * Rect);

        var xi0 = xi;
        var eta0 = eta;
        for (var j = 1; j <= 6; j++)
        {
            xi0 -= Beta[j - 1] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            eta0 -= Beta[j - 1] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
        }

        var chi = Math.Asin(Math.Sin(xi0) / Math.Cosh(eta0));
        var phi = chi;
        for (var j = 1; j <= 6; j++)
        {
            phi += Delta[j - 1] * Math.Sin(2 * j * chi);
        }

        var lambda = CentralMeridian + Math.Atan2(Math.Sinh(eta0), Math.Cos(xi0));
        return new Wgs84Point(Snap(phi * 180 / Math.PI, 48.9, 55.0), Snap(lambda * 180 / Math.PI, 14.0, 24.2));
    }

    // A point on the edge of the range can round-trip a few ulps outside it; pull such results back onto the edge.
    private static double Snap(double degrees, double min, double max) =>
        degrees < min && min - degrees < 1e-9 ? min : degrees > max && degrees - max < 1e-9 ? max : degrees;

    // Math.Atanh does not exist on netstandard2.0.
    private static double Atanh(double x) => 0.5 * Math.Log((1 + x) / (1 - x));

    private static double[] Series(params double[][] rows)
    {
        var result = new double[rows.Length];
        for (var j = 0; j < rows.Length; j++)
        {
            var power = 1.0;
            for (var k = 0; k < 6; k++)
            {
                power *= N;
                result[j] += rows[j][k] * power;
            }
        }

        return result;
    }
}
