namespace Veda.Core;

/// <summary>
/// DRY: centralizes DocumentType enum parsing for reuse across the project.
/// Moved from Veda.Api.Extensions to Veda.Core so it can be tested directly in Core.Tests.
/// </summary>
public static class DocumentTypeParser
{
    /// <summary>Parses to a concrete type, returning <paramref name="defaultType"/> on failure.</summary>
    public static DocumentType ParseOrDefault(string? value, DocumentType defaultType = DocumentType.Other)
        => TryParse(value, out var result) ? result : defaultType;

    /// <summary>Parses to a nullable type, returning null on failure (for optional filtering scenarios).</summary>
    public static DocumentType? ParseOrNull(string? value)
        => TryParse(value, out var result) ? result : null;

    private static bool TryParse(string? value, out DocumentType result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, ignoreCase: true, out result);
    }

    /// <summary>Infers the DocumentType from a file name (rules kept in sync with OrchestrationService, centralized here).</summary>
    public static DocumentType InferFromName(string documentName)
    {
        var name = documentName.ToLowerInvariant();
        if (name.Contains("invoice") || name.Contains("bill") || name.Contains("receipt"))
            return DocumentType.BillInvoice;
        if (name.Contains("spec") || name.Contains("pds") || name.Contains("requirement"))
            return DocumentType.Specification;
        if (name.Contains("report") || name.Contains("summary"))
            return DocumentType.Report;
        if (name.Contains("icas") || name.Contains("ameb") || name.Contains("cert") || name.Contains("award"))
            return DocumentType.Certificate;
        return DocumentType.Other;
    }
}
