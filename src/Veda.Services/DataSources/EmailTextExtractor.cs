using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;

namespace Veda.Services.DataSources;

/// <summary>
/// 从 .eml / .msg 邮件文件中提取纯文本，供摄取管道使用。
/// .eml 由 MimeKit 解析；.msg 由 MsgReader 解析（Outlook CFB 格式）。
/// </summary>
internal static class EmailTextExtractor
{
    /// <summary>从邮件文件提取可读纯文本。</summary>
    /// <param name="filePath">文件完整路径</param>
    /// <param name="extension">小写扩展名（".eml" 或 ".msg"）</param>
    /// <param name="ct">取消令牌</param>
    public static Task<string> ExtractAsync(string filePath, string extension, CancellationToken ct = default)
        => extension.Equals(".eml", StringComparison.OrdinalIgnoreCase)
            ? ExtractEmlAsync(filePath, ct)
            : ExtractMsgAsync(filePath);

    // ── EML ───────────────────────────────────────────────────────────────

    private static async Task<string> ExtractEmlAsync(string filePath, CancellationToken ct)
    {
        var message = await MimeMessage.LoadAsync(filePath, ct);

        var subject = message.Subject ?? string.Empty;
        var from    = message.From.ToString();
        var to      = message.To.ToString();
        var date    = message.Date.ToString("yyyy-MM-dd HH:mm");
        var body    = message.TextBody ?? StripHtml(message.HtmlBody) ?? string.Empty;

        return FormatEmail(subject, from, to, date, body);
    }

    // ── MSG ───────────────────────────────────────────────────────────────

    private static Task<string> ExtractMsgAsync(string filePath)
    {
        using var msg = new MsgReader.Outlook.Storage.Message(filePath, FileAccess.Read);

        var subject = msg.Subject ?? string.Empty;
        var from    = msg.Sender?.DisplayName ?? msg.Sender?.Email ?? string.Empty;
        var to      = BuildRecipientList(msg.Recipients);
        var date    = msg.SentOn?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
        var body    = msg.BodyText ?? StripHtml(msg.BodyHtml) ?? string.Empty;

        return Task.FromResult(FormatEmail(subject, from, to, date, body));
    }

    private static string BuildRecipientList(IEnumerable<MsgReader.Outlook.Storage.Recipient>? recipients)
    {
        if (recipients is null) return string.Empty;
        var names = recipients
            .Select(r => !string.IsNullOrWhiteSpace(r.DisplayName) ? r.DisplayName : r.Email)
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(", ", names);
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    private static string FormatEmail(string subject, string from, string to, string date, string body)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(subject)) sb.AppendLine($"Subject: {subject}");
        if (!string.IsNullOrWhiteSpace(from))    sb.AppendLine($"From: {from}");
        if (!string.IsNullOrWhiteSpace(to))      sb.AppendLine($"To: {to}");
        if (!string.IsNullOrWhiteSpace(date))    sb.AppendLine($"Date: {date}");
        if (sb.Length > 0)                       sb.AppendLine();
        sb.Append(body);
        return sb.ToString();
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        // Remove script/style blocks
        var text = Regex.Replace(html,
            @"<(script|style)[^>]*?>.*?</\1>",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Replace block-level elements with newlines
        text = Regex.Replace(text,
            @"<(br|p|div|h[1-6]|li|tr)[^>]*?/?>",
            "\n",
            RegexOptions.IgnoreCase);

        // Strip remaining tags
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);

        // Decode HTML entities
        text = WebUtility.HtmlDecode(text);

        // Collapse excessive blank lines
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }
}
