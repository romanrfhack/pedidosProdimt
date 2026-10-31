using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Prodimt.Pedidos.Infrastructure.Persistence;

internal static class CatalogPersistenceConflicts
{
    public static bool IsConcurrentChange(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
            if (cause is DbUpdateConcurrencyException or SqlException { Number: 1205 }) return true;
        return false;
    }
}
