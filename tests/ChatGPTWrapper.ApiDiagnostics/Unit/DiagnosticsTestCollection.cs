namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[CollectionDefinition(DiagnosticsTestCollection.Name, DisableParallelization = true)]
public sealed class DiagnosticsTestCollectionDefinition : ICollectionFixture<object>
{
}

public static class DiagnosticsTestCollection
{
    public const string Name = "Diagnostics isolation";
}
