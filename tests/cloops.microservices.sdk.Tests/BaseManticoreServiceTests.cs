using System.Net;
using System.Text.Json;
using CLOOPS.microservices;
using Xunit;

namespace cloops.microservices.sdk.Tests;

/// <summary>
/// Transport tests for <see cref="BaseManticoreService"/>, driven through a stub
/// <see cref="HttpMessageHandler"/> so no live Manticore is needed. The pins cover the parts that
/// are invisible from a happy-path integration test: the exact wire shape of a statement, the
/// HTTP-200-with-<c>error</c>-body trap, both documented response shapes, and the
/// transient-vs-contract exception classification.
/// </summary>
public class BaseManticoreServiceTests
{
    private const string EmptyOkBody = """[{"total":0,"error":"","warning":"","data":[]}]""";

    // ── wire shape ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_PostsTheStatementFormEncoded_ToSqlModeRaw()
    {
        var handler = new StubHandler(EmptyOkBody);
        var service = Service(handler, "http://manticore:9308");

        await service.Run("SELECT id FROM idx WHERE title = 'won\\'t'", default);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("http://manticore:9308/sql?mode=raw", handler.Request.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.RequestContentType);

        // The statement travels form-encoded as query=<sql>, byte-for-byte recoverable.
        Assert.StartsWith("query=", handler.RequestBody, StringComparison.Ordinal);
        Assert.Equal(
            "SELECT id FROM idx WHERE title = 'won\\'t'",
            Uri.UnescapeDataString(handler.RequestBody!["query=".Length..].Replace('+', ' ')));
    }

    /// <summary>
    /// The trailing slash is load-bearing: <c>new Uri(base, "sql?mode=raw")</c> discards the last
    /// path segment of a base address that does not end in one, so a URL carrying a path prefix
    /// (behind an ingress) must keep that prefix on the wire.
    /// </summary>
    [Theory]
    [InlineData("http://gateway:9308/manticore")]
    [InlineData("http://gateway:9308/manticore/")]
    [InlineData("http://gateway:9308/manticore   ")]
    public async Task Execute_PreservesAPathPrefix_WhateverTheConfiguredTrailing(string url)
    {
        var handler = new StubHandler(EmptyOkBody);
        var service = Service(handler, url);

        await service.Run("SELECT 1", default);

        Assert.Equal("http://gateway:9308/manticore/sql?mode=raw", handler.Request!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/only")]
    public void AMalformedUrl_ThrowsAtConstruction_NotOnTheFirstStatement(string url)
        => Assert.ThrowsAny<UriFormatException>(() => Service(new StubHandler(EmptyOkBody), url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankUrl_ThrowsAtConstruction(string? url)
        => Assert.ThrowsAny<ArgumentException>(() => Service(new StubHandler(EmptyOkBody), url!));

    // ── response shapes ───────────────────────────────────────────────────────────────────────

    /// <summary>The modern shape: an array of result objects; the first is the result.</summary>
    [Fact]
    public async Task Execute_ReturnsTheFirstResultObject_FromTheArrayShape()
    {
        var service = Service(
            new StubHandler("""[{"total":1,"error":"","warning":"","data":[{"id":42}]}]"""),
            "http://manticore:9308");

        var result = await service.Run("SELECT id FROM idx", default);

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal(42, result.GetProperty("data")[0].GetProperty("id").GetInt64());
    }

    /// <summary>The legacy shape: a bare result object, still returned by some statements.</summary>
    [Fact]
    public async Task Execute_AcceptsTheBareObjectShape()
    {
        var service = Service(
            new StubHandler("""{"total":1,"error":"","warning":"","data":[{"id":7}]}"""),
            "http://manticore:9308");

        var result = await service.Run("SELECT id FROM idx", default);

        Assert.Equal(7, result.GetProperty("data")[0].GetProperty("id").GetInt64());
    }

    /// <summary>
    /// Some statements answer with an empty (or whitespace) body. That is an empty result, not a
    /// failure — <c>ParseResult</c> substitutes <c>{}</c> rather than choking on it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyBody_IsAnEmptyResult_NotAFailure(string body)
    {
        var service = Service(new StubHandler(body), "http://manticore:9308");

        var result = await service.Run("DELETE FROM idx WHERE id = 1", default);

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
    }

    /// <summary>
    /// The <c>error</c> member is routinely <i>present and empty</i> on success (<c>"error":""</c>),
    /// and absent entirely in some result shapes. Only a non-empty value is a failure — treating
    /// mere presence as one would fail every statement.
    /// </summary>
    [Theory]
    [InlineData("""[{"total":0,"error":"","warning":"","data":[]}]""")]
    [InlineData("""{"total":0,"data":[]}""")]
    public async Task AnEmptyOrAbsentErrorMember_IsSuccess(string body)
    {
        var service = Service(new StubHandler(body), "http://manticore:9308");

        var result = await service.Run("SELECT 1", default);

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
    }

    // ── failure classification ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ The trap this class exists to guard: Manticore reports a rejected statement as
    /// <b>HTTP 200</b> with a non-empty <c>error</c> member. Status-code-only checking would
    /// report every syntax error as a successful write of zero rows.
    /// </summary>
    [Fact]
    public async Task AnHttp200WithAnErrorMember_IsATransientFailure_NotSuccess()
    {
        var service = Service(
            new StubHandler("""[{"total":0,"error":"P01: syntax error near 'boom'","warning":"","data":[]}]"""),
            "http://manticore:9308");

        var ex = await Assert.ThrowsAsync<ManticoreTransientException>(
            () => service.Run("SELECT broken", default));

        Assert.Contains("P01: syntax error", ex.Message, StringComparison.Ordinal);
        Assert.Contains("SELECT", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANon2xxResponse_IsTransient()
    {
        var service = Service(
            new StubHandler("daemon says no", HttpStatusCode.InternalServerError),
            "http://manticore:9308");

        var ex = await Assert.ThrowsAsync<ManticoreTransientException>(
            () => service.Run("DELETE FROM idx WHERE id = 1", default));

        Assert.Contains("500", ex.Message, StringComparison.Ordinal);
        Assert.Contains("DELETE", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableDaemon_IsTransient_AndNamesTheConfiguredUrl()
    {
        var service = Service(
            new StubHandler(new HttpRequestException("connection refused")),
            "http://manticore:9308");

        var ex = await Assert.ThrowsAsync<ManticoreTransientException>(
            () => service.Run("SELECT 1", default));

        Assert.Contains("http://manticore:9308", ex.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    /// <summary>
    /// The HttpClient timeout surfaces as TaskCanceledException with an un-cancelled caller token;
    /// that is a transient failure. Cancellation the caller actually asked for must propagate
    /// untranslated instead.
    /// </summary>
    [Fact]
    public async Task ATimeout_IsTransient_ButCallerCancellationPropagates()
    {
        var timedOut = Service(
            new StubHandler(new TaskCanceledException("timed out")),
            "http://manticore:9308");
        await Assert.ThrowsAsync<ManticoreTransientException>(() => timedOut.Run("SELECT 1", default));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = Service(
            new StubHandler(new TaskCanceledException("caller cancelled")),
            "http://manticore:9308");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.Run("SELECT 1", cts.Token));
    }

    /// <summary>
    /// A body that is not JSON, or JSON of the wrong root shape, means the URL points at something
    /// that is not Manticore's SQL endpoint — retrying cannot help, so it is a contract failure.
    /// </summary>
    [Theory]
    [InlineData("<html>502 Bad Gateway (but returned as 200 by a helpful proxy)</html>")]
    [InlineData("\"just a string\"")]
    [InlineData("[]")]
    [InlineData("[42]")]
    public async Task ABodyThatIsNotAManticoreResult_IsAContractFailure(string body)
    {
        var service = Service(new StubHandler(body), "http://manticore:9308");

        await Assert.ThrowsAsync<ManticoreContractException>(() => service.Run("SELECT 1", default));
    }

    // ── the protected result readers ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DataRows_SkipsNonObjectEntries_AndReadersAreLenientAboutJsonTypes()
    {
        var service = Service(
            new StubHandler("""[{"error":"","data":[{"id":1,"name":"a"},"noise",{"id":"2","name":7},null]}]"""),
            "http://manticore:9308");

        var rows = await service.RunAndReadRows("SELECT * FROM idx", default);

        // "noise" and null are skipped; string ids and numeric names are read leniently.
        Assert.Equal([(1, "a"), (2, "7")], rows);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────

    private static TestManticoreService Service(StubHandler handler, string url)
        => new(new StubHttpClientFactory(handler), url);

    private sealed class TestManticoreService(IHttpClientFactory factory, string url)
        : BaseManticoreService(factory, url)
    {
        public Task<JsonElement> Run(string sql, CancellationToken ct) => ExecuteAsync(sql, ct);

        public async Task<IReadOnlyList<(long Id, string? Name)>> RunAndReadRows(string sql, CancellationToken ct)
        {
            var result = await ExecuteAsync(sql, ct);
            return DataRows(result).Select(row => (ReadInt64(row, "id"), ReadString(row, "name"))).ToList();
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string? _body;
        private readonly HttpStatusCode _status;
        private readonly Exception? _throws;

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public string? RequestContentType { get; private set; }

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public StubHandler(Exception throws) => _throws = throws;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestContentType = request.Content?.Headers.ContentType?.MediaType;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(CancellationToken.None);

            if (_throws is not null) throw _throws;

            return new HttpResponseMessage(_status) { Content = new StringContent(_body!) };
        }
    }
}
