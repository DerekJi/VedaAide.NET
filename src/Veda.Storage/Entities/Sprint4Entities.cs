namespace Veda.Storage.Entities;

/// <summary>用户行为事件 SQLite 实体（隐私设计：不存储文档内容）。</summary>
public class UserBehaviorEntity
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public int Type { get; set; }  // BehaviorType enum value
    public string RelatedChunkId { get; set; } = string.Empty;
    public string RelatedDocumentId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public long OccurredAtTicks { get; set; }
}

/// <summary>共享组实体（用于家庭/团队知识共享）。</summary>
public class SharingGroupEntity
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string MembersJson { get; set; } = "[]";
    public long CreatedAtTicks { get; set; }
}

/// <summary>文档共享权限实体。</summary>
public class DocumentPermissionEntity
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public long GrantedAtTicks { get; set; }
}

/// <summary>共识候选实体（匿名化模式，用于跨用户知识聚合）。</summary>
public class ConsensusCandidateEntity
{
    public string Id { get; set; } = string.Empty;
    public string AnonymizedPattern { get; set; } = string.Empty;
    public double SupportRatio { get; set; }
    public long NominatedAtTicks { get; set; }
    public bool IsApproved { get; set; }
    public string? ReviewerId { get; set; }
    public long ReviewedAtTicks { get; set; }
}
