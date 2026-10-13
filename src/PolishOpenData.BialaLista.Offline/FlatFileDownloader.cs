using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PolishOpenData.Internal;

namespace PolishOpenData.BialaLista.Offline;

/// <summary>Downloads the daily 7z flat file. No timeout of its own: the server can be very slow, only the token cancels.</summary>
internal static class FlatFileDownloader
{
    private const int BufferSize = 81920;
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Streams <c>{baseAddress}{yyyyMMdd}.7z</c> to <paramref name="targetPath"/>; the file is deleted on any failure or cancellation.</summary>
    /// <exception cref="VatWhitelistDataUnavailableException">404 (not published yet, or outside the window), or a body shorter than announced.</exception>
    /// <exception cref="HttpRequestException">Any other non-success status.</exception>
    public static async Task DownloadAsync(
        HttpClient http,
        Uri baseAddress,
        DateOnly date,
        DateOnly today,
        string targetPath,
        IProgress<VatWhitelistPreparationProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            await DownloadCoreAsync(http, baseAddress, date, today, targetPath, progress, ct).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                File.Delete(targetPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    private static async Task DownloadCoreAsync(
        HttpClient http,
        Uri baseAddress,
        DateOnly date,
        DateOnly today,
        string targetPath,
        IProgress<VatWhitelistPreparationProgress>? progress,
        CancellationToken ct)
    {
        using var request = HttpHelpers.CreateGet(new Uri(baseAddress, date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".7z"));
        request.Headers.Accept.Clear();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw date >= today
                ? new VatWhitelistDataUnavailableException(
                    $"The Ministry of Finance has not published the flat file of {date:yyyy-MM-dd} yet.", date, VatWhitelistDataUnavailableReason.NotPublishedYet)
                // Neutral: inside the window a 404 may be a missing holiday file or a transient error, not MF's retention.
                : new VatWhitelistDataUnavailableException(
                    $"MF does not serve the flat file of {date:yyyy-MM-dd} (HTTP 404).", date, VatWhitelistDataUnavailableReason.OutsideWindow);
        }

        _ = response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        long received = 0;
        var writing = false;
        // Local file errors (missing or read-only directory, full disk) surface as they are; only network reads mean "download failed".
        using var file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        try
        {
            // A stalled read can ignore the token (.NET Framework); disposing the response ends it, and the catch below reports cancellation.
            using var cancelRead = ct.Register(response.Dispose);
#if NET
            using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#else
            using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            var buffer = new byte[BufferSize];
            var clock = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            int read;
#if NET
            while ((read = await body.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                writing = true;
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
#else
            while ((read = await body.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                writing = true;
                await file.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
#endif
                writing = false;
                received += read;
                if (clock.Elapsed - lastReport >= ReportInterval)
                {
                    lastReport = clock.Elapsed;
                    progress?.Report(new VatWhitelistPreparationProgress(VatWhitelistPreparationStage.Downloading, received, total));
                }
            }
        }
        catch (Exception exception) when (ct.IsCancellationRequested && exception is ObjectDisposedException or IOException)
        {
            throw new OperationCanceledException("The download was cancelled.", exception, ct);
        }
        catch (IOException exception) when (!writing)
        {
            throw Failed(date, received, total, exception);
        }

        if (total is { } expected && received != expected)
        {
            throw Failed(date, received, total, null);
        }

        progress?.Report(new VatWhitelistPreparationProgress(VatWhitelistPreparationStage.Downloading, received, total));
    }

    private static VatWhitelistDataUnavailableException Failed(DateOnly date, long received, long? total, Exception? inner) =>
        new(
            $"Downloading the flat file of {date:yyyy-MM-dd} stopped after {received} of {(total?.ToString(CultureInfo.InvariantCulture) ?? "an unknown number of")} bytes.",
            date,
            VatWhitelistDataUnavailableReason.DownloadFailed,
            inner);
}
