using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PolishOpenData.Ceidg;

/// <summary>
/// Read-only client for the CEIDG v3 API. Needs your own token (<see cref="CeidgClientOptions.Token"/>). A person can have
/// more than one entry (for example an ended one and a running one), so lookups return lists. CEIDG allows 1,000 requests
/// per 60 minutes per token; the client does not retry. Entries name natural persons: treat the data as personal data.
/// </summary>
public interface ICeidgClient
{
    /// <summary>Finds the entries of the owner with this NIP. Empty when there is none.</summary>
    /// <exception cref="CeidgAuthenticationException">The token was rejected (HTTP 401/403).</exception>
    /// <exception cref="QuotaExceededException">HTTP 429.</exception>
    Task<IReadOnlyList<CeidgBusiness>> FindByNipAsync(Nip nip, CancellationToken cancellationToken = default);

    /// <summary>Finds the entries of the owner with this REGON. Empty when there is none.</summary>
    /// <exception cref="CeidgAuthenticationException">The token was rejected (HTTP 401/403).</exception>
    /// <exception cref="QuotaExceededException">HTTP 429.</exception>
    Task<IReadOnlyList<CeidgBusiness>> FindByRegonAsync(Regon regon, CancellationToken cancellationToken = default);

    /// <summary>Gets one entry by its CEIDG id. <c>null</c> when there is none.</summary>
    /// <exception cref="CeidgAuthenticationException">The token was rejected (HTTP 401/403).</exception>
    /// <exception cref="QuotaExceededException">HTTP 429.</exception>
    Task<CeidgBusiness?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
