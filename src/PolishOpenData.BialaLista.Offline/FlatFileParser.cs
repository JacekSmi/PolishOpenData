using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>Streams the flat-file JSON into <see cref="ParsedFlatFile"/> without ever holding the document or its hash strings in memory.</summary>
internal sealed class FlatFileParser
{
    private const int PrefixLength = 16;
    private const int HexLength = 128;
    private const int MaskLength = 26;
    private const int InitialBuffer = 64 * 1024;
    private const int MaxBuffer = 16 * 1024 * 1024; // a single token larger than this is not a flat file

    private enum Section { None, Unknown, Header, Active, Exempt, Masks }

    private readonly DateOnly _fileDate;
    private readonly ByteChunks _active = new();
    private readonly ByteChunks _exempt = new();
    private readonly List<string> _masks = [];
    private Section _section;
    private bool _sawActive;
    private bool _sawExempt;
    private bool _sawMasks;
    private string? _headerField;
    private DateOnly? _generatedOn;
    private int? _transformations;

    private FlatFileParser(DateOnly fileDate) => _fileDate = fileDate;

    /// <summary>Parses the JSON document; when <paramref name="hash"/> is given, every byte read is also fed to it.</summary>
    /// <exception cref="VatWhitelistDataUnavailableException">The document is not a flat file (<see cref="VatWhitelistDataUnavailableReason.UnexpectedFormat"/>).</exception>
    public static ParsedFlatFile Parse(Stream json, DateOnly fileDate, CancellationToken ct, IncrementalHash? hash = null)
    {
        try
        {
            return new FlatFileParser(fileDate).Run(json, hash, ct);
        }
        catch (JsonException ex)
        {
            throw new VatWhitelistDataUnavailableException(
                $"The flat file of {fileDate:yyyy-MM-dd} is not valid JSON: {ex.Message}", fileDate, VatWhitelistDataUnavailableReason.UnexpectedFormat, ex);
        }
    }

    private ParsedFlatFile Run(Stream json, IncrementalHash? hash, CancellationToken ct)
    {
        var buffer = new byte[InitialBuffer];
        var filled = 0;
        var state = default(JsonReaderState);
        var eof = false;
        while (!eof)
        {
            ct.ThrowIfCancellationRequested();
            if (filled == buffer.Length)
            {
                if (buffer.Length >= MaxBuffer)
                {
                    throw Bad("a JSON token is unreasonably large");
                }
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            var read = json.Read(buffer, filled, buffer.Length - filled);
            if (read == 0)
            {
                eof = true;
            }
            else
            {
                hash?.AppendData(buffer, filled, read);
                filled += read;
            }

            var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), eof, state);
            while (reader.Read())
            {
                Handle(ref reader);
            }

            state = reader.CurrentState;
            var consumed = (int)reader.BytesConsumed;
            Buffer.BlockCopy(buffer, consumed, buffer, 0, filled - consumed);
            filled -= consumed;
        }

        if (_generatedOn is not { } generatedOn || _transformations is not { } transformations)
        {
            throw Bad("the header lacks dataGenerowaniaDanych or liczbaTransformacji");
        }
        if (!_sawActive || !_sawExempt || !_sawMasks)
        {
            throw Bad("a list of hashes or the masks are missing");
        }
        return new ParsedFlatFile(generatedOn, transformations, _active.ToArray(), _exempt.ToArray(), _masks.ToArray());
    }

    // Tokens are told apart by depth: the document is depth 0, its properties and the start of their values depth 1,
    // header fields and list elements depth 2; anything deeper belongs to a value we do not look at.
    private void Handle(ref Utf8JsonReader reader)
    {
        var type = reader.TokenType;
        switch (reader.CurrentDepth)
        {
            case 0:
                if (type != JsonTokenType.StartObject && type != JsonTokenType.EndObject)
                {
                    throw Bad("the document is not a JSON object");
                }
                break;
            case 1:
                if (type == JsonTokenType.PropertyName)
                {
                    _section = SectionOf(ref reader);
                }
                else if (_section >= Section.Header && type != JsonTokenType.EndObject && type != JsonTokenType.EndArray
                    && type != (_section == Section.Header ? JsonTokenType.StartObject : JsonTokenType.StartArray))
                {
                    throw Bad($"the {_section} section has the wrong JSON type");
                }
                break;
            case 2:
                HandleElement(ref reader);
                break;
        }
    }

    private Section SectionOf(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("naglowek"u8))
        {
            return Section.Header;
        }
        if (reader.ValueTextEquals("skrotyPodatnikowCzynnych"u8))
        {
            return Once(ref _sawActive, Section.Active);
        }
        if (reader.ValueTextEquals("skrotyPodatnikowZwolnionych"u8))
        {
            return Once(ref _sawExempt, Section.Exempt);
        }
        if (reader.ValueTextEquals("maski"u8))
        {
            return Once(ref _sawMasks, Section.Masks);
        }
        return Section.Unknown;
    }

    private Section Once(ref bool seen, Section section)
    {
        if (seen)
        {
            throw Bad($"the {section} section appears twice");
        }
        seen = true;
        return section;
    }

    private void HandleElement(ref Utf8JsonReader reader)
    {
        switch (_section)
        {
            case Section.Header:
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    _headerField = reader.GetString();
                }
                else if (_headerField == "dataGenerowaniaDanych")
                {
                    _generatedOn = ParseDate(ref reader);
                }
                else if (_headerField == "liczbaTransformacji")
                {
                    _transformations = ParseRounds(ref reader);
                }
                break;
            case Section.Active:
                AddHash(ref reader, _active);
                break;
            case Section.Exempt:
                AddHash(ref reader, _exempt);
                break;
            case Section.Masks:
                AddMask(ref reader);
                break;
        }
    }

    private DateOnly ParseDate(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String
        && DateOnly.TryParseExact(reader.GetString(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Bad("dataGenerowaniaDanych is not a yyyyMMdd date");

    private int ParseRounds(ref Utf8JsonReader reader)
    {
        var rounds = 0;
        var ok = reader.TokenType switch
        {
            JsonTokenType.Number => reader.TryGetInt32(out rounds),
            JsonTokenType.String => int.TryParse(reader.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out rounds),
            _ => false,
        };
        return ok && rounds >= 1 ? rounds : throw Bad("liczbaTransformacji is not a positive number");
    }

    private void AddHash(ref Utf8JsonReader reader, ByteChunks target)
    {
        if (reader.TokenType != JsonTokenType.String || reader.ValueIsEscaped || reader.HasValueSequence || reader.ValueSpan.Length != HexLength)
        {
            throw Bad("a hash is not a 128-character string");
        }

        var hex = reader.ValueSpan;
        for (var i = 0; i < HexLength; i++)
        {
            if (Nibble(hex[i]) < 0)
            {
                throw Bad("a hash contains a non-hexadecimal character");
            }
        }

        var prefix = target.Reserve(PrefixLength);
        for (var i = 0; i < PrefixLength; i++)
        {
            prefix[i] = (byte)((Nibble(hex[2 * i]) << 4) | Nibble(hex[(2 * i) + 1]));
        }
    }

    private void AddMask(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String || reader.ValueIsEscaped || reader.HasValueSequence || reader.ValueSpan.Length != MaskLength)
        {
            throw Bad("a mask is not a 26-character string");
        }

        foreach (var c in reader.ValueSpan)
        {
            if (c != 'X' && c != 'Y' && (c < '0' || c > '9'))
            {
                throw Bad("a mask contains a character other than a digit, X or Y");
            }
        }
        _masks.Add(reader.GetString()!);
    }

    private static int Nibble(byte c) => c switch
    {
        >= (byte)'0' and <= (byte)'9' => c - '0',
        >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
        _ => -1,
    };

    private VatWhitelistDataUnavailableException Bad(string what) =>
        new($"The flat file of {_fileDate:yyyy-MM-dd} has an unexpected format: {what}.", _fileDate, VatWhitelistDataUnavailableReason.UnexpectedFormat);

    /// <summary>An append-only byte store that grows in chunks, so the final exact-sized array is the only copy (peak: about twice the data).</summary>
    private sealed class ByteChunks
    {
        private const int ChunkSize = 1 << 20; // a multiple of PrefixLength
        private readonly List<byte[]> _full = [];
        private byte[] _current = new byte[ChunkSize];
        private int _used;

        public Span<byte> Reserve(int count)
        {
            if (_used + count > _current.Length)
            {
                _full.Add(_current);
                _current = new byte[ChunkSize];
                _used = 0;
            }
            var span = _current.AsSpan(_used, count);
            _used += count;
            return span;
        }

        public byte[] ToArray()
        {
            var result = new byte[(_full.Count * ChunkSize) + _used];
            var offset = 0;
            foreach (var chunk in _full)
            {
                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }
            Buffer.BlockCopy(_current, 0, result, offset, _used);
            return result;
        }
    }
}
