using System.Buffers.Binary;
using System.Text;

namespace CLOOPS.microservices.Extensions;

/// <summary>
/// Contains helper methods for deriving deterministic TigerBeetle account and transaction IDs.
/// </summary>
public static partial class TigerBeetleClientExtensions
{
    private const int TigerBeetleIdByteCount = 16;
    private const int AccountIdPrefixByteCount = 14;
    private const int AccountSubTypeMaxValue = ushort.MaxValue;
    private const int StringIdMaxByteCount = TigerBeetleIdByteCount - 1;
    private const int AccountStringIdWithSubTypeMaxByteCount = AccountIdPrefixByteCount - 1;

    /// <summary>
    /// Encodes <paramref name="id"/> as UTF-8 into the first <paramref name="maxByteCount"/> bytes
    /// of <paramref name="destination"/> in a single pass, then writes the encoded byte length
    /// into the LAST byte of <paramref name="destination"/>.
    /// The destination is expected to be one byte longer than <paramref name="maxByteCount"/>;
    /// the trailing byte distinguishes short string IDs that share a UTF-8 prefix.
    /// </summary>
    private static void PackStringId(string id, Span<byte> destination, int maxByteCount, string paramName)
    {
        // Single-pass encode bounded by maxByteCount. TryGetBytes returns false if the destination
        // would overflow, in which case we compute the actual byte count for the error message.
        if (!Encoding.UTF8.TryGetBytes(id, destination[..maxByteCount], out var byteCount))
        {
            var actualByteCount = Encoding.UTF8.GetByteCount(id);
            throw new ArgumentException(
                $"String IDs must fit in {maxByteCount} UTF-8 bytes. '{id}' is {actualByteCount} UTF-8 bytes.",
                paramName);
        }

        destination[^1] = (byte)byteCount;
    }

    /// <summary>
    /// Writes the account subtype into the last 2 bytes of <paramref name="idBytes"/>
    /// in place, then materializes a UInt128 from the full 16-byte buffer.
    /// </summary>
    private static UInt128 ApplyAccountSubType(Span<byte> idBytes, int subType)
    {
        if (subType < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subType), subType, "Account subtype must be non-negative.");
        }

        if (subType > AccountSubTypeMaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(subType), subType, "Account subtype must be 65,535 or less.");
        }

        BinaryPrimitives.WriteUInt16BigEndian(idBytes[AccountIdPrefixByteCount..], (ushort)subType);
        return BuildAccountId(idBytes);
    }

    private static UInt128 BuildAccountId(ReadOnlySpan<byte> idBytes)
    {
        var accountId = BinaryPrimitives.ReadUInt128BigEndian(idBytes);
        if (accountId == UInt128.Zero || accountId == UInt128.MaxValue)
        {
            throw new ArgumentException("Derived account ID is not valid for TigerBeetle.");
        }

        return accountId;
    }

    private static UInt128 BuildTransactionId(ReadOnlySpan<byte> idBytes)
    {
        var transactionId = BinaryPrimitives.ReadUInt128BigEndian(idBytes);
        if (transactionId == UInt128.Zero || transactionId == UInt128.MaxValue)
        {
            throw new ArgumentException("Derived transaction ID is not valid for TigerBeetle.");
        }

        return transactionId;
    }
}
