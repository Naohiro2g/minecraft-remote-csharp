using Xunit;

namespace Code2Create.MinecraftRemote.Client.Tests;

public class RootNamespaceTests
{
    [Fact]
    public void LibraryTypesUseTheRootNamespace()
    {
        Assert.Equal("Code2Create.MinecraftRemote.Client", typeof(AssemblyMarker).Namespace);
    }
}
