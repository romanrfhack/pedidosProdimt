using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Prodimt.Pedidos.Infrastructure.Authentication;

namespace Prodimt.Pedidos.SqlServer.Tests;

public sealed class CustomerAccessTokenProtectionTests
{
    private const string Plain = "synthetic-customer-access-token-for-protection-tests";
    private static readonly Guid Customer = Guid.NewGuid(), Token = Guid.NewGuid();

    [Fact]
    public void ProtectedValue_RoundTripsAcrossInstancesAndUsesFreshNonces()
    {
        var first = Create();
        var protectedValue = first.Protect(Customer, Token, Plain);
        var repeated = first.Protect(Customer, Token, Plain);
        Assert.StartsWith("v1.", protectedValue);
        Assert.NotEqual(protectedValue, repeated);
        Assert.DoesNotContain(Plain, protectedValue);
        Assert.Equal(Plain, Create().Unprotect(Customer, Token, protectedValue));
        Assert.Equal(Plain, first.Unprotect(Customer, Token, repeated));
    }

    [Fact]
    public void CiphertextCannotMoveToAnotherCustomerOrTokenOrKey()
    {
        var protector = Create();
        var value = protector.Protect(Customer, Token, Plain);
        AssertFixedFailure(() => protector.Unprotect(Guid.NewGuid(), Token, value));
        AssertFixedFailure(() => protector.Unprotect(Customer, Guid.NewGuid(), value));
        AssertFixedFailure(() => Create(2).Unprotect(Customer, Token, value));
    }

    [Theory]
    [InlineData(0)] // Nonce.
    [InlineData(12)] // Authentication tag.
    [InlineData(28)] // Ciphertext.
    public void TamperedEnvelope_FailsAuthenticationWithoutDisclosingDetails(int offset)
    {
        var protector = Create();
        var value = protector.Protect(Customer, Token, Plain);
        var bytes = Convert.FromBase64String(value[3..]);
        bytes[offset] ^= 1;
        AssertFixedFailure(() => protector.Unprotect(Customer, Token, "v1." + Convert.ToBase64String(bytes)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v2.AAAA")]
    [InlineData("v1.not-base64")]
    [InlineData("v1.AAAA")]
    public void UnknownMalformedOrTruncatedEnvelope_FailsClosed(string value)
        => AssertFixedFailure(() => Create().Unprotect(Customer, Token, value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64-key")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")] // 128 bits is not the required dedicated 256-bit key.
    public void MissingOrMalformedDedicatedKey_DoesNotFallBackToJwtOrThrowAtConstruction(string? key)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            [AesGcmCustomerAccessTokenProtector.ConfigurationKey] = key,
            ["Authentication:Jwt:SigningKey"] = "synthetic-JWT-key-must-not-be-used-for-access-encryption"
        }).Build();
        var protector = new AesGcmCustomerAccessTokenProtector(cfg);
        AssertFixedFailure(() => protector.Protect(Customer, Token, Plain));
        var validEnvelope = Create().Protect(Customer, Token, Plain);
        AssertFixedFailure(() => protector.Unprotect(Customer, Token, validEnvelope));
    }

    [Fact]
    public void NonCanonicalOrOversizedInputs_AreRejectedBeforeRecovery()
    {
        var protector = Create();
        var value = protector.Protect(Customer, Token, Plain);
        AssertFixedFailure(() => protector.Unprotect(Customer, Token, value + "\n"));
        AssertFixedFailure(() => protector.Unprotect(Customer, Token, "v1." + new string('A', 2048)));
        AssertFixedFailure(() => protector.Protect(Customer, Token, new string('x', 257)));
        AssertFixedFailure(() => protector.Protect(Guid.Empty, Token, Plain));
        AssertFixedFailure(() => protector.Unprotect(Customer, Guid.Empty, value));
    }

    private static AesGcmCustomerAccessTokenProtector Create(byte fill = 1)
    {
        var key = Enumerable.Repeat(fill, 32).ToArray();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            [AesGcmCustomerAccessTokenProtector.ConfigurationKey] = Convert.ToBase64String(key)
        }).Build();
        CryptographicOperations.ZeroMemory(key);
        return new(configuration);
    }

    private static void AssertFixedFailure(Func<string> operation)
    {
        var exception = Assert.Throws<InvalidOperationException>(operation);
        Assert.Equal("La protección de tokens de acceso no está disponible.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(Plain, exception.ToString());
        Assert.DoesNotContain("synthetic-JWT-key", exception.ToString());
    }
}
