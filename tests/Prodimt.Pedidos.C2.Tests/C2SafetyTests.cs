using System.Text.Json;

namespace Prodimt.Pedidos.C2.Tests;

public sealed class C2SafetyTests
{
    [Theory]
    [InlineData("194.238.26.70,1433")]
    [InlineData("pedidos.prodimt.site,1433")]
    [InlineData("127.0.0.1,1433")]
    [InlineData("localhost,15433")]
    [InlineData("https://pedidos.prodimt.site/")]
    [InlineData("Server=127.0.0.1;Database=ProdimtPedidos")]
    public void RejectsAnyConnectionOtherThanTheOwnedLoopbackInstance(string source)
    {
        WithConfiguration(source, (file, _) => Assert.Throws<InvalidOperationException>(() => C2Configuration.Read(file)));
    }

    [Fact]
    public void RequiresTheExactServerAndRunOwner()
    {
        WithConfiguration("127.0.0.1,15433", (file, value) =>
        {
            Assert.Equal(value, C2Configuration.Read(file));
            File.WriteAllText(file, JsonSerializer.Serialize(value with { ServerName = "another-server" }));
            Assert.Throws<InvalidOperationException>(() => C2Configuration.Read(file));
        });
    }

    [Fact]
    public void AllFunctionalCasesHaveUniqueStableNames()
    {
        Assert.Equal(43, C2Scenarios.Names.Length);
        Assert.Equal(C2Scenarios.Names.Length, C2Scenarios.Names.Distinct().Count());
    }

    private static void WithConfiguration(string source, Action<string, C2Configuration> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "c2-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "config.json");
        var id = Guid.NewGuid().ToString("N");
        var configuration = new C2Configuration(id, source, "c2-" + id, Guid.NewGuid().ToString("N"), directory);
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(configuration));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            action(file, configuration);
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }
}
