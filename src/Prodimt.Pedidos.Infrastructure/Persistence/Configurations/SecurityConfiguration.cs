using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Prodimt.Pedidos.Domain.Entities;

namespace Prodimt.Pedidos.Infrastructure.Persistence.Configurations;

public sealed class SecurityRoleConfiguration : IEntityTypeConfiguration<SecurityRole>
{
    public void Configure(EntityTypeBuilder<SecurityRole> builder)
    {
        builder.ToTable("SecurityRoles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class SecurityPermissionConfiguration : IEntityTypeConfiguration<SecurityPermission>
{
    public void Configure(EntityTypeBuilder<SecurityPermission> builder)
    {
        builder.ToTable("SecurityPermissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.Key).IsUnique();
    }
}

public sealed class SecurityUserRoleConfiguration : IEntityTypeConfiguration<SecurityUserRole>
{
    public void Configure(EntityTypeBuilder<SecurityUserRole> builder)
    {
        builder.ToTable("SecurityUserRoles");
        builder.HasKey(x => new { x.UserId, x.RoleId });
        builder.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SecurityRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SecurityRolePermissionConfiguration : IEntityTypeConfiguration<SecurityRolePermission>
{
    public void Configure(EntityTypeBuilder<SecurityRolePermission> builder)
    {
        builder.ToTable("SecurityRolePermissions");
        builder.HasKey(x => new { x.RoleId, x.PermissionId });
        builder.HasOne<SecurityRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SecurityPermission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SecurityUserPermissionConfiguration : IEntityTypeConfiguration<SecurityUserPermission>
{
    public void Configure(EntityTypeBuilder<SecurityUserPermission> builder)
    {
        builder.ToTable("SecurityUserPermissions");
        builder.HasKey(x => new { x.UserId, x.PermissionId });
        builder.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SecurityPermission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SecurityStateConfiguration : IEntityTypeConfiguration<SecurityState>
{
    public void Configure(EntityTypeBuilder<SecurityState> builder)
    {
        builder.ToTable("SecurityStates", table => table.HasCheckConstraint("CK_SecurityState_Singleton", "[Id] = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Revision).IsConcurrencyToken();
    }
}
