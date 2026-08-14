namespace Veda.Core;

/// <summary>
/// Identifies the source an evaluation dataset is loaded from.
/// Database: the Golden Dataset stored in the repository (current behavior, default).
/// HuggingFace: a dataset pulled from a HuggingFace repo.
/// LocalFile: a dataset read from a local JSON/CSV file.
/// </summary>
public enum EvalDatasetSource
{
    Database,
    HuggingFace,
    LocalFile
}
