namespace Veda.Services;

/// <summary>
/// Ephemeral context extractor (Ephemeral RAG / Context Augmentation).
///
/// Responsibility: automatically selects an extractor based on the file MIME type and
/// converts the file content to plain text without triggering the Chunk / Embed / vector-store
/// pipeline; the extracted result is returned only to the caller.
///
/// DocumentType inference rules (no manual selection needed):
///   image/*                          → RichMedia  → VisionModelFileExtractor
///   application/pdf                  → Other      → PdfTextLayerExtractor → Vision fallback
///   text/plain / text/csv / text/xml → Other      → read the string directly
///   Others (DOCX, EML, etc.)         → Other      → DocumentIntelligenceFileExtractor → Vision fallback
///
/// Context window protection: when the extraction exceeds <see cref="MaxChars"/> characters,
/// it is truncated and a note is appended.
/// </summary>
public sealed class EphemeralContextExtractor(
    VisionModelFileExtractor visionExtractor,
    DocumentIntelligenceFileExtractor docIntelExtractor,
    PdfTextLayerExtractor pdfTextLayerExtractor,
    ILogger<EphemeralContextExtractor> logger)
{
    /// <summary>Maximum characters for a single ephemeral upload (roughly 32K tokens); truncated beyond that.</summary>
    internal const int MaxChars = 60_000;

    /// <summary>
    /// Extracts plain text from a file stream without writing to the database.
    /// </summary>
    /// <param name="fileStream">The file stream (need not be seekable; buffered internally).</param>
    /// <param name="fileName">Original file name, used for logging and attribution display.</param>
    /// <param name="mimeType">MIME type, used to route to the appropriate extractor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The extracted plain text, or null if extraction failed or the result is empty.</returns>
    public async Task<string?> ExtractAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        // Buffer so we can reset and re-read on fallback paths
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

        // If the Vision model is actually a text-only model (e.g. qwen3:8b), it returns
        // an explanation like "cannot view the image" instead of real content. Detect such failures and return null.
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
        // ── Images: use the Vision model directly ──────────────────────────────────────────────
        if (mime.StartsWith("image/", StringComparison.Ordinal))
        {
            return await TryVisionAsync(buffered, fileName, mime, DocumentType.RichMedia, ct);
        }

        // ── Plain text: read the string directly ───────────────────────────────────────────────────
        if (mime is "text/plain" or "text/csv" or "text/xml" or "text/html")
        {
            buffered.Position = 0;
            using var reader = new System.IO.StreamReader(buffered, leaveOpen: true);
            return await reader.ReadToEndAsync(ct);
        }

        // ── PDF: pass-through text layer → Vision fallback ────────────────────────────────────
        if (mime == "application/pdf")
        {
            buffered.Position = 0;
            var textLayer = pdfTextLayerExtractor.TryExtract(buffered, fileName);
            if (textLayer is not null)
                return textLayer;

            // Scanned PDF: fall back to Vision
            logger.LogInformation(
                "EphemeralContextExtractor: '{Name}' is scanned PDF, falling back to Vision", fileName);
            buffered.Position = 0;
            return await TryVisionAsync(buffered, fileName, mime, DocumentType.Other, ct);
        }

        // ── Others (DOCX / EML / MSG, etc.): Azure DI → Vision fallback ─────────────
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
    /// Detects when the Vision model returns an explanation saying it cannot "see" the image
    /// rather than actual content. Common when a text-only model (e.g. qwen3:8b) is misused as a Vision service.
    /// </summary>
    private static bool IsVisionFailureResponse(string text)
    {
        // Detect common Chinese/English "cannot view the image" style responses
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
