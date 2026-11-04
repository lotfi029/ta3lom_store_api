using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Features.Payments;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Payments;
using Ta3lom.Store.Infrastructure.Payments.Fake;
using Ta3lom.Store.Infrastructure.Services.Time;

namespace Ta3lom.Store.Api.Endpoints;

/// `/dev/*` — mapped in Development only (never in Staging or Production): the SMS and e-mail sinks, running a job now,
/// and the fake gateway's hosted page (C-2).
internal sealed class DevEndpoints : IDevEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/sms", ([FromServices] IDevSmsInbox inbox, int count = 50) => TypedResults.Ok(inbox.Latest(Math.Clamp(count, 1, 200))));
        app.MapGet("/mail", async ([FromServices] IDevMailInbox inbox, CancellationToken ct, int count = 20) =>
            TypedResults.Ok(await inbox.LatestAsync(Math.Clamp(count, 1, 200), ct)));
        app.MapGet("/jobs", ([FromServices] IJobRunner jobs) => TypedResults.Ok(jobs.Jobs));
        app.MapPost("/jobs/{name}", RunJobAsync);

        app.MapGet("/payments/{id}", FakePaymentPage);
        app.MapPost("/payments/{id}/{action}", FakePaymentActionAsync).DisableAntiforgery();

        // Time travel for the expiry e2e: `{ "advanceMinutes": 61 }` or `{ "reset": true }`.
        app.MapGet("/clock", ([FromServices] IClock clock, [FromServices] DevClockOffset offset) =>
            TypedResults.Ok(new { utcNow = clock.UtcNow, offsetMinutes = offset.Value.TotalMinutes }));
        app.MapPost("/clock", ([FromBody] DevClockChange request, [FromServices] IClock clock, [FromServices] DevClockOffset offset) =>
        {
            if (request.Reset)
            {
                offset.Reset();
            }

            if (request.AdvanceMinutes is { } minutes)
            {
                offset.Advance(TimeSpan.FromMinutes(Math.Clamp(minutes, -60 * 24 * 30, 60 * 24 * 30)));
            }

            return TypedResults.Ok(new { utcNow = clock.UtcNow, offsetMinutes = offset.Value.TotalMinutes });
        }).DisableAntiforgery();
    }

    internal sealed record DevClockChange(double? AdvanceMinutes, bool Reset);

    private static async Task<IResult> RunJobAsync(string name, [FromServices] IJobRunner jobs, CancellationToken ct) =>
        await jobs.TriggerAsync(name, ct) ? TypedResults.Accepted((string?)null) : TypedResults.NotFound();

    /// Pay / Fail / Pay a wrong amount — each posts a signed fake webhook through the real webhook handler, then returns
    /// the donor to the web order page (like Moyasar's success/back URL). Plain HTML: the API's CSP allows no script/style.
    private static IResult FakePaymentPage(string id, [FromServices] FakePaymentStore store)
    {
        if (store.Invoice(id) is not { } invoice)
        {
            return TypedResults.NotFound();
        }

        static string e(string value) => WebUtility.HtmlEncode(value);
        var amount = (invoice.AmountHalalas / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var html = new StringBuilder()
            .Append("<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"><title>Fake payment ")
            .Append(e(invoice.OrderReference)).Append("</title></head><body><main><h1>بوابة الدفع التجريبية (Development)</h1>")
            .Append("<p>الطلب <strong id=\"ref\">").Append(e(invoice.OrderReference)).Append("</strong>، المبلغ <strong id=\"amount\">")
            .Append(amount).Append("</strong> SAR، الحالة <span id=\"status\">").Append(e(invoice.Status)).Append("</span></p>")
            .Append(Form(id, "pay", "pay", "ادفع (Pay)"))
            .Append(Form(id, "fail", "fail", "فشل الدفع (Fail)"))
            .Append(Form(id, "wrong-amount", "wrong", "ادفع مبلغًا مختلفًا (Pay a wrong amount)"))
            .Append("<p><a id=\"back\" href=\"").Append(e(invoice.ReturnUrl)).Append("\">رجوع (Back)</a></p></main></body></html>")
            .ToString();
        return TypedResults.Content(html, "text/html; charset=utf-8", Encoding.UTF8);

        static string Form(string id, string action, string buttonId, string label) =>
            $"<form method=\"post\" action=\"/dev/payments/{WebUtility.HtmlEncode(id)}/{action}\"><button id=\"{buttonId}\" type=\"submit\">{label}</button></form>";
    }

    private static async Task<IResult> FakePaymentActionAsync(string id, string action, [FromServices] FakePaymentStore store,
        [FromServices] IOptions<PaymentOptions> options, [FromServices] IClock clock,
        [FromServices] ICommandHandler<PaymentWebhookCommand, string> webhook, CancellationToken ct)
    {
        if (store.Invoice(id) is not { } invoice || action is not ("pay" or "fail" or "wrong-amount"))
        {
            return TypedResults.NotFound();
        }

        var payment = store.Act(id, success: action != "fail", amountOverride: action == "wrong-amount" ? invoice.AmountHalalas + 100 : null);
        var body = FakePaymentStore.WebhookBody(payment, options.Value.Fake.WebhookSecret, clock.UtcNow);
        await webhook.HandleAsync(new PaymentWebhookCommand(FakePaymentGateway.ProviderName, Encoding.UTF8.GetBytes(body),
            new Dictionary<string, string>()), ct);
        return TypedResults.Redirect(invoice.ReturnUrl);
    }
}
