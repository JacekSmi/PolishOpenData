using System;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>Why the flat file of a date cannot be used.</summary>
public enum VatWhitelistDataUnavailableReason
{
    /// <summary>The Ministry of Finance has not published the file of that date yet.</summary>
    NotPublishedYet,

    /// <summary>The date is outside the window for which the file is available (about the last 30 days and tomorrow).</summary>
    OutsideWindow,

    /// <summary>The file could not be downloaded.</summary>
    DownloadFailed,

    /// <summary>The downloaded file does not match its SHA-512 checksum.</summary>
    ChecksumMismatch,

    /// <summary>The downloaded file does not have the structure the Ministry of Finance describes.</summary>
    UnexpectedFormat,
}

/// <summary>The data for a date cannot be provided; never answered with another day's data. <see cref="Reason"/> says why.</summary>
public sealed class VatWhitelistDataUnavailableException : PolishOpenDataException
{
    /// <summary>Creates an exception.</summary>
    public VatWhitelistDataUnavailableException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public VatWhitelistDataUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public VatWhitelistDataUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for the data of one date.</summary>
    /// <param name="message">Human-readable description.</param>
    /// <param name="date">The date whose data is unavailable.</param>
    /// <param name="reason">Why the data is unavailable.</param>
    /// <param name="innerException">The underlying failure, when there is one.</param>
    public VatWhitelistDataUnavailableException(string message, DateOnly date, VatWhitelistDataUnavailableReason reason, Exception? innerException = null)
        : base(message, innerException!)
    {
        Date = date;
        Reason = reason;
    }

    /// <summary>The date whose data is unavailable.</summary>
    public DateOnly Date { get; }

    /// <summary>Why the data is unavailable.</summary>
    public VatWhitelistDataUnavailableReason Reason { get; }
}
