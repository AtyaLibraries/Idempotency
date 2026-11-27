using Atya.Foundation.Results;
using Microsoft.Extensions.Time.Testing;

namespace Atya.Application.Idempotency.UnitTests;

public sealed class InMemoryIdempotencyStoreTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Store_IsEphemeral()
    {
        new InMemoryIdempotencyStore(_time).IsEphemeral.Should().BeTrue();
    }

    [Fact]
    public void Constructor_RejectsNullTimeProviderAndNonPositiveCapacity()
    {
        Action noTime = () => _ = new InMemoryIdempotencyStore(null!);
        Action noCapacity = () => _ = new InMemoryIdempotencyStore(_time, 0);

        noTime.Should().Throw<ArgumentNullException>();
        noCapacity.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Capacity_IsEnforced_AndReportedAsCapacityExceeded()
    {
        var store = new InMemoryIdempotencyStore(_time, 3);
        for (int i = 0; i < 3; i++)
        {
            (await store.TryBeginAsync<string>(new IdempotencyKey("k" + i), TimeSpan.FromMinutes(1), null, CancellationToken.None)).IsBegun.Should().BeTrue();
        }

        var overflow = await store.TryBeginAsync<string>(new IdempotencyKey("k3"), TimeSpan.FromMinutes(1), null, CancellationToken.None);

        overflow.Status.Should().Be(IdempotencyBeginStatus.CapacityExceeded);
        store.Count.Should().Be(3);
    }

    [Fact]
    public async Task ExpiredEntries_ArePurgedByExpiryOrder_FreeingCapacity()
    {
        var store = new InMemoryIdempotencyStore(_time, 2);
        await store.TryBeginAsync<string>(new IdempotencyKey("short"), TimeSpan.FromSeconds(10), null, CancellationToken.None);
        await store.TryBeginAsync<string>(new IdempotencyKey("long"), TimeSpan.FromMinutes(10), null, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(11));

        var accepted = await store.TryBeginAsync<string>(new IdempotencyKey("new"), TimeSpan.FromMinutes(1), null, CancellationToken.None);

        accepted.IsBegun.Should().BeTrue();
        store.Keys.Select(k => k.Value).Should().BeEquivalentTo("long", "new");
    }

    [Fact]
    public async Task Property_EntryCountNeverExceedsTheBound_AcrossRandomOperations()
    {
        var random = new Random(57);
        var store = new InMemoryIdempotencyStore(_time, 25);
        var executions = new List<IdempotencyExecution>();

        for (int i = 0; i < 5000; i++)
        {
            var key = new IdempotencyKey("k" + random.Next(0, 80));
            switch (random.Next(0, 5))
            {
                case 0:
                case 1:
                    var begin = await store.TryBeginAsync<string>(key, TimeSpan.FromSeconds(random.Next(1, 30)), null, CancellationToken.None);
                    if (begin.IsBegun)
                    {
                        executions.Add(begin.Execution!);
                    }

                    break;
                case 2 when executions.Count > 0:
                    await store.CompleteAsync(executions[random.Next(executions.Count)], Result.Success("v"), TimeSpan.FromSeconds(random.Next(1, 30)), CancellationToken.None);
                    break;
                case 3 when executions.Count > 0:
                    await store.AbandonAsync(executions[random.Next(executions.Count)], CancellationToken.None);
                    break;
                default:
                    _time.Advance(TimeSpan.FromSeconds(random.Next(0, 5)));
                    break;
            }

            store.Count.Should().BeLessThanOrEqualTo(25);
        }
    }

    [Fact]
    public async Task ManyRenewals_DoNotGrowTheExpiryQueueWithoutBound()
    {
        var store = new InMemoryIdempotencyStore(_time, 10);
        var execution = (await store.TryBeginAsync<string>(new IdempotencyKey("k"), TimeSpan.FromSeconds(10), null, CancellationToken.None)).Execution!;

        for (int i = 0; i < 6000; i++)
        {
            (await store.RenewAsync(execution, TimeSpan.FromSeconds(10), CancellationToken.None)).Should().Be(IdempotencyTransition.Applied);
        }

        _time.Advance(TimeSpan.FromSeconds(11));
        (await store.TryBeginAsync<string>(new IdempotencyKey("k"), TimeSpan.FromSeconds(10), null, CancellationToken.None)).IsBegun.Should().BeTrue();
    }

    [Fact]
    public async Task Methods_RejectNullAndNonPositiveArguments()
    {
        var store = new InMemoryIdempotencyStore(_time);
        var execution = (await store.TryBeginAsync<string>(new IdempotencyKey("k"), TimeSpan.FromSeconds(10), null, CancellationToken.None)).Execution!;

        Func<Task> badLease = async () => await store.TryBeginAsync<string>(new IdempotencyKey("z"), TimeSpan.Zero, null, CancellationToken.None);
        Func<Task> nullExecution = async () => await store.AbandonAsync(null!, CancellationToken.None);
        Func<Task> nullResult = async () => await store.CompleteAsync<string>(execution, null!, TimeSpan.FromSeconds(1), CancellationToken.None);
        Func<Task> badTtl = async () => await store.CompleteAsync(execution, Result.Success("v"), TimeSpan.Zero, CancellationToken.None);
        Func<Task> badExtension = async () => await store.RenewAsync(execution, TimeSpan.Zero, CancellationToken.None);

        await badLease.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await nullExecution.Should().ThrowAsync<ArgumentNullException>();
        await nullResult.Should().ThrowAsync<ArgumentNullException>();
        await badTtl.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await badExtension.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ExecutionAndFence_RejectBlankValues()
    {
        Action blank = () => _ = new IdempotencyExecution(new IdempotencyKey("k"), new IdempotencyFence(" "), _time.GetUtcNow());

        blank.Should().Throw<ArgumentException>();
        new IdempotencyFence("abc").ToString().Should().Be("abc");
    }

    [Fact]
    public void BeginResultFactories_RejectNull()
    {
        Action begun = () => IdempotencyBeginResult.Begun<string>(null!);
        Action completed = () => IdempotencyBeginResult.Completed<string>(null!);

        begun.Should().Throw<ArgumentNullException>();
        completed.Should().Throw<ArgumentNullException>();
    }
}
