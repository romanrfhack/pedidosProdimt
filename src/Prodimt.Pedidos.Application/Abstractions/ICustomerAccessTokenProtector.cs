namespace Prodimt.Pedidos.Application.Abstractions;

// Administrative recovery only. Customer authentication continues to validate the independent hash.
public interface ICustomerAccessTokenProtector
{
    string Protect(Guid customerId, Guid tokenId, string plainToken);

    string Unprotect(Guid customerId, Guid tokenId, string protectedToken);
}
