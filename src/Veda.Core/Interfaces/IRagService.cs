namespace Veda.Core.Interfaces;

/// <summary>
/// 已拆分为 <see cref="IDocumentIngestor"/> 和 <see cref="IQueryService"/>（ISP 原则）。
/// 请直接依赖具体接口，本接口记录是分拆前的历史版本，新代码不应使用。
/// </summary>
[Obsolete("Use IDocumentIngestor (for ingestion) and IQueryService (for querying) instead.")]
public interface IRagService : IDocumentIngestor, IQueryService { }
