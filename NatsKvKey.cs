using System.Globalization;

namespace CLOOPS.microservices;

/// <summary>
/// Validation helpers for NATS JetStream Key-Value keys, including the keys used for
/// distributed locks (a distributed lock is a KV entry in the <c>locks</c> bucket).
/// </summary>
/// <remarks>
/// <para>
/// NATS restricts KV keys to <c>[-/_=.a-zA-Z0-9]</c> and additionally rejects keys that are
/// empty or that start or end with a period. Notably <c>':'</c> is <b>not</b> allowed, so the
/// Redis-style <c>service:resource</c> convention cannot be used for KV or lock keys. Use
/// <c>'.'</c> as the separator instead: <c>service.resource</c>.
/// </para>
/// <para>
/// The NATS client reports an invalid key as an opaque <c>NatsKVException</c> that does not name
/// the key. Call <see cref="Validate"/> when building a key so the failure names the key and the
/// offending character instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var lockKey = $"my-service.nightly-cleanup";
/// NatsKvKey.Validate(lockKey, nameof(lockKey));
/// await using var handle = await natsClient.AcquireDistributedLockAsync(lockKey, ct: ct);
/// </code>
/// </example>
public static class NatsKvKey
{
    /// <summary>
    /// The separator convention for NATS KV and distributed-lock keys. NATS does not allow
    /// <c>':'</c>, so segments are joined with <c>'.'</c> (for example <c>cache-refresh.patients</c>).
    /// </summary>
    public const char Separator = '.';

    /// <summary>
    /// Human-readable description of the characters NATS accepts in a KV key.
    /// </summary>
    public const string AllowedCharactersDescription =
        "letters, digits and '-', '_', '=', '/', '.'";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="key"/> is a valid NATS KV key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    public static bool IsValid(string? key) => GetValidationError(key) == null;

    /// <summary>
    /// Returns a human-readable description of why <paramref name="key"/> is not a valid NATS KV
    /// key, or <c>null</c> when the key is valid. The message names the key and, for a character
    /// violation, the offending character and its position.
    /// </summary>
    /// <param name="key">The key to check.</param>
    public static string? GetValidationError(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "NATS KV key cannot be null, empty or whitespace.";
        }

        if (key[0] == Separator || key[^1] == Separator)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"NATS KV key '{key}' is invalid: a key cannot start or end with '{Separator}'.");
        }

        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (IsAllowed(c))
            {
                continue;
            }

            var hint = c == ':'
                ? $" ':' is a Redis-style separator and is never valid in a NATS KV key; use '{Separator}' instead (for example '{key.Replace(':', Separator)}')."
                : string.Empty;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"NATS KV key '{key}' is invalid: character '{c}' at position {i} is not allowed. NATS KV keys may only contain {AllowedCharactersDescription}.{hint}");
        }

        return null;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> naming the key and the offending character when
    /// <paramref name="key"/> is not a valid NATS KV key.
    /// </summary>
    /// <param name="key">The key to validate.</param>
    /// <param name="paramName">Name of the argument being validated, used in the exception.</param>
    /// <exception cref="ArgumentException">The key is not a valid NATS KV key.</exception>
    public static void Validate(string? key, string paramName = "key")
    {
        var error = GetValidationError(key);
        if (error != null)
        {
            throw new ArgumentException(error, paramName);
        }
    }

    private static bool IsAllowed(char c) =>
        c is >= 'a' and <= 'z'
        or >= 'A' and <= 'Z'
        or >= '0' and <= '9'
        or '-' or '_' or '=' or '/' or '.';
}
