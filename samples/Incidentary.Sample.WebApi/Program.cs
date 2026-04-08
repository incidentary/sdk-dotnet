using Incidentary.Sdk;
using Incidentary.Sdk.AspNetCore;
using Incidentary.Sdk.Extensions.DependencyInjection;
using Incidentary.Sdk.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// Register Incidentary SDK
builder.Services.AddIncidentary(options =>
{
    options.ApiKey = builder.Configuration["Incidentary:ApiKey"] ?? "demo-key";
    options.ServiceName = "sample-webapi";
    options.BaseUrl = builder.Configuration["Incidentary:BaseUrl"] ?? "https://api.incidentary.io";
    options.Environment = builder.Environment.EnvironmentName;
});

// Register an outbound HttpClient with tracing
builder.Services.AddHttpClient("downstream")
    .AddIncidentaryTracing();

var app = builder.Build();

// Add Incidentary middleware (early in pipeline for accurate timing)
app.UseIncidentary();

app.MapGet("/", () => "Incidentary .NET SDK Sample");

app.MapGet("/orders/{id:int}", (int id, IIncidentaryClient incidentary) =>
{
    // The middleware automatically records HTTP_IN
    // You can also record custom events:
    incidentary.RecordEvent("order_lookup", new RecordEventOptions
    {
        Status = 200,
        EventAttrs = new Dictionary<string, object> { ["order_id"] = id }
    });

    return Results.Ok(new { Id = id, Status = "shipped" });
});

app.MapPost("/webhooks/stripe", (IIncidentaryClient incidentary) =>
{
    incidentary.RecordWebhookIn();
    return Results.Ok();
});

app.Run();
