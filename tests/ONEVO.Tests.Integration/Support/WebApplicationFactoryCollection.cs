namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Every test class that constructs a WebApplicationFactory&lt;Program&gt; via
/// IntegrationTestEnvironmentScope must be tagged [Collection(Name)]. IntegrationTestEnvironmentScope
/// mutates process-wide environment variables for the window between "set them" and "the factory's
/// first host build" - xUnit runs distinct test classes in parallel by default, so without a shared
/// collection two of these tests could race and one host could boot against another test's ephemeral
/// database. Tests that never build a WebApplicationFactory (they construct ApplicationDbContext
/// directly, e.g. RestrictedRoleRlsEnforcementTests) do not need this collection and keep running in
/// parallel with everything else.
/// </summary>
[CollectionDefinition(Name)]
public sealed class WebApplicationFactoryCollection
{
    public const string Name = "WebApplicationFactory host bootstrap";
}
