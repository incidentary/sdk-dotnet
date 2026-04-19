using System.Globalization;
using System.Security.Cryptography;

namespace Incidentary.Sdk;

/// <summary>
/// Canonical UUIDv7 id generator for the Incidentary SDK.
/// </summary>
/// <remarks>
/// UUIDv7 (RFC 9562 §5.7) encodes a Unix-millis timestamp in the most
/// significant 48 bits, so ids generated minutes apart sort
/// lexicographically in the order they were created. That property
/// materially improves B-tree locality on hot ingest paths.
/// Binary-compatible with v4 on the wire, so anywhere the SDK
/// previously emitted <see cref="Guid.NewGuid"/> can switch to this
/// transparently.
///
/// On .NET 9+ this delegates to the native <c>Guid.CreateVersion7()</c>.
/// On .NET 8 it packs the layout manually using
/// <see cref="RandomNumberGenerator"/>.
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
}
