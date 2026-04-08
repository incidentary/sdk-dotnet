namespace Incidentary.Sdk.Tests.PreArm;

using FluentAssertions;
using Incidentary.Sdk.PreArm;
using Xunit;

public sealed class PreArmEngineTests
{
    private long _now = 100_000;
    private long TimeProvider() => _now;

    private readonly List<ClientCaptureMode> _modeChanges = [];

    private PreArmEngine CreateEngine(IncidentaryClientOptions? options = null)
    {
        var opts = options ?? new IncidentaryClientOptions
        {
            ApiKey = "test-key",
            ServiceName = "test-service",
        };

        return new PreArmEngine(opts, TimeProvider, mode => _modeChanges.Add(mode));
    }

    [Fact]
    public void InitialMode_IsNormal()
    {
        var engine = CreateEngine();

        engine.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    [Fact]
    public void HighErrorRate_TransitionsToPreArmed()
    {
        var engine = CreateEngine();

        // Generate high error rate: 15% 5xx
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.PreArmed);
        _modeChanges.Should().Contain(ClientCaptureMode.PreArmed);
    }

    [Fact]
    public void PreArmedToNormal_AfterTriggersClear()
    {
        var engine = CreateEngine(new IncidentaryClientOptions
        {
            ApiKey = "test-key",
            ServiceName = "test-service",
            PreArmMinDurationMs = 1_000, // Short min duration for testing
            PreArmCooldownMs = 0,        // No cooldown for this test
        });

        // Trigger pre-arm with high error rate
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.PreArmed);

        // Advance past min duration
        _now += 2_000;

        // Advance past error rate window (10s) so old buckets expire
        _now += 11_000;

        // Send only successes to clear the trigger
        for (var i = 0; i < 100; i++)
            engine.OnRequestCompleted(200, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    [Fact]
    public void PreArmedToIncident_OnEscalate()
    {
        var engine = CreateEngine();

        // Trigger pre-arm
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.PreArmed);

        engine.EscalateToIncident("incident-123");

        engine.Mode.Should().Be(ClientCaptureMode.Incident);
        _modeChanges.Should().Contain(ClientCaptureMode.Incident);
    }

    [Fact]
    public void IncidentToNormal_OnClose()
    {
        var engine = CreateEngine();

        // Trigger pre-arm then escalate
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        engine.EscalateToIncident("incident-123");
        engine.Mode.Should().Be(ClientCaptureMode.Incident);

        engine.CloseIncident();

        engine.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    [Fact]
    public void PreArmedTtl_ExpiresBackToNormal()
    {
        var engine = CreateEngine(new IncidentaryClientOptions
        {
            ApiKey = "test-key",
            ServiceName = "test-service",
            PreArmTtlMs = 5_000, // Short TTL for testing
        });

        // Trigger pre-arm
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.PreArmed);

        // Advance time past TTL
        _now += 6_000;

        // Trigger evaluation by sending a request
        engine.OnRequestCompleted(200, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    [Fact]
    public void Cooldown_PreventsReentry()
    {
        var engine = CreateEngine(new IncidentaryClientOptions
        {
            ApiKey = "test-key",
            ServiceName = "test-service",
            PreArmMinDurationMs = 1_000,
            PreArmCooldownMs = 5_000,
        });

        // Trigger pre-arm
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.PreArmed);

        // Advance past min duration + error window to let it clear
        _now += 12_000;

        // Send successes to clear triggers and exit pre-arm
        for (var i = 0; i < 100; i++)
            engine.OnRequestCompleted(200, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.Normal);

        // Now try to re-trigger immediately (within cooldown)
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        // Should stay Normal due to cooldown
        engine.Mode.Should().Be(ClientCaptureMode.Normal);

        // Advance past cooldown
        _now += 6_000;

        // Now trigger again — should work
        // Need to be in a fresh error window
        _now += 11_000; // past old error window
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.PreArmed);
    }

    [Fact]
    public void GetDebugState_ReturnsValidSnapshot()
    {
        var engine = CreateEngine();

        var state = engine.GetDebugState();

        state.Mode.Should().Be(ClientCaptureMode.Normal);
        state.Counters.Should().NotBeNull();
    }

    [Fact]
    public void EscalateFromNormal_TransitionsToIncident()
    {
        var engine = CreateEngine();

        // Escalate directly from Normal (external signal)
        engine.EscalateToIncident("incident-456");

        engine.Mode.Should().Be(ClientCaptureMode.Incident);
    }

    // ── Disabled sub-trigger branch coverage ──────────────────────────────────
    // PreArmEngine uses `_options.PreArmEnableXxx ? trigger.Evaluate() : TriggerResult.None`.
    // These tests cover the `TriggerResult.None` (disabled) branches.

    [Fact]
    public void DisabledSlowSuccess_DoesNotTriggerOnSlowRequests()
    {
        var engine = CreateEngine(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            PreArmEnableSlowSuccess = false,
        });

        // All requests are extremely slow — would trigger SlowSuccess if enabled
        for (var i = 0; i < 100; i++)
            engine.OnRequestCompleted(200, 99_999, null, false);

        // Slow success is disabled → stays Normal
        engine.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    [Fact]
    public void DisabledInFlight_DoesNotTriggerOnPileup()
    {
        var engine = CreateEngine(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            PreArmEnableInFlight = false,
        });

        // Open many concurrent requests without completing
        for (var i = 0; i < 64; i++)
            engine.OnRequestStarted();

        // InFlight trigger is disabled → stays Normal
        engine.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    [Fact]
    public void DisabledRetry_DoesNotTriggerOnRetryBurst()
    {
        var engine = CreateEngine(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            PreArmEnableRetry = false,
        });

        // All requests are retries
        for (var i = 0; i < 100; i++)
            engine.OnRequestCompleted(200, 50, null, isRetry: true);

        // Retry trigger is disabled → stays Normal
        engine.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    // ── Null onModeChanged callback coverage ─────────────────────────────────
    // EscalateToIncident/CloseIncident/EvaluateAndTransition call
    // `_onModeChanged?.Invoke(mode)`. When null, the null branch must be a no-op.

    [Fact]
    public void EscalateToIncident_NullCallback_NoThrow()
    {
        var engine = new PreArmEngine(
            new IncidentaryClientOptions { ApiKey = "key", ServiceName = "svc" },
            onModeChanged: null);

        var act = () => engine.EscalateToIncident("incident-1");

        act.Should().NotThrow();
        engine.Mode.Should().Be(ClientCaptureMode.Incident);
    }

    [Fact]
    public void CloseIncident_NullCallback_NoThrow()
    {
        var engine = new PreArmEngine(
            new IncidentaryClientOptions { ApiKey = "key", ServiceName = "svc" },
            onModeChanged: null);

        engine.EscalateToIncident("incident-1");
        var act = () => engine.CloseIncident();

        act.Should().NotThrow();
        engine.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    // ── GetDebugState in all modes ────────────────────────────────────────────
    // Line 190: `_mode == PreArmed || _mode == Incident` — covers both OR branches.

    [Fact]
    public void GetDebugState_InIncidentMode_IncludesActiveWindow()
    {
        var engine = CreateEngine();
        engine.EscalateToIncident("incident-1");

        var state = engine.GetDebugState();

        state.Mode.Should().Be(ClientCaptureMode.Incident);
        state.ActiveWindow.Should().NotBeNull();
        state.ActiveWindow!.BoundIncidentId.Should().Be("incident-1");
    }

    [Fact]
    public void GetDebugState_InPreArmedMode_IncludesActiveWindow()
    {
        var engine = CreateEngine();

        // Drive into PreArmed via high error rate
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        engine.Mode.Should().Be(ClientCaptureMode.PreArmed);

        var state = engine.GetDebugState();

        state.Mode.Should().Be(ClientCaptureMode.PreArmed);
        state.ActiveWindow.Should().NotBeNull();
    }

    // ── EvaluateAndTransition branch coverage (lines 223, 284) ──────────────

    [Fact]
    public void DisabledInFlight_ViaOnRequestCompleted_DoesNotTrigger()
    {
        // Covers line 223 false branch: `_options.PreArmEnableInFlight ? ... : TriggerResult.None`
        // Must use OnRequestCompleted (which calls EvaluateAndTransition) — NOT OnRequestStarted.
        var engine = CreateEngine(new IncidentaryClientOptions
        {
            ApiKey = "key",
            ServiceName = "svc",
            PreArmEnableInFlight = false,
        });

        // Drive through EvaluateAndTransition many times without triggering via other means
        for (var i = 0; i < 50; i++)
            engine.OnRequestCompleted(200, 50, null, false);

        // InFlight disabled → line 223 false branch taken, mode stays Normal
        engine.Mode.Should().Be(ClientCaptureMode.Normal);
    }

    [Fact]
    public void EvaluateAndTransition_NullCallback_ModeChangeSilentlySucceeds()
    {
        // Covers line 284 null branch: `_onModeChanged?.Invoke(notifyMode.Value)`
        // when _onModeChanged is null and a mode change actually occurs.
        var engine = new PreArmEngine(
            new IncidentaryClientOptions { ApiKey = "key", ServiceName = "svc" },
            onModeChanged: null);

        // Trigger high error rate → transitions to PreArmed → notifyMode.HasValue=true → null callback invoked
        for (var i = 0; i < 85; i++)
            engine.OnRequestCompleted(200, 50, null, false);
        for (var i = 0; i < 15; i++)
            engine.OnRequestCompleted(500, 50, null, false);

        // Should complete without throwing despite null callback
        engine.Mode.Should().Be(ClientCaptureMode.PreArmed);
    }
}
