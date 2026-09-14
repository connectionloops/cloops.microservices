using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CLOOPS.microservices;

/// <summary>
/// Base class for services backed by a Manticore index, spoken to over the <b>HTTP JSON API on
/// port 9308</b> (<c>POST /sql?mode=raw</c>). Derive from it, build your statements with
/// <see cref="ManticoreSql"/>, and run them through <see cref="ExecuteAsync"/> — see
/// <c>docs/manticore.md</c> for the full guide.
///
/// <para>═══ WHY HTTP AND NOT THE MYSQL PROTOCOL (SETTLED — DO NOT RELITIGATE) ═════════════════</para>
///
/// <para>Manticore also listens on 9306 speaking the MySQL wire protocol. Pulling
/// <c>MySqlConnector</c> in buys a connection pool, a second SQL dialect in the dependency graph
/// and a whole class of "connection reset" failures, in exchange for saving a form-encode.
/// <c>POST /sql?mode=raw</c> is the same SQL over a stateless request that
/// <see cref="BaseHttpService"/> already knows how to pool. This trade was evaluated and decided;
/// the HTTP endpoint is the supported path.</para>
///
/// <para>═══ THE TRAP: HTTP 200 IS NOT SUCCESS ═════════════════════════════════════════════════</para>
///
/// <para>Manticore answers a rejected statement with <b>HTTP 200</b> and a non-empty <c>error</c>
/// member in the JSON body. Code that only checks the status code therefore reports every syntax
/// error, missing index and type mismatch as a successful write of zero rows — silently, for as
/// long as it takes someone to notice that search returns nothing. <see cref="ExecuteAsync"/>
/// checks the body, always, and that check is the reason every statement a derived class runs
/// must go through it.</para>
///
/// <para>═══ ESCAPING IS THE SECURITY BOUNDARY ═════════════════════════════════════════════════</para>
///
/// <para>There are no bound parameters over this API: every value is interpolated into a SQL
/// string. Use <see cref="ManticoreSql.EscapeLiteral"/> on every literal and
/// <see cref="ManticoreSql.EscapeMatch"/> on every piece of user text bound for a
/// <c>MATCH()</c> expression, in that order and no other — the ordering contract is documented on
/// <see cref="ManticoreSql"/> and it is load-bearing.</para>
///
/// <para>═══ FAILURE CLASSIFICATION ════════════════════════════════════════════════════════════</para>
///
/// <para>Transport failures, timeouts, non-2xx responses and rejected statements are
/// <see cref="ManticoreTransientException"/> — retrying is safe and likely to work, and a
/// Manticore index is typically a rebuildable projection, so a nak-and-retry repairs drift. Only
/// a response body that is not the documented shape is a <see cref="ManticoreContractException"/>:
/// that means the endpoint is not the Manticore we think it is, and no amount of retrying changes
/// it. Translate these into your platform's own transient/internal taxonomy at the service
/// boundary.</para>
/// </summary>
public abstract class BaseManticoreService : BaseHttpService
{
    private readonly string _manticoreUrl;
    private readonly Uri _baseAddress;

    /// <summary>
    /// Creates the service against one Manticore HTTP endpoint.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create logical HTTP clients per statement.</param>
    /// <param name="manticoreUrl">
    /// The endpoint URL, e.g. <c>http://manticore.manticore.svc.cluster.local:9308</c>. Parsed
    /// once here — a malformed value throws at construction rather than on every request. That is
    /// deliberate, and deliberately different from <see cref="ManticoreSql.SafeIndexName"/>, which
    /// falls back: an index name has a universally safe fallback, but a URL does not —
    /// substituting a default for a mistyped address would quietly talk to the wrong Manticore,
    /// which is worse than a crash loop with the offending value in the message.
    /// </param>
    /// <param name="logger">Optional logger for derived services.</param>
    protected BaseManticoreService(
        IHttpClientFactory httpClientFactory,
        string manticoreUrl,
        ILogger? logger = null)
        : base(httpClientFactory, logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manticoreUrl);

        _manticoreUrl = manticoreUrl;
        _baseAddress = BuildBaseAddress(manticoreUrl);
    }

    /// <summary>
    /// Per-statement timeout. <b>Default 15 seconds, and think twice before raising it.</b>
    ///
    /// <para>Not the <see cref="HttpClient"/> default of 100: Manticore statements typically run
    /// inside a request/reply whose caller (a NATS request, an HTTP request thread) has long since
    /// given up by then, and a hung statement would pin that caller — e.g. a NATS consumer slot —
    /// for the whole wait. Failing fast and letting a reconcile pass repair any drift beats
    /// holding a request thread for a minute and a half. This timeout is the only guard against a
    /// hung statement doing exactly that, so raising it should be a deliberate, documented act,
    /// not a tuning reflex.</para>
    /// </summary>
    protected virtual TimeSpan Timeout => TimeSpan.FromSeconds(15);

    /// <summary>
    /// Base address and timeout for every outbound statement. <see cref="BaseHttpService.CreateClient"/>
    /// calls this on <b>every</b> statement, which is why the address is computed once at
    /// construction and only assigned here. Sealed so a derived class cannot accidentally undo the
    /// base-address handling; customise via <see cref="Timeout"/> or the constructor URL instead.
    /// </summary>
    protected sealed override void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = _baseAddress;
        client.Timeout = Timeout;
    }

    /// <summary>
    /// Normalises and parses the endpoint URL, once.
    ///
    /// <para>The trailing slash is load-bearing: <c>new Uri(baseAddress, "sql?mode=raw")</c>
    /// discards the last path segment of a base address that does not end in one, so a URL
    /// carrying a path prefix (behind an ingress, say) would silently lose it.</para>
    /// </summary>
    private static Uri BuildBaseAddress(string configured)
    {
        var url = configured.TrimEnd();
        if (!url.EndsWith('/')) url += "/";

        var parsed = new Uri(url, UriKind.Absolute);

        // On Unix a bare path like "/manticore" parses as an absolute file:// URI, so UriKind
        // alone does not catch a mangled value; the endpoint is only ever http(s).
        if (parsed.Scheme is not ("http" or "https"))
            throw new UriFormatException(
                $"Manticore URL '{configured}' must be an absolute http or https URL.");

        return parsed;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Transport
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs one statement against <c>POST /sql?mode=raw</c> and returns the first result object.
    ///
    /// <para><c>mode=raw</c> is what makes the endpoint accept arbitrary SQL rather than only
    /// SELECTs, and the body must be form-encoded as <c>query=&lt;sql&gt;</c> — a JSON body is
    /// accepted by a different endpoint with a different dialect, and mixing them up produces a
    /// confident 200 with an error inside.</para>
    ///
    /// <para>Statements travel URL-encoded in a form body, so keep them bounded: batch
    /// <c>VALUES</c> tuples and <c>IN (…)</c> lists to a few hundred entries per statement rather
    /// than materialising one multi-megabyte line some proxy in the path will truncate at a size
    /// nobody documented.</para>
    /// </summary>
    /// <exception cref="ManticoreTransientException">
    /// The daemon was unreachable, the request timed out, the response was non-2xx, or the
    /// statement was rejected (HTTP 200 with an <c>error</c> member).
    /// </exception>
    /// <exception cref="ManticoreContractException">
    /// The response body was not the documented Manticore shape; the endpoint is misconfigured.
    /// </exception>
    protected async Task<JsonElement> ExecuteAsync(string sql, CancellationToken ct)
    {
        var client = CreateClient();

        using var content = new StringContent(
            "query=" + Uri.EscapeDataString(sql),
            Encoding.UTF8,
            "application/x-www-form-urlencoded");

        string body;

        try
        {
            using var response = await client.PostAsync("sql?mode=raw", content, ct).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new ManticoreTransientException(
                    $"Manticore returned HTTP {(int)response.StatusCode} for a {StatementKind(sql)} statement: {Truncate(body)}");
        }
        catch (HttpRequestException ex)
        {
            throw new ManticoreTransientException($"Manticore is unreachable at '{_manticoreUrl}'.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Cancellation the CALLER asked for propagates; a TaskCanceledException with an
            // un-cancelled token is the HttpClient timeout wearing a confusing exception type.
            throw new ManticoreTransientException($"Manticore timed out at '{_manticoreUrl}'.", ex);
        }

        return ParseResult(body, sql);
    }

    /// <summary>
    /// Parses a Manticore response body into its single result object.
    ///
    /// <para>⚠ Two shapes are in the wild and both have to be handled: an <b>array</b> of result
    /// objects (what recent builds return for <c>mode=raw</c>) and a <b>bare object</b> (what
    /// older builds return, and what some statements still return). Assuming either one alone
    /// produces a service that works in dev and fails on the cluster, or vice versa.</para>
    ///
    /// <para>And the part that is easy to miss: <b>a non-empty <c>error</c> member is a failure
    /// even though the status was 200.</b></para>
    /// </summary>
    private static JsonElement ParseResult(string body, string sql)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (JsonException ex)
        {
            throw new ManticoreContractException(
                $"Manticore returned a body that is not JSON for a {StatementKind(sql)} statement: {Truncate(body)}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            var result = root.ValueKind switch
            {
                JsonValueKind.Array => root.GetArrayLength() > 0 ? root[0] : default,
                JsonValueKind.Object => root,
                _ => default,
            };

            if (result.ValueKind != JsonValueKind.Object)
                throw new ManticoreContractException(
                    $"Manticore returned an unexpected body shape ({root.ValueKind}) for a {StatementKind(sql)} statement: {Truncate(body)}");

            if (result.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(error.GetString()))
            {
                // HTTP 200 with an error member. A Manticore index is typically a rebuildable
                // projection ⇒ transient, so a nak and a retry is the right platform response.
                throw new ManticoreTransientException(
                    $"Manticore rejected a {StatementKind(sql)} statement: {error.GetString()}");
            }

            // The document is disposed on the way out of this scope; Clone lifts the element off it.
            return result.Clone();
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Result reading
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The object rows of a result's <c>data</c> array. A missing or non-array <c>data</c> member
    /// yields nothing — the normal shape for a write statement, not an error — and non-object
    /// entries are skipped rather than trusted.
    /// </summary>
    protected static IEnumerable<JsonElement> DataRows(JsonElement result)
    {
        if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var row in data.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object) yield return row;
        }
    }

    /// <summary>
    /// Reads a string cell defensively: Manticore returns string attributes as JSON strings, but a
    /// column that was never written comes back as <c>null</c>, and some builds stringify numbers
    /// (so a numeric cell is also accepted).
    /// </summary>
    protected static string? ReadString(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var v)) return null;

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null,
        };
    }

    /// <summary>
    /// Reads an integer cell defensively — number or stringified number, missing or malformed
    /// reads as <c>0</c>. See <see cref="ReadString"/> for why the leniency.
    /// </summary>
    protected static long ReadInt64(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var v)) return 0;

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => 0,
        };
    }

    /// <summary>The leading keyword, for log lines that must never carry document content.</summary>
    private static string StatementKind(string sql)
    {
        var space = sql.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? sql[..space].ToUpperInvariant() : "SQL";
    }

    private static string Truncate(string? value)
        => string.IsNullOrEmpty(value) ? "(empty)"
            : value.Length <= 512 ? value
            : value[..512] + "…";
}
