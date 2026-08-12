namespace Veda.Core.Interfaces;

/// <summary>
/// Public resume tailoring service contract: retrieves public resume material based on the JD and streams a tailored Markdown resume.
/// Does not depend on the current user's identity; only retrieves documents with Visibility=Public.
/// </summary>
public interface IPublicResumeTailoringService
{
    /// <summary>
    /// Streams a tailored resume in Markdown format based on the Job Description.
    /// Each yield returns a string fragment of one LLM token.
    /// </summary>
    /// <param name="jobDescription">Job description provided by the employer, up to 4000 characters.</param>
    /// <param name="topK">Maximum number of resume chunks returned by vector retrieval.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<string> TailorStreamAsync(string jobDescription, int topK, CancellationToken ct = default);
}
