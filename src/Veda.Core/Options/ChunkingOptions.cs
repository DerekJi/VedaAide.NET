namespace Veda.Core.Options;

using Veda.Core;

/// <summary>
/// Configuration that drives the dynamic chunking strategy, determined by DocumentType.
/// DedupThreshold: cosine similarity threshold for semantic deduplication during ingestion, default 0.95;
/// higher values mean more lenient deduplication (only near-duplicates with extremely high similarity are detected).
/// Set to 1.0 for the Certificate type (effectively disabling semantic deduplication) because the embeddings
/// of certificates of the same kind (English/Math/Science) have very high cosine similarity (> 0.97);
/// at 0.70, certificates of different subjects would mistakenly eliminate each other.
/// Deduplication still guarantees that identical content is never stored twice via ContentHash (SHA-256).
/// </summary>
public record ChunkingOptions(int TokenSize, int OverlapTokens, float DedupThreshold = RagDefaults.SimilarityDedupThreshold)
{
	public static ChunkingOptions ForDocumentType(DocumentType type) => type switch
	{
		DocumentType.BillInvoice   => new(256,  32),
		DocumentType.Specification => new(1024, 128),
		DocumentType.Report        => new(512,  64),
		DocumentType.PersonalNote  => new(256,  32),
		DocumentType.RichMedia     => new(512,  64),
		DocumentType.Certificate   => new(256,  32, DedupThreshold: 1.0f),
		_                          => new(512,  64)
	};
}
