namespace Veda.Core.Options;

/// <summary>Semantics 模块配置项。</summary>
public sealed class SemanticsOptions
{
    /// <summary>
    /// 个人词库 JSON 文件路径（绝对路径或相对路径）。
    /// 留空则回退到 NoOpSemanticEnhancer（透传）。
    /// </summary>
    public string? VocabularyFilePath { get; set; }
}
