using System.Security.Cryptography;
using System.Text;
using Prodimt.Pedidos.Application.Abstractions;
using Prodimt.Pedidos.Application.AdminOrders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Application.AdminCatalogs;

public sealed class AdminCustomerAccessTokenService(
    ICustomerRepository customers,
    ICustomerAccessTokenRepository accessTokens,
    ICustomerAccessTokenHasher tokenHasher,
    IAuditLogRepository auditLogs,
    IDateTimeProvider dateTimeProvider,
    ICustomerAccessTokenProtector tokenProtector)
{
    public async Task<IReadOnlyList<AdminCustomerAccessTokenResponse>> GetByCustomerAsync(
        Guid customerId, CancellationToken cancellationToken, bool activeOnly = false)
    {
        var customer = await GetRequiredCustomerAsync(customerId, cancellationToken);
        var tokens = await accessTokens.GetByCustomerIdAsync(customerId, cancellationToken);
        var now = dateTimeProvider.Now;
        return tokens.Select(token => MapToken(token, customer, now))
            .Where(token => !activeOnly || token.IsUsable).ToArray();
    }

    public Task<CreatedCustomerAccessTokenResponse> CreateAsync(
        Guid customerId, CreateCustomerAccessTokenRequest request, AdminActorContext? actor,
        CancellationToken cancellationToken)
        => accessTokens.ExecuteMutationAsync(async ct =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var customer = await GetRequiredCustomerAsync(customerId, ct);
            if (!customer.IsActive)
                throw new ArgumentException("No se puede crear token para un cliente inactivo.", nameof(customerId));
            var now = dateTimeProvider.Now;
            ValidateExpiry(request.ExpiresAt, request.ReminderLink, now);
            if (request.ReminderLink) CustomerPhoneNumber.Normalize(customer.PhoneNumber);
            var description = NormalizeDescription(request.Description, request.ReminderLink, allowDefault: true);
            var plainToken = GeneratePlainToken();
            var token = CustomerAccessToken.Create(customerId, tokenHasher.HashToken(plainToken), description,
                request.ExpiresAt, now, request.ReminderLink);
            // Encrypt before staging any mutation. Plaintext is never included in an audit or a listing.
            try { token.ProtectedToken = tokenProtector.Protect(customerId, token.Id, plainToken); }
            catch (InvalidOperationException) { throw new CustomerAccessTokenProtectionException(); }
            await accessTokens.AddAsync(token, ct);
            await auditLogs.AddAsync(CatalogAudit.Create(
                CatalogEntityTypes.CustomerAccessToken, token.Id, CatalogAuditEventTypes.CustomerAccessTokenCreated,
                now, actor, "Token de acceso creado para cliente.",
                new { token.CustomerId, description = token.DisplayName, token.ExpiresAt, version = token.MetadataVersion }), ct);
            await accessTokens.SaveChangesAsync(ct);
            return MapSecret(token, plainToken);
        }, cancellationToken);

    public Task<AdminCustomerAccessTokenResponse> UpdateAsync(
        Guid customerId, Guid tokenId, UpdateCustomerAccessTokenRequest request,
        AdminActorContext? actor, CancellationToken cancellationToken)
        => accessTokens.ExecuteMutationAsync(async ct =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var customer = await GetRequiredCustomerAsync(customerId, ct);
            var token = await GetRequiredTokenAsync(customerId, tokenId, ct);
            var now = dateTimeProvider.Now;
            EnsureUsable(token, customer, now);
            if (request.ExpectedVersion < 1 || request.ExpectedVersion != token.MetadataVersion)
                throw new CatalogConflictException("El acceso cambió. Recarga la lista antes de editarlo.");
            var description = NormalizeDescription(request.Description, token.IsReminderLink, allowDefault: false);
            ValidateExpiry(request.ExpiresAt, token.IsReminderLink, now);
            // Only mutable metadata changes. Identity, ciphertext, hash and usage timestamp are retained.
            token.DisplayName = description;
            token.ExpiresAt = request.ExpiresAt;
            token.MetadataVersion = checked(token.MetadataVersion + 1);
            await auditLogs.AddAsync(CatalogAudit.Create(
                CatalogEntityTypes.CustomerAccessToken, token.Id, CatalogAuditEventTypes.CustomerAccessTokenUpdated,
                now, actor, "Descripción o vigencia de acceso actualizada.",
                new { token.CustomerId, description = token.DisplayName, token.ExpiresAt, version = token.MetadataVersion }), ct);
            await accessTokens.SaveChangesAsync(ct);
            return MapToken(token, customer, now);
        }, cancellationToken);

    public Task<CreatedCustomerAccessTokenResponse> RevealAsync(
        Guid customerId, Guid tokenId, AdminActorContext? actor, CancellationToken cancellationToken)
        => accessTokens.ExecuteMutationAsync(async ct =>
        {
            var customer = await GetRequiredCustomerAsync(customerId, ct);
            var token = await GetRequiredTokenAsync(customerId, tokenId, ct);
            EnsureUsable(token, customer, dateTimeProvider.Now);
            if (string.IsNullOrWhiteSpace(token.ProtectedToken))
                throw new CatalogConflictException("El token original no está disponible. Genera un acceso nuevo.");
            string plainToken;
            try { plainToken = tokenProtector.Unprotect(customerId, tokenId, token.ProtectedToken); }
            catch (InvalidOperationException) { throw new CustomerAccessTokenProtectionException(); }
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token.TokenHash),
                Encoding.UTF8.GetBytes(tokenHasher.HashToken(plainToken))))
                throw new CustomerAccessTokenProtectionException();
            // Recheck after decryption, before persisting the successful administrative recovery.
            EnsureUsable(token, customer, dateTimeProvider.Now);
            await auditLogs.AddAsync(CatalogAudit.Create(
                CatalogEntityTypes.CustomerAccessToken, token.Id, CatalogAuditEventTypes.CustomerAccessTokenRevealed,
                dateTimeProvider.Now, actor, "Acceso recuperado para copiar o compartir.",
                new { token.CustomerId, version = token.MetadataVersion }), ct);
            await accessTokens.SaveChangesAsync(ct);
            return MapSecret(token, plainToken);
        }, cancellationToken);

    public Task<AdminCustomerAccessTokenResponse> RevokeAsync(
        Guid customerId, Guid tokenId, AdminActorContext? actor, CancellationToken cancellationToken)
        => accessTokens.ExecuteMutationAsync(async ct =>
        {
            var customer = await GetRequiredCustomerAsync(customerId, ct);
            var token = await GetRequiredTokenAsync(customerId, tokenId, ct);
            if (token.IsActive)
            {
                token.Revoke();
                await auditLogs.AddAsync(CatalogAudit.Create(
                    CatalogEntityTypes.CustomerAccessToken, token.Id, CatalogAuditEventTypes.CustomerAccessTokenRevoked,
                    dateTimeProvider.Now, actor, "Token de acceso de cliente revocado.",
                    new { token.CustomerId, version = token.MetadataVersion }), ct);
                await accessTokens.SaveChangesAsync(ct);
            }
            return MapToken(token, customer, dateTimeProvider.Now);
        }, cancellationToken);

    private async Task<Customer> GetRequiredCustomerAsync(Guid customerId, CancellationToken cancellationToken)
        => await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("El cliente no existe.");

    private async Task<CustomerAccessToken> GetRequiredTokenAsync(Guid customerId, Guid tokenId, CancellationToken cancellationToken)
    {
        var token = await accessTokens.GetByIdAsync(tokenId, cancellationToken);
        if (token is null || token.CustomerId != customerId)
            throw new InvalidOperationException("El acceso no existe para este cliente.");
        return token;
    }

    private static void EnsureUsable(CustomerAccessToken token, Customer customer, DateTimeOffset now)
    {
        if (!customer.IsActive || !token.IsUsableAt(now))
            throw new CatalogConflictException("El acceso está vencido, revocado o el cliente está inactivo. Actualiza la lista.");
    }

    private static void ValidateExpiry(DateTimeOffset? expiresAt, bool reminderLink, DateTimeOffset now)
    {
        if (reminderLink && expiresAt is null)
            throw new ArgumentException("La vigencia es obligatoria para el enlace de recordatorio.", nameof(expiresAt));
        if (expiresAt is not null && expiresAt <= now)
            throw new ArgumentException("La expiración del acceso debe ser futura.", nameof(expiresAt));
    }

    private static string NormalizeDescription(string? description, bool reminderLink, bool allowDefault)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            if (!allowDefault) throw new ArgumentException("La descripción es obligatoria.", nameof(description));
            return reminderLink ? "Recuerda hacer tu próximo pedido" : "Token de cliente";
        }
        var normalized = description.Trim();
        if (normalized.Length > 200)
            throw new ArgumentException("La descripción admite hasta 200 caracteres.", nameof(description));
        return normalized;
    }

    private static string GeneratePlainToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static AdminCustomerAccessTokenResponse MapToken(CustomerAccessToken token, Customer customer, DateTimeOffset now)
    {
        var usable = customer.IsActive && token.IsUsableAt(now);
        return new(token.Id, token.CustomerId, token.DisplayName, token.ExpiresAt, token.IsActive,
            token.CreatedAt, token.LastUsedAt, token.MetadataVersion, usable,
            usable && !string.IsNullOrWhiteSpace(token.ProtectedToken), token.IsReminderLink);
    }

    private static CreatedCustomerAccessTokenResponse MapSecret(CustomerAccessToken token, string plainToken)
        => new(token.Id, token.CustomerId, plainToken, token.DisplayName, token.ExpiresAt, token.IsActive,
            token.MetadataVersion, token.IsReminderLink);
}
