using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OtpNet;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Auth;

/// Staff sign-in: email + password + mandatory TOTP, forced password change, lockout (BR-A11, D-11, D-12, BR-A13).
public sealed class StaffAuthTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private const string NewPassword = "Correct-Horse-Battery-77";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private HttpClient Bearer(string token)
    {
        var client = factory.Browser();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        await factory.Browser().PostAsJsonAsync("/v1/admin/auth/login", new { email, password }, Ct);

    private async Task<string> MfaTokenAsync(string email, string password)
    {
        var login = await LoginAsync(email, password);
        login.StatusCode.ShouldBe(HttpStatusCode.OK, await login.Content.ReadAsStringAsync(Ct));
        return (await JsonAsync(login)).GetProperty("mfaToken").GetString()!;
    }

    private async Task<string> EnrolAsync(string mfaToken)
    {
        var enrol = await Bearer(mfaToken).PostAsync("/v1/admin/auth/totp/enrol", null, Ct);
        enrol.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await JsonAsync(enrol)).GetProperty("secretBase32").GetString()!;
    }

    /// The code of the time step <paramref name="stepOffset"/> steps from now (the window accepts −1 … +1).
    private static string Code(string secretBase32, int stepOffset = 0) =>
        new Totp(Base32Encoding.ToBytes(secretBase32)).ComputeTotp(DateTime.UtcNow.AddSeconds(30 * stepOffset));

    private async Task<HttpResponseMessage> VerifyAsync(string mfaToken, string code) =>
        await Bearer(mfaToken).PostAsJsonAsync("/v1/admin/auth/totp/verify", new { code }, Ct);

    /// A new account signed in to the end: (secret, access token, refresh cookie).
    private async Task<(string Secret, string AccessToken, string RefreshCookie, string Email)> SignInNewAccountAsync(params string[] roles)
    {
        var (_, email, oneTime) = await factory.NewStaffAccountAsync(roles);
        var mfa = await MfaTokenAsync(email, oneTime);
        var secret = await EnrolAsync(mfa);
        var verify = await VerifyAsync(mfa, Code(secret));
        verify.StatusCode.ShouldBe(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync(Ct));
        var cookie = verify.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("t3s_srt=", StringComparison.Ordinal)).Split(';')[0]["t3s_srt=".Length..];
        return (secret, (await JsonAsync(verify)).GetProperty("accessToken").GetString()!, cookie, email);
    }

    private static Dictionary<string, JsonElement> Claims(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Convert.FromBase64String(payload))!;
    }

    [Fact]
    [Rule("D-12")]
    public async Task A_new_account_signs_in_with_TOTP_and_must_change_its_password_first()
    {
        var (_, access, cookie, _) = await SignInNewAccountAsync(Roles.Admin);

        Claims(access)[StoreClaims.PasswordChangeRequired].GetString().ShouldBe("true");
        var me = await Bearer(access).GetAsync("/v1/admin/me", Ct);
        me.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await me.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.PasswordChangeRequired);

        using (var refresh = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/auth/refresh"))
        {
            refresh.Headers.Add("Cookie", "t3s_srt=" + cookie);
            var refreshed = await factory.Browser().SendAsync(refresh, Ct);
            refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
            Claims((await JsonAsync(refreshed)).GetProperty("accessToken").GetString()!).ShouldContainKey(StoreClaims.PasswordChangeRequired);   // R-5
        }
    }

    [Fact]
    [Rule("D-12")]
    public async Task Changing_the_password_issues_a_clean_session_and_revokes_the_old_family()
    {
        var (_, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Admin);
        var mfa = await MfaTokenAsync(email, oneTime);
        var secret = await EnrolAsync(mfa);
        var verify = await VerifyAsync(mfa, Code(secret));
        var access = (await JsonAsync(verify)).GetProperty("accessToken").GetString()!;
        var oldCookie = verify.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("t3s_srt=", StringComparison.Ordinal)).Split(';')[0]["t3s_srt=".Length..];

        var weak = await Bearer(access).PostAsJsonAsync("/v1/admin/auth/password", new { currentPassword = oneTime, newPassword = "password1234" }, Ct);
        var weakProblem = await weak.ReadProblemAsync();
        weakProblem.Str("code").ShouldBe(ErrorCodes.PasswordWeak);
        weakProblem.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ShouldContain("common");
        var shortOne = await Bearer(access).PostAsJsonAsync("/v1/admin/auth/password", new { currentPassword = oneTime, newPassword = "Short-1" }, Ct);
        (await shortOne.ReadProblemAsync()).GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ShouldContain("too_short");
        var wrong = await Bearer(access).PostAsJsonAsync("/v1/admin/auth/password", new { currentPassword = "not-the-password", newPassword = NewPassword }, Ct);
        (await wrong.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.CurrentPasswordInvalid);
        (await factory.ScalarAsync<int>($"SELECT failed_count FROM store.staff_user WHERE email = '{email}'")).ShouldBe(1);           // L-7
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.password_change_failed' AND actor = '{email}'")).ShouldBe(1);

        var changed = await Bearer(access).PostAsJsonAsync("/v1/admin/auth/password", new { currentPassword = oneTime, newPassword = NewPassword }, Ct);

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fresh = (await JsonAsync(changed)).GetProperty("accessToken").GetString()!;
        Claims(fresh).ShouldNotContainKey(StoreClaims.PasswordChangeRequired);
        (await Bearer(fresh).GetAsync("/v1/admin/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/auth/refresh");
        refresh.Headers.Add("Cookie", "t3s_srt=" + oldCookie);
        (await factory.Browser().SendAsync(refresh, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.password_changed' AND actor = '{email}'")).ShouldBe(1);
        (await LoginAsync(email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Rule("D-12")]
    public async Task A_locked_account_cannot_change_its_password_even_with_the_right_one()
    {
        var (_, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Admin);
        var mfa = await MfaTokenAsync(email, oneTime);
        var secret = await EnrolAsync(mfa);
        var access = (await JsonAsync(await VerifyAsync(mfa, Code(secret)))).GetProperty("accessToken").GetString()!;
        for (var i = 0; i < 5; i++)
        {
            (await LoginAsync(email, "Wrong-Password-" + i)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var hashBefore = await factory.ScalarAsync<string>($"SELECT pass_hash FROM store.staff_user WHERE email = '{email}'");
        var countBefore = await factory.ScalarAsync<int>($"SELECT failed_count FROM store.staff_user WHERE email = '{email}'");

        var change = await Bearer(access).PostAsJsonAsync("/v1/admin/auth/password", new { currentPassword = oneTime, newPassword = NewPassword }, Ct);

        change.StatusCode.ShouldBe(HttpStatusCode.Locked);
        (await change.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.AccountLocked);
        (await factory.ScalarAsync<string>($"SELECT pass_hash FROM store.staff_user WHERE email = '{email}'")).ShouldBe(hashBefore);
        (await factory.ScalarAsync<int>($"SELECT failed_count FROM store.staff_user WHERE email = '{email}'")).ShouldBe(countBefore);   // not judged, not counted
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.password_change_failed' AND actor = '{email}' AND diff->>'reason' = 'locked'"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Enrolling_an_authenticator_is_audited()
    {
        var (_, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Admin);
        var mfa = await MfaTokenAsync(email, oneTime);

        await EnrolAsync(mfa);

        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.totp_enrolled' AND actor = '{email}'")).ShouldBe(1);
    }

    [Fact]
    [Rule("D-12")]
    public async Task A_TOTP_code_and_an_MFA_token_are_single_use()
    {
        var (_, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Viewer);
        var mfa = await MfaTokenAsync(email, oneTime);
        var secret = await EnrolAsync(mfa);
        var code = Code(secret);
        (await VerifyAsync(mfa, code)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var spentToken = await VerifyAsync(mfa, Code(secret, 1));
        (await spentToken.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.TotpRequired);

        var replay = await VerifyAsync(await MfaTokenAsync(email, oneTime), code);
        (await replay.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.TotpInvalid);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.totp_failed' AND actor = '{email}'")).ShouldBe(1);

        var reenrol = await Bearer(await MfaTokenAsync(email, oneTime)).PostAsync("/v1/admin/auth/totp/enrol", null, Ct);
        (await reenrol.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.TotpAlreadyEnrolled);
    }

    [Fact]
    public async Task Re_enrolling_is_allowed_until_the_first_verified_code()
    {
        var (_, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Viewer);
        var mfa = await MfaTokenAsync(email, oneTime);
        var first = await EnrolAsync(mfa);

        var second = await EnrolAsync(mfa);

        second.ShouldNotBe(first);
        (await VerifyAsync(mfa, Code(first))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await VerifyAsync(mfa, Code(second))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Rule("D-12")]
    public async Task Five_wrong_passwords_lock_and_a_locked_account_does_not_reveal_whether_the_password_is_right()
    {
        var (id, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Finance);
        for (var i = 0; i < 5; i++)
        {
            (await LoginAsync(email, "wrong-password-" + i)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var rightWhileLocked = await LoginAsync(email, oneTime);
        rightWhileLocked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await rightWhileLocked.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.InvalidCredentials);
        var wrongWhileLocked = await LoginAsync(email, "wrong-password-x");
        wrongWhileLocked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await wrongWhileLocked.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.InvalidCredentials);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.locked' AND entity_id = '{id}'")).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.login_failed' AND entity_id = '{id}'")).ShouldBe(7);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.login_failed' AND entity_id = '{id}' AND diff->>'reason' = 'locked'"))
            .ShouldBe(2);

        var admin = await factory.StaffAsync(Roles.Admin);
        (await admin.PostAsync($"/v1/admin/staff/{id}/unlock")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoginAsync(email, oneTime)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Rule("D-12")]
    public async Task After_the_lock_runs_out_the_right_password_gets_an_MFA_token()
    {
        var (id, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Viewer);
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, "wrong-password-" + i);
        }

        (await LoginAsync(email, oneTime)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await factory.ExecuteSqlAsync($"UPDATE store.staff_user SET locked_until = now() - interval '1 minute' WHERE id = '{id}'");

        var login = await LoginAsync(email, oneTime);

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(login)).GetProperty("mfaToken").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    [Rule("D-12")]
    public async Task Parallel_wrong_passwords_are_all_counted_and_lock_the_account()
    {
        var (id, email, _) = await factory.NewStaffAccountAsync(Roles.Viewer);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => LoginAsync(email, "parallel-wrong-" + i)));

        responses.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.Unauthorized);
        (await factory.ScalarAsync<int>($"SELECT failed_count FROM store.staff_user WHERE id = '{id}'")).ShouldBe(6);
        (await factory.ScalarAsync<bool>($"SELECT locked_until > now() FROM store.staff_user WHERE id = '{id}'")).ShouldBeTrue();
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.login_failed' AND entity_id = '{id}'")).ShouldBe(6);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.locked' AND entity_id = '{id}'")).ShouldBe(1);
    }

    [Fact]
    [Rule("D-12")]
    public async Task Parallel_wrong_TOTP_codes_are_all_counted_and_lock_the_account()
    {
        var (id, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Viewer);
        var mfa = await MfaTokenAsync(email, oneTime);
        var secret = await EnrolAsync(mfa);
        var right = Code(secret);
        var wrong = right == "000000" ? "111111" : "000000";

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => VerifyAsync(mfa, wrong)));

        // Serialised on the row: four wrong codes, the fifth locks (423), the sixth meets the lock before its code is checked.
        responses.Select(r => (int)r.StatusCode).OrderBy(s => s).ShouldBe([401, 401, 401, 401, 423, 423]);
        (await factory.ScalarAsync<int>($"SELECT failed_count FROM store.staff_user WHERE id = '{id}'")).ShouldBe(5);
        (await factory.ScalarAsync<bool>($"SELECT locked_until > now() FROM store.staff_user WHERE id = '{id}'")).ShouldBeTrue();
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'staff.totp_failed' AND entity_id = '{id}'")).ShouldBe(5);
    }

    [Fact]
    public async Task A_TOTP_secret_that_cannot_be_decrypted_answers_503_alerts_staff_and_keeps_the_enrolment()
    {
        var (id, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Viewer);
        var mfa = await MfaTokenAsync(email, oneTime);
        await EnrolAsync(mfa);
        await factory.ExecuteSqlAsync($"UPDATE store.staff_user SET totp_secret_enc = '\\x00010203'::bytea WHERE id = '{id}'");

        var attempts = new List<HttpResponseMessage>();
        for (var i = 0; i < 3; i++)
        {
            attempts.Add(await VerifyAsync(mfa, "123456"));
        }

        foreach (var verify in attempts)
        {
            verify.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await verify.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.TotpUnavailable);
        }

        (await factory.ScalarAsync<int>($"SELECT failed_count FROM store.staff_user WHERE id = '{id}'")).ShouldBe(0);
        (await factory.ScalarAsync<string>($"SELECT encode(totp_secret_enc, 'hex') FROM store.staff_user WHERE id = '{id}'")).ShouldBe("00010203");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'StaffAlert' AND payload->>'kind' = 'totp_key_lost' AND payload->>'reference' = '{id}'"))
            .ShouldBe(1);                                                                                         // R11-1: once per 24 h

        var admin = await factory.StaffAsync(Roles.Admin);
        (await admin.PostAsync($"/v1/admin/staff/{id}/reset-totp")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var secret = await EnrolAsync(await MfaTokenAsync(email, oneTime));
        secret.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task The_email_is_matched_case_insensitively_with_the_store_public_search_path()
    {
        new Npgsql.NpgsqlConnectionStringBuilder(factory.DatabaseConnectionString).SearchPath.ShouldBe("store,public");
        var (_, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Viewer);

        var login = await LoginAsync(char.ToUpperInvariant(email[0]) + email[1..].ToUpperInvariant(), oneTime);

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Rule("D-12")]
    public async Task An_unknown_email_answers_like_a_wrong_password()
    {
        var unknown = await LoginAsync($"nobody-{Guid.NewGuid():N}@test.ta3lom.sa", "whatever-password");
        var (_, email, _) = await factory.NewStaffAccountAsync(Roles.Viewer);
        var wrong = await LoginAsync(email, "whatever-password");

        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await unknown.ReadProblemAsync()).Str("code").ShouldBe((await wrong.ReadProblemAsync()).Str("code"));
    }

    [Fact]
    [Rule("D-12")]
    public async Task Passwords_are_PBKDF2_SHA512_with_600000_iterations_and_old_hashes_are_upgraded()
    {
        var (id, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Viewer);
        var stored = Convert.FromBase64String(await factory.ScalarAsync<string>($"SELECT pass_hash FROM store.staff_user WHERE id = '{id}'"));
        BinaryPrimitives.ReadUInt32BigEndian(stored.AsSpan(1)).ShouldBe(2u);                     // HMAC-SHA512
        BinaryPrimitives.ReadUInt32BigEndian(stored.AsSpan(5)).ShouldBe(600_000u);

        var legacy = LegacyHash(oneTime, 100_000);
        await factory.ExecuteSqlAsync($"UPDATE store.staff_user SET pass_hash = '{legacy}' WHERE id = '{id}'");
        (await LoginAsync(email, oneTime)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var upgraded = Convert.FromBase64String(await factory.ScalarAsync<string>($"SELECT pass_hash FROM store.staff_user WHERE id = '{id}'"));
        BinaryPrimitives.ReadUInt32BigEndian(upgraded.AsSpan(5)).ShouldBe(600_000u);
    }

    [Fact]
    [Rule("D-11")]
    public async Task Staff_tokens_last_ten_minutes_and_the_family_eight_hours()
    {
        var (_, access, cookie, _) = await SignInNewAccountAsync(Roles.Viewer);

        var claims = Claims(access);
        (claims["exp"].GetInt64() - claims["iat"].GetInt64()).ShouldBe(600);
        (await factory.ScalarAsync<int>($"SELECT extract(epoch FROM family_expires_at - created_at)::int FROM store.refresh_token WHERE token_hash = encode(sha256(convert_to('{cookie}', 'UTF8')), 'hex')"))
            .ShouldBe(8 * 3600);
    }

    [Fact]
    [Rule("D-12")]
    public async Task The_last_active_admin_cannot_be_removed()
    {
        await factory.ExecuteSqlAsync("UPDATE store.staff_user SET roles = ARRAY['viewer'] WHERE 'admin' = ANY (roles)");
        var admin = await factory.StaffAsync(Roles.Admin);
        var version = await factory.ScalarAsync<long>($"SELECT xmin::text::bigint FROM store.staff_user WHERE id = '{admin.Id}'");

        var demote = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/staff/{admin.Id}", new { name = "Admin", roles = new[] { Roles.Viewer }, isActive = true, version });
        demote.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        demote.Body.Str("code").ShouldBe(ErrorCodes.StaffLastAdmin);
        var deactivate = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/staff/{admin.Id}", new { name = "Admin", roles = new[] { Roles.Admin }, isActive = false, version });
        deactivate.Body.Str("code").ShouldBe(ErrorCodes.StaffLastAdmin);

        var other = await factory.StaffAsync(Roles.Admin);
        var allowed = await other.SendAsync(HttpMethod.Put, $"/v1/admin/staff/{admin.Id}", new { name = "Former admin", roles = new[] { Roles.Viewer }, isActive = true, version });
        allowed.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        allowed.Body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe([Roles.Viewer]);
    }

    [Fact]
    public async Task Two_admins_demoting_each_other_at_once_leave_one_admin()
    {
        await factory.ExecuteSqlAsync("UPDATE store.staff_user SET roles = ARRAY['viewer'] WHERE 'admin' = ANY (roles)");
        var first = await factory.StaffAsync(Roles.Admin);
        var second = await factory.StaffAsync(Roles.Admin);
        async Task<long> VersionAsync(Guid id) => await factory.ScalarAsync<long>($"SELECT xmin::text::bigint FROM store.staff_user WHERE id = '{id}'");
        var firstVersion = await VersionAsync(first.Id);
        var secondVersion = await VersionAsync(second.Id);

        var results = await Task.WhenAll(
            first.SendAsync(HttpMethod.Put, $"/v1/admin/staff/{second.Id}", new { name = "Second", roles = new[] { Roles.Viewer }, isActive = true, version = secondVersion }),
            second.SendAsync(HttpMethod.Put, $"/v1/admin/staff/{first.Id}", new { name = "First", roles = new[] { Roles.Viewer }, isActive = true, version = firstVersion }));

        results.Count(r => r.Response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        results.Single(r => r.Response.StatusCode != HttpStatusCode.OK).Body.Str("code").ShouldBe(ErrorCodes.StaffLastAdmin);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.staff_user WHERE is_active AND 'admin' = ANY (roles)")).ShouldBe(1);
    }

    [Fact]
    [Rule("D-12")]
    public async Task A_locked_account_loses_its_session_family_at_the_next_refresh()
    {
        var (_, _, cookie, email) = await SignInNewAccountAsync(Roles.Admin);
        for (var i = 0; i < 5; i++)
        {
            (await LoginAsync(email, "Wrong-Password-" + i)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/auth/refresh");
        refresh.Headers.Add("Cookie", "t3s_srt=" + cookie);
        (await factory.Browser().SendAsync(refresh, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await factory.ScalarAsync<long>($"""
            SELECT count(*) FROM store.refresh_token t JOIN store.staff_user s ON s.id = t.subject_id
             WHERE s.email = '{email}' AND t.revoked_at IS NULL
            """)).ShouldBe(0);
    }

    /// The v3 format with fewer iterations, as an earlier release wrote it.
    private static string LegacyHash(string password, int iterations)
    {
        var salt = new byte[16];
        var subkey = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA512, 32);
        var output = new byte[13 + 16 + 32];
        output[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(1), 2);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(5), (uint)iterations);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(9), 16);
        subkey.CopyTo(output, 29);
        return Convert.ToBase64String(output);
    }
}
