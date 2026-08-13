
using Veda.Evaluation.Scorers;
using Veda.Services;
namespace Veda.Evaluation;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVedaEvaluation(this IServiceCollection services)
    {
        services.AddScoped<FaithfulnessScorer>();
        services.AddScoped<AnswerRelevancyScorer>();
        services.AddScoped<ContextRecallScorer>();
        services.AddScoped<IEvalDatasetProvider, DatabaseEvalDatasetProvider>();
        services.AddScoped<IEvaluationRunner, EvaluationRunner>();
        return services;
    }
}
