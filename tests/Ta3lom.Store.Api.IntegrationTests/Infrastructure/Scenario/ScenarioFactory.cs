using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.DTOs.Dev;
using Ta3lom.Store.Application.Features.Dev.Commands.ConfirmPayment;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.IRepositories;
using Ta3lom.Store.Infrastructure.Services.Outbox;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;

/// T-1 (Run-plan-review §3): a host with its own migrated and seeded database (reference catalogue 002 + SeedRunner), so
/// a test class can place orders, settle and allocate without touching other classes. Deterministic pieces:
/// <list type="bullet">
/// <item>`Clock` (FakeClock, starts at the real time so signed tokens stay valid) replaces the system clock;</item>
/// <item>SMS and e-mail go to the sinks (`Sms`, `MailAsync`); the e-mail sink writes to a per-factory folder;</item>
/// <item>the outbox loop and Quartz are off: `DrainOutboxAsync` and `RunAsync` run them on demand;</item>
/// <item>tokens are minted with the app's own issuer (`CustomerAsync`, `StaffAsync`), settlements go through the same
/// command as the dev trigger (`SettleAsync`).</item>
/// </list>
public class ScenarioFactory : StoreApiFactory
{
    public const string WebOrigin = "https://ta3lom.sa";

    private static int _mobileCounter;

    private readonly Lazy<string> _database;
    private readonly string _mailFolder = Path.Combine(Path.GetTempPath(), $"ta3lom-store-mail-{Guid.NewGuid():N}");

    public ScenarioFactory()
    {
        _database = new(() =>
        {
            var cs = CreateDatabaseAsync().GetAwaiter().GetResult();
            Clock.Set(DateTimeOffset.UtcNow.AddSeconds(1));   // after the seed: campaigns seeded with starts_at = now() have started
            return cs;
        });
    }

    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    public string DatabaseConnectionString => _database.Value;

    protected override string ConnectionString => _database.Value;

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Store:Sms:Provider"] = "sink",
        ["Store:Email:Mode"] = "sink",
        ["Store:Email:SinkFolder"] = _mailFolder,
        ["Store:Files:Root"] = Path.Combine(_mailFolder, "files"),
        ["Store:Outbox:Enabled"] = "false",
        // Every client of a test class shares one address; the limits themselves are covered by RateLimitTests and S-04.
        ["Store:RateLimits:Public:TokenLimit"] = "100000",
        ["Store:RateLimits:Public:TokensPerPeriod"] = "100000",
        ["Store:RateLimits:OtpRequest:PermitLimit"] = "100000",
        ["Store:RateLimits:OtpVerify:PermitLimit"] = "100000",
        ["Store:RateLimits:AuthRefresh:PermitLimit"] = "100000",
        ["Store:RateLimits:StaffLogin:PermitLimit"] = "100000",
        ["Store:RateLimits:Cart:PermitLimit"] = "100000",
        ["Store:RateLimits:Checkout:PermitLimit"] = "100000",
        ["Store:RateLimits:Me:PermitLimit"] = "100000",
        ["Store:RateLimits:Download:PermitLimit"] = "100000",
        ["Store:RateLimits:Admin:PermitLimit"] = "100000",
    };

    /// A client for the browser flows with cookies (`t3s_cart`, `t3s_rt` are Secure, so the base address is https).
    public HttpClient Browser(string? remoteIp = null)
    {
        var client = CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add(RemoteIpStartupFilter.Header, remoteIp ?? $"198.51.100.{Random.Shared.Next(1, 250)}");
        return client;
    }

    /// OTP sign-in through the API (code read from the SMS sink); returns the verify body (token, customer, cart notices).
    public async Task<JsonElement> SignInAsync(HttpClient browser, string mobile)
    {
        var ct = TestContext.Current.CancellationToken;
        (await browser.PostAsJsonAsync("/v1/auth/otp/request", new { mobile }, ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);
        var code = System.Text.RegularExpressions.Regex.Match(SmsTo(mobile)[0].Body, @"\d{6}").Value;
        var verified = await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code }, ct);
        verified.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await verified.Content.ReadAsStringAsync(ct));
        var body = JsonDocument.Parse(await verified.Content.ReadAsStringAsync(ct)).RootElement.Clone();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return body;
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(Clock);
        foreach (var quartz in services.Where(d => d.ServiceType == typeof(IHostedService)
                                                   && (d.ImplementationType?.FullName?.StartsWith("Quartz", StringComparison.Ordinal) ?? false)).ToList())
        {
            services.Remove(quartz);
        }
    }

    /// A fresh Saudi mobile per call (05 + 8 digits), unique across the test run.
    public static string NewMobile() => "059" + Interlocked.Increment(ref _mobileCounter).ToString("D7", System.Globalization.CultureInfo.InvariantCulture);

    public static string E164(string mobile) => "+966" + mobile[1..];

    /// A valid one-page PDF (for book files).
    public static byte[] MinimalPdf()
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << >> >>",
            "<< /Length 5 >>\nstream\nBT ET\nendstream",
        ];
        var sb = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = sb.Length;
        sb.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (var o in offsets)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"{o:D10} 00000 n \n");
        }

        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }

    public IDevSmsInbox Sms => Services.GetRequiredService<IDevSmsInbox>();

    public IReadOnlyList<DevSmsMessage> SmsTo(string mobile) => [.. Sms.Latest(200).Where(m => m.To == E164(mobile))];

    public Task<IReadOnlyList<DevMailMessage>> MailAsync(int count = 50) =>
        Services.GetRequiredService<IDevMailInbox>().LatestAsync(count, TestContext.Current.CancellationToken);

    /// Runs the outbox until no message is due (a failing handler is retried later, like production). Messages written while
    /// the FakeClock was ahead carry a future `next_attempt_at`; never-attempted ones are made due first.
    public async Task DrainOutboxAsync()
    {
        await ExecuteSqlAsync("UPDATE store.outbox_message SET next_attempt_at = now() WHERE processed_at IS NULL AND attempts = 0 AND next_attempt_at > now()");
        var dispatcher = Services.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();
        for (var i = 0; i < 50 && await dispatcher.RunOnceAsync(TestContext.Current.CancellationToken) > 0; i++)
        {
        }
    }

    /// Runs one Application command in its own scope (what a Quartz job or the dev tooling does).
    public async Task<Result<TResult>> RunAsync<TCommand, TResult>(TCommand command)
        where TCommand : ICommand<TResult>
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>().HandleAsync(command, TestContext.Current.CancellationToken);
    }

    /// The dev trigger (doc 15 §2.4) without HTTP: a verified capture for the order's grand total or a given amount.
    public async Task<DevSettlementResponse> SettleAsync(string reference, string? amount = null, string? confirmationId = null)
    {
        var result = await RunAsync<ConfirmPaymentCommand, DevSettlementResponse>(new ConfirmPaymentCommand(reference, amount, confirmationId));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    /// A signed-in customer (row created as the first OTP login would) and a client carrying its bearer.
    public async Task<ActorClient> CustomerAsync(string? mobile = null)
    {
        mobile ??= NewMobile();
        await using var scope = Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var customer = Customer.RegisterOnFirstLogin(E164(mobile), Clock.UtcNow);
        sp.GetRequiredService<IGenericRepository<Customer>>().Add(customer);
        await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        var token = sp.GetRequiredService<IAccessTokenIssuer>().IssueCustomer(customer.Id);
        return new ActorClient(Authorized(token.Token), customer.Id, mobile, null);
    }

    /// A staff member with the given roles (created directly; TOTP is covered by the S-05 tests) and a bearer client.
    public async Task<ActorClient> StaffAsync(params string[] roles)
    {
        await using var scope = Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var email = $"{string.Join('-', roles).Replace('_', '-')}-{Guid.NewGuid():N}@test.ta3lom.sa";
        var staff = StaffUser.Create(email, "Test " + string.Join(", ", roles), "not-a-hash", roles, Clock.UtcNow);
        sp.GetRequiredService<IGenericRepository<StaffUser>>().Add(staff);
        await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestContext.Current.CancellationToken);
        var token = sp.GetRequiredService<IAccessTokenIssuer>().IssueStaff(staff.Id, email, roles);
        return new ActorClient(Authorized(token.Token), staff.Id, null, email);
    }

    public HttpClient Anonymous() => CreateClient();

    public Task<T> ScalarAsync<T>(string sql) => SqlRunner.ScalarAsync<T>(DatabaseConnectionString, sql);

    public Task ExecuteSqlAsync(string sql) => SqlRunner.ExecuteAsync(DatabaseConnectionString, sql);

    /// db/reference/003: the integrity checks; returns "number: rows" for every check that found rows.
    public async Task<IReadOnlyList<string>> IntegrityFailuresAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var checks = await scope.ServiceProvider.GetRequiredService<IOperationsStatus>().RunIntegrityChecksAsync(TestContext.Current.CancellationToken);
        return [.. checks.Where(c => c.Rows > 0).Select(c => $"{c.Number} {c.Title}: {c.Rows}")];
    }

    private HttpClient Authorized(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> CreateDatabaseAsync()
    {
        var cs = await StoreSchema.CreateMigratedAsync("scn");
        await SqlRunner.ExecuteAsync(cs, SqlRunner.ReferenceFile("002_store_seed.sql"));
        await StoreSchema.SeedAsync(cs);
        return cs;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_mailFolder))
        {
            Directory.Delete(_mailFolder, recursive: true);
        }
    }
}

/// An HTTP client acting as one customer or staff member.
public sealed record ActorClient(HttpClient Http, Guid Id, string? Mobile, string? Email)
{
    public async Task<(HttpResponseMessage Response, JsonElement Body)> SendAsync(HttpMethod method, string path, object? body = null,
        IDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        foreach (var (k, v) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(k, v);
        }

        var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = text.Length > 0 && (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) ?? false)
            ? JsonDocument.Parse(text).RootElement.Clone()
            : default;
        return (response, json);
    }

    public Task<(HttpResponseMessage Response, JsonElement Body)> GetAsync(string path) => SendAsync(HttpMethod.Get, path);

    public Task<(HttpResponseMessage Response, JsonElement Body)> PostAsync(string path, object? body = null, IDictionary<string, string>? headers = null) =>
        SendAsync(HttpMethod.Post, path, body ?? new { }, headers);
}
