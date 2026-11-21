using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class MachineWorkerConfiguration : IEntityTypeConfiguration<MachineWorker>
{
    public void Configure(EntityTypeBuilder<MachineWorker> builder)
    {
        builder.ToTable("MachineWorkers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
    }
}

public sealed class MachineTeamTemplateConfiguration : IEntityTypeConfiguration<MachineTeamTemplate>
{
    public void Configure(EntityTypeBuilder<MachineTeamTemplate> builder)
    {
        builder.ToTable("MachineTeamTemplates");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.MachineId, x.EffectiveDate }).IsUnique();
        builder.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MachineWorker>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MachineWorker>().WithMany().HasForeignKey(x => x.FirstCollectorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MachineWorker>().WithMany().HasForeignKey(x => x.SecondCollectorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MachineWorker>().WithMany().HasForeignKey(x => x.PackerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MachineTeamChangeConfiguration : IEntityTypeConfiguration<MachineTeamChange>
{
    public void Configure(EntityTypeBuilder<MachineTeamChange> builder)
    {
        builder.ToTable("MachineTeamChanges", table =>
        {
            table.HasCheckConstraint("CK_MachineTeamChanges_Position", "[Position] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_MachineTeamChanges_OtherPosition",
                "[OtherPosition] IS NULL OR [OtherPosition] BETWEEN 1 AND 4");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ActorName).HasMaxLength(120);
        builder.HasIndex(x => new { x.Date, x.MachineId, x.Position, x.Revision }).IsUnique();
        builder.HasOne<Machine>().WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Machine>().WithMany().HasForeignKey(x => x.OtherMachineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MachineWorker>().WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MachineWorker>().WithMany().HasForeignKey(x => x.PreviousWorkerId).OnDelete(DeleteBehavior.Restrict);
    }
}
