using Naraka.Server.Application.Modules;
using Naraka.Server.Domain;

namespace Naraka.Server.ArchitectureTests;

public sealed class DependencyDirectionTests
{
    [Fact]
    public void DomainDoesNotReferenceOuterLayers()
    {
        var references = typeof(RequestId).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name is not null && reference.Name.StartsWith("Naraka.Server.", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationDoesNotReferenceInfrastructureOrHost()
    {
        var references = typeof(ModuleCatalog).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name is "Naraka.Server.Infrastructure" or "Naraka.Server.Host");
    }

    [Fact]
    public void RequiredModulesAreDeclaredOnce()
    {
        Assert.Equal(ModuleCatalog.Names.Count, ModuleCatalog.Names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("Expedition", ModuleCatalog.Names);
        Assert.Contains("Presence", ModuleCatalog.Names);
    }
}
