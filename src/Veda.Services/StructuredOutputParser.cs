using System.Text.Json;
using System.Text.RegularExpressions;

namespace Veda.Services;

/// <summary>
/// LLM 结构化输出解析器。
/// 尝试从 LLM 回答（JSON 格式）中提取 StructuredFinding，解析失败则安全降级为 null。
/// </summary>
public sealed class StructuredOutputParser
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 尝试从 LLM 的 JSON 回答中解析结构化推理结果。
    /// 实现可容错：若 JSON 提取失败，返回 null 而非抛出异常。
    /// </summary>
    public StructuredFinding? TryParse(
        string llmOutput,
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> sources)
    {
        var json = ExtractJson(llmOutput);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            var raw = JsonSerializer.Deserialize<RawFinding>(json, JsonOpts);
            if (raw is null) return null;

            var findingType = Enum.TryParse<FindingType>(raw.Type, ignoreCase: true, out var ft)
                ? ft : FindingType.Information;

            var evidence = BuildEvidence(raw.Evidence, sources);
            var counterEvidence = raw.CounterEvidence?.Count > 0
                ? BuildEvidence(raw.CounterEvidence, sources)
                : null;

            return new StructuredFinding(
                findingType,
                raw.Summary ?? llmOutput,
                evidence,
                counterEvidence,
                raw.Confidence,
                raw.UncertaintyNote);
        }
        catch
        {
            // 降级：返回 null，让调用方使用纯文本 Answer
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>从 LLM 输出中提取第一个 JSON 对象（容忍前后有自由文本）。</summary>
    private static string? ExtractJson(string text)
    {
        var match = Regex.Match(text, @"\{[\s\S]*\}", RegexOptions.Singleline);
        return match.Success ? match.Value : null;
    }

    private static IReadOnlyList<EvidenceItem> BuildEvidence(
        List<string>? refs,
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> sources)
    {
        if (refs is null || refs.Count == 0) return [];

        return refs
            .SelectMany(r =>
                sources
                    .Where(s => s.Chunk.DocumentName.Contains(r, StringComparison.OrdinalIgnoreCase)
                             || s.Chunk.Content.Contains(r, StringComparison.OrdinalIgnoreCase))
                    .Take(1)
                    .Select(s => new EvidenceItem(
                        s.Chunk.DocumentId,
                        s.Chunk.DocumentName,
                        s.Chunk.Content.Length > 200
                            ? s.Chunk.Content[..200] + "..."
                            : s.Chunk.Content,
                        s.Similarity)))
            .ToList()
            .AsReadOnly();
    }

    // ── Raw JSON model ─────────────────────────────────────────────────────────
    private sealed class RawFinding
    {
        public string? Type { get; set; }
        public string? Summary { get; set; }
        public List<string>? Evidence { get; set; }
        public List<string>? CounterEvidence { get; set; }
        public double Confidence { get; set; }
        public string? UncertaintyNote { get; set; }
    }
}
