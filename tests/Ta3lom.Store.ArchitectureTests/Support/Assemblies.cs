using System.Reflection;
using Ta3lom.Store.Application;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.Infrastructure.Services.Time;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.ArchitectureTests.Support;

internal static class Assemblies
{
    public static readonly Assembly SharedKernel = typeof(Result).Assembly;
    public static readonly Assembly Domain = typeof(Money).Assembly;
    public static readonly Assembly Application = typeof(ApplicationAssemblyMarker).Assembly;
    public static readonly Assembly Infrastructure = typeof(SystemClock).Assembly;
    public static readonly Assembly Api = typeof(Program).Assembly;

    public static readonly Assembly[] Production = [SharedKernel, Domain, Application, Infrastructure, Api];

    public static readonly Assembly[] Tests =
    [
        typeof(Ta3lom.Store.Domain.Tests.Common.MoneyTests).Assembly,
        typeof(Ta3lom.Store.Application.Tests.Abstractions.ResultTests).Assembly,
        typeof(Ta3lom.Store.Api.IntegrationTests.Infrastructure.StoreApiFactory).Assembly,
    ];

    public static string Doc(string fileName) => Path.Combine(AppContext.BaseDirectory, "docs", fileName);
}
