namespace Veda.Core.Options;

/// <summary>Configuration options for the Semantics module.</summary>
public sealed class SemanticsOptions
{
    /// <summary>
    /// Path to the personal vocabulary JSON file (absolute or relative).
    /// Leave empty to fall back to NoOpSemanticEnhancer (pass-through).
    /// </summary>
    public string? VocabularyFilePath { get; set; }
}
