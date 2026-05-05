namespace Incidentary.Sdk.WireFormat;

/// <summary>Event type vocabulary. Additive — new types may appear without a major version bump.</summary>
public static class EventTypes
{
    public const string HttpServer = "http_server";
    public const string HttpClient = "http_client";
    public const string GrpcIn = "grpc_in";
    public const string GrpcOut = "grpc_out";
    public const string WebhookIn = "webhook_in";
    public const string WebhookOut = "webhook_out";
    public const string QueuePublish = "queue_publish";
    public const string QueueConsume = "queue_consume";
    public const string JobStart = "job_start";
    public const string JobEnd = "job_end";
    public const string DbQuery = "db_query";
    public const string InternalTask = "internal_task";

    // V1 aliases — kept so public API callers using the old names still compile.
    // These resolve to the same string constants as the V2 names.
    /// <summary>Alias for <see cref="HttpServer"/>.</summary>
    public const string HttpIn = HttpServer;
    /// <summary>Alias for <see cref="HttpClient"/>.</summary>
    public const string HttpOut = HttpClient;
}
