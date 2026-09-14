using System.Buffers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CLOOPS.microservices;

/// <summary>
/// The escaping layer for Manticore's HTTP SQL endpoint — the <b>injection boundary</b> of every
/// service built on <see cref="BaseManticoreService"/>.
///
/// <para>There are no bound parameters over the <c>/sql?mode=raw</c> API: every value is
/// interpolated into a SQL string. So <see cref="EscapeLiteral"/> and <see cref="EscapeMatch"/>
/// are not tidiness, they are the security boundary, and they are <c>public static</c> precisely
/// so every consumer — and every consumer's tests — hits the <i>same</i> copy rather than
/// hand-porting it and reintroducing one of the traps documented below.</para>
///
/// <para>═══ THE TWO-LAYER ESCAPE CONTRACT ═════════════════════════════════════════════════════</para>
///
/// <para>There are two nested languages between user-supplied text and the full-text matcher, and
/// each gets its own pass, <b>in this order and no other</b>:</para>
///
/// <list type="number">
///   <item><description><see cref="EscapeMatch"/> first, escaping for the <i>full-text query
///   parser</i>: every character in <see cref="MatchOperators"/> gets one backslash.</description></item>
///   <item><description><see cref="EscapeLiteral"/> second, applied to the whole assembled MATCH
///   expression, escaping for the <i>SQL string literal</i>: it doubles those backslashes, and
///   backslash-escapes any apostrophe — which <see cref="EscapeMatch"/> deliberately leaves alone,
///   because an apostrophe means nothing to the full-text parser and everything to the SQL
///   lexer.</description></item>
/// </list>
///
/// <para>So a user's <c>/</c> travels as <c>/</c> → <c>\/</c> → <c>\\/</c> on the wire; the SQL
/// layer unescapes it to <c>\/</c>, and the query parser unescapes <i>that</i> to a literal slash.
/// An apostrophe travels <c>'</c> → <c>'</c> → <c>\'</c> and reaches the parser as a plain
/// <c>'</c>. Swapping the order breaks both: <see cref="EscapeLiteral"/> first would double
/// nothing (there are no backslashes yet), and <see cref="EscapeMatch"/> would then add
/// backslashes the SQL layer never got the chance to double — so Manticore's lexer would consume
/// them itself and every operator would go back to being an operator.</para>
/// </summary>
public static class ManticoreSql
{
    /// <summary>
    /// Manticore's extended full-text query operators. Anything a user types that happens to be
    /// one of these has to be neutralised, or a search for <c>"C++ | Java"</c> becomes an OR
    /// expression and a stray <c>(</c> becomes a parse error reported as an empty result set.
    ///
    /// <para>The set is <c>\ ( ) | - ! @ ~ " &amp; / ^ $ = &lt; &gt; *</c>. Three of them are worth
    /// calling out because leaving them in is not a parse error but a <i>silently wrong answer</i>:
    /// <c>"</c> would terminate any phrase/quorum quotes a query builder wraps user terms in and
    /// let the rest of the text be read as query syntax; <c>/</c> would be read as the
    /// quorum/proximity operator attached to that same closing quote; and <c>*</c> is the wildcard,
    /// so a pasted glob like <c>report*.pdf</c> would either expand to a prefix search over the
    /// whole dictionary or be rejected outright depending on how the index was built. Removing any
    /// of these from the set does not produce a compile error or a failing parse — it produces a
    /// query the user can steer.</para>
    /// </summary>
    public const string MatchOperators = "\\()|-!@~\"&/^$=<>*";

    /// <summary>
    /// <see cref="MatchOperators"/> compiled for vectorised probing — <see cref="EscapeMatch"/>
    /// runs over every character of every search query, so the per-character membership test is
    /// the one place in this class where scan cost is multiplied by traffic.
    /// </summary>
    private static readonly SearchValues<char> MatchOperatorProbe = SearchValues.Create(MatchOperators);

    /// <summary>
    /// Escapes a value for interpolation into a Manticore SQL string literal.
    ///
    /// <para>═══ MANTICORE ESCAPES WITH A BACKSLASH. <c>''</c> IS A SYNTAX ERROR. ══════════════</para>
    ///
    /// <para>⚠ This is the most transferable-<i>looking</i> and least transferable piece of
    /// knowledge in this class. Every other SQL engine — and the ANSI standard — escapes an
    /// embedded single quote by <b>doubling</b> it. <b>Manticore does not support that form at
    /// all.</b> It recognises only <c>\'</c>, and answers the doubled form with a parse error:</para>
    ///
    /// <code>
    /// SELECT id FROM idx WHERE customer_id = 'won''t' LIMIT 1
    ///   → {"error":"P01: syntax error, unexpected string, expecting $end near ''t' LIMIT 1'"}
    ///
    /// SELECT id FROM idx WHERE customer_id = 'won\'t' LIMIT 1
    ///   → parses, 0 rows, no error
    /// </code>
    ///
    /// <para><b>This was a live production defect, not a hypothetical.</b> An implementation that
    /// doubled the quote meant a single apostrophe anywhere in the input — "won't", "doesn't",
    /// "O'Brien" — took the whole statement down as an HTTP 500 on the read path, and silently
    /// failed to index the document at all on the write path (a document that never indexes can
    /// never be found, whatever the query). Do not "fix" this method back to the ANSI form.</para>
    ///
    /// <para>═══ WHAT HAPPENS, AND WHY THE ORDER IS SAFE ══════════════════════════════════════</para>
    ///
    /// <list type="bullet">
    ///   <item><description>A single quote becomes <c>\'</c>. This is what closes the injection
    ///   hole: the quote can no longer terminate the literal.</description></item>
    ///   <item><description>A backslash is <b>doubled</b>. Manticore treats a backslash as an
    ///   escape character inside a literal, so a Windows path in a document field would otherwise
    ///   silently eat the character after it, and a trailing backslash would escape the closing
    ///   quote and turn the rest of the statement into string data.</description></item>
    ///   <item><description>Control characters are <b>dropped</b>, with tab/CR/LF folded to a
    ///   space so words do not run together. A raw newline inside a literal is legal but makes
    ///   every log line of a failed statement unreadable.</description></item>
    /// </list>
    ///
    /// <para>The two escapes cannot interfere, because this walks the input <b>one character at a
    /// time</b> and never re-examines what it has already written. The case worth checking by hand
    /// is a backslash immediately followed by an apostrophe <i>as data</i>: the backslash is
    /// emitted as <c>\\</c> and the apostrophe as <c>\'</c>, giving <c>\\\'</c> — which Manticore
    /// reads back as one literal backslash then one literal apostrophe. An implementation that
    /// instead escaped quotes in one pass and backslashes in another would mangle exactly this
    /// input, by escaping its own output.</para>
    ///
    /// <para>On a search path this composes <i>after</i> <see cref="EscapeMatch"/> — see the class
    /// remarks for the two-layer contract. Note that <c>'</c> is deliberately absent from
    /// <see cref="MatchOperators"/>: an apostrophe means nothing to the full-text parser and
    /// everything to the SQL lexer, so it is this layer's job alone.</para>
    /// </summary>
    public static string EscapeLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\'':
                    // BACKSLASH, never the ANSI '' form — Manticore rejects '' with a P01 parse
                    // error. See the remarks; the doubled form was a production outage.
                    sb.Append("\\'");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\t' or '\n' or '\r':
                    sb.Append(' ');
                    break;
                default:
                    if (!char.IsControl(c)) sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Neutralises Manticore's extended full-text syntax in user-supplied search terms.
    ///
    /// <para>Everything in <see cref="MatchOperators"/> is backslash-escaped and runs of
    /// whitespace are collapsed to one space. Without this, a search for <c>"invoice (final)"</c>
    /// is a parse error, <c>"A | B"</c> silently becomes an OR, and a leading <c>-</c> turns a
    /// term into a negation that excludes the very documents the user was looking for. None of
    /// those surface as an error the user can see — they surface as "search is broken".</para>
    ///
    /// <para>If a query builder wraps this method's output in double quotes (a phrase or quorum
    /// expression), that wrapper is only safe because <c>"</c> is in
    /// <see cref="MatchOperators"/>: a user typing <c>CBC" | anything</c> produces <c>CBC\" |
    /// anything</c> here, so the quote they supplied cannot close the builder's and the text after
    /// it stays text. The same applies to <c>/</c>, which would otherwise let a user attach their
    /// own quorum or proximity threshold to that closing quote.</para>
    ///
    /// <para>Apply this <b>before</b> <see cref="EscapeLiteral"/>, never after — see the class
    /// remarks for why the order is load-bearing.</para>
    /// </summary>
    public static string EscapeMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var sb = new StringBuilder(value.Length + 8);
        var pendingSpace = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) pendingSpace = true;
                continue;
            }

            if (char.IsControl(c)) continue;

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            if (MatchOperatorProbe.Contains(c)) sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Guards a configured index name, the one value that is typically interpolated into SQL
    /// without escaping.
    ///
    /// <para>An index name usually comes from configuration and appears in every statement, so a
    /// typo with a quote in it would be an injection vector from the configuration side. Rejecting
    /// it at construction would stop the pod from starting over what is usually a rebuildable
    /// projection — the wrong trade — so a malformed name is loudly logged and the caller's
    /// <paramref name="fallback"/> is used instead, and the service keeps working.</para>
    ///
    /// <para>The <paramref name="fallback"/> is developer-supplied, not configuration, and is
    /// trusted as-is: it must be a bare identifier (ASCII letters, digits, underscore).</para>
    /// </summary>
    /// <param name="configured">The configured index name, possibly malformed.</param>
    /// <param name="fallback">The known-good default to use when <paramref name="configured"/> is not a bare identifier.</param>
    /// <param name="logger">Where to report a rejected name; silent when null.</param>
    public static string SafeIndexName(string? configured, string fallback, ILogger? logger)
    {
        if (!string.IsNullOrWhiteSpace(configured)
            && configured.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return configured;
        }

        logger?.LogError(
            "Configured Manticore index name '{Configured}' is not a bare identifier (letters, digits and '_' only); falling back to '{Fallback}'.",
            configured, fallback);

        return fallback;
    }
}
