using System.Reflection;
using Ta3lom.Store.Application;
using Ta3lom.Store.Contracts.Common;
using Ta3lom.Store.Domain.Common;
using Ta3lom.Store.Infrastructure.Time;

namespace Ta3lom.Store.ArchitectureTests.Support;

internal static class Assemblies
{
    public static readonly Assembly Domain = typeof(Money).Assembly;
    public static readonly Assembly Application = typeof(ApplicationAssemblyMarker).Assembly;
    public static readonly Assembly Contracts = typeof(PagedResponse<>).Assembly;
    public static readonly Assembly Infrastructure = typeof(SystemClock).Assembly;
    public static readonly Assembly Api = typeof(Program).Assembly;

    public static readonly Assembly[] Production = [Domain, Application, Contracts, Infrastructure, Api];

    public static readonly Assembly[] Tests =
    [
        typeof(Ta3lom.Store.Domain.Tests.Common.MoneyTests).Assembly,
        typeof(Ta3lom.Store.Application.Tests.Abstractions.ResultTests).Assembly,
        typeof(Ta3lom.Store.Api.IntegrationTests.Infrastructure.StoreApiFactory).Assembly,
    ];

    public static string Doc(string fileName) => Path.Combine(AppContext.BaseDirectory, "docs", fileName);
}
