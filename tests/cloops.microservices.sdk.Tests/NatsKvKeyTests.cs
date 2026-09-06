using CLOOPS.microservices;
using NATS.Client.KeyValueStore;
using Xunit;

namespace cloops.microservices.sdk.Tests;

/// <summary>
/// Tests for <see cref="NatsKvKey"/>, the NATS KV / distributed-lock key validator.
/// </summary>
public class NatsKvKeyTests
{
    [Theory]
    [InlineData("cache-refresh.patients")]
    [InlineData("db-migrations.my.assembly.name")]
    [InlineData("a")]
    [InlineData("A1")]
    [InlineData("tenant/123/profile")]
    [InlineData("seq_invoices=1")]
    [InlineData("cljps.scheduling")]
    public void IsValid_ReturnsTrue_ForKeysNatsAccepts(string key)
    {
        Assert.True(NatsKvKey.IsValid(key));
        Assert.Null(NatsKvKey.GetValidationError(key));
    }

    [Theory]
    [InlineData("cache-refresh:patients")]
    [InlineData("my-service:nightly-cleanup")]
    [InlineData("db-migrations:my.assembly")]
    public void IsValid_ReturnsFalse_ForColonSeparatedKeys(string key)
    {
        Assert.False(NatsKvKey.IsValid(key));
    }

    [Fact]
    public void Validate_ForColonKey_ThrowsNamingTheKeyAndTheOffendingCharacter()
    {
        const string key = "cache-refresh:patients";

        var ex = Assert.Throws<ArgumentException>(() => NatsKvKey.Validate(key, nameof(key)));

        // The message must name the key, the offending character and its position, and point at
        // the '.' convention - not leave the caller with an opaque NatsKVException.
        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains("':'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("position 13", ex.Message, StringComparison.Ordinal);
        Assert.Contains("cache-refresh.patients", ex.Message, StringComparison.Ordinal);
        Assert.Equal(nameof(key), ex.ParamName);
    }

    [Theory]
    [InlineData("has space", ' ')]
    [InlineData("has@at", '@')]
    [InlineData("has#hash", '#')]
    [InlineData("has*star", '*')]
    [InlineData("has\\backslash", '\\')]
    public void Validate_ForOtherInvalidCharacters_NamesTheCharacter(string key, char offending)
    {
        var ex = Assert.Throws<ArgumentException>(() => NatsKvKey.Validate(key));

        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains($"'{offending}'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ForEmptyKey_Throws(string? key)
    {
        var ex = Assert.Throws<ArgumentException>(() => NatsKvKey.Validate(key));

        Assert.Contains("cannot be null, empty or whitespace", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".leading")]
    [InlineData("trailing.")]
    public void Validate_ForLeadingOrTrailingSeparator_Throws(string key)
    {
        var ex = Assert.Throws<ArgumentException>(() => NatsKvKey.Validate(key));

        Assert.Contains("cannot start or end with '.'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ForValidKey_DoesNotThrow()
    {
        NatsKvKey.Validate("cache-refresh.patients");
    }

    /// <summary>
    /// Guards against drift: <see cref="NatsKvKey"/> must agree with the NATS client's own
    /// key validator (<c>NatsKVStore.IsValidKey</c>) for every single-character key across the
    /// printable ASCII range, so a key this SDK accepts is never rejected by NATS at runtime.
    /// </summary>
    [Fact]
    public void IsValid_AgreesWithTheNatsClientValidator_AcrossAscii()
    {
        for (var c = (char)0x20; c <= (char)0x7E; c++)
        {
            var key = c.ToString();
            var natsAccepts = NatsKVStore.IsValidKey(key).Success;

            Assert.True(
                NatsKvKey.IsValid(key) == natsAccepts,
                $"Disagreement on character '{c}' (0x{(int)c:X2}): NatsKvKey={NatsKvKey.IsValid(key)}, NATS={natsAccepts}");
        }
    }

    /// <summary>
    /// The keys this SDK composes must be accepted by the NATS client validator itself.
    /// </summary>
    [Theory]
    [InlineData("cache-refresh.patients")]
    [InlineData("db-migrations.my.assembly")]
    public void ComposedKeys_AreAcceptedByTheNatsClientValidator(string key)
    {
        Assert.True(NatsKVStore.IsValidKey(key).Success);
    }
}
