namespace Incidentary.Sdk.PreArm;

/// <summary>
/// Combines the outputs of all individual triggers into a single verdict.
/// If any trigger is Severe, the combined result is Severe. If any is Mild, the result is Mild.
/// </summary>
internal static class TriggerArbiter
{
    /// <summary>
    /// Evaluate the combined severity from all trigger results.
    /// </summary>
    public static TriggerResult Evaluate(
        TriggerResult errorRate,
        TriggerResult slowSuccess,
        TriggerResult inFlight,
        TriggerResult retryOnset)
    {
        var inputs = new[] { errorRate, slowSuccess, inFlight, retryOnset };
        var hasSevere = false;
        var hasMild = false;
        var triggerNames = new List<string>();
        var reasons = new List<string>();

        foreach (var input in inputs)
        {
            switch (input.Severity)
            {
                case TriggerSeverity.Severe:
                    hasSevere = true;
                    if (input.TriggerType is not null)
                    {
                        triggerNames.Add(input.TriggerType);
                    }

                    if (input.Reason is not null)
                    {
                        reasons.Add(input.Reason);
                    }

                    break;

                case TriggerSeverity.Mild:
                    hasMild = true;
                    if (input.TriggerType is not null)
                    {
                        triggerNames.Add(input.TriggerType);
                    }

                    if (input.Reason is not null)
                    {
                        reasons.Add(input.Reason);
                    }

                    break;
            }
        }

        if (hasSevere)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Severe,
                TriggerType = string.Join("+", triggerNames),
                Reason = string.Join("; ", reasons),
            };
        }

        if (hasMild)
        {
            return new TriggerResult
            {
                Severity = TriggerSeverity.Mild,
                TriggerType = string.Join("+", triggerNames),
                Reason = string.Join("; ", reasons),
            };
        }

        return TriggerResult.None;
    }
}
