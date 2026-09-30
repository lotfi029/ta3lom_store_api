using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Application.DTOs.Checkout;
using Ta3lom.Store.Application.DTOs.Orders;
using Ta3lom.Store.Application.Features.Account;
using Ta3lom.Store.Application.Features.Downloads.Commands.CreateDownloadLink;
using Ta3lom.Store.Application.Features.Enrolments.Commands.LaunchCourse;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/me` — the customer's account (11 §5, #21–#39): bearer only, `no-store`, rate policy `me`. Every route sees the caller's
/// own rows only.
internal sealed class MeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/me").WithTags("Me").RequireAuthorization(Policies.Customer).RequireRateLimiting(RatePolicies.Me)
            .AddEndpointFilter(async (ctx, next) =>
            {
                ctx.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(ctx);
            });

        me.MapGet("/", (IQueryHandler<GetProfileQuery, ProfileResponse> h, CancellationToken ct) => Run(h.HandleAsync(new GetProfileQuery(), ct)))
            .Produces<ProfileResponse>();
        me.MapPatch("/", async ([FromBody] UpdateProfileRequest body, [FromServices] ICommandHandler<UpdateProfileCommand, ProfileResponse> h, CancellationToken ct) =>
            (await h.HandleAsync(new UpdateProfileCommand(body), ct)).ToHttp()).Produces<ProfileResponse>();
        me.MapDelete("/", DeleteAsync).Produces(StatusCodes.Status204NoContent);

        me.MapGet("/addresses", (IQueryHandler<ListAddressesQuery, IReadOnlyList<AddressResponse>> h, CancellationToken ct) =>
            Run(h.HandleAsync(new ListAddressesQuery(), ct))).Produces<IReadOnlyList<AddressResponse>>();
        me.MapPost("/addresses", async ([FromBody] AddressInput body, [FromServices] ICommandHandler<SaveAddressCommand, AddressResponse> h, CancellationToken ct) =>
            (await h.HandleAsync(new SaveAddressCommand(null, body), ct)).ToHttp(a => TypedResults.Created($"/v1/me/addresses/{a.Id}", a)))
            .Produces<AddressResponse>(StatusCodes.Status201Created);
        me.MapPut("/addresses/{id:guid}", async (Guid id, [FromBody] AddressInput body, [FromServices] ICommandHandler<SaveAddressCommand, AddressResponse> h,
            CancellationToken ct) => (await h.HandleAsync(new SaveAddressCommand(id, body), ct)).ToHttp())
            .Produces<AddressResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        me.MapDelete("/addresses/{id:guid}", async (Guid id, [FromServices] ICommandHandler<DeleteAddressCommand> h, CancellationToken ct) =>
            (await h.HandleAsync(new DeleteAddressCommand(id), ct)).ToHttp(() => TypedResults.NoContent()))
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        me.MapPost("/addresses/{id:guid}/default", async (Guid id, [FromServices] ICommandHandler<SetDefaultAddressCommand, AddressResponse> h, CancellationToken ct) =>
            (await h.HandleAsync(new SetDefaultAddressCommand(id), ct)).ToHttp()).Produces<AddressResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/enrolments", (IQueryHandler<ListEnrolmentsQuery, IReadOnlyList<EnrolmentItem>> h, CancellationToken ct) =>
            Run(h.HandleAsync(new ListEnrolmentsQuery(), ct))).Produces<IReadOnlyList<EnrolmentItem>>();
        me.MapPost("/enrolments/{id:guid}/launch", LaunchAsync).Produces<LaunchResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        me.MapGet("/enrolments/{id:guid}/certificate", async (Guid id, HttpContext http, [FromServices] IQueryHandler<CertificateQuery, string> h, CancellationToken ct) =>
            (await h.HandleAsync(new CertificateQuery(id, Locale(http)), ct)).ToHttp(url => TypedResults.Redirect(url)))
            .Produces(StatusCodes.Status302Found).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        me.MapGet("/books", (IQueryHandler<BooksQuery, BooksResponse> h, CancellationToken ct) => Run(h.HandleAsync(new BooksQuery(), ct)))
            .Produces<BooksResponse>();
        me.MapPost("/downloads/{grantId:guid}/link", DownloadLinkAsync).RequireRateLimiting(RatePolicies.Download).Produces<DownloadLinkResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status410Gone);

        me.MapGet("/giving", (IQueryHandler<MyGivingQuery, GivingResponse> h, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Run(h.HandleAsync(new MyGivingQuery(page, pageSize), ct))).Produces<GivingResponse>();
        me.MapGet("/giving/lines/{orderLineId:guid}", (Guid orderLineId, IQueryHandler<MyGivingLineQuery, GivingLineDetail> h, CancellationToken ct) =>
            Run(h.HandleAsync(new MyGivingLineQuery(orderLineId), ct))).Produces<GivingLineDetail>().ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/orders", (IQueryHandler<ListMyOrdersQuery, PagedResult<OrderSummary>> h, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Run(h.HandleAsync(new ListMyOrdersQuery(page, pageSize), ct))).Produces<PagedResult<OrderSummary>>();
        me.MapGet("/orders/{reference}", (string reference, IQueryHandler<GetMyOrderQuery, OrderDetailResponse> h, CancellationToken ct) =>
            Run(h.HandleAsync(new GetMyOrderQuery(reference), ct))).Produces<OrderDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        me.MapGet("/documents/{number}", async (string number, [FromServices] IQueryHandler<GetMyDocumentQuery, DocumentFile> h, CancellationToken ct) =>
            (await h.HandleAsync(new GetMyDocumentQuery(number), ct)).ToHttp(f => TypedResults.Stream(f.Content, "application/pdf", f.FileName)))
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        me.MapGet("/export", ExportAsync).Produces<MeExport>();
    }

    private static async Task<IResult> Run<T>(Task<Ta3lom.Store.SharedKernel.Result<T>> call) => (await call).ToHttp();

    private static string Locale(HttpContext http) =>
        http.Request.Headers.AcceptLanguage.ToString().StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar";

    private static async Task<IResult> DeleteAsync(HttpContext http, [FromBody] DeleteAccountRequest? body, [FromServices] ICommandHandler<DeleteAccountCommand> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new DeleteAccountCommand(body?.Confirm ?? false), ct);
        if (result.IsSuccess)
        {
            AuthCookies.Clear(http.Response, AuthCookies.CustomerRefresh, AuthCookies.CustomerRefreshPath);
        }

        return result.ToHttp(() => TypedResults.NoContent());
    }

    private static async Task<IResult> ExportAsync(HttpContext http, [FromServices] IQueryHandler<ExportMeQuery, MeExport> handler,
        [FromServices] IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json, CancellationToken ct) =>
        (await handler.HandleAsync(new ExportMeQuery(), ct)).ToHttp(export =>
            TypedResults.File(JsonSerializer.SerializeToUtf8Bytes(export, json.Value.SerializerOptions), "application/json", "ta3lom-store-export.json"));

    private static async Task<IResult> DownloadLinkAsync(Guid grantId, [FromServices] ICommandHandler<CreateDownloadLinkCommand, DownloadLinkResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new CreateDownloadLinkCommand(grantId), ct)).ToHttp();

    private static async Task<IResult> LaunchAsync(Guid id, HttpContext http, [FromServices] ICommandHandler<LaunchCourseCommand, LaunchResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new LaunchCourseCommand(id, Locale(http)), ct)).ToHttp();
}
