namespace Ta3lom.Store.Application.Interfaces;

/// RUL-41: signed download tokens, valid 10 minutes, bound to the grant and the customer (`Store:Downloads:LinkKeyBase64`).
public interface IDownloadLinks
{
    string Create(Guid grantId, Guid customerId, DateTimeOffset expiresAt);

    /// Null when the signature is wrong or the token expired.
    DownloadToken? Read(string token, DateTimeOffset now);
}

public sealed record DownloadToken(Guid GrantId, Guid CustomerId, DateTimeOffset ExpiresAt);

/// RUL-41 (P0-4): stamps every page with the buyer's name, masked mobile and order reference.
public interface IPdfWatermarker
{
    Task<byte[]> WatermarkAsync(Stream source, PdfWatermark watermark, CancellationToken ct = default);
}

public sealed record PdfWatermark(string BuyerName, string MaskedMobile, string OrderReference);
