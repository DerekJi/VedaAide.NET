namespace Veda.Core;

/// <summary>
/// 知识作用域元数据，用于多维度过滤与偏好排序。
/// 所有属性均可选——不传 Scope 时不做任何过滤。
/// </summary>
public record KnowledgeScope(
    string? Domain = null,
    string? SourceType = null,
    IReadOnlyList<string>? Tags = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    string? OwnerId = null,
    Visibility Visibility = Visibility.Private);

/// <summary>知识可见性级别。</summary>
public enum Visibility
{
    Private,   // 仅所有者可见
    Shared,    // 授权成员可见
    Public     // 所有用户可见
}
