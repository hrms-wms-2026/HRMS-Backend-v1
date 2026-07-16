using FluentAssertions;
using ONEVO.Application.Common.Models;
using Xunit;

namespace ONEVO.Tests.Unit;

public class ResultTests
{
    [Fact]
    public void Success_ReturnsSuccessResult_WithValue()
    {
        var result = Result<string>.Success("hello");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_ReturnsFailureResult_WithError()
    {
        var result = Result<string>.Failure("Something went wrong", 400);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Something went wrong");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public void NotFound_Returns404StatusCode()
    {
        var result = Result<string>.NotFound("Resource not found");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public void Forbidden_Returns403StatusCode()
    {
        var result = Result<string>.Forbidden();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
