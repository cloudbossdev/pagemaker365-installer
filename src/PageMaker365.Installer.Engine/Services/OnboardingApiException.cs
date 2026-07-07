using System.Net;

namespace PageMaker365.Installer.Engine.Services;

public sealed class OnboardingApiException : InvalidOperationException
{
    public OnboardingApiException(
        string message,
        Uri? endpoint = null,
        HttpStatusCode? statusCode = null,
        string correlationId = "",
        Exception? innerException = null)
        : base(message, innerException)
    {
        Endpoint = endpoint;
        StatusCode = statusCode;
        CorrelationId = correlationId;
    }

    public Uri? Endpoint { get; }

    public HttpStatusCode? StatusCode { get; }

    public string CorrelationId { get; }
}
