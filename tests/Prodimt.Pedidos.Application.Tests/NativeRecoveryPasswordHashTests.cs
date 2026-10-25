using System.Diagnostics;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.Application.Tests;

public sealed class NativeRecoveryPasswordHashTests
{
    [Theory]
    [InlineData("Fictional reset password 2026!")]
    [InlineData("Contraseña-ficticia-ñ-漢字-123!$(no-shell)")]
    public async Task PythonRecoveryHash_IsAcceptedByTheDeployedPasswordService(string password)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "scripts", "pilot", "reset-native-admin.py")))
            root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo("python3")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import importlib.util,sys; s=importlib.util.spec_from_file_location('recovery_fixture',sys.argv[1]); m=importlib.util.module_from_spec(s); s.loader.exec_module(m); print(m.identity_hash(sys.stdin.read()))");
        start.ArgumentList.Add(Path.Combine(root.FullName, "scripts", "pilot", "reset-native-admin.py"));
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(password);
        process.StandardInput.Close();
        var hash = (await process.StandardOutput.ReadToEndAsync()).Trim();
        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(string.Empty, errors);
        var service = new PasswordHashService();
        Assert.True(service.VerifyPassword(hash, password));
        Assert.False(service.VerifyPassword(hash, password + "wrong"));
    }
}
