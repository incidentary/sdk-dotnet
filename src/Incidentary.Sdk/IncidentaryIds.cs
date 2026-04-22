using System.Globalization;

namespace Incidentary.Sdk;

/// <summary>
/// Canonical UUIDv4 id generator for the Incidentary .NET SDK.
/// </summary>
/// <remarks>
/// UUIDv4 (RFC 9562 §5.4) is 122 bits of CSPRNG random with no
/// embedded timestamp. The server accepts v1/v4/v7 transparently —
/// the binary representation is identical across versions — but all
/// first-party SDKs emit v4.
///
/// Earlier drafts of this helper emitted UUIDv7 on the grounds that
/// the 48-bit millisecond prefix would improve server-side storage
/// locality. That reasoning was wrong for the Incidentary server
/// schema:
/// <list type="bullet">
///   <item>ClickHouse compares UUIDs second-half-first for historical
///   reasons, so v7's timestamp prefix contributes nothing to
///   sparse-index ordering or pruning.</item>
///   <item>Every UUID-bearing ClickHouse table already carries time
///   locality in an explicit <c>i64</c> nanosecond column
///   (<c>wall_ts_ns</c> / <c>occurred_at</c>) that sits before the
///   UUID in the sort key.</item>
/// </list>
/// With the storage-locality case empty, the remaining consideration
/// is v7's 48-bit timestamp prefix — a recoverable creation-time
/// side channel for any value that might cross a trust boundary. v4
/// has no such leak.
///
/// Randomness comes from <see cref="Guid.NewGuid"/>, which on every
/// supported runtime is implemented via the OS CSPRNG.
/// </remarks>
public static class IncidentaryIds
{
    /// <summary>
    /// Return a canonical UUIDv4 string in the form
    /// <c>xxxxxxxx-xxxx-4xxx-Nxxx-xxxxxxxxxxxx</c> where N is 8/9/a/b.
    /// </summary>
    public static string NewId()
    {
        return Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
    }
}
