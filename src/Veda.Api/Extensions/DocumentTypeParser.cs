namespace Veda.Api.Extensions;

/// <summary>
/// DRY：统一 DocumentType 枚举解析，避免在多个 Controller 重复 Enum.TryParse 逻辑。
/// </summary>
internal static class DocumentTypeParser
{
    /// <summary>解析为具体类型，解析失败返回 <paramref name="defaultType"/>。</summary>
    internal static DocumentType ParseOrDefault(string? value, DocumentType defaultType = DocumentType.Other)
        => TryParse(value, out var result) ? result : defaultType;

    /// <summary>解析为可空类型，解析失败返回 null（用于可选过滤场景）。</summary>
    internal static DocumentType? ParseOrNull(string? value)
        => TryParse(value, out var result) ? result : null;

    private static bool TryParse(string? value, out DocumentType result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, ignoreCase: true, out result);
    }
}
