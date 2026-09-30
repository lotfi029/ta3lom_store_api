using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Identity;

/// OTP challenges and refresh tokens (RUL-60).
public sealed class OtpAndTokenTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static OtpChallenge Challenge() =>
        OtpChallenge.Issue(Guid.CreateVersion7(), "+966551234567", OtpPurpose.Login, "hmac", "ip-hash", T0 + SessionPolicy.OtpLifetime, T0);

    [Fact]
    [Rule("RUL-60")]
    public void A_code_is_valid_for_five_minutes()
    {
        var otp = Challenge();

        otp.IsOpen(T0.AddMinutes(4).AddSeconds(59)).ShouldBeTrue();
        otp.IsOpen(T0.AddMinutes(5)).ShouldBeFalse();
        otp.ExpiresAt.ShouldBe(T0.AddMinutes(5));
    }

    [Fact]
    [Rule("RUL-60")]
    public void Issue_keeps_the_chosen_id_and_stores_only_the_hash()
    {
        var id = Guid.CreateVersion7();

        var otp = OtpChallenge.Issue(id, "+966551234567", OtpPurpose.BeneficiaryInvite, "hmac", null, T0.AddMinutes(5), T0);

        otp.Id.ShouldBe(id);
        otp.Mobile.ShouldBe("+966551234567");
        otp.Purpose.ShouldBe(OtpPurpose.BeneficiaryInvite);
        otp.CodeHash.ShouldBe("hmac");
        otp.IpHash.ShouldBeNull();
        otp.Attempts.ShouldBe((short)0);
        otp.CreatedAt.ShouldBe(T0);
        otp.ConsumedAt.ShouldBeNull();
        Should.Throw<DomainException>(() => OtpChallenge.Issue(Guid.Empty, "m", OtpPurpose.Login, "h", null, T0, T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    public void The_public_constructor_opens_a_challenge()
    {
        var otp = new OtpChallenge("+966551234567", OtpPurpose.Login, "hmac", T0.AddMinutes(5), T0);

        otp.IsOpen(T0).ShouldBeTrue();
        otp.AttemptsLeft.ShouldBe(OtpChallenge.MaxAttempts);
        Should.Throw<DomainException>(() => new OtpChallenge("+966551234567", OtpPurpose.Login, null!, T0, T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    [Rule("RUL-60")]
    public void Five_wrong_attempts_exhaust_the_code()
    {
        var otp = Challenge();
        for (var i = 1; i <= 4; i++)
        {
            otp.RecordFailure();
            otp.AttemptsLeft.ShouldBe(5 - i);
            otp.IsExhausted.ShouldBeFalse();
        }

        otp.RecordFailure();

        otp.Attempts.ShouldBe((short)5);
        otp.AttemptsLeft.ShouldBe(0);
        otp.IsExhausted.ShouldBeTrue();

        otp.RecordFailure();
        otp.AttemptsLeft.ShouldBe(0);
    }

    [Fact]
    [Rule("RUL-60")]
    public void A_consumed_code_is_closed_and_cannot_be_used_again()
    {
        var otp = Challenge();

        otp.Consume(T0.AddMinutes(1));

        otp.ConsumedAt.ShouldBe(T0.AddMinutes(1));
        otp.IsOpen(T0.AddMinutes(1)).ShouldBeFalse();
        Should.Throw<DomainException>(() => otp.Consume(T0.AddMinutes(2))).Code.ShouldBe("otp.consumed");
        Should.Throw<DomainException>(otp.RecordFailure).Code.ShouldBe("otp.consumed");
    }

    [Fact]
    public void A_refresh_token_keeps_its_family_and_truncates_the_user_agent()
    {
        var subject = Guid.CreateVersion7();
        var family = Guid.CreateVersion7();

        var token = RefreshToken.Issue(TokenSubjectType.Staff, subject, "sha256", family, T0.AddHours(8), new string('u', 400), T0);

        token.SubjectType.ShouldBe(TokenSubjectType.Staff);
        token.SubjectId.ShouldBe(subject);
        token.TokenHash.ShouldBe("sha256");
        token.FamilyId.ShouldBe(family);
        token.ExpiresAt.ShouldBe(T0.AddHours(8));
        token.CreatedAt.ShouldBe(T0);
        token.UserAgent!.Length.ShouldBe(300);
        token.IsRevoked.ShouldBeFalse();
        token.ReplacedBy.ShouldBeNull();
        RefreshToken.Issue(TokenSubjectType.Customer, subject, "h", family, T0, "Safari", T0).UserAgent.ShouldBe("Safari");
        RefreshToken.Issue(TokenSubjectType.Customer, subject, "h", family, T0, null, T0).UserAgent.ShouldBeNull();
        Should.Throw<DomainException>(() => RefreshToken.Issue(TokenSubjectType.Customer, subject, "h", Guid.Empty, T0, null, T0));
    }

    [Fact]
    [Rule("RUL-60")]
    public void Revoking_keeps_the_first_time_and_replacement()
    {
        var token = new RefreshToken(TokenSubjectType.Customer, Guid.CreateVersion7(), "h", Guid.CreateVersion7(), T0.AddDays(60), T0);
        var next = Guid.CreateVersion7();

        token.Revoke(T0.AddMinutes(1), next);
        token.Revoke(T0.AddMinutes(2), Guid.CreateVersion7());

        token.IsRevoked.ShouldBeTrue();
        token.RevokedAt.ShouldBe(T0.AddMinutes(1));
        token.ReplacedBy.ShouldBe(next);
    }

    [Fact]
    public void A_family_revoke_has_no_replacement()
    {
        var token = new RefreshToken(TokenSubjectType.Customer, Guid.CreateVersion7(), "h", Guid.CreateVersion7(), T0.AddDays(60), T0);

        token.Revoke(T0);

        token.IsRevoked.ShouldBeTrue();
        token.ReplacedBy.ShouldBeNull();
    }
}
