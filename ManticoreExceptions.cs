namespace CLOOPS.microservices;

/// <summary>
/// Base type for failures raised by <see cref="BaseManticoreService"/>.
///
/// <para>The two derived types encode the <b>only</b> decision a caller has to make: retry or
/// don't. <see cref="ManticoreTransientException"/> means the statement may well succeed if sent
/// again; <see cref="ManticoreContractException"/> means the endpoint is not behaving like
/// Manticore at all, and no amount of retrying changes that. Catch the derived types, not this
/// one, unless the caller genuinely treats both the same way.</para>
/// </summary>
public abstract class ManticoreException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    protected ManticoreException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying cause.</summary>
    protected ManticoreException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// A failure where retrying is both safe and likely to work: the daemon was unreachable, the
/// request timed out, the response was non-2xx, or the statement itself was rejected (which
/// Manticore reports as <b>HTTP 200 with an <c>error</c> member</b> — see
/// <see cref="BaseManticoreService"/>).
///
/// <para>A rejected statement is classified transient rather than terminal because a Manticore
/// index is typically a <b>rebuildable projection</b> of a system of record: a nak-and-retry (or a
/// reconcile pass) repairs the drift, whereas failing terminally turns a momentary hiccup into
/// permanent data loss in the index. Callers on a platform with its own transient/permanent
/// taxonomy should translate this type into their transient one.</para>
/// </summary>
public sealed class ManticoreTransientException : ManticoreException
{
    /// <summary>Creates the exception with a message.</summary>
    public ManticoreTransientException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying cause.</summary>
    public ManticoreTransientException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The response was not the documented Manticore shape at all — a body that is not JSON, or JSON
/// whose root is neither the result object nor an array of result objects.
///
/// <para>That means the configured URL is pointing at something other than the Manticore HTTP SQL
/// endpoint (a proxy error page, a different service, the wrong port), and retrying cannot help.
/// Callers should surface this as an internal/configuration error, loudly.</para>
/// </summary>
public sealed class ManticoreContractException : ManticoreException
{
    /// <summary>Creates the exception with a message.</summary>
    public ManticoreContractException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying cause.</summary>
    public ManticoreContractException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
