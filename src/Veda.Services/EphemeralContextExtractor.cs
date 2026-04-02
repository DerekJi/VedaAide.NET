namespace Veda.Services;

/// <summary>
/// 临时上下文提取器（Ephemeral RAG / Context Augmentation）。
///
/// 职责：根据文件 MIME 类型自动选择提取器，将文件内容转为纯文本，
/// 不触发 Chunk / Embed / 向量写库流程，提取结果仅返回给调用方。
///
/// DocumentType 推断规则（用户无需手动选择）：
///   image/*                          → RichMedia  → VisionModelFileExtractor
///   application/pdf                  → Other      → PdfTextLayerExtractor → Vision fallback
///   text/plain / text/csv / text/xml → Other      → 直接读取字符串
///   其他（DOCX、EML 等）              → Other      → DocumentIntelligenceFileExtractor → Vision fallback
///
/// Context window 保护：提取结果超过 <see cref="MaxChars"/> 字符时截断并追加说明。
/// </summary>
public sealed class EphemeralContextExtractor(
    VisionModelFileExtractor visionExtractor,
    DocumentIntelligenceFileExtractor docIntelExtractor,
    PdfTextLayerExtractor pdfTextLayerExtractor,
    ILogger<EphemeralContextExtractor> logger)
{
    /// <summary>单次临时上传最大字符数（约 32K tokens），超出后截断。</summary>
    internal const int MaxChars = 60_000;

    /// <summary>
    /// 从文件流提取纯文本，不写数据库。
    /// </summary>
    /// <param name="fileStream">文件流（不要求 seekable，方法内部缓冲）。</param>
    /// <param name="fileName">原始文件名，用于日志和归属展示。</param>
    /// <param name="mimeType">MIME 类型，用于路由提取器。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>提取的纯文本；提取失败或结果为空时返回 null。</returns>
    public async Task<string?> ExtractAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        // 缓冲：允许在降级路径中重置并重读
        using var buffered = new MemoryStream();
        await fileStream.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        var normalizedMime = mimeType.Split(';')[0].Trim().ToLowerInvariant();

        logger.LogInformation(
            "EphemeralContextExtractor: extracting '{Name}' ({Mime}, {Bytes} bytes)",
            fileName, normalizedMime, buffered.Length);

        string? text = await ExtractByMimeAsync(buffered, fileName, normalizedMime, ct);

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("EphemeralContextExtractor: empty result for '{Name}'", fileName);
            return null;
        }

        // 如果 Vision 模型实际上是纯文字模型（如 qwen3:8b），它会返回
        // "无法直接查看图片" 之类的说明而非真实内容。检测此类失败，返回 null。
        if (IsVisionFailureResponse(text))
        {
            logger.LogWarning(
                "EphemeralContextExtractor: Vision model could not process image '{Name}'. " +
                "Ensure a multimodal model is configured under Veda:Vision:OllamaModel.", fileName);
            return null;
        }

        if (text.Length > MaxChars)
        {
            logger.LogInformation(
                "EphemeralContextExtractor: truncating '{Name}' from {Len} to {Max} chars",
                fileName, text.Length, MaxChars);
            text = text[..MaxChars] + "\n\n[... 内容过长，已截断 ...]";
        }

        logger.LogInformation(
            "EphemeralContextExtractor: '{Name}' → {Chars} chars", fileName, text.Length);

        return text;
    }

    private async Task<string?> ExtractByMimeAsync(
        MemoryStream buffered, string fileName, string mime, CancellationToken ct)
    {
        // ── 图片：直接走 Vision 模型 ──────────────────────────────────────────────
        if (mime.StartsWith("image/", StringComparison.Ordinal))
        {
            return await TryVisionAsync(buffered, fileName, mime, DocumentType.RichMedia, ct);
        }

        // ── 纯文本：直接读字符串 ───────────────────────────────────────────────────
        if (mime is "text/plain" or "text/csv" or "text/xml" or "text/html")
        {
            buffered.Position = 0;
            using var reader = new System.IO.StreamReader(buffered, leaveOpen: true);
            return await reader.ReadToEndAsync(ct);
        }

        // ── PDF：文字层直通 → Vision fallback ────────────────────────────────────
        if (mime == "application/pdf")
        {
            buffered.Position = 0;
            var textLayer = pdfTextLayerExtractor.TryExtract(buffered, fileName);
            if (textLayer is not null)
                return textLayer;

            // 扫描件：降级 Vision
            logger.LogInformation(
                "EphemeralContextExtractor: '{Name}' is scanned PDF, falling back to Vision", fileName);
            buffered.Position = 0;
            return await TryVisionAsync(buffered, fileName, mime, DocumentType.Other, ct);
        }

        // ── 其他（DOCX / EML / MSG 等）：Azure DI → Vision fallback ─────────────
        try
        {
            buffered.Position = 0;
            return await docIntelExtractor.ExtractAsync(buffered, fileName, mime, DocumentType.Other, ct);
        }
        catch (Exception ex)
        {
            var reason = ex is QuotaExceededException ? "quota exceeded" : $"{ex.GetType().Name}: {ex.Message}";
            logger.LogWarning(
                ex, "EphemeralContextExtractor: Azure DI failed ({Reason}) for '{Name}', trying Vision",
                reason, fileName);
            buffered.Position = 0;
            return await TryVisionAsync(buffered, fileName, mime, DocumentType.Other, ct);
        }
    }

    private async Task<string?> TryVisionAsync(
        MemoryStream buffered, string fileName, string mime, DocumentType docType, CancellationToken ct)
    {
        try
        {
            buffered.Position = 0;
            return await visionExtractor.ExtractAsync(buffered, fileName, mime, docType, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "EphemeralContextExtractor: Vision extraction failed for '{Name}' ({ExType})",
                fileName, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// 检测 Vision 模型返回了"看不到图片"的说明文字而非实际内容。
    /// 常见于纯文字模型（如 qwen3:8b）被误用为 Vision 服务时。
    /// </summary>
    private static bool IsVisionFailureResponse(string text)
    {
        // 检测常见的中英文"无法查看图片"类回复
        ReadOnlySpan<string> indicators =
        [
            "无法直接查看图片",
            "无法查看图片",
            "无法识别图片",
            "cannot view the image",
            "cannot see the image",
            "unable to view the image",
            "I don't have the ability to view images",
            "以下为通用模板",
            "请提供具体图片描述",
        ];

        var lowerText = text.AsSpan()[..Math.Min(300, text.Length)];
        foreach (var indicator in indicators)
        {
            if (lowerText.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
