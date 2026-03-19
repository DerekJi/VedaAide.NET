namespace Veda.Core;

/// <summary>
/// DRY：统一 DocumentType 枚举解析，全项目复用。
/// 从 Veda.Api.Extensions 迁移至 Veda.Core，使其可在 Core.Tests 中直接测试。
/// </summary>
public static class DocumentTypeParser
{
    /// <summary>解析为具体类型，解析失败返回 <paramref name="defaultType"/>。</summary>
    public static DocumentType ParseOrDefault(string? value, DocumentType defaultType = DocumentType.Other)
        => TryParse(value, out var result) ? result : defaultType;

    /// <summary>解析为可空类型，解析失败返回 null（用于可选过滤场景）。</summary>
    public static DocumentType? ParseOrNull(string? value)
        => TryParse(value, out var result) ? result : null;

    private static bool TryParse(string? value, out DocumentType result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, ignoreCase: true, out result);
    }
}
