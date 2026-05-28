using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalCode)
            .HasMaxLength(80);

        builder.Property(x => x.Name)
            .HasMaxLength(120);

        builder.HasIndex(x => x.Number)
            .IsUnique();

        builder.HasMany<OrderLine>()
            .WithOne()
            .HasForeignKey(x => x.AssignedMachineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany<CustomerMachineAssignment>()
            .WithOne()
            .HasForeignKey(x => x.MachineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ExternalCode)
            .IsUnique()
            .HasFilter("[ExternalCode] IS NOT NULL");
    }
}
