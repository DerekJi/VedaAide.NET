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
        // Concrete providers self-register in their own layer (Veda.Services → AddVedaAiServices) via
        // TryAddEnumerable, and the dispatcher injects them as IEnumerable<IEvalDatasetProvider> — so this
        // registration is order-independent and the dispatcher never appears in its own provider enumerable.
        services.AddScoped<IEvalDatasetSourceRouter, EvalDatasetProviderDispatcher>();

        services.AddScoped<IEvaluationRunner, EvaluationRunner>();
        return services;
    }
}
