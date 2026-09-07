using System.Data;
using System.Globalization;
using System.Text;
using CLOOPS.microservices;
using Microsoft.Data.SqlClient;
using Xunit;

namespace cloops.microservices.sdk.Tests;

/// <summary>
/// Integration tests for the <c>datetime</c> / <c>datetime2</c> behaviour of <see cref="DB.pars"/>.
///
/// <para>Everything here is a SQL Server behaviour - how it rounds on assignment, which type wins a
/// comparison, whether an index can still be sought - so these run against a real instance. See
/// <see cref="SqlServer"/> for the connection string and the container command; the tests skip
/// rather than fail when no server answers.</para>
///
/// <para>Legacy <c>datetime</c> stores 1/300 of a second, so the only millisecond values it can hold
/// end in 0, 3 or 7. Every "unroundable" value below is deliberately not one of those.</para>
/// </summary>
public class DbParsDateTimeIntegrationTests
{
    /// <summary>12:34:56.789 - legacy <c>datetime</c> cannot hold it; it snaps to .790.</summary>
    private static readonly DateTime unroundable = new(2026, 9, 6, 12, 34, 56, 789);

    /// <summary>What <see cref="DB.pars"/> used to build: the value, with the type left to SqlClient.</summary>
    private static SqlParameter[] Inferred(string name, object value) => [new SqlParameter(name, value)];

    private static DateTime At(int millisecond) => new(2026, 1, 1, 0, 0, 0, millisecond);

    #region round trip into datetime2

    /// <summary>
    /// The bug, end to end. A millisecond value that legacy <c>datetime</c> cannot represent goes
    /// into a <c>datetime2</c> column and comes back changed when the parameter type is left to
    /// SqlClient - even though nothing in the schema is a <c>datetime</c>.
    /// </summary>
    [SqlServerTheory]
    [InlineData(789, "2026-09-06T12:34:56.7900000")] // rounds up
    [InlineData(1, "2026-09-06T12:34:56.0000000")]   // rounds down, losing the millisecond entirely
    [InlineData(2, "2026-09-06T12:34:56.0033333")]   // rounds to a value no caller ever asked for
    [InlineData(999, "2026-09-06T12:34:57.0000000")] // rounds into the next second
    public void InferredDateTime_CorruptsMillisecondsOnTheWayIntoADateTime2Column(int millisecond, string corrupted)
    {
        var value = new DateTime(2026, 9, 6, 12, 34, 56, millisecond);

        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIME2(7) NOT NULL");
        table.Execute($"INSERT INTO {table} (v) VALUES (@v)", Inferred("@v", value));

        var stored = (DateTime)table.Scalar($"SELECT v FROM {table}")!;

        Assert.NotEqual(value, stored);
        Assert.Equal(DateTime.Parse(corrupted, CultureInfo.InvariantCulture), stored);
    }

    /// <inheritdoc cref="InferredDateTime_CorruptsMillisecondsOnTheWayIntoADateTime2Column"/>
    [SqlServerTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(500)]
    [InlineData(789)]
    [InlineData(999)]
    public void pars_RoundTripsEveryMillisecondThroughADateTime2Column(int millisecond)
    {
        var value = new DateTime(2026, 9, 6, 12, 34, 56, millisecond);

        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIME2(7) NOT NULL");
        table.Execute($"INSERT INTO {table} (v) VALUES (@v)", DB.pars(("@v", value)));

        Assert.Equal(value, (DateTime)table.Scalar($"SELECT v FROM {table}")!);
    }

    /// <summary>
    /// No scale is pinned on the parameter, so sub-millisecond ticks survive into a
    /// <c>datetime2(7)</c> column too.
    /// </summary>
    [SqlServerFact]
    public void pars_RoundTripsSubMillisecondTicks()
    {
        var value = new DateTime(637_000_000_000_000_001L);

        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIME2(7) NOT NULL");
        table.Execute($"INSERT INTO {table} (v) VALUES (@v)", DB.pars(("@v", value)));

        Assert.Equal(value, (DateTime)table.Scalar($"SELECT v FROM {table}")!);
    }

    /// <summary>
    /// A <c>datetime2(3)</c> column still rounds the full-precision parameter down to milliseconds
    /// on assignment. Deciding precision is the column's job, not the parameter's - which is why no
    /// scale is set.
    /// </summary>
    [SqlServerFact]
    public void pars_LetsTheColumnDecideItsOwnPrecision()
    {
        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIME2(3) NOT NULL");
        table.Execute($"INSERT INTO {table} (v) VALUES (@v)", DB.pars(("@v", new DateTime(637_000_000_000_000_001L))));

        Assert.Equal(new DateTime(637_000_000_000_000_000L), (DateTime)table.Scalar($"SELECT v FROM {table}")!);
    }

    #endregion

    #region the symptom: keyset pagination boundaries

    /// <summary>
    /// The shape the bug actually presented in: a keyset cursor over a <c>datetime2</c> column, on a
    /// 25-row table. When the cursor value rounds <i>up</i>, <c>v &lt; @cursor</c> lets rows the
    /// caller has already seen back in and the walk returns more rows than the table holds.
    /// </summary>
    [SqlServerFact]
    public void KeysetCursor_OverADateTime2Column_NeitherRepeatsNorSkipsRows()
    {
        using var table = Seeded("id INT NOT NULL PRIMARY KEY, v DATETIME2(3) NOT NULL, INDEX ix_v NONCLUSTERED (v, id)", 25);

        const string firstPage = "SELECT TOP 5 id, v FROM {0} ORDER BY v DESC, id DESC";
        const string nextPage = """
            SELECT TOP 5 id, v FROM {0}
            WHERE v < @cursorV OR (v = @cursorV AND id < @cursorId)
            ORDER BY v DESC, id DESC
            """;

        List<int> Walk(Func<DateTime, SqlParameter[]> cursorParameter)
        {
            var seen = new List<int>();
            var rows = table.Rows(string.Format(firstPage, table));

            while (rows.Count > 0)
            {
                seen.AddRange(rows.Select(r => (int)r[0]));
                if (seen.Count > 100) break; // a cursor that rounds back on itself would loop forever

                var cursorId = (int)rows[^1][0];
                var cursorV = (DateTime)rows[^1][1];
                SqlParameter[] parameters = [.. cursorParameter(cursorV), new SqlParameter("@cursorId", cursorId)];
                rows = table.Rows(string.Format(nextPage, table), parameters);
            }

            return seen;
        }

        var withPars = Walk(v => DB.pars(("@cursorV", v)));
        var withInferred = Walk(v => Inferred("@cursorV", v));

        // 25 rows, walked from the top: 24, 23, ... 0, exactly once each.
        var expected = Enumerable.Range(0, 25).Reverse().ToList();
        Assert.Equal(expected, withPars);

        // The old behaviour hands back rows it has already handed back.
        Assert.NotEqual(expected, withInferred);
        Assert.True(
            withInferred.Count > withInferred.Distinct().Count(),
            $"expected repeated rows, walked {withInferred.Count} rows: {string.Join(",", withInferred)}");
    }

    /// <summary>
    /// The same off-by-one reduced to the predicate that causes it: a <c>&lt;</c> comparison whose
    /// bound is a real stored value legacy <c>datetime</c> cannot represent.
    /// </summary>
    [SqlServerFact]
    public void RangeComparison_AgainstADateTime2Column_CountsTheRightRows()
    {
        using var table = Seeded("id INT NOT NULL PRIMARY KEY, v DATETIME2(3) NOT NULL, INDEX ix_v NONCLUSTERED (v, id)", 25);

        // Row 21 sits at .021, which rounds to .020 - so the old parameter loses row 20 as well.
        var bound = At(21);

        Assert.Equal(21, table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v < @v", DB.pars(("@v", bound))));
        Assert.Equal(20, table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v < @v", Inferred("@v", bound)));
    }

    #endregion

    #region legacy datetime column compatibility

    /// <summary>
    /// Widening the parameter to <c>datetime2</c> does not change what lands in a legacy
    /// <c>datetime</c> column: SQL Server rounds on assignment exactly as the client used to.
    /// </summary>
    [SqlServerTheory]
    [InlineData(789)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(999)]
    [InlineData(500)]
    public void LegacyDateTimeColumn_StoresTheSameValueEitherWay(int millisecond)
    {
        var value = new DateTime(2026, 9, 6, 12, 34, 56, millisecond);

        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIME NOT NULL");

        table.Execute($"INSERT INTO {table} (v) VALUES (@v)", Inferred("@v", value));
        var withInferred = (DateTime)table.Scalar($"SELECT v FROM {table}")!;

        table.Execute($"DELETE FROM {table}");
        table.Execute($"INSERT INTO {table} (v) VALUES (@v)", DB.pars(("@v", value)));
        var withPars = (DateTime)table.Scalar($"SELECT v FROM {table}")!;

        Assert.Equal(withInferred, withPars);
    }

    /// <summary>
    /// Range comparisons against a legacy <c>datetime</c> column are unaffected when the bound is a
    /// value that column can hold - only whole multiples of 10 ms are, plus anything read back out
    /// of the column. That covers the bounds repositories use in practice: a stored cursor value, a
    /// day or hour boundary, a whole second.
    /// </summary>
    [SqlServerTheory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(20)]
    public void LegacyDateTimeColumn_RangeComparisonWithABoundItCanHold_IsUnchanged(int millisecond)
    {
        using var table = Seeded("id INT NOT NULL PRIMARY KEY, v DATETIME NOT NULL, INDEX ix_v NONCLUSTERED (v, id)", 25);
        var bound = At(millisecond);

        Assert.Equal(
            table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v < @v", Inferred("@v", bound)),
            table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v < @v", DB.pars(("@v", bound))));
    }

    /// <summary>
    /// <b>Behaviour change, pinned deliberately (1 of 2).</b>
    ///
    /// <para>A range comparison against a legacy <c>datetime</c> column moves by up to 1.67 ms when
    /// the bound is a value that column cannot hold. The client used to snap the bound to the
    /// column's grid before sending it; a <c>datetime2</c> parameter keeps the caller's value and
    /// SQL Server widens the column for the comparison instead.</para>
    ///
    /// <para>Here the bound is .021: the old parameter asked the server for <c>&lt; .020</c> and got
    /// 19 rows, while the fixed parameter asks for what the caller actually wrote and gets the 22
    /// rows whose stored instant really is below .021. The new answer is the right one, but it is a
    /// different answer.</para>
    /// </summary>
    [SqlServerFact]
    public void LegacyDateTimeColumn_RangeComparisonWithABoundItCannotHold_MovesTheBoundary()
    {
        using var table = Seeded("id INT NOT NULL PRIMARY KEY, v DATETIME NOT NULL, INDEX ix_v NONCLUSTERED (v, id)", 25);
        var bound = At(21);

        Assert.Equal(19, table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v < @v", Inferred("@v", bound)));
        Assert.Equal(22, table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v < @v", DB.pars(("@v", bound))));
    }

    /// <summary>
    /// Equality against a legacy <c>datetime</c> column is unaffected when the value came out of
    /// that column - the round trip every repository actually does.
    /// </summary>
    [SqlServerFact]
    public void LegacyDateTimeColumn_EqualityOnAValueReadBackFromItIsUnchanged()
    {
        using var table = Seeded("id INT NOT NULL PRIMARY KEY, v DATETIME NOT NULL, INDEX ix_v NONCLUSTERED (v, id)", 25);
        var stored = (DateTime)table.Scalar($"SELECT v FROM {table} WHERE id = 21")!;

        Assert.Equal(
            table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v = @v", Inferred("@v", stored)),
            table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v = @v", DB.pars(("@v", stored))));
    }

    /// <summary>
    /// <b>Behaviour change, pinned deliberately (2 of 2) - the sharpest form of the one above.</b>
    ///
    /// <para>Equality against a legacy <c>datetime</c> column, using a CLR value that column cannot
    /// represent, used to match: the client rounded the parameter the same way the column had
    /// rounded the row on insert, and the two met in the middle. A <c>datetime2</c> parameter keeps
    /// its precision, the column is widened for the comparison instead, and .789 no longer equals
    /// .790.</para>
    ///
    /// <para>This is inherent - a parameter cannot both preserve the caller's value and match a
    /// value that was rounded away. A caller on a legacy <c>datetime</c> column that looks rows up
    /// by exact equality must round the value itself, or compare a range.</para>
    /// </summary>
    [SqlServerFact]
    public void LegacyDateTimeColumn_EqualityOnAValueTheColumnCannotHold_NoLongerMatches()
    {
        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIME NOT NULL");
        table.Execute($"INSERT INTO {table} (v) VALUES (@v)", DB.pars(("@v", unroundable)));

        // The row went in as .790 - the column has no .789 to find.
        Assert.Equal(new DateTime(2026, 9, 6, 12, 34, 56, 790), (DateTime)table.Scalar($"SELECT v FROM {table}")!);

        Assert.Equal(1, table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v = @v", Inferred("@v", unroundable)));
        Assert.Equal(0, table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v = @v", DB.pars(("@v", unroundable))));

        // The way through: compare a range, or round the value to what the column can hold.
        Assert.Equal(1, table.Scalar(
            $"SELECT COUNT(*) FROM {table} WHERE v >= @from AND v < @to",
            DB.pars(("@from", unroundable.AddMilliseconds(-5)), ("@to", unroundable.AddMilliseconds(5)))));
    }

    #endregion

    #region query plans

    /// <summary>
    /// The compatibility risk that would not show up as a wrong answer: a type mismatch defeating an
    /// index seek. It does not. Against a legacy <c>datetime</c> column SQL Server plans a dynamic
    /// seek - it converts the range, not the column - and against a <c>datetime2</c> column the seek
    /// is direct.
    /// </summary>
    [SqlServerTheory]
    [InlineData("DATETIME")]
    [InlineData("DATETIME2(3)")]
    [InlineData("DATETIME2(7)")]
    public void Comparison_AgainstAnIndexedColumn_StillSeeks(string columnType)
    {
        using var table = Seeded($"id INT NOT NULL PRIMARY KEY, v {columnType} NOT NULL, INDEX ix_v NONCLUSTERED (v, id)", 3000);

        // Selective enough that a seek is unambiguously the right plan.
        var bound = (DateTime)table.Scalar($"SELECT v FROM {table} WHERE id = 10")!;

        foreach (var (label, parameters) in new (string, SqlParameter[])[]
                 {
                     ("inferred", Inferred("@v", bound)),
                     ("pars", DB.pars(("@v", bound))),
                 })
        {
            var plan = table.Plan($"SELECT COUNT(*) FROM {table} WHERE v < @v", parameters);

            Assert.True(
                plan.Contains("<SeekPredicates>", StringComparison.Ordinal),
                $"{columnType} / {label} did not seek the index. Plan: {plan}");
            Assert.DoesNotContain("<TableScan", plan, StringComparison.Ordinal);
        }
    }

    #endregion

    #region other types

    /// <summary>
    /// <see cref="DateTimeOffset"/> never had the analogous problem - SqlClient already infers
    /// <see cref="SqlDbType.DateTimeOffset"/>, which is full precision and carries the offset - so
    /// it was left alone.
    /// </summary>
    [SqlServerFact]
    public void DateTimeOffset_RoundTripsAtFullPrecisionIncludingItsOffset()
    {
        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIMEOFFSET(7) NOT NULL");

        foreach (var value in new[]
                 {
                     new DateTimeOffset(2026, 9, 6, 12, 34, 56, 789, TimeSpan.FromHours(5.5)),
                     new DateTimeOffset(new DateTime(637_000_000_000_000_001L), TimeSpan.Zero),
                     DateTimeOffset.MinValue,
                     DateTimeOffset.MaxValue,
                 })
        {
            table.Execute($"DELETE FROM {table}");
            table.Execute($"INSERT INTO {table} (v) VALUES (@v)", DB.pars(("@v", value)));

            var stored = (DateTimeOffset)table.Scalar($"SELECT v FROM {table}")!;
            Assert.Equal(value, stored);
            Assert.Equal(value.Offset, stored.Offset);
        }
    }

    /// <summary>
    /// The classic companion bug: a <c>decimal</c> parameter left with precision and scale 0. It is
    /// not one here - SqlClient derives both from the value - so nothing was changed. This pins it.
    /// </summary>
    [SqlServerTheory]
    [InlineData("0.12345678901234567890")]
    [InlineData("1.0000000000000000001")]
    [InlineData("123.456")]
    [InlineData("-1.005")]
    [InlineData("1234567890123456.789")]
    public void Decimal_RoundTripsWithoutTruncation(string literal)
    {
        var value = decimal.Parse(literal, CultureInfo.InvariantCulture);

        using var table = Table("id INT IDENTITY PRIMARY KEY, v DECIMAL(38, 20) NOT NULL");
        table.Execute($"INSERT INTO {table} (v) VALUES (@v)", DB.pars(("@v", value)));

        Assert.Equal(value, (decimal)table.Scalar($"SELECT v FROM {table}")!);
    }

    #endregion

    #region boundaries and nulls

    /// <summary>
    /// <c>default(DateTime)</c> and <see cref="DateTime.MinValue"/> are outside legacy
    /// <c>datetime</c>'s range, so they used to throw <c>SqlTypeException</c> before the value ever
    /// reached the server. They now store as themselves - a second, smaller behaviour change: an
    /// unset timestamp that used to fail loudly is now written as year 0001.
    /// </summary>
    [SqlServerFact]
    public void BoundaryValues_ThatLegacyDateTimeCouldNotCarry_NowRoundTrip()
    {
        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIME2(7) NOT NULL");

        foreach (var value in new[] { default, DateTime.MinValue, DateTime.MaxValue, new DateTime(1753, 1, 1) })
        {
            table.Execute($"DELETE FROM {table}");
            table.Execute($"INSERT INTO {table} (v) VALUES (@v)", DB.pars(("@v", value)));

            Assert.Equal(value, (DateTime)table.Scalar($"SELECT v FROM {table}")!);
        }

        table.Execute($"DELETE FROM {table}");
        Assert.Throws<System.Data.SqlTypes.SqlTypeException>(
            () => table.Execute($"INSERT INTO {table} (v) VALUES (@v)", Inferred("@v", DateTime.MinValue)));
    }

    /// <summary>
    /// A null value is still bound as <see cref="DBNull"/> with the type SqlClient picks - there is
    /// no CLR type to widen - and still writes NULL to a timestamp column of either flavour.
    /// </summary>
    [SqlServerFact]
    public void Null_StillWritesNullToBothColumnTypes()
    {
        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIME2(3) NULL, w DATETIME NULL");
        table.Execute($"INSERT INTO {table} (v, w) VALUES (@v, @v)", DB.pars(("@v", null)));

        Assert.Equal(1, table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v IS NULL AND w IS NULL"));
    }

    /// <summary>A null and a value in the same call do not interfere with each other.</summary>
    [SqlServerFact]
    public void Null_AlongsideADateTimeInTheSameCall()
    {
        using var table = Table("id INT IDENTITY PRIMARY KEY, v DATETIME2(7) NULL, w DATETIME2(7) NULL");
        table.Execute($"INSERT INTO {table} (v, w) VALUES (@v, @w)", DB.pars(("@v", null), ("@w", unroundable)));

        Assert.Equal(1, table.Scalar($"SELECT COUNT(*) FROM {table} WHERE v IS NULL"));
        Assert.Equal(unroundable, (DateTime)table.Scalar($"SELECT w FROM {table}")!);
    }

    #endregion

    #region harness

    private static ScratchTable Table(string columns) => new(columns);

    /// <summary>
    /// A scratch table holding <paramref name="rows"/> rows, one per millisecond from
    /// 2026-01-01T00:00:00.000, so row <c>n</c> sits at <c>.n</c> milliseconds.
    /// </summary>
    private static ScratchTable Seeded(string columns, int rows)
    {
        var table = new ScratchTable(columns);
        table.Execute($"""
            INSERT INTO {table} (id, v)
            SELECT n, DATEADD(millisecond, n, CAST('2026-01-01T00:00:00.000' AS DATETIME2(3)))
            FROM (
                SELECT TOP ({rows}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b
            ) numbers;
            """);
        return table;
    }

    /// <summary>A throwaway table on the test server, dropped when the test finishes.</summary>
    private sealed class ScratchTable : IDisposable
    {
        private readonly SqlConnection connection;
        private readonly string name;

        internal ScratchTable(string columns)
        {
            connection = SqlServer.Open();
            name = $"dbo.[cloops_test_{Guid.NewGuid():N}]";
            Execute($"CREATE TABLE {name} ({columns});");
        }

        public override string ToString() => name;

        internal void Execute(string sql, SqlParameter[]? parameters = null)
        {
            using var command = Command(sql, parameters);
            command.ExecuteNonQuery();
        }

        internal object? Scalar(string sql, SqlParameter[]? parameters = null)
        {
            using var command = Command(sql, parameters);
            var value = command.ExecuteScalar();
            return value == DBNull.Value ? null : value;
        }

        internal List<object[]> Rows(string sql, SqlParameter[]? parameters = null)
        {
            using var command = Command(sql, parameters);
            using var reader = command.ExecuteReader();
            var rows = new List<object[]>();
            while (reader.Read())
            {
                var row = new object[reader.FieldCount];
                reader.GetValues(row);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>The actual execution plan, so an index seek can be asserted rather than assumed.</summary>
        internal string Plan(string sql, SqlParameter[]? parameters = null)
        {
            Execute("SET STATISTICS XML ON;");
            var plan = new StringBuilder();
            using (var command = Command(sql, parameters))
            using (var reader = command.ExecuteReader())
            {
                do
                {
                    while (reader.Read())
                    {
                        if (reader.GetValue(0) is string fragment && fragment.StartsWith("<ShowPlanXML", StringComparison.Ordinal))
                        {
                            plan.Append(fragment);
                        }
                    }
                } while (reader.NextResult());
            }
            Execute("SET STATISTICS XML OFF;");
            return plan.ToString();
        }

        private SqlCommand Command(string sql, SqlParameter[]? parameters)
        {
            var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            if (parameters != null)
            {
                command.Parameters.AddRange(parameters);
            }
            return command;
        }

        public void Dispose()
        {
            try
            {
                Execute($"DROP TABLE IF EXISTS {name};");
            }
            catch
            {
                // A dropped scratch table is housekeeping; never fail a test over it.
            }

            connection.Dispose();
        }
    }

    #endregion
}
