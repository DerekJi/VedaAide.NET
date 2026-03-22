using FluentAssertions;
using NUnit.Framework;
using Veda.Prompts;

namespace Veda.Core.Tests;

[TestFixture]
public class ChainOfThoughtStrategyTests
{
    private ChainOfThoughtStrategy _sut = null!;

    [SetUp]
    public void SetUp() => _sut = new ChainOfThoughtStrategy();

    [Test]
    public void Enhance_ValidInput_ShouldContainOriginalContext()
    {
        const string context  = "Alice was born in 1990.";
        const string question = "How old is Alice?";

        var result = _sut.Enhance(question, context);

        result.Should().Contain(context);
    }

    [Test]
    public void Enhance_ValidInput_ShouldContainOriginalQuestion()
    {
        const string context  = "Some context";
        const string question = "What is the capital of France?";

        var result = _sut.Enhance(question, context);

        result.Should().Contain(question);
    }

    [Test]
    public void Enhance_ValidInput_ShouldContainCoTStepInstruction()
    {
        var result = _sut.Enhance("Q?", "ctx");

        // The CoT prefix must guide LLM to reason step-by-step before concluding
        result.Should().ContainAny("步骤", "推导", "分析", "step", "think");
    }

    [Test]
    public void Enhance_ContextAppearsBeforeQuestion()
    {
        const string context  = "UNIQUE_CONTEXT_MARKER";
        const string question = "UNIQUE_QUESTION_MARKER";

        var result = _sut.Enhance(question, context);

        result.IndexOf(context, StringComparison.Ordinal)
              .Should().BeLessThan(result.IndexOf(question, StringComparison.Ordinal));
    }
}
