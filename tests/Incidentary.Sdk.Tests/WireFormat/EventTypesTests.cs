using FluentAssertions;
using Incidentary.Sdk.WireFormat;
using Xunit;

namespace Incidentary.Sdk.Tests.WireFormat;

public sealed class EventTypesTests
{
    [Fact]
    public void HttpIn_IsCorrect() => EventTypes.HttpIn.Should().Be("http_in");

    [Fact]
    public void HttpOut_IsCorrect() => EventTypes.HttpOut.Should().Be("http_out");

    [Fact]
    public void GrpcIn_IsCorrect() => EventTypes.GrpcIn.Should().Be("grpc_in");

    [Fact]
    public void GrpcOut_IsCorrect() => EventTypes.GrpcOut.Should().Be("grpc_out");

    [Fact]
    public void WebhookIn_IsCorrect() => EventTypes.WebhookIn.Should().Be("webhook_in");

    [Fact]
    public void WebhookOut_IsCorrect() => EventTypes.WebhookOut.Should().Be("webhook_out");

    [Fact]
    public void QueuePublish_IsCorrect() => EventTypes.QueuePublish.Should().Be("queue_publish");

    [Fact]
    public void QueueConsume_IsCorrect() => EventTypes.QueueConsume.Should().Be("queue_consume");

    [Fact]
    public void JobStart_IsCorrect() => EventTypes.JobStart.Should().Be("job_start");

    [Fact]
    public void JobEnd_IsCorrect() => EventTypes.JobEnd.Should().Be("job_end");

    [Fact]
    public void DbQuery_IsCorrect() => EventTypes.DbQuery.Should().Be("db_query");

    [Fact]
    public void InternalTask_IsCorrect() => EventTypes.InternalTask.Should().Be("internal_task");
}
