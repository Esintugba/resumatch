using System.Net;
using System.Net.Http.Json;
using System.Text;
using Api.DTOs.Resume;
using Api.Entities;
using API.Tests.Integration.Api.Infrastructure;
using API.Tests.Integration.Infrastructure;

namespace API.Tests.Integration.Api;

/// <summary>
/// Optimistic concurrency for the contact (PUT and DELETE) and for archiving
/// and unarchiving a resume: If-Match is required for a change, 428 without
/// it, 412 when it is stale; asking for the state a resume is already in
/// changes nothing and needs no precondition.
/// </summary>
public sealed class ResumeConcurrencyEndpointTests(PostgreSqlFixture fixture) : AnalysisApiTestBase(fixture)
{
    private const string ContactJson =
        """{"firstName":"Deneme","lastName":"Kişi","email":"deneme@example.test","city":"İstanbul"}""";

    private async Task<(User User, Resume Resume)> SeedAsync(CancellationToken cancellationToken, int offset = 0)
    {
        var user = CreateUser(10 + offset, $"concurrency{offset}@example.test");
        var resume = CreateResume(20 + offset, user.Id);
        await using var db = CreateDbContext();
        db.AddRange(user, resume);
        await db.SaveChangesAsync(cancellationToken);
        return (user, resume);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? json = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiJson.Options, cancellationToken))!;

    [Fact]
    public async Task Contact_IsCreatedWithoutIfMatch_ThenNeedsTheCurrentETag()
    {
        using var cancellation = CreateCancellation();
        var token = cancellation.Token;
        var (user, resume) = await SeedAsync(token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);
        var path = $"/api/resumes/{resume.Id}/contact";

        using var created = await client.SendAsync(Request(HttpMethod.Put, path, ContactJson), token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = await ReadAsync<ResumeContactResponse>(created, token);
        Assert.False(string.IsNullOrEmpty(first.ETag));
        Assert.Equal(first.ETag, created.Headers.ETag?.Tag);

        using var missing = await client.SendAsync(Request(HttpMethod.Put, path, ContactJson), token);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);

        using var updated = await client.SendAsync(Request(HttpMethod.Put, path, ContactJson, first.ETag), token);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var second = await ReadAsync<ResumeContactResponse>(updated, token);
        Assert.NotEqual(first.ETag, second.ETag);

        using var stale = await client.SendAsync(Request(HttpMethod.Put, path, ContactJson, first.ETag), token);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Contains("precondition_failed", await stale.Content.ReadAsStringAsync(token), StringComparison.Ordinal);

        // The GET and the editor carry the same current ETag.
        using var read = await client.GetAsync(path, token);
        Assert.Equal(second.ETag, (await ReadAsync<ResumeContactResponse>(read, token)).ETag);
        Assert.Equal(second.ETag, read.Headers.ETag?.Tag);
        using var editor = await client.GetAsync($"/api/resumes/{resume.Id}/editor", token);
        Assert.Contains(second.ETag.Replace("\"", "\\\"", StringComparison.Ordinal), await editor.Content.ReadAsStringAsync(token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contact_WithIfMatchButGone_Is412()
    {
        using var cancellation = CreateCancellation();
        var token = cancellation.Token;
        var (user, resume) = await SeedAsync(token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);

        using var response = await client.SendAsync(
            Request(HttpMethod.Put, $"/api/resumes/{resume.Id}/contact", ContactJson, "\"00000001\""),
            token);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task ContactDelete_NeedsTheCurrentETag()
    {
        using var cancellation = CreateCancellation();
        var token = cancellation.Token;
        var (user, resume) = await SeedAsync(token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);
        var path = $"/api/resumes/{resume.Id}/contact";
        using var created = await client.SendAsync(Request(HttpMethod.Put, path, ContactJson), token);
        var first = await ReadAsync<ResumeContactResponse>(created, token);
        using var updated = await client.SendAsync(Request(HttpMethod.Put, path, ContactJson, first.ETag), token);
        var current = await ReadAsync<ResumeContactResponse>(updated, token);

        using var missing = await client.SendAsync(Request(HttpMethod.Delete, path), token);
        using var stale = await client.SendAsync(Request(HttpMethod.Delete, path, ifMatch: first.ETag), token);
        using var deleted = await client.SendAsync(Request(HttpMethod.Delete, path, ifMatch: current.ETag), token);
        using var gone = await client.GetAsync(path, token);

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Contact_OfAnotherUsersResume_Is404()
    {
        using var cancellation = CreateCancellation();
        var token = cancellation.Token;
        var (_, resume) = await SeedAsync(token);
        var (intruder, _) = await SeedAsync(token, offset: 1);
        using var client = AuthenticatedClients.CreateForExistingUser(intruder);
        var path = $"/api/resumes/{resume.Id}/contact";

        using var put = await client.SendAsync(Request(HttpMethod.Put, path, ContactJson), token);
        using var delete = await client.SendAsync(Request(HttpMethod.Delete, path, ifMatch: "\"00000001\""), token);

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task ArchiveAndUnarchive_NeedTheCurrentETag_AndAreIdempotent()
    {
        using var cancellation = CreateCancellation();
        var token = cancellation.Token;
        var (user, resume) = await SeedAsync(token);
        using var client = AuthenticatedClients.CreateForExistingUser(user);
        var archivePath = $"/api/resumes/{resume.Id}/archive";
        var unarchivePath = $"/api/resumes/{resume.Id}/unarchive";
        using var initial = await client.GetAsync($"/api/resumes/{resume.Id}", token);
        var original = await ReadAsync<ResumeResponse>(initial, token);

        using var missing = await client.SendAsync(Request(HttpMethod.Patch, archivePath), token);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);

        using var archived = await client.SendAsync(Request(HttpMethod.Patch, archivePath, ifMatch: original.ETag), token);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        var afterArchive = await ReadAsync<ResumeResponse>(archived, token);
        Assert.True(afterArchive.IsArchived);
        Assert.NotEqual(original.ETag, afterArchive.ETag);
        Assert.Equal(afterArchive.ETag, archived.Headers.ETag?.Tag);

        // Already archived: unchanged, whatever If-Match says (or none at all).
        using var again = await client.SendAsync(Request(HttpMethod.Patch, archivePath, ifMatch: original.ETag), token);
        using var againWithout = await client.SendAsync(Request(HttpMethod.Patch, archivePath), token);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, againWithout.StatusCode);
        Assert.Equal(afterArchive.ETag, (await ReadAsync<ResumeResponse>(again, token)).ETag);

        // Unarchive with a stale ETag is refused; with the current one it works.
        using var stale = await client.SendAsync(Request(HttpMethod.Patch, unarchivePath, ifMatch: original.ETag), token);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var unarchived = await client.SendAsync(Request(HttpMethod.Patch, unarchivePath, ifMatch: afterArchive.ETag), token);
        Assert.Equal(HttpStatusCode.OK, unarchived.StatusCode);
        var afterUnarchive = await ReadAsync<ResumeResponse>(unarchived, token);
        Assert.False(afterUnarchive.IsArchived);
        Assert.NotEqual(afterArchive.ETag, afterUnarchive.ETag);

        using var unarchivedAgain = await client.SendAsync(Request(HttpMethod.Patch, unarchivePath), token);
        Assert.Equal(HttpStatusCode.OK, unarchivedAgain.StatusCode);
        Assert.False((await ReadAsync<ResumeResponse>(unarchivedAgain, token)).IsArchived);
    }

    [Fact]
    public async Task Archive_OfAnotherUsersResume_Is404()
    {
        using var cancellation = CreateCancellation();
        var token = cancellation.Token;
        var (_, resume) = await SeedAsync(token);
        var (intruder, _) = await SeedAsync(token, offset: 1);
        using var client = AuthenticatedClients.CreateForExistingUser(intruder);

        using var archive = await client.SendAsync(
            Request(HttpMethod.Patch, $"/api/resumes/{resume.Id}/archive", ifMatch: "\"00000001\""),
            token);
        using var unarchive = await client.SendAsync(
            Request(HttpMethod.Patch, $"/api/resumes/{resume.Id}/unarchive", ifMatch: "\"00000001\""),
            token);

        Assert.Equal(HttpStatusCode.NotFound, archive.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unarchive.StatusCode);
    }
}
