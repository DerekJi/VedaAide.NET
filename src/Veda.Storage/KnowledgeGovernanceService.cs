using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// 多用户知识治理服务实现。
/// 管理共享组、文档授权、共识候选和隐私隔离。
/// </summary>
public sealed class KnowledgeGovernanceService(
    VedaDbContext db,
    ILogger<KnowledgeGovernanceService> logger) : IKnowledgeGovernanceService
{
    public async Task<string> CreateSharingGroupAsync(
        string ownerId,
        IReadOnlyList<string> memberIds,
        CancellationToken ct = default)
    {
        var groupId = Guid.NewGuid().ToString();
        var entity = new SharingGroupEntity
        {
            Id = groupId,
            OwnerId = ownerId,
            MembersJson = System.Text.Json.JsonSerializer.Serialize(memberIds),
            CreatedAtTicks = DateTimeOffset.UtcNow.UtcTicks
        };
        db.SharingGroups.Add(entity);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created sharing group {GroupId} for owner {OwnerId}", groupId, ownerId);
        return groupId;
    }

    public async Task ShareDocumentAsync(
        string documentId,
        string ownerId,
        string groupId,
        CancellationToken ct = default)
    {
        // Verify the group exists and owner matches
        var group = await db.SharingGroups.FindAsync([groupId], ct);
        if (group is null)
            throw new InvalidOperationException($"Sharing group '{groupId}' not found.");
        if (!string.Equals(group.OwnerId, ownerId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Only the group owner can share documents.");

        var permission = new DocumentPermissionEntity
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            GroupId = groupId,
            GrantedAtTicks = DateTimeOffset.UtcNow.UtcTicks
        };
        db.DocumentPermissions.Add(permission);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Shared document {DocumentId} with group {GroupId}", documentId, groupId);
    }

    public async Task NominateConsensusAsync(
        string anonymizedPattern,
        double supportRatio,
        CancellationToken ct = default)
    {
        var candidate = new ConsensusCandidateEntity
        {
            Id = Guid.NewGuid().ToString(),
            AnonymizedPattern = anonymizedPattern,
            SupportRatio = supportRatio,
            NominatedAtTicks = DateTimeOffset.UtcNow.UtcTicks,
            IsApproved = false
        };
        db.ConsensusCandidates.Add(candidate);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Nominated consensus candidate (supportRatio={Ratio})", supportRatio);
    }

    public async Task<bool> ReviewConsensusAsync(
        string candidateId,
        bool approved,
        string reviewerId,
        CancellationToken ct = default)
    {
        var candidate = await db.ConsensusCandidates.FindAsync([candidateId], ct);
        if (candidate is null) return false;

        candidate.IsApproved = approved;
        candidate.ReviewerId = reviewerId;
        candidate.ReviewedAtTicks = DateTimeOffset.UtcNow.UtcTicks;
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Consensus candidate {CandidateId} {Action} by {ReviewerId}",
            candidateId, approved ? "approved" : "rejected", reviewerId);
        return true;
    }

    public async Task<IReadOnlyList<ConsensusCandidate>> GetPendingCandidatesAsync(CancellationToken ct = default)
    {
        var entities = await db.ConsensusCandidates
            .Where(c => !c.IsApproved && c.ReviewerId == null)
            .OrderByDescending(c => c.SupportRatio)
            .ToListAsync(ct);

        return entities.Select(e => new ConsensusCandidate(
            e.Id,
            e.AnonymizedPattern,
            e.SupportRatio,
            new DateTimeOffset(e.NominatedAtTicks, TimeSpan.Zero),
            e.IsApproved,
            e.ReviewerId,
            e.ReviewedAtTicks > 0 ? new DateTimeOffset(e.ReviewedAtTicks, TimeSpan.Zero) : null
        )).ToList();
    }

    public async Task<bool> IsDocumentVisibleToUserAsync(
        string documentId,
        string userId,
        CancellationToken ct = default)
    {
        // 1. Document is owned by the user (stored in chunk scope ownerId)
        var isOwner = await db.VectorChunks.AnyAsync(
            c => c.DocumentId == documentId && c.MetadataJson.Contains($"\"_scope_ownerId\":\"{userId}\""), ct);
        if (isOwner) return true;

        // 2. Document is shared with a group the user belongs to
        // 使用引号边界匹配（"userId"），避免 userId="user1" 误匹配 MembersJson=["user12"] 的子串问题。
        var quotedUserId = $"\"{userId}\"";
        var groups = await db.SharingGroups
            .Where(g => g.MembersJson.Contains(quotedUserId))
            .Select(g => g.Id)
            .ToListAsync(ct);
        if (groups.Count > 0)
        {
            var isShared = await db.DocumentPermissions.AnyAsync(
                p => p.DocumentId == documentId && groups.Contains(p.GroupId), ct);
            if (isShared) return true;
        }

        // 3. Document visibility is Public (no scope restriction)
        var hasNoScope = await db.VectorChunks.AnyAsync(
            c => c.DocumentId == documentId && !c.MetadataJson.Contains("_scope_ownerId"), ct);
        return hasNoScope;
    }
}
