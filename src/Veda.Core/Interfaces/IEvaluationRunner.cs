using Veda.Core.Options;
namespace Veda.Core.Interfaces;

/// <summary>
/// 评估运行器：根据 Golden Dataset 批量跑 RAG 管道，
/// 对每个问题计算三维评估指标，汇总为 <see cref="EvaluationReport"/>。
/// </summary>
public interface IEvaluationRunner
{
    Task<EvaluationReport> RunAsync(EvalRunOptions options, CancellationToken ct = default);
}
