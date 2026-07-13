using Atya.Foundation.Results;

namespace Atya.Application.Idempotency.UnitTests;

public sealed class IdempotencyErrorsTests
{
    [Fact]
    public void InProgress_Creates_Conflict_Error()
    {
        var key = new IdempotencyKey("orders/123");

        Error error = IdempotencyErrors.InProgress(key);

        error.Code.Should().Be(IdempotencyErrors.InProgressCode);
        error.Message.Should().Contain("orders/123");
        error.Kind.Should().Be(ErrorKind.Conflict);
    }

    [Fact]
    public void Expired_Creates_Conflict_Error()
    {
        var key = new IdempotencyKey("orders/456");

        Error error = IdempotencyErrors.Expired(key);

        error.Code.Should().Be(IdempotencyErrors.ExpiredCode);
        error.Message.Should().Contain("orders/456");
        error.Kind.Should().Be(ErrorKind.Conflict);
    }

    [Fact]
    public void InProgressResult_Creates_Failed_Result()
    {
        var key = new IdempotencyKey("orders/789");

        Result<string> result = IdempotencyErrors.InProgressResult<string>(key);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(IdempotencyErrors.InProgressCode);
    }

    [Fact]
    public void ExpiredResult_Creates_Failed_Result()
    {
        var key = new IdempotencyKey("orders/987");

        Result<string> result = IdempotencyErrors.ExpiredResult<string>(key);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(IdempotencyErrors.ExpiredCode);
    }
}
