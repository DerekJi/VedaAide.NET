namespace Veda.Storage.Entities;

public class EvalRunEntity
{
    public string RunId { get; set; } = string.Empty;
    public long RunAtTicks { get; set; }
    public string ModelName { get; set; } = string.Empty;
    /// <summary>Full <see cref="Veda.Core.EvaluationReport"/> serialised as JSON.</summary>
    public string ReportJson { get; set; } = string.Empty;
}
