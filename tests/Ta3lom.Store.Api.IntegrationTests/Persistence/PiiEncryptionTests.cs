using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Enums;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// Beneficiary PII is encrypted before it reaches the database (database/01 §1.5, 09 §5.13, PDPL).
public sealed class PiiEncryptionTests
{
    private const string Name = "نورة العتيبي";
    private const string Mobile = "+966551234567";

    [Fact]
    public async Task Name_and_mobile_are_stored_encrypted_and_read_back_in_clear()
    {
        var cs = await StoreSchema.CreateMigratedAsync("pii");
        var id = await InsertBeneficiaryAsync(cs, "B-000001", Mobile);

        var raw = (await RawColumnsAsync(cs, id)).ToList();
        raw.ShouldAllBe(bytes => bytes.Length > 16);
        raw.ShouldAllBe(bytes => !Encoding.UTF8.GetString(bytes).Contains(Name, StringComparison.Ordinal)
                                 && !Encoding.UTF8.GetString(bytes).Contains("551234567", StringComparison.Ordinal));

        await using var read = StoreSchema.CreateContext(cs);
        var beneficiary = await read.Set<Beneficiary>().SingleAsync(b => b.Id == id, TestContext.Current.CancellationToken);
        beneficiary.FullName.ShouldBe(Name);
        beneficiary.Mobile.ShouldBe(Mobile);
    }

    [Fact]
    public async Task Same_plaintext_encrypts_differently_so_lookups_use_the_mobile_hash()
    {
        var cs = await StoreSchema.CreateMigratedAsync("pii_hash");
        var a = await InsertBeneficiaryAsync(cs, "B-000001", "+966551111111");
        var b = await InsertBeneficiaryAsync(cs, "B-000002", "+966552222222");

        var nameA = (await RawColumnsAsync(cs, a)).First();
        var nameB = (await RawColumnsAsync(cs, b)).First();
        nameA.ShouldNotBe(nameB);   // same name, different ciphertext

        var hash = StoreSchema.CreatePiiProtector().MobileHash("+966551111111");
        hash.ShouldMatch("^[0-9a-f]{64}$");
        (await SqlRunner.ScalarAsync<long>(cs, $"SELECT count(*) FROM store.beneficiary WHERE mobile_hash = '{hash}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_second_beneficiary_with_the_same_mobile_is_rejected_by_ux_beneficiary_mobile()
    {
        var cs = await StoreSchema.CreateMigratedAsync("pii_dup");
        await InsertBeneficiaryAsync(cs, "B-000001", Mobile);

        var ex = await Should.ThrowAsync<DbUpdateException>(() => InsertBeneficiaryAsync(cs, "B-000002", Mobile));

        ((PostgresException)ex.InnerException!).ConstraintName.ShouldBe("ux_beneficiary_mobile");
    }

    private static async Task<Guid> InsertBeneficiaryAsync(string cs, string code, string mobile)
    {
        await using var db = StoreSchema.CreateContext(cs);
        var beneficiary = new Beneficiary(code, BeneficiaryKind.Teacher, "rural_private_school_teachers", "القصيم", Name, "officer@ta3lom.sa", DateTimeOffset.UnixEpoch);
        db.Add(beneficiary);
        db.Entry(beneficiary).Property(x => x.Mobile).CurrentValue = mobile;
        db.Entry(beneficiary).Property(x => x.MobileHash).CurrentValue = StoreSchema.CreatePiiProtector().MobileHash(mobile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return beneficiary.Id;
    }

    private static async Task<IEnumerable<byte[]>> RawColumnsAsync(string cs, Guid id)
    {
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT full_name_enc, mobile_enc FROM store.beneficiary WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        return [(byte[])reader[0], (byte[])reader[1]];
    }
}
