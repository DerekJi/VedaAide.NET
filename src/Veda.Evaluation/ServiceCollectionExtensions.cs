using Veda.Evaluation.Scorers;

namespace Veda.Evaluation;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVedaEvaluation(this IServiceCollection services)
    {
        services.AddScoped<FaithfulnessScorer>();
        services.AddScoped<AnswerRelevancyScorer>();
        services.AddScoped<ContextRecallScorer>();

        // Composite dispatcher: routes each EvalDatasetSource to the provider registered for it.
        // Concrete providers self-register in their own layer (Veda.Services → AddVedaAiServices),
        // so new sources (HuggingFace / LocalFile) can be added without changing this assembly.
        services.AddScoped<IEvalDatasetProvider>(sp => new EvalDatasetProviderDispatcher(sp));

        services.AddScoped<IEvaluationRunner, EvaluationRunner>();
        return services;
    }
}
