using TigerBeetle;

namespace CLOOPS.microservices.Extensions;

public static partial class TigerBeetleClientExtensions
{
    /// <summary>
    /// Derives a deterministic TigerBeetle account ID from a customer UUID.
    /// The full 128 bits of the UUID are used.
    /// </summary>
    /// <param name="client">The TigerBeetle client.</param>
    /// <param name="id">The customer UUID.</param>
    /// <returns>A deterministic TigerBeetle account ID.</returns>
    public static UInt128 GetAccountId(this Client client, Guid id)
    {
        ArgumentNullException.ThrowIfNull(client);

        return GetAccountId(id);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle account ID from any non-empty string ID.
    /// UUID strings are parsed and mapped the same way as <see cref="GetAccountId(Client, Guid)"/>.
    /// Non-UUID strings must fit in 15 UTF-8 bytes and are packed directly, so this method does not
    /// hash strings and has no hash-collision risk.
    /// </summary>
    /// <param name="client">The TigerBeetle client.</param>
    /// <param name="id">The source ID.</param>
    /// <returns>A deterministic TigerBeetle account ID.</returns>
    public static UInt128 GetAccountId(this Client client, string id)
    {
        ArgumentNullException.ThrowIfNull(client);

        return GetAccountId(id);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle account ID from a customer UUID and account subtype.
    /// This overload replaces the UUID's last 16 bits with the subtype, so it is not a lossless UUID mapping.
    /// For sequential UUIDs, prefer <see cref="Guid.CreateVersion7()"/> for a modern sequential UUID with
    /// extremely unlikely collisions in this layout. For the strongest collision profile with this layout,
    /// prefer <see cref="Guid.NewGuid()"/> / UUIDv4 because its randomness is spread across the UUID.
    /// </summary>
    /// <param name="client">The TigerBeetle client.</param>
    /// <param name="id">The customer UUID.</param>
    /// <param name="subType">
    /// The account subtype to store in the low 16 bits. Must be non-negative;
    /// valid values are 0 through 65,535.
    /// </param>
    /// <returns>A deterministic TigerBeetle account ID.</returns>
    public static UInt128 GetAccountId(this Client client, Guid id, int subType)
    {
        ArgumentNullException.ThrowIfNull(client);

        return GetAccountId(id, subType);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle account ID from any non-empty string ID and account subtype.
    /// UUID strings are parsed and mapped the same way as <see cref="GetAccountId(Client, Guid, int)"/>;
    /// all other strings must fit in 13 UTF-8 bytes and are packed directly before the subtype suffix is applied.
    /// Prefer this overload for account IDs when the source ID can fit in 13 UTF-8 bytes, because non-UUID
    /// strings are packed without hashing or truncating identifier bytes.
    /// </summary>
    /// <param name="client">The TigerBeetle client.</param>
    /// <param name="id">The source ID.</param>
    /// <param name="subType">
    /// The account subtype to store in the low 16 bits. Must be non-negative;
    /// valid values are 0 through 65,535.
    /// </param>
    /// <returns>A deterministic TigerBeetle account ID.</returns>
    public static UInt128 GetAccountId(this Client client, string id, int subType)
    {
        ArgumentNullException.ThrowIfNull(client);

        return GetAccountId(id, subType);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle account ID from a customer UUID.
    /// The full 128 bits of the UUID are used.
    /// </summary>
    /// <param name="id">The customer UUID.</param>
    /// <returns>A deterministic TigerBeetle account ID.</returns>
    public static UInt128 GetAccountId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Account IDs cannot be derived from an empty UUID.", nameof(id));
        }

        Span<byte> guidBytes = stackalloc byte[TigerBeetleIdByteCount];
        id.TryWriteBytes(guidBytes, bigEndian: true, out _);

        return BuildAccountId(guidBytes);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle account ID from any non-empty string ID.
    /// UUID strings are parsed and mapped the same way as <see cref="GetAccountId(Guid)"/>.
    /// Non-UUID strings must fit in 15 UTF-8 bytes and are packed directly, so this method does not
    /// hash strings and has no hash-collision risk.
    /// </summary>
    /// <param name="id">The source ID.</param>
    /// <returns>A deterministic TigerBeetle account ID.</returns>
    public static UInt128 GetAccountId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Account IDs cannot be derived from an empty string.", nameof(id));
        }

        if (Guid.TryParse(id, out var uuid))
        {
            return GetAccountId(uuid);
        }

        Span<byte> accountIdBytes = stackalloc byte[TigerBeetleIdByteCount];
        PackStringId(id, accountIdBytes, StringIdMaxByteCount, nameof(id));

        return BuildAccountId(accountIdBytes);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle account ID from a customer UUID and account subtype.
    /// This overload replaces the UUID's last 16 bits with the subtype, so it is not a lossless UUID mapping.
    /// For sequential UUIDs, prefer <see cref="Guid.CreateVersion7()"/> for a modern sequential UUID with
    /// extremely unlikely collisions in this layout. For the strongest collision profile with this layout,
    /// prefer <see cref="Guid.NewGuid()"/> / UUIDv4 because its randomness is spread across the UUID.
    /// </summary>
    /// <param name="id">The customer UUID.</param>
    /// <param name="subType">
    /// The account subtype to store in the low 16 bits. Must be non-negative;
    /// valid values are 0 through 65,535.
    /// </param>
    /// <returns>A deterministic TigerBeetle account ID.</returns>
    public static UInt128 GetAccountId(Guid id, int subType)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Account IDs cannot be derived from an empty UUID.", nameof(id));
        }

        Span<byte> accountIdBytes = stackalloc byte[TigerBeetleIdByteCount];
        id.TryWriteBytes(accountIdBytes, bigEndian: true, out _);
        // Overwrite the trailing 2 bytes of the UUID with the subtype, in place.
        return ApplyAccountSubType(accountIdBytes, subType);
    }

    /// <summary>
    /// Derives a deterministic TigerBeetle account ID from any non-empty string ID and account subtype.
    /// UUID strings are parsed and mapped the same way as <see cref="GetAccountId(Guid, int)"/>;
    /// all other strings must fit in 13 UTF-8 bytes and are packed directly before the subtype suffix is applied.
    /// Prefer this overload for account IDs when the source ID can fit in 13 UTF-8 bytes, because non-UUID
    /// strings are packed without hashing or truncating identifier bytes.
    /// </summary>
    /// <param name="id">The source ID.</param>
    /// <param name="subType">
    /// The account subtype to store in the low 16 bits. Must be non-negative;
    /// valid values are 0 through 65,535.
    /// </param>
    /// <returns>A deterministic TigerBeetle account ID.</returns>
    public static UInt128 GetAccountId(string id, int subType)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Account IDs cannot be derived from an empty string.", nameof(id));
        }

        if (Guid.TryParse(id, out var uuid))
        {
            return GetAccountId(uuid, subType);
        }

        // Pack the string into the first 14 bytes (with its length-suffix at byte 13),
        // then overwrite the trailing 2 bytes (positions 14-15) with the subtype.
        Span<byte> accountIdBytes = stackalloc byte[TigerBeetleIdByteCount];
        PackStringId(id, accountIdBytes[..AccountIdPrefixByteCount], AccountStringIdWithSubTypeMaxByteCount, nameof(id));
        return ApplyAccountSubType(accountIdBytes, subType);
    }
}
