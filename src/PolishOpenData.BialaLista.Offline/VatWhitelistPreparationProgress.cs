namespace PolishOpenData.BialaLista.Offline;

/// <summary>The step of preparing the offline data of a date.</summary>
public enum VatWhitelistPreparationStage
{
    /// <summary>The flat file is being downloaded.</summary>
    Downloading,

    /// <summary>The downloaded file is being verified and indexed.</summary>
    Indexing,
}

/// <summary>Progress of preparing the offline data of a date (the download can take minutes on a slow server).</summary>
public sealed class VatWhitelistPreparationProgress
{
    /// <summary>Creates a progress report.</summary>
    /// <param name="stage">The current step.</param>
    /// <param name="bytesReceived">Bytes downloaded so far.</param>
    /// <param name="totalBytes">The size of the download, or <c>null</c> when the server did not say.</param>
    public VatWhitelistPreparationProgress(VatWhitelistPreparationStage stage, long bytesReceived, long? totalBytes)
    {
        Stage = stage;
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
    }

    /// <summary>The current step.</summary>
    public VatWhitelistPreparationStage Stage { get; }

    /// <summary>Bytes downloaded so far.</summary>
    public long BytesReceived { get; }

    /// <summary>The size of the download, or <c>null</c> when the server did not say.</summary>
    public long? TotalBytes { get; }
}
