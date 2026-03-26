namespace Veda.Core.Interfaces;

/// <summary>
/// 多用户知识治理服务接口。
/// 管理个人/共享/共识/公共四层知识治理模型。
/// </summary>
public interface IKnowledgeGovernanceService
{
    /// <summary>创建共享组（家庭/团队），返回组 ID。</summary>
    Task<string> CreateSharingGroupAsync(
        string ownerId,
        IReadOnlyList<string> memberIds,
        CancellationToken ct = default);

    /// <summary>
    /// 授权指定文档对共享组可见。
    /// 调用后该文档在指定组成员的查询中可见。
    /// </summary>
    Task ShareDocumentAsync(
        string documentId,
        string ownerId,
        string groupId,
        CancellationToken ct = default);

    /// <summary>提名共识候选（系统自动触发，匿名聚合）。</summary>
    Task NominateConsensusAsync(
        string anonymizedPattern,
        double supportRatio,
        CancellationToken ct = default);

    /// <summary>审核共识候选（管理员操作）。</summary>
    Task<bool> ReviewConsensusAsync(
        string candidateId,
        bool approved,
        string reviewerId,
        CancellationToken ct = default);

    /// <summary>获取待审核的共识候选列表。</summary>
    Task<IReadOnlyList<ConsensusCandidate>> GetPendingCandidatesAsync(CancellationToken ct = default);

    /// <summary>检查文档对指定用户是否可见（隐私隔离）。</summary>
    Task<bool> IsDocumentVisibleToUserAsync(
        string documentId,
        string userId,
        CancellationToken ct = default);
}

/// <summary>共识候选项。</summary>
public record ConsensusCandidate(
    string Id,
    string AnonymizedPattern,
    double SupportRatio,
    DateTimeOffset NominatedAt,
    bool IsApproved,
    string? ReviewerId,
    DateTimeOffset? ReviewedAt);
