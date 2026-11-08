using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace AuthSandbox.Tests;

/// <summary>Spins up the API in memory with a controllable clock (2026-11-08 09:00 UTC).</summary>
public sealed class TestApi : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;
    public FakeTimeProvider Clock { get; } = new(Start);
    public HttpClient Client { get; }

    public TestApi(params (string key, string value)[] settings)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DemoAuth:Mode", "None");   // flow tests run open; auth tests override this
            foreach (var (k, v) in settings) b.UseSetting(k, v);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(Clock);
            });
        });
        Client = _factory.CreateClient();
    }

    public async Task<JsonNode> Send(HttpMethod method, string url, object? body, HttpStatusCode expected)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is HttpContent content) req.Content = content;
        else if (body is not null) req.Content = JsonContent.Create(body);
        using var res = await Client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"{method} {url}: expected {(int)expected}, got {(int)res.StatusCode}: {text}");
        return string.IsNullOrEmpty(text) || !res.Content.Headers.ContentType!.MediaType!.Contains("json") ? new JsonObject() : JsonNode.Parse(text)!;
    }

    public Task<JsonNode> Post(string url, object? body, HttpStatusCode expected = HttpStatusCode.OK) => Send(HttpMethod.Post, url, body, expected);
    public Task<JsonNode> Get(string url, HttpStatusCode expected = HttpStatusCode.OK) => Send(HttpMethod.Get, url, null, expected);

    public static void AssertCode(JsonNode problem, string code) => Assert.Equal(code, (string?)problem["code"]);

    public static readonly byte[] SamplePdf = "%PDF-1.4\n% synthetic test document\n%%EOF"u8.ToArray();
    public static readonly byte[] SamplePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    public static MultipartFormDataContent Form(Dictionary<string, string> fields, string fileField, byte[] file, string contentType, string fileName)
    {
        var form = new MultipartFormDataContent();
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        var fileContent = new ByteArrayContent(file);
        fileContent.Headers.ContentType = new(contentType);
        form.Add(fileContent, fileField, fileName);
        return form;
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }
}
