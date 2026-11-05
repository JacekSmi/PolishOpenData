using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PolishOpenData.Geo;
using PolishOpenData.Uldk;

namespace PolishOpenData.Demo.Geo;

/// <summary>Turns a ULDK parcel boundary (WKT) into the GeoJSON geometry that Leaflet draws.</summary>
internal static class ParcelOutline
{
    private const int Pl1992 = 2180;
    private const int Wgs84 = 4326;

    // The writer rounds to centimetres (7 decimals of a degree) and writes the shortest text that reads back as that number.
    private const int Decimals = 7;

    /// <summary>
    /// A GeoJSON <c>Polygon</c> or <c>MultiPolygon</c> (longitude, latitude), or <c>null</c> when the text is EMPTY, has a Z or M
    /// dimension, is another geometry type, is malformed, has an unknown SRID, or has a vertex that is not in Poland once converted.
    /// Never throws. A hole stays an inner ring of its polygon; every ring keeps its closing vertex.
    /// </summary>
    public static string? ToGeoJson(ParcelGeometry geometry)
    {
        if (geometry.Srid is not (Pl1992 or Wgs84) || !WktReader.TryRead(geometry.Wkt, geometry.Srid, out var multi, out var polygons))
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", multi ? "MultiPolygon" : "Polygon");
            writer.WriteStartArray("coordinates");
            foreach (var polygon in polygons)
            {
                if (multi)
                {
                    writer.WriteStartArray();
                }

                foreach (var ring in polygon)
                {
                    writer.WriteStartArray();
                    foreach (var (lon, lat) in ring)
                    {
                        writer.WriteStartArray();
                        writer.WriteNumberValue(Math.Round(lon, Decimals));
                        writer.WriteNumberValue(Math.Round(lat, Decimals));
                        writer.WriteEndArray();
                    }

                    writer.WriteEndArray();
                }

                if (multi)
                {
                    writer.WriteEndArray();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// A small reader for <c>POLYGON</c> and <c>MULTIPOLYGON</c> in two dimensions. Vertices are checked and converted to
    /// (longitude, latitude) as they are read, so a successful read leaves nothing that could make the JSON writer throw.
    /// </summary>
    private sealed class WktReader
    {
        private readonly string _text;
        private readonly int _srid;
        private int _position;

        private WktReader(string text, int srid)
        {
            _text = text;
            _srid = srid;
        }

        public static bool TryRead(string text, int srid, out bool multi, out List<List<List<(double Lon, double Lat)>>> polygons)
        {
            var reader = new WktReader(text, srid);
            polygons = [];
            multi = false;

            var keyword = reader.ReadWord();
            if (keyword.Equals("POLYGON", StringComparison.OrdinalIgnoreCase))
            {
                if (!reader.TryReadPolygon(out var rings))
                {
                    return false;
                }

                polygons.Add(rings);
            }
            else if (keyword.Equals("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase))
            {
                multi = true;
                if (!reader.Take('('))
                {
                    return false;
                }

                do
                {
                    if (!reader.TryReadPolygon(out var rings))
                    {
                        return false;
                    }

                    polygons.Add(rings);
                }
                while (reader.Take(','));

                if (!reader.Take(')'))
                {
                    return false;
                }
            }
            else
            {
                return false; // EMPTY, Z, M and other types all fail here or at the next token
            }

            reader.SkipWhitespace();
            return reader._position == text.Length;
        }

        // polygon = "(" ring { "," ring } ")"
        private bool TryReadPolygon(out List<List<(double Lon, double Lat)>> rings)
        {
            rings = [];
            if (!Take('('))
            {
                return false;
            }

            do
            {
                if (!TryReadRing(out var ring))
                {
                    return false;
                }

                rings.Add(ring);
            }
            while (Take(','));

            return Take(')');
        }

        // ring = "(" x y { "," x y } ")", at least four vertices, the last equal to the first
        private bool TryReadRing(out List<(double Lon, double Lat)> ring)
        {
            ring = [];
            if (!Take('('))
            {
                return false;
            }

            do
            {
                if (!TryReadNumber(out var x) || !TryReadNumber(out var y) || !TryConvert(x, y, out var vertex))
                {
                    return false;
                }

                ring.Add(vertex);
            }
            while (Take(','));

            return Take(')') && ring.Count >= 4 && ring[0] == ring[^1];
        }

        private bool TryConvert(double x, double y, out (double Lon, double Lat) vertex)
        {
            vertex = default;
            if (_srid == Wgs84)
            {
                // x is longitude, y is latitude; swapped axes and non-finite numbers fail the range check.
                vertex = (x, y);
                return Wgs84Point.IsInPoland(latitude: y, longitude: x);
            }

            if (!Pl1992Point.IsInPoland(x, y))
            {
                return false;
            }

            try
            {
                var point = new Pl1992Point(x, y).ToWgs84();
                vertex = (point.Longitude, point.Latitude);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false; // inside the grid's range, but its WGS-84 position is outside Poland
            }
        }

        private string ReadWord()
        {
            SkipWhitespace();
            var start = _position;
            while (_position < _text.Length && char.IsAsciiLetter(_text[_position]))
            {
                _position++;
            }

            return _text[start.._position];
        }

        private bool TryReadNumber(out double value)
        {
            SkipWhitespace();
            var start = _position;
            while (_position < _text.Length && (char.IsAsciiDigit(_text[_position]) || _text[_position] is '+' or '-' or '.' or 'e' or 'E'))
            {
                _position++;
            }

            return double.TryParse(_text.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private bool Take(char c)
        {
            SkipWhitespace();
            if (_position < _text.Length && _text[_position] == c)
            {
                _position++;
                return true;
            }

            return false;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }
    }
}
