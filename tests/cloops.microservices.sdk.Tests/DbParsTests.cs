using System.Data;
using CLOOPS.microservices;
using Microsoft.Data.SqlClient;
using Xunit;

namespace cloops.microservices.sdk.Tests;

/// <summary>
/// Type-inference tests for <see cref="DB.pars"/>.
///
/// <para><see cref="DB.pars"/> used to hand the value straight to
/// <see cref="SqlParameter(string, object)"/>, and SqlClient infers <see cref="SqlDbType.DateTime"/>
/// for a CLR <see cref="DateTime"/>. Legacy <c>datetime</c> has 3.33 ms resolution, so roughly two
/// of every three millisecond values were silently re-rounded on the way to a <c>datetime2</c>
/// column. The database half of this is proved in
/// <see cref="DbParsDateTimeIntegrationTests"/>; these tests pin the inference itself.</para>
/// </summary>
public class DbParsTests
{
    [Fact]
    public void pars_InfersDateTime2_ForADateTimeValue()
    {
        var parameter = DB.pars(("@v", new DateTime(2026, 9, 6, 12, 34, 56, 789)))[0];

        Assert.Equal(SqlDbType.DateTime2, parameter.SqlDbType);
    }

    [Fact]
    public void pars_LeavesScaleUnset_SoDateTimeTravelsAtFullPrecision()
    {
        // Scale 0 makes SqlClient send datetime2(7). Pinning a smaller scale here - datetime2(3),
        // say - would truncate a datetime2(7) column instead of letting the server round to whatever
        // the target column declares.
        var parameter = DB.pars(("@v", new DateTime(2026, 9, 6, 12, 34, 56, 789)))[0];

        Assert.Equal(0, parameter.Scale);
    }

    [Fact]
    public void pars_InfersDateTimeOffset_ForADateTimeOffsetValue()
    {
        // DateTimeOffset never had the problem: SqlClient already infers the full-precision type.
        var parameter = DB.pars(("@v", new DateTimeOffset(2026, 9, 6, 12, 34, 56, 789, TimeSpan.FromHours(5.5))))[0];

        Assert.Equal(SqlDbType.DateTimeOffset, parameter.SqlDbType);
    }

    [Fact]
    public void pars_MapsNullToDBNull()
    {
        var parameter = DB.pars(("@v", (object?)null))[0];

        Assert.Equal(DBNull.Value, parameter.Value);
    }

    [Fact]
    public void pars_KeepsTheNameAndValueItWasGiven()
    {
        var value = new DateTime(2026, 9, 6, 12, 34, 56, 789);

        var parameters = DB.pars(("@first", value), ("@second", 42));

        Assert.Equal("@first", parameters[0].ParameterName);
        Assert.Equal(value, parameters[0].Value);
        Assert.Equal("@second", parameters[1].ParameterName);
        Assert.Equal(42, parameters[1].Value);
    }

    /// <summary>
    /// Everything that is not a <see cref="DateTime"/> keeps the type SqlClient infers - the change
    /// is deliberately narrow.
    /// </summary>
    [Theory]
    [InlineData("hello", SqlDbType.NVarChar)]
    [InlineData(42, SqlDbType.Int)]
    [InlineData(42L, SqlDbType.BigInt)]
    [InlineData((short)42, SqlDbType.SmallInt)]
    [InlineData((byte)42, SqlDbType.TinyInt)]
    [InlineData(true, SqlDbType.Bit)]
    [InlineData(1.5d, SqlDbType.Float)]
    [InlineData(1.5f, SqlDbType.Real)]
    public void pars_LeavesOtherTypesToSqlClient(object value, SqlDbType expected)
    {
        Assert.Equal(expected, DB.pars(("@v", value))[0].SqlDbType);
    }

    /// <inheritdoc cref="pars_LeavesOtherTypesToSqlClient"/>
    [Fact]
    public void pars_LeavesTheRemainingTypesToSqlClient()
    {
        Assert.Equal(SqlDbType.Decimal, DB.pars(("@v", 123.456m))[0].SqlDbType);
        Assert.Equal(SqlDbType.UniqueIdentifier, DB.pars(("@v", Guid.NewGuid()))[0].SqlDbType);
        Assert.Equal(SqlDbType.Time, DB.pars(("@v", new TimeSpan(1, 2, 3)))[0].SqlDbType);
        Assert.Equal(SqlDbType.Date, DB.pars(("@v", new DateOnly(2026, 9, 6)))[0].SqlDbType);
        Assert.Equal(SqlDbType.Time, DB.pars(("@v", new TimeOnly(12, 34, 56)))[0].SqlDbType);
        Assert.Equal(SqlDbType.VarBinary, DB.pars(("@v", new byte[] { 1, 2, 3 }))[0].SqlDbType);
    }

    /// <summary>
    /// The classic companion bug to the <c>datetime</c> one: a <c>decimal</c> parameter with no
    /// precision or scale set. SqlClient derives both from the value itself, so nothing is
    /// truncated and there is nothing to fix here - this test exists so a regression would be
    /// noticed. <see cref="DbParsDateTimeIntegrationTests.Decimal_RoundTripsWithoutTruncation"/>
    /// proves the same end to end.
    /// </summary>
    [Fact]
    public void pars_LeavesDecimalPrecisionAndScaleToSqlClient()
    {
        var parameter = DB.pars(("@v", 0.12345678901234567890m))[0];

        Assert.Equal(SqlDbType.Decimal, parameter.SqlDbType);
        Assert.Equal(0, parameter.Precision);
        Assert.Equal(0, parameter.Scale);
    }
}
