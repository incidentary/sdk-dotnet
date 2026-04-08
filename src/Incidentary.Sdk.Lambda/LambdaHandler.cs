using System.Diagnostics;
using System.Text.Json;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.WireFormat;

namespace Incidentary.Sdk.Lambda;

/// <summary>AWS Lambda handler wrapper with automatic flush-before-freeze semantics.</summary>
public static class LambdaHandler
{
    /// <summary>
    /// Wraps a Lambda handler function with Incidentary instrumentation.
    /// ALWAYS flushes events before returning (critical for Lambda execution freeze).
    /// </summary>
    public static Func<TInput, Amazon.Lambda.Core.ILambdaContext, Task<TOutput>> Wrap<TInput, TOutput>(
        IIncidentaryClient client,
        Func<TInput, Amazon.Lambda.Core.ILambdaContext, Task<TOutput>> handler)
    {
        return async (input, lambdaContext) =>
        {
            var traceId = Guid.NewGuid().ToString();
            var ceId = Guid.NewGuid().ToString();
            using var scope = IncidentaryActivity.SetContext(new TraceContext(traceId, ceId));

            client.RecordRequestStart(CeKind.Internal);
            var sw = Stopwatch.StartNew();
            int status = 200;

            try
            {
                var result = await handler(input, lambdaContext).ConfigureAwait(false);
                return result;
            }
            catch
            {
                status = 500;
                throw;
            }
            finally
            {
                sw.Stop();
                client.RecordEvent(EventTypes.InternalTask, new RecordEventOptions
                {
                    Kind = CeKind.Internal,
                    Status = status,
                    DurationNs = sw.Elapsed.Ticks * 100,
                    TraceId = traceId
                });

                // CRITICAL: Always flush before Lambda freeze
                await client.FlushToBackendAsync().ConfigureAwait(false);
            }
        };
    }
}
