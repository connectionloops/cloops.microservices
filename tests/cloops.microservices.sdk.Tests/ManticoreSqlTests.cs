using CLOOPS.microservices;
using Microsoft.Extensions.Logging;
using Xunit;

namespace cloops.microservices.sdk.Tests;

/// <summary>
/// Tests for <see cref="ManticoreSql"/> — the injection boundary for every service built on
/// <see cref="BaseManticoreService"/>. These pins were extracted together with the code from a
/// production consumer; several of them pin behaviour that was a live outage when it was wrong.
/// </summary>
public class ManticoreSqlTests
{
    // ── EscapeLiteral: Manticore escapes with a backslash, NOT by ANSI doubling ───────────────

    /// <summary>
    /// ⚠ <b>Manticore does not support the ANSI <c>''</c> form at all.</b> It recognises only
    /// <c>\'</c>, and answers the doubled form with a parse error:
    ///
    /// <code>
    /// WHERE customer_id = 'won''t'  → P01: syntax error, unexpected string, expecting $end
    /// WHERE customer_id = 'won\'t'  → parses, no error
    /// </code>
    ///
    /// <para>This is the one place a habit carried over from every other SQL engine produces a
    /// confident, wrong, and <i>silently destructive</i> result, so it is pinned both ways: the
    /// backslash form must be produced, and the doubled form must be absent.</para>
    /// </summary>
    [Fact]
    public void EscapeLiteral_UsesBackslash_BecauseManticoreRejectsTheAnsiForm()
    {
        var escaped = ManticoreSql.EscapeLiteral("won't");

        Assert.Equal(@"won\'t", escaped);
        Assert.DoesNotContain("''", escaped, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("won't", @"won\'t")]
    [InlineData("O'Brien", @"O\'Brien")]
    [InlineData("the patient's panel", @"the patient\'s panel")]
    [InlineData("'", @"\'")]
    // Input that already LOOKS like an ANSI escape is data, and is escaped as two apostrophes.
    [InlineData("''", @"\'\'")]
    public void EscapeLiteral_UsesTheBackslashForm_ForEveryApostrophe(string raw, string expected)
        => Assert.Equal(expected, ManticoreSql.EscapeLiteral(raw));

    /// <summary>
    /// The two escape characters must not mangle each other. <c>EscapeLiteral</c> walks the input
    /// one character at a time and never re-reads its own output, so a backslash immediately
    /// followed by an apostrophe <i>as data</i> becomes <c>\\</c> + <c>\'</c> = <c>\\\'</c> — one
    /// literal backslash, then one literal apostrophe, when Manticore reads it back.
    ///
    /// <para>A two-pass implementation (escape quotes, then escape backslashes, or the reverse)
    /// gets exactly this input wrong, and it is the input a Windows path produces.</para>
    /// </summary>
    [Fact]
    public void EscapeLiteral_BackslashBeforeApostrophe_EscapesBothWithoutMangling()
    {
        Assert.Equal(@"\\", ManticoreSql.EscapeLiteral(@"\"));
        Assert.Equal(@"\\\'", ManticoreSql.EscapeLiteral(@"\'"));
        Assert.Equal(@"C:\\reports\\won\'t.txt", ManticoreSql.EscapeLiteral(@"C:\reports\won't.txt"));
    }

    /// <summary>
    /// A backslash is Manticore's own escape character inside a literal, so a trailing one would
    /// escape the closing quote and turn the rest of the statement into string data.
    /// </summary>
    [Fact]
    public void EscapeLiteral_TrailingBackslash_CannotEscapeTheClosingQuote()
    {
        var escaped = ManticoreSql.EscapeLiteral(@"C:\reports\");

        Assert.EndsWith(@"\\", escaped, StringComparison.Ordinal);
        Assert.DoesNotContain('\'', escaped);
    }

    [Fact]
    public void EscapeLiteral_ControlCharacters_AreFoldedOrDropped_NotCarriedIntoTheStatement()
    {
        var escaped = ManticoreSql.EscapeLiteral("line one\r\nline\ttwo\0");

        Assert.DoesNotContain('\n', escaped);
        Assert.DoesNotContain('\r', escaped);
        Assert.DoesNotContain('\t', escaped);
        Assert.DoesNotContain('\0', escaped);
        // Tab/CR/LF fold to spaces so words do not run together.
        Assert.Contains("line one  line two", escaped, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EscapeLiteral_NullOrEmpty_IsEmpty(string? raw)
        => Assert.Equal(string.Empty, ManticoreSql.EscapeLiteral(raw));

    // ── EscapeMatch: full-text operators are neutralised ──────────────────────────────────────

    /// <summary>
    /// Manticore's extended query syntax gives <c>@ ( ) | - ! ~ " / ^ $ = &lt; &gt; *</c>
    /// operator meanings. A user pasting a log line into a search box must not accidentally write
    /// a query expression — or, worse, an unbalanced one, which Manticore rejects with a parse
    /// error rather than returning no results.
    /// </summary>
    [Theory]
    [InlineData("report (urgent)")]
    [InlineData("CBC | HbA1c")]
    [InlineData("not-generating")]
    [InlineData("@title hack")]
    [InlineData("\"exact phrase\"")]
    public void EscapeMatch_EscapesExtendedSyntaxOperators(string userText)
    {
        var escaped = ManticoreSql.EscapeMatch(userText);

        foreach (var op in "()|-!@\"~/^$")
        {
            var index = escaped.IndexOf(op);
            if (index < 0) continue;

            Assert.True(index > 0 && escaped[index - 1] == '\\',
                $"'{op}' at {index} in \"{escaped}\" is not backslash-escaped.");
        }
    }

    /// <summary>
    /// Every character in <see cref="ManticoreSql.MatchOperators"/> must actually be escaped —
    /// removing one from the set does not fail a compile or a parse, it produces a query the user
    /// can steer, so the set itself is pinned here.
    /// </summary>
    [Fact]
    public void EscapeMatch_EscapesEveryDocumentedOperator()
    {
        foreach (var op in ManticoreSql.MatchOperators)
        {
            Assert.Equal($"a\\{op}b", ManticoreSql.EscapeMatch($"a{op}b"));
        }
    }

    /// <summary>
    /// <c>*</c> is the wildcard operator and historically was missing from the escape set.
    /// Unescaped, a pasted glob like <c>report*.pdf</c> is either a prefix search over the whole
    /// dictionary or an outright rejection, depending on how the index was built.
    /// </summary>
    [Fact]
    public void EscapeMatch_EscapesTheWildcardOperator()
        => Assert.Equal(@"report\*.pdf", ManticoreSql.EscapeMatch("report*.pdf"));

    /// <summary>
    /// The apostrophe is a SQL-lexer concern only — it means nothing to the full-text parser, so
    /// <c>EscapeMatch</c> deliberately leaves it alone and <c>EscapeLiteral</c> alone handles it.
    /// </summary>
    [Fact]
    public void EscapeMatch_LeavesTheApostropheToTheLiteralLayer()
        => Assert.Equal("patient's CBC", ManticoreSql.EscapeMatch("patient's CBC"));

    [Fact]
    public void EscapeMatch_CollapsesWhitespaceRuns_AndTrims()
        => Assert.Equal("alpha beta gamma", ManticoreSql.EscapeMatch("  alpha \t\r\n beta   gamma  "));

    [Fact]
    public void EscapeMatch_DropsControlCharacters()
        => Assert.Equal(string.Empty, ManticoreSql.EscapeMatch("\u0001\u0002\u0007"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n  ")]
    public void EscapeMatch_BlankInput_IsEmpty(string? raw)
        => Assert.Equal(string.Empty, ManticoreSql.EscapeMatch(raw));

    /// <summary>
    /// The two-layer contract: one backslash from <c>EscapeMatch</c> for the full-text parser,
    /// doubled by <c>EscapeLiteral</c> for the SQL literal. Manticore undoes them in the opposite
    /// order and the matcher sees a literal character. The backslash itself is the input where the
    /// two passes compound: <c>\</c> → <c>\\</c> → <c>\\\\</c> on the wire.
    /// </summary>
    [Fact]
    public void EscapeMatch_ThenEscapeLiteral_ComposeInThatOrder()
    {
        Assert.Equal(@"alpha\\/beta", ManticoreSql.EscapeLiteral(ManticoreSql.EscapeMatch("alpha/beta")));
        Assert.Equal(@"alpha\\\\beta", ManticoreSql.EscapeLiteral(ManticoreSql.EscapeMatch(@"alpha\beta")));
    }

    /// <summary>
    /// The same, pinned per operator: through both layers each operator travels with its
    /// <c>EscapeMatch</c> backslash doubled by <c>EscapeLiteral</c> — <c>a\\(b</c>, <c>a\\|b</c>, …
    /// The backslash operator is its own special case: it is escaped by <c>EscapeMatch</c>
    /// (<c>\\</c>) and then both characters are doubled by <c>EscapeLiteral</c> (<c>\\\\</c>).
    /// </summary>
    [Fact]
    public void EscapeMatch_ThenEscapeLiteral_DoublesTheBackslashForEveryOperator()
    {
        foreach (var op in ManticoreSql.MatchOperators)
        {
            var wire = ManticoreSql.EscapeLiteral(ManticoreSql.EscapeMatch($"a{op}b"));
            var expected = op == '\\' ? @"a\\\\b" : $@"a\\{op}b";

            Assert.Equal(expected, wire);
        }
    }

    // ── SafeIndexName: the config-side injection guard ────────────────────────────────────────

    [Theory]
    [InlineData("ticket_shell")]
    [InlineData("Idx42")]
    [InlineData("a")]
    public void SafeIndexName_PassesBareIdentifiersThrough(string configured)
        => Assert.Equal(configured, ManticoreSql.SafeIndexName(configured, "fallback_idx", null));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad name")]
    [InlineData("idx'; DROP TABLE x --")]
    [InlineData("idx-dash")]
    public void SafeIndexName_FallsBack_ForAnythingNotABareIdentifier(string? configured)
        => Assert.Equal("fallback_idx", ManticoreSql.SafeIndexName(configured, "fallback_idx", null));

    [Fact]
    public void SafeIndexName_LogsTheRejection_SoTheTypoIsFindable()
    {
        var logger = new ListLogger();

        var name = ManticoreSql.SafeIndexName("bad name", "fallback_idx", logger);

        Assert.Equal("fallback_idx", name);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("bad name", entry.Message, StringComparison.Ordinal);
        Assert.Contains("fallback_idx", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeIndexName_NullLogger_DoesNotThrow()
        => Assert.Equal("fb", ManticoreSql.SafeIndexName("bad name", "fb", null));

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
