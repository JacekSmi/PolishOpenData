using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.BialaLista;

/// <summary>
/// Client for the Ministry of Finance VAT whitelist API. Limits per IP: 100 search requests (≤30 identifiers each) and
/// 5,000 check requests per day; exceeding one blocks the IP until midnight, including the ministry's web search.
/// Prefer <c>CheckBankAccountAsync</c> for payment verification.
/// </summary>
public interface IBialaListaClient
{
    /// <summary>Finds a taxpayer by NIP. <paramref name="date"/> defaults to today in Poland.</summary>
    /// <exception cref="PolishOpenDataApiException">
    /// Biała Lista rejected the request (its code, such as <c>WL-115</c>, is in
    /// <see cref="PolishOpenDataApiException.ErrorCode"/>), or answered with a success response that is not readable
    /// (malformed JSON, a value of the wrong type or format, or no <c>result</c>; then
    /// <see cref="PolishOpenDataApiException.StatusCode"/> and <see cref="PolishOpenDataApiException.ResponseSnippet"/>
    /// describe the response).
    /// </exception>
    /// <exception cref="QuotaExceededException">The daily limit is reached (reported by the API or by the local quota tracker).</exception>
    Task<BialaListaResult<VatSubject?>> FindByNipAsync(Nip nip, DateOnly? date = null, CancellationToken cancellationToken = default);

    /// <summary>Finds taxpayers for up to 30 NIPs in one request.</summary>
    /// <exception cref="PolishOpenDataApiException">
    /// Biała Lista rejected the request (its code, such as <c>WL-115</c>, is in
    /// <see cref="PolishOpenDataApiException.ErrorCode"/>), or answered with a success response that is not readable
    /// (malformed JSON, a value of the wrong type or format, or no <c>result</c>; then
    /// <see cref="PolishOpenDataApiException.StatusCode"/> and <see cref="PolishOpenDataApiException.ResponseSnippet"/>
    /// describe the response).
    /// </exception>
    /// <exception cref="QuotaExceededException">The daily limit is reached (reported by the API or by the local quota tracker).</exception>
    Task<BialaListaResult<IReadOnlyList<VatBatchEntry>>> FindByNipsAsync(IReadOnlyCollection<Nip> nips, DateOnly? date = null, CancellationToken cancellationToken = default);

    /// <summary>Finds a taxpayer by REGON.</summary>
    /// <exception cref="PolishOpenDataApiException">
    /// Biała Lista rejected the request (its code, such as <c>WL-115</c>, is in
    /// <see cref="PolishOpenDataApiException.ErrorCode"/>), or answered with a success response that is not readable
    /// (malformed JSON, a value of the wrong type or format, or no <c>result</c>; then
    /// <see cref="PolishOpenDataApiException.StatusCode"/> and <see cref="PolishOpenDataApiException.ResponseSnippet"/>
    /// describe the response).
    /// </exception>
    /// <exception cref="QuotaExceededException">The daily limit is reached (reported by the API or by the local quota tracker).</exception>
    Task<BialaListaResult<VatSubject?>> FindByRegonAsync(Regon regon, DateOnly? date = null, CancellationToken cancellationToken = default);

    /// <summary>Finds taxpayers for up to 30 REGONs in one request.</summary>
    /// <exception cref="PolishOpenDataApiException">
    /// Biała Lista rejected the request (its code, such as <c>WL-115</c>, is in
    /// <see cref="PolishOpenDataApiException.ErrorCode"/>), or answered with a success response that is not readable
    /// (malformed JSON, a value of the wrong type or format, or no <c>result</c>; then
    /// <see cref="PolishOpenDataApiException.StatusCode"/> and <see cref="PolishOpenDataApiException.ResponseSnippet"/>
    /// describe the response).
    /// </exception>
    /// <exception cref="QuotaExceededException">The daily limit is reached (reported by the API or by the local quota tracker).</exception>
    Task<BialaListaResult<IReadOnlyList<VatBatchEntry>>> FindByRegonsAsync(IReadOnlyCollection<Regon> regons, DateOnly? date = null, CancellationToken cancellationToken = default);

    /// <summary>Finds the taxpayers that list a bank account.</summary>
    /// <exception cref="PolishOpenDataApiException">
    /// Biała Lista rejected the request (its code, such as <c>WL-115</c>, is in
    /// <see cref="PolishOpenDataApiException.ErrorCode"/>), or answered with a success response that is not readable
    /// (malformed JSON, a value of the wrong type or format, or no <c>result</c>; then
    /// <see cref="PolishOpenDataApiException.StatusCode"/> and <see cref="PolishOpenDataApiException.ResponseSnippet"/>
    /// describe the response).
    /// </exception>
    /// <exception cref="QuotaExceededException">The daily limit is reached (reported by the API or by the local quota tracker).</exception>
    Task<BialaListaResult<IReadOnlyList<VatSubject>>> FindByBankAccountAsync(Nrb account, DateOnly? date = null, CancellationToken cancellationToken = default);

    /// <summary>Finds taxpayers for up to 30 bank accounts in one request.</summary>
    /// <exception cref="PolishOpenDataApiException">
    /// Biała Lista rejected the request (its code, such as <c>WL-115</c>, is in
    /// <see cref="PolishOpenDataApiException.ErrorCode"/>), or answered with a success response that is not readable
    /// (malformed JSON, a value of the wrong type or format, or no <c>result</c>; then
    /// <see cref="PolishOpenDataApiException.StatusCode"/> and <see cref="PolishOpenDataApiException.ResponseSnippet"/>
    /// describe the response).
    /// </exception>
    /// <exception cref="QuotaExceededException">The daily limit is reached (reported by the API or by the local quota tracker).</exception>
    Task<BialaListaResult<IReadOnlyList<VatBatchEntry>>> FindByBankAccountsAsync(IReadOnlyCollection<Nrb> accounts, DateOnly? date = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an account is on the whitelist for a NIP. <c>false</c> means the account is not assigned to this
    /// taxpayer <b>or</b> the taxpayer is not an active VAT payer — it does not say who owns the account.
    /// </summary>
    /// <exception cref="PolishOpenDataApiException">
    /// Biała Lista rejected the request (its code, such as <c>WL-115</c>, is in
    /// <see cref="PolishOpenDataApiException.ErrorCode"/>), or answered with a success response that is not readable
    /// (malformed JSON, a value of the wrong type or format, or no <c>result</c>; then
    /// <see cref="PolishOpenDataApiException.StatusCode"/> and <see cref="PolishOpenDataApiException.ResponseSnippet"/>
    /// describe the response). An <c>accountAssigned</c> value other than <c>TAK</c> or <c>NIE</c> also throws it, with
    /// the value in the message.
    /// </exception>
    /// <exception cref="QuotaExceededException">The daily limit is reached (reported by the API or by the local quota tracker).</exception>
    Task<BialaListaResult<bool>> CheckBankAccountAsync(Nip nip, Nrb account, DateOnly? date = null, CancellationToken cancellationToken = default);

    /// <summary>Checks whether an account is on the whitelist for a REGON (same semantics as the NIP overload).</summary>
    /// <exception cref="PolishOpenDataApiException">
    /// Biała Lista rejected the request (its code, such as <c>WL-115</c>, is in
    /// <see cref="PolishOpenDataApiException.ErrorCode"/>), or answered with a success response that is not readable
    /// (malformed JSON, a value of the wrong type or format, or no <c>result</c>; then
    /// <see cref="PolishOpenDataApiException.StatusCode"/> and <see cref="PolishOpenDataApiException.ResponseSnippet"/>
    /// describe the response). An <c>accountAssigned</c> value other than <c>TAK</c> or <c>NIE</c> also throws it, with
    /// the value in the message.
    /// </exception>
    /// <exception cref="QuotaExceededException">The daily limit is reached (reported by the API or by the local quota tracker).</exception>
    Task<BialaListaResult<bool>> CheckBankAccountAsync(Regon regon, Nrb account, DateOnly? date = null, CancellationToken cancellationToken = default);
}
