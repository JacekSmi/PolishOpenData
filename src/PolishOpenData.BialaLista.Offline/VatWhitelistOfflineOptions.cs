using System;
using System.IO;
using System.Threading;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>Options for <see cref="VatWhitelistOffline"/>. Invalid values are rejected when they are set.</summary>
public sealed class VatWhitelistOfflineOptions
{
    private string? _dataDirectory;
    private Uri _baseAddress = new("https://plikplaski.mf.gov.pl/pliki/");
    private int _datesToKeep = 5;
    private TimeSpan _lockTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Where the prepared indexes are kept; <c>null</c> selects <c>PolishOpenData/BialaListaOffline</c> under the user's local
    /// application data folder. Use a folder on a local disk: a network share is not supported. Processes that share the
    /// folder share the prepared indexes (a lock file per date keeps them from preparing one date twice).
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty or whitespace.</exception>
    public string? DataDirectory
    {
        get => _dataDirectory;
        set
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("VatWhitelistOfflineOptions.DataDirectory must not be empty or whitespace; use null for the default folder.", nameof(value));
            }

            _dataDirectory = value;
        }
    }

    /// <summary>
    /// The folder the flat files are downloaded from, as an absolute URI ending with <c>/</c>. Default
    /// <c>https://plikplaski.mf.gov.pl/pliki/</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not an absolute URI that ends with <c>/</c>.</exception>
    public Uri BaseAddress
    {
        get => _baseAddress;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!value.IsAbsoluteUri || !value.AbsoluteUri.EndsWith('/'))
            {
                throw new ArgumentException("VatWhitelistOfflineOptions.BaseAddress must be an absolute URI that ends with '/'.", nameof(value));
            }

            _baseAddress = value;
        }
    }

    /// <summary>How many prepared dates (the most recently used) stay on disk. Default 5; at least 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int DatesToKeep
    {
        get => _datesToKeep;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _datesToKeep = value;
        }
    }

    /// <summary>
    /// How long a call waits for another process that is preparing the same date before it fails with
    /// <see cref="TimeoutException"/>. Default 15 minutes; must be greater than zero (<see cref="Timeout.InfiniteTimeSpan"/> is not accepted).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public TimeSpan LockTimeout
    {
        get => _lockTimeout;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "VatWhitelistOfflineOptions.LockTimeout must be greater than zero.");
            }

            _lockTimeout = value;
        }
    }

    internal string ResolveDataDirectory() =>
        _dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "PolishOpenData",
            "BialaListaOffline");
}
