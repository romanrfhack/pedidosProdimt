using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class CustomerMachineAssignmentConfiguration : IEntityTypeConfiguration<CustomerMachineAssignment>
{
    public void Configure(EntityTypeBuilder<CustomerMachineAssignment> builder)
    {
        builder.HasKey(x => new { x.CustomerId, x.MachineId });

        builder.Property(x => x.Notes)
            .HasMaxLength(500);
    }
}
