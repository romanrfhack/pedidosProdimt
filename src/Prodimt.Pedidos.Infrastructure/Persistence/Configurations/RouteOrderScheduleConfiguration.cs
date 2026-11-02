using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;
namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;
public sealed class RouteOrderScheduleConfiguration : IEntityTypeConfiguration<RouteOrderSchedule>
{
    public void Configure(EntityTypeBuilder<RouteOrderSchedule> b)
    {
        b.ToTable("RouteOrderSchedules", t => t.HasCheckConstraint("CK_RouteOrderSchedules_Cutoff", "[AutomaticConfirmationCutoff] IS NULL OR [AutomaticConfirmationCutoff] < '18:00:00'")); b.HasKey(x => x.RouteCode);
        b.Property(x => x.RouteCode).HasMaxLength(80);
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}
