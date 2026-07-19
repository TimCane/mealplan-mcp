using System.Net;

namespace Mealplan.Tests.Fakes;

/// <summary>
/// Answers requests from a queue of canned responses so a crawler can be driven
/// through a source failing partway. Real politeness and retry handlers are left
/// out - what is under test is how the crawler reacts once they have given up.
/// </summary>
public class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<string, HttpResponseMessage>> _responses = new();

    public List<string> Requests { get; } = [];

    /// <summary>Used once every canned response has been taken.</summary>
    public Func<string, HttpResponseMessage> Fallback { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public FakeHttpHandler Respond(string body)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        });

        return this;
    }

    public FakeHttpHandler RespondWith(HttpStatusCode status)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;
        Requests.Add(path);

        var next = _responses.Count > 0 ? _responses.Dequeue() : Fallback;

        return Task.FromResult(next(path));
    }
}

/// <summary>Hands every named client the same handler.</summary>
public class FakeHttpClientFactory(HttpMessageHandler handler, string baseAddress)
    : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
}
