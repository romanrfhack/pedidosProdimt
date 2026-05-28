using Microsoft.Extensions.Configuration;

namespace Prodimt.Pedidos.Infrastructure.Authentication;

public sealed record DevelopmentAuthSeedValues(
    string AdminUserName,
    string AdminPassword,
    string CustomerToken)
{
    public const string DefaultAdminUserName = "admin";
    public const string DefaultAdminPassword = "prodimt-admin-demo";
    public const string DefaultCustomerToken = "demo-customer-token";

    public static DevelopmentAuthSeedValues FromConfiguration(IConfiguration? configuration)
    {
        return new DevelopmentAuthSeedValues(
            Read(configuration, "DevelopmentSeed:AdminUserName", "PRODIMT_DEMO_ADMIN_USERNAME", DefaultAdminUserName),
            Read(configuration, "DevelopmentSeed:AdminPassword", "PRODIMT_DEMO_ADMIN_PASSWORD", DefaultAdminPassword),
            Read(configuration, "DevelopmentSeed:CustomerToken", "PRODIMT_DEMO_CUSTOMER_TOKEN", DefaultCustomerToken));
    }

    private static string Read(
        IConfiguration? configuration,
        string configurationKey,
        string environmentVariableName,
        string defaultValue)
    {
        return configuration?[configurationKey]
            ?? Environment.GetEnvironmentVariable(environmentVariableName)
            ?? defaultValue;
    }
}
