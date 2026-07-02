namespace CLOOPS.microservices;

/// <summary>
/// Defines whether a REST endpoint requires the SDK REST shared secret.
/// </summary>
public enum RestAuth
{
    /// <summary>
    /// The endpoint is callable without REST authentication.
    /// </summary>
    Public,

    /// <summary>
    /// The endpoint requires REST authentication.
    /// </summary>
    Required
}

/// <summary>
/// Supported lightweight REST endpoint HTTP methods.
/// </summary>
public enum RestHttpMethod
{
    /// <summary>
    /// HTTP DELETE.
    /// </summary>
    Delete,

    /// <summary>
    /// HTTP GET.
    /// </summary>
    Get,

    /// <summary>
    /// HTTP HEAD.
    /// </summary>
    Head,

    /// <summary>
    /// HTTP OPTIONS.
    /// </summary>
    Options,

    /// <summary>
    /// HTTP PATCH.
    /// </summary>
    Patch,

    /// <summary>
    /// HTTP POST.
    /// </summary>
    Post,

    /// <summary>
    /// HTTP PUT.
    /// </summary>
    Put
}

/// <summary>
/// Marks a method as a lightweight REST endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RestEndpointAttribute : Attribute
{
    /// <summary>
    /// Creates a REST endpoint mapping.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute endpoint path, for example /healthz.</param>
    /// <param name="auth">The endpoint authentication mode.</param>
    public RestEndpointAttribute(RestHttpMethod method, string path, RestAuth auth)
    {
        Method = method;
        Path = path;
        Auth = auth;
    }

    /// <summary>
    /// Gets the HTTP method.
    /// </summary>
    public RestHttpMethod Method { get; }

    /// <summary>
    /// Gets the endpoint path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the endpoint authentication mode.
    /// </summary>
    public RestAuth Auth { get; }
}
