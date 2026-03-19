namespace Veda.Core.Interfaces;

/// <summary>
/// 文档处理管道：解析原始内容并输出分块列表。
/// </summary>
public interface IDocumentProcessor
{
    /// <param name="content">原始文本内容</param>
    /// <param name="documentName">文件名，用于元数据</param>
    /// <param name="documentType">影响分块粒度</param>
    /// <param name="documentId">由 Service 层传入的文档 ID，确保调用方可持有并用于删除和测试</param>
    IReadOnlyList<DocumentChunk> Process(string content, string documentName, DocumentType documentType, string documentId);
}
