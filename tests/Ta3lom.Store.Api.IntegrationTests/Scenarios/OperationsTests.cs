using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.AdminOps.Commands;
using Ta3lom.Store.Application.Features.Enrolments.Commands.LmsProgressSync;
using Ta3lom.Store.Application.Features.Giving.Commands;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-5 — the remaining plan-14 "Tests" lines of S-24, S-31, S-33, S-35, S-40, S-42, S-43 and S-45.
public sealed class OperationsTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static string Q(Guid id) => $"'{id}'";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Rule("RUL-42")]
    [Rule("RUL-50")]
    public async Task LMS_progress_sync_is_idempotent_and_a_completion_reaches_the_sponsored_unit_once()
    {
        var product = await factory.ProductIdAsync("digital-teacher");
        await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.SponsorUnits(product, 1));
        var admin = await factory.StaffAsync(Roles.Admin);
        var officer = await factory.StaffAsync(Roles.AllocationOfficer);
        var (_, b) = await admin.PostAsync("/v1/admin/beneficiaries", new
        {
            kind = "teacher",
            beneficiaryGroup = "rural_private_school_teachers",
            region = "الجوف",
            fullName = "معلم تجربة",
            mobile = ScenarioFactory.NewMobile(),
            organisationName = "مدرسة",
        });
        await admin.PostAsync($"/v1/admin/beneficiaries/{b.GetProperty("id").GetGuid()}/eligibility", new { status = "approved", zakatEligible = false });
        var (_, allocated) = await officer.PostAsync($"/v1/admin/allocation/products/{product}/allocate", new { beneficiaryIds = new[] { b.GetProperty("id").GetGuid() } });
        var unit = allocated.GetProperty("units")[0].GetProperty("unitId").GetGuid();
        await factory.DrainOutboxAsync();
        var enrolment = await factory.ScalarAsync<Guid>($"SELECT id FROM store.enrolment WHERE sponsored_unit_id = {Q(unit)}");

        factory.Services.GetRequiredService<IDevLmsProgressBoard>()
            .Report(enrolment, new LmsProgress(100, factory.Clock.UtcNow.AddDays(-3), factory.Clock.UtcNow, "CERT-T5-1"));
        await factory.RunAsync<LmsProgressSyncCommand, int>(new LmsProgressSyncCommand());
        await factory.RunAsync<LmsProgressSyncCommand, int>(new LmsProgressSyncCommand());

        (await factory.ScalarAsync<string>($"SELECT status FROM store.sponsored_unit WHERE id = {Q(unit)}")).ShouldBe("completed");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.sponsored_unit_event WHERE unit_id = {Q(unit)} AND to_status = 'completed'")).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'SponsorUnitCompleted' AND payload->>'unitId' = '{unit}'")).ShouldBe(1);
        (await factory.ScalarAsync<string>($"SELECT certificate_no FROM store.enrolment WHERE id = {Q(enrolment)}")).ShouldBe("CERT-T5-1");
    }

    [Fact]
    [Rule("RUL-51")]
    public async Task A_CSV_import_is_atomic_with_a_dry_run_and_refuses_duplicate_mobiles()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var template = await admin.Http.GetAsync("/v1/admin/beneficiaries/import/template", Ct);
        var header = (await template.Content.ReadAsStringAsync(Ct)).TrimStart('﻿').Split('\n')[0].Trim();
        string Row(string mobile) => $"teacher,rural_private_school_teachers,حائل,معلمة {mobile[^3..]},{mobile},,مدرسة,";
        var m1 = ScenarioFactory.NewMobile();
        var m2 = ScenarioFactory.NewMobile();

        async Task<(HttpStatusCode, System.Text.Json.JsonElement)> ImportAsync(string csv, bool dryRun)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            form.Add(file, "file", "b.csv");
            form.Add(new StringContent(dryRun ? "true" : "false"), "dryRun");
            var r = await admin.Http.PostAsync("/v1/admin/beneficiaries/import", form, Ct);
            return (r.StatusCode, System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct)).RootElement.Clone());
        }

        var before = await factory.ScalarAsync<long>("SELECT count(*) FROM store.beneficiary");
        var withDuplicate = $"{header}\n{Row(m1)}\n{Row(m2)}\n{Row(m1)}\n";
        (await ImportAsync(withDuplicate, dryRun: true)).Item1.ShouldBe(HttpStatusCode.OK);
        var (failed, report) = await ImportAsync(withDuplicate, dryRun: false);
        failed.ShouldBe(HttpStatusCode.UnprocessableEntity);
        report.ToString().ShouldContain("mobile_exists");
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.beneficiary")).ShouldBe(before);          // nothing saved

        (await ImportAsync($"{header}\n{Row(m1)}\n{Row(m2)}\n", dryRun: false)).Item1.ShouldBe(HttpStatusCode.OK);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.beneficiary")).ShouldBe(before + 2);
    }

    [Fact]
    [Rule("RUL-46")]
    public async Task Closing_campaigns_sweeps_the_remainder_once_and_the_integrity_checks_stay_at_zero()
    {
        var product = await factory.ProductIdAsync("smart-study");                                      // seat 120.00
        await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.SponsorAmount(product, "50.00"));
        await factory.ExecuteSqlAsync($"UPDATE store.sponsorship_offer SET starts_at = now() - interval '2 days', ends_at = now() - interval '1 minute' WHERE product_id = {Q(product)} AND is_active");

        await factory.RunAsync<CloseCampaignsCommand, int>(new CloseCampaignsCommand());
        await factory.RunAsync<CloseCampaignsCommand, int>(new CloseCampaignsCommand());

        (await factory.ScalarAsync<decimal>($"SELECT p.balance FROM store.sponsorship_pool p JOIN store.sponsorship_offer o ON o.id = p.sponsorship_offer_id WHERE o.product_id = {Q(product)}")).ShouldBe(0m);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.goal_adjustment WHERE product_id = {Q(product)} AND kind = 'pool_sweep_out'")).ShouldBe(1);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.goal_adjustment WHERE kind = 'pool_sweep_in'")).ShouldBe(1);
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-20")]
    public async Task A_voucher_above_the_zakat_balance_is_refused()
    {
        var cause = await factory.ProductIdAsync("tablet-for-student");
        await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.Donate(cause, 1, "zakat"));   // 950.00 in
        var finance = await factory.StaffAsync(Roles.Finance);
        object Voucher(string amount) => new { productId = cause, amount, voucherNo = $"V-{Guid.NewGuid():N}"[..12], disbursedOn = factory.Clock.RiyadhToday, purpose = "أجهزة" };

        var (refused, problem) = await finance.PostAsync("/v1/admin/zakat/disbursements", Voucher("951.00"));
        refused.IsSuccessStatusCode.ShouldBeFalse();
        problem.Code().ShouldBe("zakat.balance_insufficient");
        (await finance.PostAsync("/v1/admin/zakat/disbursements", Voucher("950.00"))).Response.IsSuccessStatusCode.ShouldBeTrue();
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();                                           // v_zakat_balance never negative
    }

    [Fact]
    [Rule("RUL-61")]
    public async Task Another_customers_document_is_not_found()
    {
        var owner = await factory.CustomerAsync();
        var order = await factory.PaidOrderAsync(owner, Shop.Donate(await factory.ProductIdAsync("school-bag")));
        var number = (await owner.GetAsync($"/v1/me/orders/{order.Reference}")).Body.GetProperty("documents")[0].GetProperty("number").GetString();

        (await owner.GetAsync($"/v1/me/documents/{number}")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await factory.CustomerAsync()).GetAsync($"/v1/me/documents/{number}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await factory.CustomerAsync()).GetAsync($"/v1/me/orders/{order.Reference}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Report_totals_equal_the_ledger_and_the_CSV_carries_a_BOM()
    {
        await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.Donate(await factory.ProductIdAsync("water-for-schools"), 2));
        var finance = await factory.StaffAsync(Roles.Finance);
        var today = factory.Clock.RiyadhToday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var (_, gifts) = await finance.GetAsync($"/v1/admin/reports/gifts?from={today}&to={today}");
        var reported = gifts.GetProperty("rows").EnumerateArray().Sum(r => decimal.Parse(r.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture));
        var ledger = await factory.ScalarAsync<decimal>("""
            SELECT coalesce(sum(l.amount), 0) FROM store.order_line l JOIN store.orders o ON o.id = l.order_id
             WHERE l.kind <> 'buy' AND o.status IN ('paid','fulfilled','partially_refunded','refunded')
            """);
        reported.ShouldBe(ledger);

        var csv = await finance.Http.GetAsync($"/v1/admin/reports/gifts?from={today}&to={today}&format=csv", Ct);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var bytes = await csv.Content.ReadAsByteArrayAsync(Ct);
        bytes[..3].ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes).ShouldContain("water-for-schools");
    }

    [Fact]
    [Rule("RUL-63")]
    public async Task An_invalid_template_is_rejected_at_save()
    {
        var admin = await factory.StaffAsync(Roles.Admin);

        var (status, problem) = await admin.SendAsync(HttpMethod.Put, "/v1/admin/templates/order_paid/sms/ar", new { body = "طلبك {{ reference" });

        status.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        problem.Code().ShouldBe("template.render_failed");
        problem.GetProperty("line").GetInt32().ShouldBeGreaterThan(0);
        (await admin.SendAsync(HttpMethod.Put, "/v1/admin/templates/order_paid/sms/ar", new { body = "x {{ if total }}" })).Body.Code().ShouldBe("template.render_failed");
        (await factory.ScalarAsync<string>("SELECT body FROM store.message_template WHERE key = 'order_paid' AND channel = 'sms' AND locale = 'ar'")).ShouldContain("{{ reference }}");
        (await admin.SendAsync(HttpMethod.Put, "/v1/admin/templates/order_paid/sms/ar",
            new { body = "تم استلام طلبك {{ reference }} بمبلغ {{ total }} ر.س. الإيصال: {{ link }}" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_integrity_job_raises_an_alert_on_a_corrupted_counter()
    {
        var print = await factory.FormatIdAsync("study-planner", "print");
        (await factory.RunAsync<IntegrityChecksCommand, int>(new IntegrityChecksCommand())).Value.ShouldBe(0);
        await factory.ExecuteSqlAsync($"UPDATE store.book_format SET stock_on_hand = stock_on_hand + 7 WHERE id = {Q(print)}");
        try
        {
            (await factory.RunAsync<IntegrityChecksCommand, int>(new IntegrityChecksCommand())).Value.ShouldBeGreaterThanOrEqualTo(1);
            (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE type = 'FinanceAlert' AND payload->>'kind' = 'integrity_check_failed'")).ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            await factory.ExecuteSqlAsync($"UPDATE store.book_format SET stock_on_hand = stock_on_hand - 7 WHERE id = {Q(print)}");
        }
    }

    [Fact]
    public async Task Retention_scrubs_payment_payloads_older_than_18_months()
    {
        var tag = Guid.NewGuid().ToString("N");
        await factory.ExecuteSqlAsync($"""
            INSERT INTO store.payment_event (provider, provider_event_id, event_type, signature_valid, payload, received_at)
            VALUES ('dev', 'old-{tag}', 'captured', true, '{"{"}"card":"4111"{"}"}', now() - interval '19 months'),
                   ('dev', 'new-{tag}', 'captured', true, '{"{"}"card":"4111"{"}"}', now() - interval '17 months')
            """);

        await factory.RunAsync<RetentionCommand, int>(new RetentionCommand());

        (await factory.ScalarAsync<string>($"SELECT string_agg(payload::text, '|' ORDER BY received_at) FROM store.payment_event WHERE provider_event_id LIKE '%{tag}'"))
            .ShouldBe("{}|{\"card\": \"4111\"}");
    }

    [Fact]
    [Rule("RUL-63")]
    [Rule("RUL-50")]
    public async Task The_monthly_digest_is_sent_once_and_never_names_a_beneficiary()
    {
        var sponsor = await factory.CustomerAsync();
        await sponsor.SendAsync(HttpMethod.Patch, "/v1/me", new { fullName = "راعٍ تجربة", email = $"s{Guid.NewGuid():N}@example.com" });
        var product = await factory.ProductIdAsync("letters-tales");
        await factory.PaidOrderAsync(sponsor, Shop.SponsorUnits(product, 1));
        var admin = await factory.StaffAsync(Roles.Admin);
        var (_, b) = await admin.PostAsync("/v1/admin/beneficiaries", new
        {
            kind = "student",
            beneficiaryGroup = "beneficiary_family_children",
            region = "نجران",
            fullName = "اسم سري جدًا",
            mobile = ScenarioFactory.NewMobile(),
            guardianName = "ولي",
            organisationName = "أسرة",
        });
        await admin.PostAsync($"/v1/admin/beneficiaries/{b.GetProperty("id").GetGuid()}/eligibility", new { status = "approved", zakatEligible = false });
        await (await factory.StaffAsync(Roles.AllocationOfficer)).PostAsync($"/v1/admin/allocation/products/{product}/allocate", new { beneficiaryIds = new[] { b.GetProperty("id").GetGuid() } });
        await factory.DrainOutboxAsync();

        await factory.RunAsync<SponsorDigestCommand, int>(new SponsorDigestCommand());
        await factory.RunAsync<SponsorDigestCommand, int>(new SponsorDigestCommand());
        await factory.DrainOutboxAsync();

        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'SponsorDigest' AND payload->>'customerId' = '{sponsor.Id}'")).ShouldBe(1);
        var email = await factory.ScalarAsync<string>($"SELECT email FROM store.customer WHERE id = {Q(sponsor.Id)}");
        var digest = (await factory.MailAsync(200)).Single(m => m.To == email);
        digest.Body.ShouldContain("نجران");                                                                   // region only
        digest.Body.ShouldNotContain("اسم سري");
        digest.Body.ShouldNotContain(b.GetProperty("code").GetString()!);
    }

    [Fact]
    [Rule("RUL-62")]
    public void TOTP_accepts_one_step_either_side_and_nothing_further()
    {
        var totp = factory.Services.GetRequiredService<ITotpService>();
        var enrolment = totp.CreateEnrolment("t5@test.ta3lom.sa");
        var generator = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(enrolment.SecretBase32));
        var now = DateTime.UtcNow;

        totp.Verify(enrolment.ProtectedSecret, generator.ComputeTotp(now)).ShouldBeTrue();
        totp.Verify(enrolment.ProtectedSecret, generator.ComputeTotp(now.AddSeconds(-30))).ShouldBeTrue();
        totp.Verify(enrolment.ProtectedSecret, generator.ComputeTotp(now.AddSeconds(30))).ShouldBeTrue();
        totp.Verify(enrolment.ProtectedSecret, generator.ComputeTotp(now.AddSeconds(-95))).ShouldBeFalse();
        totp.Verify(enrolment.ProtectedSecret, generator.ComputeTotp(now.AddSeconds(95))).ShouldBeFalse();
    }
}
