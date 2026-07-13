namespace Atya.Application.Idempotency.UnitTests;

public sealed class IdempotencyKeyTests
{
    [Fact]
    public void Constructor_With_Value_Stores_Value()
    {
        var key = new IdempotencyKey("payment:123");

        key.Value.Should().Be("payment:123");
        key.ToString().Should().Be("payment:123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_With_Invalid_Value_Throws(string? value)
    {
        var act = () => new IdempotencyKey(value!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_With_Value_Returns_Key()
    {
        var result = IdempotencyKey.Create("shipment:123");

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("shipment:123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_With_Invalid_Value_Returns_Validation_Failure(string? value)
    {
        var result = IdempotencyKey.Create(value);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("atya.application.idempotency.key_required");
    }
}
