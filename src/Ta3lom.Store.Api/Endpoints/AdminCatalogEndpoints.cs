using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.AddBookFormat;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.ArchiveProduct;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.CloseOffer;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.CreateOffer;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.CreateProduct;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.PublishProduct;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.ReplaceModules;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.SetGoalSeed;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpdateBookFormat;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpdateOffer;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpdateProduct;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpsertBookDetail;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpsertCauseDetail;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpsertCourseDetail;
using Ta3lom.Store.Application.Features.AdminCatalog.Queries.GetAdminProduct;
using Ta3lom.Store.Application.Features.AdminCatalog.Queries.ListAdminProducts;
using Ta3lom.Store.Application.Features.AdminCatalog.Queries.ListOffers;
using Ta3lom.Store.Application.Features.AdminCatalog.Queries.PreviewProduct;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/products`, `/v1/admin/formats`, `/v1/admin/offers` (11 §9.3–§9.4, endpoints #54–#71; RUL-01 … RUL-07,
/// RUL-10). Reads need `staff:read`, writes `staff:catalogue`; writes are audited and evict the public cache.
internal sealed class AdminCatalogEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").WithTags("Admin · Catalogue").RequireRateLimiting(RatePolicies.Admin)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        var read = admin.MapGroup(string.Empty).RequireAuthorization(Policies.StaffRead);
        var write = admin.MapGroup(string.Empty).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        read.MapGet("/products", ListAsync).Produces<PagedResult<AdminProductRow>>();
        write.MapPost("/products", CreateAsync).Produces<AdminProductResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        read.MapGet("/products/{id:guid}", GetAsync).Produces<AdminProductResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        write.MapPut("/products/{id:guid}", UpdateAsync).Produces<AdminProductResponse>().ProducesProblem(StatusCodes.Status409Conflict);
        write.MapPut("/products/{id:guid}/course", CourseAsync).Produces<AdminProductResponse>();
        write.MapPut("/products/{id:guid}/modules", ModulesAsync).Produces<AdminProductResponse>();
        write.MapPut("/products/{id:guid}/book", BookAsync).Produces<AdminProductResponse>();
        write.MapPost("/products/{id:guid}/formats", AddFormatAsync).Produces<AdminBookFormat>(StatusCodes.Status201Created);
        write.MapPut("/formats/{formatId:guid}", UpdateFormatAsync).Produces<AdminBookFormat>();
        write.MapPut("/products/{id:guid}/cause", CauseAsync).Produces<AdminProductResponse>().ProducesProblem(StatusCodes.Status409Conflict);
        write.MapPost("/products/{id:guid}/publish", PublishAsync).Produces<AdminProductResponse>().ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        write.MapPost("/products/{id:guid}/archive", ArchiveAsync).Produces<AdminProductResponse>();
        read.MapGet("/products/{id:guid}/preview", PreviewAsync).Produces<ProductDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        write.MapPut("/products/{id:guid}/goal-seed", GoalSeedAsync).Produces<GoalFiguresResponse>();
        read.MapGet("/products/{id:guid}/offers", OffersAsync).Produces<IReadOnlyList<OfferResponse>>();
        write.MapPost("/products/{id:guid}/offers", CreateOfferAsync).Produces<OfferResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        write.MapPut("/offers/{offerId:guid}", UpdateOfferAsync).Produces<OfferResponse>();
        write.MapPost("/offers/{offerId:guid}/close", CloseOfferAsync).Produces<CloseOfferResponse>();
    }

    private static async Task<IResult> ListAsync([FromServices] IQueryHandler<ListAdminProductsQuery, PagedResult<AdminProductRow>> handler, CancellationToken ct,
        string? type = null, string? status = null, string? category = null, string? q = null, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListAdminProductsQuery(type, status, category, q, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> CreateAsync([FromBody] ProductInput input, [FromServices] ICommandHandler<CreateProductCommand, AdminProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CreateProductCommand(input), ct)).ToHttp(p => TypedResults.Created($"/v1/admin/products/{p.Id}", p));

    private static async Task<IResult> GetAsync(Guid id, [FromServices] IQueryHandler<GetAdminProductQuery, AdminProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetAdminProductQuery(id), ct)).ToHttp();

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] ProductInput input, [FromServices] ICommandHandler<UpdateProductCommand, AdminProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateProductCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> CourseAsync(Guid id, [FromBody] CourseDetailInput input, [FromServices] ICommandHandler<UpsertCourseDetailCommand, AdminProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpsertCourseDetailCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> ModulesAsync(Guid id, [FromBody] IReadOnlyList<CourseModuleInput> input, [FromServices] ICommandHandler<ReplaceModulesCommand, AdminProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ReplaceModulesCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> BookAsync(Guid id, [FromBody] BookDetailInput input, [FromServices] ICommandHandler<UpsertBookDetailCommand, AdminProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpsertBookDetailCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> AddFormatAsync(Guid id, [FromBody] BookFormatInput input, [FromServices] ICommandHandler<AddBookFormatCommand, AdminBookFormat> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new AddBookFormatCommand(id, input), ct)).ToHttp(f => TypedResults.Created($"/v1/admin/formats/{f.Id}", f));

    private static async Task<IResult> UpdateFormatAsync(Guid formatId, [FromBody] BookFormatInput input, [FromServices] ICommandHandler<UpdateBookFormatCommand, AdminBookFormat> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateBookFormatCommand(formatId, input), ct)).ToHttp();

    private static async Task<IResult> CauseAsync(Guid id, [FromBody] CauseDetailInput input, [FromServices] ICommandHandler<UpsertCauseDetailCommand, AdminProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpsertCauseDetailCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> PublishAsync(Guid id, [FromServices] ICommandHandler<PublishProductCommand, AdminProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new PublishProductCommand(id), ct)).ToHttp();

    private static async Task<IResult> ArchiveAsync(Guid id, [FromServices] ICommandHandler<ArchiveProductCommand, AdminProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ArchiveProductCommand(id), ct)).ToHttp();

    private static async Task<IResult> PreviewAsync(Guid id, [FromServices] IQueryHandler<PreviewProductQuery, ProductDetailResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new PreviewProductQuery(id), ct)).ToHttp();

    private static async Task<IResult> GoalSeedAsync(Guid id, [FromBody] GoalSeedInput input, [FromServices] ICommandHandler<SetGoalSeedCommand, GoalFiguresResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new SetGoalSeedCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> OffersAsync(Guid id, [FromServices] IQueryHandler<ListOffersQuery, IReadOnlyList<OfferResponse>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListOffersQuery(id), ct)).ToHttp();

    private static async Task<IResult> CreateOfferAsync(Guid id, [FromBody] OfferInput input, [FromServices] ICommandHandler<CreateOfferCommand, OfferResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CreateOfferCommand(id, input), ct)).ToHttp(o => TypedResults.Created($"/v1/admin/offers/{o.Id}", o));

    private static async Task<IResult> UpdateOfferAsync(Guid offerId, [FromBody] OfferInput input, [FromServices] ICommandHandler<UpdateOfferCommand, OfferResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateOfferCommand(offerId, input), ct)).ToHttp();

    private static async Task<IResult> CloseOfferAsync(Guid offerId, [FromBody] CloseOfferInput? input, [FromServices] ICommandHandler<CloseOfferCommand, CloseOfferResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CloseOfferCommand(offerId, input?.SweepPool ?? false), ct)).ToHttp();
}
