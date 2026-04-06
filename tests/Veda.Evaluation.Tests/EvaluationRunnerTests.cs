using Veda.Core.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Evaluation.Scorers;

namespace Veda.Evaluation.Tests;

[TestFixture]
public class EvaluationRunnerTests
{
    private Mock<IEvalDatasetRepository> _dataset     = null!;
    private Mock<IQueryService>          _queryService = null!;
    private Mock<IChatService>           _chatService  = null!;
    private Mock<IEmbeddingService>      _embedding    = null!;
    private EvaluationRunner             _sut          = null!;

    [SetUp]
    public void SetUp()
    {
        _dataset      = new Mock<IEvalDatasetRepository>();
        _queryService = new Mock<IQueryService>();
        _chatService  = new Mock<IChatService>();
        _embedding    = new Mock<IEmbeddingService>();

        var faithfulness = new FaithfulnessScorer(_chatService.Object, NullLogger<FaithfulnessScorer>.Instance);
        var relevancy    = new AnswerRelevancyScorer(_embedding.Object);
        var recall       = new ContextRecallScorer(_embedding.Object);

        _sut = new EvaluationRunner(
            _dataset.Object,
            _queryService.Object,
            faithfulness,
            relevancy,
            recall,
            NullLogger<EvaluationRunner>.Instance);
    }

    [Test]
    public async Task RunAsync_EmptyDataset_ShouldReturnEmptyReport()
    {
        _dataset.Setup(d => d.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var report = await _sut.RunAsync(new EvalRunOptions());

        report.Results.Should().BeEmpty();
        report.AvgOverall.Should().Be(0f);
    }

    [Test]
    public async Task RunAsync_SingleQuestion_ShouldProduceResult()
    {
        var question = new EvalQuestion
        {
            Question       = "What is X?",
            ExpectedAnswer = "X is Y.",
        };

        _dataset.Setup(d => d.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([question]);

        _queryService.Setup(q => q.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse
            {
                Answer          = "X is Y.",
                Sources         = [],
                IsHallucination = false,
                AnswerConfidence = 0.9f,
            });

        // FaithfulnessScorer (LLM) → return 0.8
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0.8");

        // Embeddings for AnswerRelevancyScorer and ContextRecallScorer
        var vec = new float[] { 1f, 0f };
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vec);
        _embedding.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { vec });

        var report = await _sut.RunAsync(new EvalRunOptions());

        report.Results.Should().HaveCount(1);
        report.Results[0].Question.Should().Be(question.Question);
        report.Results[0].Metrics.Faithfulness.Should().BeApproximately(0.8f, 0.001f);
    }

    [Test]
    public async Task RunAsync_FilterByQuestionIds_ShouldRunOnlySpecifiedQuestions()
    {
        var q1 = new EvalQuestion { Question = "Q1", ExpectedAnswer = "A1" };
        var q2 = new EvalQuestion { Question = "Q2", ExpectedAnswer = "A2" };

        _dataset.Setup(d => d.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([q1, q2]);

        _queryService.Setup(q => q.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse { Answer = "A", Sources = [], AnswerConfidence = 0.5f });

        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0.5");

        var vec = new float[] { 1f, 0f };
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vec);
        _embedding.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]>());

        var options = new EvalRunOptions { QuestionIds = [q1.Id] };
        var report = await _sut.RunAsync(options);

        report.Results.Should().HaveCount(1);
        report.Results[0].Question.Should().Be("Q1");
    }

    [Test]
    public async Task RunAsync_QueryServiceThrows_ShouldIncludeEmptyResultWithoutCrashing()
    {
        var question = new EvalQuestion { Question = "Q?", ExpectedAnswer = "A." };

        _dataset.Setup(d => d.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([question]);

        _queryService.Setup(q => q.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM down"));

        var report = await _sut.RunAsync(new EvalRunOptions());

        report.Results.Should().HaveCount(1);
        report.Results[0].ActualAnswer.Should().BeEmpty();
    }
}
