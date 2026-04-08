# Incidentary SDK for .NET

Official .NET SDK for [Incidentary](https://incidentary.io). Zero-overhead instrumentation with local pre-arm anomaly detection.

## Packages

| Package                                            | Description                | NuGet                                                                                                                                                                            |
| -------------------------------------------------- | -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Incidentary.Sdk`                                  | Core library               | [![NuGet](https://img.shields.io/nuget/v/Incidentary.Sdk.svg)](https://www.nuget.org/packages/Incidentary.Sdk)                                                                   |
| `Incidentary.Sdk.Extensions.DependencyInjection`   | DI registration            | [![NuGet](https://img.shields.io/nuget/v/Incidentary.Sdk.Extensions.DependencyInjection.svg)](https://www.nuget.org/packages/Incidentary.Sdk.Extensions.DependencyInjection)     |
| `Incidentary.Sdk.Extensions.Http`                  | HttpClient instrumentation | [![NuGet](https://img.shields.io/nuget/v/Incidentary.Sdk.Extensions.Http.svg)](https://www.nuget.org/packages/Incidentary.Sdk.Extensions.Http)                                   |
| `Incidentary.Sdk.AspNetCore`                       | ASP.NET Core middleware    | [![NuGet](https://img.shields.io/nuget/v/Incidentary.Sdk.AspNetCore.svg)](https://www.nuget.org/packages/Incidentary.Sdk.AspNetCore)                                             |
| `Incidentary.Sdk.Integrations.Grpc`                | gRPC interceptors          | [![NuGet](https://img.shields.io/nuget/v/Incidentary.Sdk.Integrations.Grpc.svg)](https://www.nuget.org/packages/Incidentary.Sdk.Integrations.Grpc)                               |
| `Incidentary.Sdk.Integrations.MassTransit`         | MassTransit filters        | [![NuGet](https://img.shields.io/nuget/v/Incidentary.Sdk.Integrations.MassTransit.svg)](https://www.nuget.org/packages/Incidentary.Sdk.Integrations.MassTransit)                 |
| `Incidentary.Sdk.Integrations.EntityFrameworkCore` | EF Core interceptor        | [![NuGet](https://img.shields.io/nuget/v/Incidentary.Sdk.Integrations.EntityFrameworkCore.svg)](https://www.nuget.org/packages/Incidentary.Sdk.Integrations.EntityFrameworkCore) |
| `Incidentary.Sdk.Lambda`                           | AWS Lambda wrapper         | [![NuGet](https://img.shields.io/nuget/v/Incidentary.Sdk.Lambda.svg)](https://www.nuget.org/packages/Incidentary.Sdk.Lambda)                                                     |

## Quick Start

### ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIncidentary(options =>
{
    options.ApiKey = builder.Configuration["Incidentary:ApiKey"]!;
    options.ServiceName = "checkout-api";
    options.Environment = builder.Environment.EnvironmentName;
});

var app = builder.Build();
app.UseIncidentary();   // Add early in the pipeline
app.MapControllers();
app.Run();
```

### Outbound HTTP instrumentation

```csharp
builder.Services.AddHttpClient("payments-api")
    .AddIncidentaryTracing();  // Records HTTP_OUT events, propagates trace context
```

### Event vocabulary helpers

```csharp
public class OrderProcessor
{
    private readonly IIncidentaryClient _incidentary;

    public OrderProcessor(IIncidentaryClient incidentary)
    {
        _incidentary = incidentary;
    }

    public async Task ProcessAsync(Order order)
    {
        _incidentary.RecordJobStart();

        // ... process order ...

        _incidentary.RecordJobEnd(new RecordEventOptions { Status = 200 });
    }
}
```

Available helpers: `RecordQueuePublish`, `RecordQueueConsume`, `RecordJobStart`, `RecordJobEnd`, `RecordWebhookIn`, `RecordWebhookOut`.

### gRPC instrumentation

```csharp
// Server
services.AddGrpc(options =>
{
    options.Interceptors.Add<IncidentaryServerInterceptor>();
});

// Client
var channel = GrpcChannel.ForAddress("https://api.internal");
var invoker = channel.Intercept(new IncidentaryClientInterceptor(incidentaryClient));
```

### EF Core instrumentation

```csharp
services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.AddInterceptors(new IncidentaryDbCommandInterceptor(incidentaryClient));
});
```

### AWS Lambda

```csharp
public class Function
{
    private static readonly IncidentaryClient Client = new(new IncidentaryClientOptions
    {
        ApiKey = Environment.GetEnvironmentVariable("INCIDENTARY_API_KEY")!,
        ServiceName = "order-lambda",
        BaseUrl = "https://api.incidentary.io"
    });

    public Task<APIGatewayProxyResponse> Handler(APIGatewayProxyRequest request, ILambdaContext context) =>
        LambdaHandler.Wrap<APIGatewayProxyRequest, APIGatewayProxyResponse>(Client, async (req, ctx) =>
        {
            // Always flushes before Lambda freeze
            return new APIGatewayProxyResponse { StatusCode = 200 };
        })(request, context);
}
```

## How it works

The SDK instruments your .NET services to capture **causal events** — lightweight records of what happened, when, and why. These events form a distributed causal graph that Incidentary uses to reconstruct incident timelines.

### Capture modes

| Mode          | What is captured                                   | When                     |
| ------------- | -------------------------------------------------- | ------------------------ |
| **Normal**    | Skeleton events (timing, status, causal links)     | Default operation        |
| **Pre-armed** | Full detail (headers, retry info, route templates) | Anomaly detected locally |
| **Incident**  | Full detail, bound to incident ID                  | External alert fired     |

### Local pre-arm triggers

The SDK monitors traffic patterns and auto-escalates to detailed capture before any external alert fires:

| Trigger              | Detects                    | Default threshold        |
| -------------------- | -------------------------- | ------------------------ |
| **Error rate (5xx)** | Spike in server errors     | 10% error rate           |
| **Slow success**     | Latency degradation        | 2x EWMA baseline         |
| **In-flight pileup** | Concurrent request buildup | 32 absolute, 2x baseline |
| **Retry onset**      | Downstream retry storms    | 10% retry rate           |

## Configuration defaults

| Option                 | Default        | Description                     |
| ---------------------- | -------------- | ------------------------------- |
| `Environment`          | `"production"` | Environment label               |
| `TimeoutMs`            | `5000`         | HTTP timeout (ms)               |
| `BufferCapacity`       | `4000`         | Ring buffer size                |
| `PreArmThresholdHigh`  | `10.0`         | 5xx % to enter PRE_ARMED        |
| `PreArmThresholdLow`   | `2.0`          | 5xx % to exit PRE_ARMED         |
| `PreArmTtlMs`          | `300000`       | Max time in PRE_ARMED (5 min)   |
| `PreArmCooldownMs`     | `30000`        | Cooldown after exit (30s)       |
| `PreArmMinDurationMs`  | `60000`        | Min PRE_ARMED duration (60s)    |
| `DetailCaptureEnabled` | `true`         | Enable detail in elevated modes |
| `DetailPayloadEnabled` | `false`        | Capture payload snippets        |
| `AutoInstrument`       | `true`         | Auto-discover integrations      |

## Flush behavior

- Events are buffered in a **ring buffer** (4,000 capacity, FIFO overwrite)
- Flushed to backend in batches of up to 500 events
- Retry backoff: 1s, 4s, 16s (3 retries, then drop)
- Circuit breaker: opens after 3 consecutive failures, 60s cooldown
- Quota pause: HTTP 429 pauses until next UTC month

## Trace context propagation

The SDK propagates two headers on all outbound HTTP, gRPC, and queue calls:

```
x-incidentary-trace-id: <UUID>    # Groups events in a distributed trace
x-incidentary-parent-ce: <UUID>   # Names the causal parent event
```

## Target frameworks

- .NET 8 (LTS)
- .NET 9
- .NET 10 (LTS)

## Enterprise features

- **Strong-named assemblies** for GAC and enterprise policies
- **SourceLink** for debugging into SDK source from NuGet
- **Deterministic builds** for reproducibility
- **System.Text.Json source generators** for AOT compatibility
- **ConfigureAwait(false)** throughout for sync-over-async safety
- **Fail-open semantics** — SDK never throws into user code

## License

[Apache 2.0](./LICENSE)
