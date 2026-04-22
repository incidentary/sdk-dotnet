using System.Globalization;
using System.Security.Cryptography;

namespace Incidentary.Sdk;

/// <summary>
/// Identifier generators for the Incidentary .NET SDK.
/// </summary>
/// <remarks>
/// Exposes two methods with a deliberate split:
/// <list type="bullet">
///   <item><see cref="NewId"/> — UUIDv7 for DB-backed identifiers
///   (trace IDs, CE IDs, anywhere sort-key locality matters). The
///   48-bit millisecond prefix improves B-tree locality on hot ingest
///   paths.</item>
///   <item><see cref="NewRandomToken"/> — UUIDv4 for externally visible,
///   privacy-sensitive tokens where the timestamp embedded in v7 would
///   leak creation time across a trust boundary.</item>
/// </list>
/// Both share the 128-bit UUID layout, so either form slots into a
/// <c>uuid</c> column transparently.
///
/// On .NET 9+ the v7 path delegates to the native
/// <c>Guid.CreateVersion7()</c>. On .NET 8 it packs the layout manually
/// using <see cref="RandomNumberGenerator"/>. The v4 path always uses
/// <see cref="Guid.NewGuid"/>, which has been CSPRNG-backed on every
/// supported runtime.
/// </remarks>
public static class IncidentaryIds
{
    private static readonly char[] HexLower = "0123456789abcdef".ToCharArray();

    /// <summary>
    /// Return a canonical UUIDv7 string in the form
    /// <c>xxxxxxxx-xxxx-7xxx-Nxxx-xxxxxxxxxxxx</c> where N is 8/9/a/b.
    /// </summary>
    public static string NewId()
    {
#if NET9_0_OR_GREATER
        return Guid.CreateVersion7().ToString("D", CultureInfo.InvariantCulture);
#else
        Span<byte> buf = stackalloc byte[16];
        RandomNumberGenerator.Fill(buf);

        var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // 48 bits of timestamp, big-endian, into buf[0..5].
        buf[0] = (byte)(ms >> 40);
        buf[1] = (byte)(ms >> 32);
        buf[2] = (byte)(ms >> 24);
        buf[3] = (byte)(ms >> 16);
        buf[4] = (byte)(ms >> 8);
        buf[5] = (byte)ms;

        // Version = 7 in the top 4 bits of byte 6.
        buf[6] = (byte)((buf[6] & 0x0F) | 0x70);

        // Variant = 10 in the top 2 bits of byte 8.
        buf[8] = (byte)((buf[8] & 0x3F) | 0x80);

        // Hand-roll the 36-char canonical form to avoid an allocation.
        Span<char> chars = stackalloc char[36];
        var ci = 0;
        for (var bi = 0; bi < 16; bi++)
        {
            if (bi == 4 || bi == 6 || bi == 8 || bi == 10)
            {
                chars[ci++] = '-';
            }
            var b = buf[bi];
            chars[ci++] = HexLower[b >> 4];
            chars[ci++] = HexLower[b & 0x0F];
        }
        return new string(chars);
#endif
    }

    /// <summary>
    /// Return a canonical UUIDv4 string — 122 bits of CSPRNG output
    /// with no embedded timestamp.
    /// </summary>
    /// <remarks>
    /// Use this for externally visible tokens (deploy dedup keys
    /// attached to visible URLs, share-URL slugs, CSRF nonces) where
    /// the 48-bit millisecond prefix that <see cref="NewId"/> carries
    /// would leak the token's creation time across a trust boundary.
    /// </remarks>
    public static string NewRandomToken()
    {
        return Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
    }
}
