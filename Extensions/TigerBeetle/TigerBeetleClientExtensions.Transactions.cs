using TigerBeetle;

namespace CLOOPS.microservices.Extensions;

public static partial class TigerBeetleClientExtensions
{
    /// <summary>
    /// Derives a deterministic TigerBeetle transaction ID from a UUID.
    /// The full 128 bits of the UUID are used.
    /// </summary>
    /// <param name="client">The TigerBeetle client.</param>
    /// <param name="id">The transaction UUID.</param>
    /// <returns>A deterministic TigerBeetle transaction ID.</returns>
    public static UInt128 GetTransactionId(this Client client, Guid id)
    {
        ArgumentNullException.ThrowIfNull(client);

        return GetTransactionId(id);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle transaction ID from any non-empty string ID.
    /// UUID strings are parsed and mapped the same way as <see cref="GetTransactionId(Client, Guid)"/>.
    /// Non-UUID strings must fit in 15 UTF-8 bytes and are packed directly, so this method does not
    /// hash strings and has no hash-collision risk.
    /// </summary>
    /// <param name="client">The TigerBeetle client.</param>
    /// <param name="id">The source transaction ID.</param>
    /// <returns>A deterministic TigerBeetle transaction ID.</returns>
    public static UInt128 GetTransactionId(this Client client, string id)
    {
        ArgumentNullException.ThrowIfNull(client);

        return GetTransactionId(id);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle transaction ID from a UUID.
    /// The full 128 bits of the UUID are used.
    /// </summary>
    /// <param name="id">The transaction UUID.</param>
    /// <returns>A deterministic TigerBeetle transaction ID.</returns>
    public static UInt128 GetTransactionId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Transaction IDs cannot be derived from an empty UUID.", nameof(id));
        }

        Span<byte> idBytes = stackalloc byte[TigerBeetleIdByteCount];
        id.TryWriteBytes(idBytes, bigEndian: true, out _);

        return BuildTransactionId(idBytes);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle transaction ID from any non-empty string ID.
    /// UUID strings are parsed and mapped the same way as <see cref="GetTransactionId(Guid)"/>.
    /// Non-UUID strings must fit in 15 UTF-8 bytes and are packed directly, so this method does not
    /// hash strings and has no hash-collision risk.
    /// </summary>
    /// <param name="id">The source transaction ID.</param>
    /// <returns>A deterministic TigerBeetle transaction ID.</returns>
    public static UInt128 GetTransactionId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Transaction IDs cannot be derived from an empty string.", nameof(id));
        }

        if (Guid.TryParse(id, out var uuid))
        {
            return GetTransactionId(uuid);
        }

        Span<byte> transactionIdBytes = stackalloc byte[TigerBeetleIdByteCount];
        PackStringId(id, transactionIdBytes, StringIdMaxByteCount, nameof(id));

        return BuildTransactionId(transactionIdBytes);
    }
}
