namespace Veda.Storage.Entities;

public class EvalQuestionEntity
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string ExpectedAnswer { get; set; } = string.Empty;
    /// <summary>JSON array of tag strings.</summary>
    public string TagsJson { get; set; } = "[]";
    public long CreatedAtTicks { get; set; }
}
