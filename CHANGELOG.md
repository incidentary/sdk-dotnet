# Changelog

## 0.2.0

Initial release of the Incidentary .NET SDK.

### Added

- **Wire format v1** — Full compliance with the frozen Incidentary wire format
- **Pre-arm anomaly detection** — 4 local triggers: error rate (5xx), slow success (EWMA), in-flight pileup, retry onset
- **Ring buffer** — 4,000-event circular buffer with FIFO overwrite
- **Retry queue** — Exponential backoff (1s/4s/16s) with circuit breaker (3 failures, 60s cooldown)
- **ASP.NET Core middleware** — Automatic HTTP_IN recording with trace context propagation
- **HttpClient integration** — DelegatingHandler for HTTP_OUT recording via IHttpClientFactory
- **gRPC interceptors** — Client and server interceptors for grpc_in/grpc_out events
- **MassTransit filters** — Publish and consume filters for queue_publish/queue_consume events
- **EF Core interceptor** — DbCommandInterceptor for db_query events
- **AWS Lambda wrapper** — Handler wrapping with flush-before-freeze semantics
- **Trace context** — AsyncLocal-based propagation across async boundaries
- **Payload redaction** — Sensitive field redaction (passwords, tokens, credit cards)
- **Event attrs sanitization** — Wire format constraint enforcement (32 keys, primitives only)
- **Downstream edge key resolution** — 5-level quality hierarchy for retry detection
- **Multi-TFM support** — .NET 8, .NET 9, .NET 10
- **Enterprise-ready** — Strong naming, SourceLink, deterministic builds, AOT-compatible serialization
