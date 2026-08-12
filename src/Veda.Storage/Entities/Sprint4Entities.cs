namespace Veda.Storage.Entities;

/// <summary>SQLite entity for user behavior events (privacy by design: no document content is stored).</summary>
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

/// <summary>Sharing group entity (used for family/team knowledge sharing).</summary>
public class SharingGroupEntity
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string MembersJson { get; set; } = "[]";
    public long CreatedAtTicks { get; set; }
}

/// <summary>Document sharing permission entity.</summary>
public class DocumentPermissionEntity
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public long GrantedAtTicks { get; set; }
}

/// <summary>Consensus candidate entity (anonymized pattern, used for cross-user knowledge aggregation).</summary>
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
