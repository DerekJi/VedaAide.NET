namespace Veda.Core.Interfaces;

/// <summary>
/// Multi-user knowledge governance service interface.
/// Manages the four-tier knowledge governance model: personal / shared / consensus / public.
/// </summary>
public interface IKnowledgeGovernanceService
{
    /// <summary>Creates a sharing group (family / team) and returns its ID.</summary>
    Task<string> CreateSharingGroupAsync(
        string ownerId,
        IReadOnlyList<string> memberIds,
        CancellationToken ct = default);

    /// <summary>
    /// Authorizes a specified document to be visible to a sharing group.
    /// After the call, the document becomes visible in queries by members of the specified group.
    /// </summary>
    Task ShareDocumentAsync(
        string documentId,
        string ownerId,
        string groupId,
        CancellationToken ct = default);

    /// <summary>Nominates a consensus candidate (system-triggered, anonymously aggregated).</summary>
    Task NominateConsensusAsync(
        string anonymizedPattern,
        double supportRatio,
        CancellationToken ct = default);

    /// <summary>Reviews a consensus candidate (administrator action).</summary>
    Task<bool> ReviewConsensusAsync(
        string candidateId,
        bool approved,
        string reviewerId,
        CancellationToken ct = default);

    /// <summary>Gets the list of consensus candidates awaiting review.</summary>
    Task<IReadOnlyList<ConsensusCandidate>> GetPendingCandidatesAsync(CancellationToken ct = default);

    /// <summary>Checks whether a document is visible to the specified user (privacy isolation).</summary>
    Task<bool> IsDocumentVisibleToUserAsync(
        string documentId,
        string userId,
        CancellationToken ct = default);
}

/// <summary>A consensus candidate.</summary>
public record ConsensusCandidate(
    string Id,
    string AnonymizedPattern,
    double SupportRatio,
    DateTimeOffset NominatedAt,
    bool IsApproved,
    string? ReviewerId,
    DateTimeOffset? ReviewedAt);
