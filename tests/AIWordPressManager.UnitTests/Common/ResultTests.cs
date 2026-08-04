using AIWordPressManager.Application.Common.Results;
using FluentAssertions;

namespace AIWordPressManager.UnitTests.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_ShouldExposeValue()
    {
        Result<int> result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_ShouldExposeError()
    {
        Error error = Error.Validation("Invalid input.");

        Result result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void FailedGenericResult_ValueAccess_ShouldThrow()
    {
        Result<string> result = Result.Failure<string>(Error.NotFound("Missing."));

        Action action = () => _ = result.Value;

        action.Should().Throw<InvalidOperationException>();
    }
}
