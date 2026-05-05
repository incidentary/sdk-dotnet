using System.Text.Json;
using FluentAssertions;
using Incidentary.Sdk.WireFormat;
using Xunit;

namespace Incidentary.Sdk.Tests.WireFormat;

public sealed class SerializationTests
{
    private static readonly JsonSerializerOptions Options = WireJson.Options;

    [Theory]
    [InlineData(CeKind.HttpIn, "HTTP_SERVER")]
    [InlineData(CeKind.HttpOut, "HTTP_CLIENT")]
    [InlineData(CeKind.QueuePublish, "QUEUE_PUBLISH")]
    [InlineData(CeKind.QueueConsume, "QUEUE_CONSUME")]
    [InlineData(CeKind.Internal, "INTERNAL")]
    [InlineData(CeKind.DbQuery, "DB_QUERY")]
    [InlineData(CeKind.DbConnect, "DB_CONNECT")]
    [InlineData(CeKind.RpcServer, "RPC_SERVER")]
    [InlineData(CeKind.RpcClient, "RPC_CLIENT")]
    [InlineData(CeKind.Job, "JOB")]
    public void CeKind_SerializesToCorrectWireString(CeKind kind, string expected)
    {
        var json = JsonSerializer.Serialize(kind, Options);
        json.Should().Be($"\"{expected}\"");
    }

    [Theory]
    [InlineData("HTTP_SERVER", CeKind.HttpIn)]
    [InlineData("HTTP_CLIENT", CeKind.HttpOut)]
    [InlineData("QUEUE_PUBLISH", CeKind.QueuePublish)]
    [InlineData("QUEUE_CONSUME", CeKind.QueueConsume)]
    [InlineData("INTERNAL", CeKind.Internal)]
    [InlineData("DB_QUERY", CeKind.DbQuery)]
    [InlineData("DB_CONNECT", CeKind.DbConnect)]
    [InlineData("RPC_SERVER", CeKind.RpcServer)]
    [InlineData("RPC_CLIENT", CeKind.RpcClient)]
    [InlineData("JOB", CeKind.Job)]
    public void CeKind_DeserializesFromWireString(string wire, CeKind expected)
    {
        var result = JsonSerializer.Deserialize<CeKind>($"\"{wire}\"", Options);
        result.Should().Be(expected);
    }

    [Fact]
    public void CausalEvent_RoundTrip_AllFields()
    {
        var original = new CausalEvent
        {
            Id = "550e8400-e29b-41d4-a716-446655440000",
            TraceId = "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
            ParentId = "parent-id-123",
            SpanId = "span-abc",
            ServiceId = "checkout-api",
            OccurredAt = 1733103000000000001,
            Kind = CeKind.HttpIn,
            Type = EventTypes.HttpServer,
            StatusCode = 200,
            Severity = "info",
            DurationNs = 45000000,
            Attributes = new Dictionary<string, object>
            {
                ["route_template"] = "/orders/:id/checkout"
            },
            CapturedBeforeAlert = true,
            RingBufferSeq = 42
        };

        var json = JsonSerializer.Serialize(original, Options);

        // Verify V2 field names
        json.Should().Contain("\"id\"");
        json.Should().Contain("\"trace_id\"");
        json.Should().Contain("\"parent_id\"");
        json.Should().Contain("\"span_id\"");
        json.Should().Contain("\"service_id\"");
        json.Should().Contain("\"occurred_at\"");
        json.Should().Contain("\"kind\"");
        json.Should().Contain("\"type\"");
        json.Should().Contain("\"status_code\"");
        json.Should().Contain("\"severity\"");
        json.Should().Contain("\"duration_ns\"");
        json.Should().Contain("\"attributes\"");
        json.Should().Contain("\"captured_before_alert\"");
        json.Should().Contain("\"ring_buffer_seq\"");

        // V1 field names must NOT appear
        json.Should().NotContain("\"ce_id\"");
        json.Should().NotContain("\"parent_ce_id\"");
        json.Should().NotContain("\"wall_ts_ns\"");
        json.Should().NotContain("\"event_type\"");
        json.Should().NotContain("\"event_class\"");
        json.Should().NotContain("\"status\":");
        json.Should().NotContain("\"sdk_version\"");
        json.Should().NotContain("\"event_attrs\"");

        // Verify CeKind serialization (V2: HTTP_SERVER)
        json.Should().Contain("\"HTTP_SERVER\"");

        // Verify values
        json.Should().Contain("\"checkout-api\"");
        json.Should().Contain("1733103000000000001");
        json.Should().Contain("\"http_server\"");

        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<CausalEvent>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(original.Id);
        deserialized.TraceId.Should().Be(original.TraceId);
        deserialized.ParentId.Should().Be(original.ParentId);
        deserialized.SpanId.Should().Be(original.SpanId);
        deserialized.ServiceId.Should().Be(original.ServiceId);
        deserialized.OccurredAt.Should().Be(original.OccurredAt);
        deserialized.Kind.Should().Be(original.Kind);
        deserialized.Type.Should().Be(original.Type);
        deserialized.StatusCode.Should().Be(original.StatusCode);
        deserialized.Severity.Should().Be(original.Severity);
        deserialized.DurationNs.Should().Be(original.DurationNs);
        deserialized.CapturedBeforeAlert.Should().Be(true);
        deserialized.RingBufferSeq.Should().Be(42);
    }

    [Fact]
    public void IngestBatch_RoundTrip()
    {
        var batch = new IngestBatch
        {
            Specversion = "2",
            Resource = new IngestResource
            {
                WorkspaceId = "ws_01ABCDEF",
                ServiceId = "checkout-api",
                Environment = "production",
                DeployId = "deploy-123",
                GitSha = "abc1234",
            },
            Agent = new IngestAgent
            {
                SdkVersion = "1.0.0",
                QueueDepth = 5,
                DroppedCeCount = 0,
                FlushLatencyMs = 12,
            },
            FlushedAt = 1733103000000000000,
            CaptureMode = CaptureModes.Skeleton,
            Events = new List<CausalEvent>
            {
                new()
                {
                    Id = "550e8400-e29b-41d4-a716-446655440000",
                    TraceId = "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
                    ServiceId = "checkout-api",
                    OccurredAt = 1733103000000000001,
                    Kind = CeKind.HttpIn,
                    Type = EventTypes.HttpServer,
                    StatusCode = 200,
                    DurationNs = 45000000,
                },
                new()
                {
                    Id = "660e8400-e29b-41d4-a716-446655440001",
                    TraceId = "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
                    ParentId = "550e8400-e29b-41d4-a716-446655440000",
                    ServiceId = "checkout-api",
                    OccurredAt = 1733103000000000010,
                    Kind = CeKind.HttpOut,
                    Type = EventTypes.HttpClient,
                    StatusCode = 201,
                    DurationNs = 12000000,
                }
            },
        };

        var json = JsonSerializer.Serialize(batch, Options);

        // Verify V2 batch envelope fields
        json.Should().Contain("\"specversion\"");
        json.Should().Contain("\"resource\"");
        json.Should().Contain("\"agent\"");
        json.Should().Contain("\"flushed_at\"");
        json.Should().Contain("\"capture_mode\"");
        json.Should().Contain("\"events\"");

        // V1 batch fields must NOT appear at top level
        json.Should().NotContain("\"schema_version\"");

        // Round-trip
        var deserialized = JsonSerializer.Deserialize<IngestBatch>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.Specversion.Should().Be("2");
        deserialized.Resource.WorkspaceId.Should().Be("ws_01ABCDEF");
        deserialized.Resource.ServiceId.Should().Be("checkout-api");
        deserialized.Resource.Environment.Should().Be("production");
        deserialized.Resource.DeployId.Should().Be("deploy-123");
        deserialized.Resource.GitSha.Should().Be("abc1234");
        deserialized.Agent.SdkVersion.Should().Be("1.0.0");
        deserialized.Agent.SdkLanguage.Should().Be("dotnet");
        deserialized.Agent.QueueDepth.Should().Be(5);
        deserialized.FlushedAt.Should().Be(1733103000000000000);
        deserialized.CaptureMode.Should().Be(CaptureModes.Skeleton);
        deserialized.Events.Should().HaveCount(2);
        deserialized.Events[0].Kind.Should().Be(CeKind.HttpIn);
        deserialized.Events[1].Kind.Should().Be(CeKind.HttpOut);
        deserialized.Events[1].ParentId.Should().Be("550e8400-e29b-41d4-a716-446655440000");
    }

    [Fact]
    public void NullFields_AreOmittedFromJson()
    {
        var ce = new CausalEvent
        {
            Id = "test-id",
            TraceId = "test-trace",
            ServiceId = "test-svc",
            OccurredAt = 100,
            Kind = CeKind.Internal,
            StatusCode = 200,
            DurationNs = 1000,
        };

        var json = JsonSerializer.Serialize(ce, Options);

        json.Should().NotContain("\"parent_id\"");
        json.Should().NotContain("\"span_id\"");
        json.Should().NotContain("\"type\"");
        json.Should().NotContain("\"severity\"");
        json.Should().NotContain("\"attributes\"");
        json.Should().NotContain("\"detail\"");
        json.Should().NotContain("\"captured_before_alert\"");
        json.Should().NotContain("\"ring_buffer_seq\"");
    }

    [Fact]
    public void CeDetail_RoundTrip_AllNestedObjects()
    {
        var detail = new CeDetail
        {
            Method = "POST",
            RouteKey = "POST /orders/:id",
            RouteTemplate = "/orders/:id",
            RequestBytes = 1024,
            ResponseBytes = 2048,
            RequestHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Authorization"] = "Bearer ***"
            },
            ResponseHeaders = new Dictionary<string, string>
            {
                ["X-Request-Id"] = "req-123"
            },
            Retry = new RetryDetail
            {
                ExplicitObserved = true,
                KeyQuality = "explicit",
                EdgeKey = "checkout-api->payment-svc",
                OperationKey = "charge"
            },
            Downstream = new DownstreamDetail
            {
                EdgeKey = "checkout-api->payment-svc",
                Service = "payment-svc",
                OperationName = "charge",
                KeyQuality = "explicit"
            },
            LocalErrorClassification = "none",
            PayloadSnippet = "{\"order_id\":123}"
        };

        var ce = new CausalEvent
        {
            Id = "test-id",
            TraceId = "test-trace",
            ServiceId = "test-svc",
            OccurredAt = 100,
            Kind = CeKind.HttpOut,
            StatusCode = 200,
            DurationNs = 1000,
            Detail = detail
        };

        var json = JsonSerializer.Serialize(ce, Options);

        // Verify nested field names
        json.Should().Contain("\"method\"");
        json.Should().Contain("\"route_key\"");
        json.Should().Contain("\"route_template\"");
        json.Should().Contain("\"request_bytes\"");
        json.Should().Contain("\"response_bytes\"");
        json.Should().Contain("\"request_headers\"");
        json.Should().Contain("\"response_headers\"");
        json.Should().Contain("\"retry\"");
        json.Should().Contain("\"downstream\"");
        json.Should().Contain("\"local_error_classification\"");
        json.Should().Contain("\"payload_snippet\"");
        json.Should().Contain("\"explicit_observed\"");
        json.Should().Contain("\"key_quality\"");
        json.Should().Contain("\"edge_key\"");
        json.Should().Contain("\"operation_key\"");
        json.Should().Contain("\"operation_name\"");

        // Round-trip
        var deserialized = JsonSerializer.Deserialize<CausalEvent>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.Detail.Should().NotBeNull();
        deserialized.Detail!.Method.Should().Be("POST");
        deserialized.Detail.RouteKey.Should().Be("POST /orders/:id");
        deserialized.Detail.RouteTemplate.Should().Be("/orders/:id");
        deserialized.Detail.RequestBytes.Should().Be(1024);
        deserialized.Detail.ResponseBytes.Should().Be(2048);
        deserialized.Detail.RequestHeaders.Should().ContainKey("Content-Type");
        deserialized.Detail.ResponseHeaders.Should().ContainKey("X-Request-Id");
        deserialized.Detail.Retry.Should().NotBeNull();
        deserialized.Detail.Retry!.ExplicitObserved.Should().Be(true);
        deserialized.Detail.Retry.KeyQuality.Should().Be("explicit");
        deserialized.Detail.Retry.EdgeKey.Should().Be("checkout-api->payment-svc");
        deserialized.Detail.Retry.OperationKey.Should().Be("charge");
        deserialized.Detail.Downstream.Should().NotBeNull();
        deserialized.Detail.Downstream!.Service.Should().Be("payment-svc");
        deserialized.Detail.Downstream.OperationName.Should().Be("charge");
        deserialized.Detail.LocalErrorClassification.Should().Be("none");
        deserialized.Detail.PayloadSnippet.Should().Be("{\"order_id\":123}");
    }

    [Fact]
    public void SpecExampleMatch_SkeletonBatch()
    {
        var batch = new IngestBatch
        {
            Specversion = "2",
            Resource = new IngestResource
            {
                WorkspaceId = "ws_01ABCDEF",
                ServiceId = "checkout-api",
                Environment = "production",
            },
            Agent = new IngestAgent
            {
                SdkVersion = "1.0.0",
            },
            FlushedAt = 1733103000000000000,
            CaptureMode = CaptureModes.Skeleton,
            Events = new List<CausalEvent>
            {
                new()
                {
                    Id = "550e8400-e29b-41d4-a716-446655440000",
                    TraceId = "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
                    ServiceId = "checkout-api",
                    OccurredAt = 1733103000000000001,
                    Kind = CeKind.HttpIn,
                    Type = EventTypes.HttpServer,
                    StatusCode = 200,
                    DurationNs = 45000000,
                    Attributes = new Dictionary<string, object>
                    {
                        ["route_template"] = "/orders/:id/checkout"
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(batch, Options);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Batch envelope
        root.GetProperty("specversion").GetString().Should().Be("2");
        var resource = root.GetProperty("resource");
        resource.GetProperty("workspace_id").GetString().Should().Be("ws_01ABCDEF");
        resource.GetProperty("service_id").GetString().Should().Be("checkout-api");
        resource.GetProperty("environment").GetString().Should().Be("production");
        // Null fields must be absent (WhenWritingNull)
        resource.TryGetProperty("deploy_id", out _).Should().BeFalse();
        resource.TryGetProperty("git_sha", out _).Should().BeFalse();
        root.GetProperty("flushed_at").GetInt64().Should().Be(1733103000000000000);
        root.GetProperty("capture_mode").GetString().Should().Be("SKELETON");

        // Events array
        var events = root.GetProperty("events");
        events.GetArrayLength().Should().Be(1);

        var ev = events[0];
        ev.GetProperty("id").GetString().Should().Be("550e8400-e29b-41d4-a716-446655440000");
        ev.GetProperty("trace_id").GetString().Should().Be("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        ev.GetProperty("service_id").GetString().Should().Be("checkout-api");
        ev.GetProperty("occurred_at").GetInt64().Should().Be(1733103000000000001);
        ev.GetProperty("kind").GetString().Should().Be("HTTP_SERVER");
        ev.GetProperty("type").GetString().Should().Be("http_server");
        ev.GetProperty("status_code").GetInt32().Should().Be(200);
        ev.GetProperty("duration_ns").GetInt64().Should().Be(45000000);

        // parent_id is null so must be absent (WhenWritingNull)
        ev.TryGetProperty("parent_id", out _).Should().BeFalse();

        // attributes
        var attrs = ev.GetProperty("attributes");
        attrs.GetProperty("route_template").GetString().Should().Be("/orders/:id/checkout");
    }

    [Fact]
    public void IngestResponse_RoundTrip()
    {
        var response = new IngestResponse
        {
            Accepted = 10,
            Dropped = 2,
            DropReasons = new Dictionary<string, int>
            {
                ["invalid_trace_id"] = 1,
                ["quota_exceeded"] = 1
            }
        };

        var json = JsonSerializer.Serialize(response, Options);

        json.Should().Contain("\"accepted\"");
        json.Should().Contain("\"dropped\"");
        json.Should().Contain("\"drop_reasons\"");

        var deserialized = JsonSerializer.Deserialize<IngestResponse>(json, Options);
        deserialized.Should().NotBeNull();
        deserialized!.Accepted.Should().Be(10);
        deserialized.Dropped.Should().Be(2);
        deserialized.DropReasons.Should().ContainKey("invalid_trace_id").WhoseValue.Should().Be(1);
        deserialized.DropReasons.Should().ContainKey("quota_exceeded").WhoseValue.Should().Be(1);
    }

    [Fact]
    public void IngestResponse_NullDropReasons_Omitted()
    {
        var response = new IngestResponse
        {
            Accepted = 5,
            Dropped = 0
        };

        var json = JsonSerializer.Serialize(response, Options);
        json.Should().NotContain("\"drop_reasons\"");
    }

    [Fact]
    public void IngestAgent_DefaultLanguageIsDotnet()
    {
        var agent = new IngestAgent
        {
            SdkVersion = "1.0.0"
        };

        var json = JsonSerializer.Serialize(agent, Options);
        json.Should().Contain("\"dotnet\"");

        var deserialized = JsonSerializer.Deserialize<IngestAgent>(json, Options);
        deserialized.Should().NotBeNull();
        deserialized!.SdkLanguage.Should().Be("dotnet");
    }

    // ── Unknown property coverage ────────────────────────────────────────────
    // The source-generated JSON context has a `default: reader.Skip()` branch
    // in each type deserializer for unknown JSON properties. These tests ensure
    // that branch is executed for every registered type.

    [Fact]
    public void CausalEvent_Deserialize_UnknownProperty_IsIgnored()
    {
        const string json = """
            {
                "id": "abc",
                "trace_id": "trace-1",
                "service_id": "svc",
                "occurred_at": 100,
                "kind": "HTTP_SERVER",
                "status_code": 200,
                "duration_ns": 1000,
                "__unknown_future_field__": "should be skipped gracefully"
            }
            """;

        var result = JsonSerializer.Deserialize<CausalEvent>(json, Options);

        result.Should().NotBeNull();
        result!.Id.Should().Be("abc");
    }

    [Fact]
    public void IngestBatch_Deserialize_UnknownProperty_IsIgnored()
    {
        const string json = """
            {
                "specversion": "2",
                "resource": {
                    "workspace_id": "ws-1",
                    "service_id": "svc",
                    "environment": "test"
                },
                "agent": {
                    "sdk_version": "1.0.0"
                },
                "flushed_at": 100,
                "capture_mode": "SKELETON",
                "events": [],
                "__unknown__": "ignored"
            }
            """;

        var result = JsonSerializer.Deserialize<IngestBatch>(json, Options);

        result.Should().NotBeNull();
        result!.Resource.WorkspaceId.Should().Be("ws-1");
    }

    [Fact]
    public void IngestResponse_Deserialize_UnknownProperty_IsIgnored()
    {
        const string json = """{"accepted": 5, "dropped": 1, "__unknown__": true}""";

        var result = JsonSerializer.Deserialize<IngestResponse>(json, Options);

        result.Should().NotBeNull();
        result!.Accepted.Should().Be(5);
        result.Dropped.Should().Be(1);
    }

    [Fact]
    public void IngestAgent_Deserialize_UnknownProperty_IsIgnored()
    {
        const string json = """
            {
                "sdk_version": "1.0.0",
                "sdk_language": "dotnet",
                "queue_depth": 10,
                "dropped_ce_count": 2,
                "flush_latency_ms": 50,
                "__future__": null
            }
            """;

        var result = JsonSerializer.Deserialize<IngestAgent>(json, Options);

        result.Should().NotBeNull();
        result!.QueueDepth.Should().Be(10);
        result.DroppedCeCount.Should().Be(2);
    }

    [Fact]
    public void IngestResource_Deserialize_UnknownProperty_IsIgnored()
    {
        const string json = """
            {
                "workspace_id": "ws-1",
                "service_id": "svc",
                "environment": "prod",
                "__future__": "ignored"
            }
            """;

        var result = JsonSerializer.Deserialize<IngestResource>(json, Options);

        result.Should().NotBeNull();
        result!.WorkspaceId.Should().Be("ws-1");
    }

    [Fact]
    public void CeDetail_Deserialize_UnknownProperty_IsIgnored()
    {
        var detail = new CeDetail
        {
            Method = "GET",
            RouteTemplate = "/users/:id"
        };
        var ce = new CausalEvent
        {
            Id = "x",
            TraceId = "t",
            ServiceId = "svc",
            OccurredAt = 100,
            Kind = CeKind.HttpIn,
            StatusCode = 200,
            DurationNs = 1000,
            Detail = detail
        };

        // Round-trip through JSON then inject an unknown field manually
        var json = JsonSerializer.Serialize(ce, Options);
        // Inject an unknown field into the detail object
        var withUnknown = json.Replace("\"method\"", "\"__unknown__\":\"x\",\"method\"");

        var result = JsonSerializer.Deserialize<CausalEvent>(withUnknown, Options);

        result.Should().NotBeNull();
        result!.Detail.Should().NotBeNull();
        result.Detail!.Method.Should().Be("GET");
    }

    [Fact]
    public void RetryDetail_Deserialize_UnknownProperty_IsIgnored()
    {
        const string json = """
            {
                "explicit_observed": true,
                "key_quality": "explicit",
                "edge_key": "svc-a->svc-b",
                "__unknown__": 42
            }
            """;

        var result = JsonSerializer.Deserialize<RetryDetail>(json, Options);

        result.Should().NotBeNull();
        result!.ExplicitObserved.Should().BeTrue();
        result.KeyQuality.Should().Be("explicit");
    }

    [Fact]
    public void DownstreamDetail_Deserialize_UnknownProperty_IsIgnored()
    {
        const string json = """
            {
                "edge_key": "svc->target",
                "service": "target",
                "operation_name": "do_thing",
                "key_quality": "inferred",
                "__unknown__": {}
            }
            """;

        var result = JsonSerializer.Deserialize<DownstreamDetail>(json, Options);

        result.Should().NotBeNull();
        result!.Service.Should().Be("target");
        result.OperationName.Should().Be("do_thing");
    }

    // ── Partial-null object serialization ────────────────────────────────────
    // Source-generated serializers have per-property null-check branches.
    // These tests exercise both null and non-null paths for optional fields.

    [Fact]
    public void CeDetail_WithNullOptionalFields_SerializesWithoutThem()
    {
        // Only required-ish fields set; everything else null
        var detail = new CeDetail
        {
            Method = "DELETE"
        };
        var ce = new CausalEvent
        {
            Id = "x",
            TraceId = "t",
            ServiceId = "svc",
            OccurredAt = 100,
            Kind = CeKind.HttpIn,
            StatusCode = 204,
            DurationNs = 500,
            Detail = detail
        };

        var json = JsonSerializer.Serialize(ce, Options);
        var doc = JsonDocument.Parse(json);

        var detailElem = doc.RootElement.GetProperty("detail");
        detailElem.GetProperty("method").GetString().Should().Be("DELETE");
        detailElem.TryGetProperty("route_key", out _).Should().BeFalse();
        detailElem.TryGetProperty("retry", out _).Should().BeFalse();
        detailElem.TryGetProperty("downstream", out _).Should().BeFalse();
        detailElem.TryGetProperty("payload_snippet", out _).Should().BeFalse();
    }

    [Fact]
    public void IngestBatch_WithNullOptionalResourceFields_SerializesWithoutThem()
    {
        var batch = new IngestBatch
        {
            Specversion = "2",
            Resource = new IngestResource
            {
                WorkspaceId = "ws-1",
                ServiceId = "svc",
                Environment = "prod",
            },
            Agent = new IngestAgent
            {
                SdkVersion = "1.0.0",
            },
            FlushedAt = 100,
            CaptureMode = CaptureModes.Skeleton,
            Events = []
        };

        var json = JsonSerializer.Serialize(batch, Options);
        var doc = JsonDocument.Parse(json);
        var resource = doc.RootElement.GetProperty("resource");

        resource.TryGetProperty("deploy_id", out _).Should().BeFalse();
        resource.TryGetProperty("git_sha", out _).Should().BeFalse();
    }

    [Fact]
    public void CeKind_UnknownWireValue_ThrowsJsonException()
    {
        var act = () => JsonSerializer.Deserialize<CeKind>("\"UNKNOWN_KIND\"", Options);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void CeKind_InvalidEnumValue_ThrowsJsonException()
    {
        // Cast an out-of-range integer to CeKind -> Write switch falls to default
        var invalidKind = (CeKind)999;
        var act = () => JsonSerializer.Serialize(invalidKind, Options);
        act.Should().Throw<JsonException>();
    }
}
