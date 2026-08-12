using Veda.Core.Options;
namespace Veda.Core.Interfaces;

/// <summary>
/// Evaluation runner: runs the RAG pipeline in batch against the Golden Dataset,
/// computes three-dimensional evaluation metrics for each question, and aggregates them into an <see cref="EvaluationReport"/>.
/// </summary>
public interface IEvaluationRunner
{
    Task<EvaluationReport> RunAsync(EvalRunOptions options, CancellationToken ct = default);
}
