using Microsoft.AspNetCore.Identity;
using Prodimt.Pedidos.Application.Abstractions;

namespace Prodimt.Pedidos.Infrastructure.Authentication;

public sealed class PasswordHashService : IPasswordHashService
{
    private readonly PasswordHasher<object> _passwordHasher = new();
    private readonly object _user = new();

    public string HashPassword(string password)
    {
        return _passwordHasher.HashPassword(_user, password);
    }

    public bool VerifyPassword(string passwordHash, string password)
    {
        var result = _passwordHasher.VerifyHashedPassword(_user, passwordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
