using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using PolishOpenData.Bir.Wire;
using PolishOpenData.Internal;

namespace PolishOpenData.Bir;

/// <summary>
/// The BIR operations with session handling, the request limiter and the error rules. One
/// instance per client; the session and the limiter are shared by every client of the process. A dead session answers
/// with an empty result (observed behaviour of the GUS service): the service logs in again once and retries
/// once, never in a loop and without a <c>GetValue</c> probe.
/// </summary>
internal sealed class BirService
{
    private readonly BirTransport _transport;
    private readonly Uri _endpoint;
    private readonly string _userKey;
    private readonly BirSession _session;
    private readonly RegonRateLimiter _limiter;

    public BirService(BirTransport transport, Uri endpoint, string userKey, BirSession session, RegonRateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(userKey);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(limiter);
        _transport = transport;
        _endpoint = endpoint;
        _userKey = userKey;
        _session = session;
        _limiter = limiter;
    }

    /// <summary>
    /// One search. An empty answer (after the retry) and ErrorCode 4 both mean nothing was found: an empty list. Any
    /// other ErrorCode throws. <paramref name="weight"/> is the number of identifiers in the request.
    /// </summary>
    public async Task<IReadOnlyList<BirRow>> SearchAsync(BirSearchField field, string value, int weight, CancellationToken cancellationToken)
    {
        var request = BirRequest.Search(_endpoint, field, value);
        var result = await ExecuteAsync(request, weight, cancellationToken).ConfigureAwait(false);
        if (IsEmpty(result))
        {
            return [];
        }

        var rows = ParseRows(result);
        var error = BirErrors.Find(rows);
        if (error is null)
        {
            return rows;
        }

        if (error.Code == BirErrors.NotFound)
        {
            return [];
        }

        throw BirErrors.ToException(error, request.Operation, HttpHelpers.Truncate(result));
    }

    /// <summary>
    /// One full report of one entity. ErrorCodes 4, 11, 21 and 22 mean this report has no data for the entity (not
    /// found, PKD of an entity removed before 2014, not a civil partnership, no partners): an empty list. Any other
    /// ErrorCode throws, and so does an empty answer after the retry.
    /// </summary>
    public async Task<IReadOnlyList<BirRow>> ReportAsync(string regon, string reportWireName, CancellationToken cancellationToken)
    {
        var request = BirRequest.Report(_endpoint, regon, reportWireName);
        var result = await ExecuteAsync(request, 1, cancellationToken).ConfigureAwait(false);
        if (IsEmpty(result))
        {
            throw EmptyTwice(reportWireName);
        }

        var rows = ParseRows(result);
        var error = BirErrors.Find(rows);
        if (error is null)
        {
            return rows;
        }

        if (error.Code is BirErrors.NotFound or BirErrors.NoPkdForRemovedEntity or BirErrors.NotCivilPartnership or BirErrors.NoPartners)
        {
            return [];
        }

        throw BirErrors.ToException(error, request.Operation, HttpHelpers.Truncate(result));
    }

    /// <summary>
    /// Refuses a batch of <paramref name="identifiers"/> before its first request when it cannot fit into what is left of the
    /// per-minute or per-hour window (counting the login it would need), instead of failing part way and losing what it fetched.
    /// </summary>
    public void EnsureBatchCapacity(int identifiers) => _limiter.EnsureCapacity(identifiers, _session.HasFreshSid ? 0 : 1);

    /// <summary>One <c>GetValue</c> call; the text is trimmed. An empty answer after the retry throws.</summary>
    public async Task<string> GetValueAsync(string parameterName, CancellationToken cancellationToken)
    {
        var request = BirRequest.GetValue(_endpoint, parameterName);
        var result = await ExecuteAsync(request, 1, cancellationToken).ConfigureAwait(false);
        if (IsEmpty(result))
        {
            throw EmptyTwice("GetValue(" + parameterName + ")");
        }

        return result.Trim();
    }

    /// <summary>
    /// Sends <c>Wyloguj</c> once when a session exists and forgets the session. Transport and GUS errors are swallowed
    /// (the session ends by itself after 60 minutes); cancellation is not.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        var sid = _session.Detach();
        if (sid is null)
        {
            return;
        }

        try
        {
            await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
            await _transport.SendAsync(BirRequest.Logout(_endpoint, sid), sid, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // best effort: GUS closes an abandoned session itself
        }
        catch (PolishOpenDataApiException)
        {
            // best effort: also covers QuotaExceededException from the local limiter
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // best effort: HttpClient.Timeout surfaces as TaskCanceledException; the caller's own cancellation is not swallowed
        }
    }

    private static bool IsEmpty([NotNullWhen(false)] string? text) => string.IsNullOrWhiteSpace(text);

    private static PolishOpenDataApiException EmptyTwice(string what) =>
        new("GUS REGON returned an empty answer for " + what + " twice, even after a new login.", HttpStatusCode.OK, null, null);

    private static List<BirRow> ParseRows(string inner)
    {
        try
        {
            return BirReader.ReadRows(inner);
        }
        catch (XmlException exception)
        {
            throw new PolishOpenDataApiException(
                "GUS REGON returned a result that could not be read: " + exception.Message,
                HttpStatusCode.OK,
                null,
                HttpHelpers.Truncate(inner),
                exception);
        }
    }

    // Data operations: log in lazily; an empty result means a dead session, so log in once more and send once more.
    private async Task<string?> ExecuteAsync(BirRequest request, int weight, CancellationToken cancellationToken)
    {
        var sid = await _session.GetSidAsync(LoginAsync, cancellationToken).ConfigureAwait(false);
        await _limiter.AcquireAsync(weight, cancellationToken).ConfigureAwait(false);
        var result = await _transport.SendAsync(request, sid, cancellationToken).ConfigureAwait(false);
        if (IsEmpty(result))
        {
            sid = await _session.RenewAsync(sid, LoginAsync, cancellationToken).ConfigureAwait(false);
            await _limiter.AcquireAsync(weight, cancellationToken).ConfigureAwait(false);
            result = await _transport.SendAsync(request, sid, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    // An empty ZalogujResult is a rejected key (observed behaviour of the GUS service): never retried, never echoed.
    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        var result = await _transport.SendAsync(BirRequest.Login(_endpoint, _userKey), null, cancellationToken).ConfigureAwait(false);
        if (IsEmpty(result))
        {
            throw new RegonAuthenticationException("GUS did not accept the configured key.");
        }

        var sid = result.Trim();
        if (!BirTransport.IsValidSid(sid))
        {
            throw new PolishOpenDataApiException("GUS REGON returned a session identifier that this client cannot use.", HttpStatusCode.OK, null, null);
        }

        return sid;
    }
}
