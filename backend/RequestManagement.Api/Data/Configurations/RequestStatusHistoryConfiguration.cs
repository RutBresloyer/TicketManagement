using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RequestManagement.Api.Entities;

namespace RequestManagement.Api.Data.Configurations;

public sealed class RequestStatusHistoryConfiguration : IEntityTypeConfiguration<RequestStatusHistory>
{
    public const int ChangedByMaxLength = 100;

    public void Configure(EntityTypeBuilder<RequestStatusHistory> builder)
    {
        builder.ToTable("RequestStatusHistory", table =>
        {
            table.HasCheckConstraint("CK_RequestStatusHistory_PreviousStatus", "[PreviousStatus] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_RequestStatusHistory_NewStatus", "[NewStatus] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_RequestStatusHistory_StatusChanged", "[PreviousStatus] <> [NewStatus]");
            table.HasCheckConstraint("CK_RequestStatusHistory_ChangedBy_NotBlank", "LEN([ChangedBy]) > 0");
        });

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).UseIdentityColumn();
        builder.Property(h => h.PreviousStatus).IsRequired();
        builder.Property(h => h.NewStatus).IsRequired();
        builder.Property(h => h.ChangedAt).IsRequired();
        builder.Property(h => h.ChangedBy).HasMaxLength(ChangedByMaxLength).IsRequired();

        // Restrict: a request that has audit history cannot be deleted out from under it.
        builder.HasOne<Request>()
            .WithMany()
            .HasForeignKey(h => h.RequestId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves GET /api/requests/{id}/history as a single ordered range seek; also covers the FK.
        builder.HasIndex(h => new { h.RequestId, h.ChangedAt, h.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_RequestStatusHistory_RequestId_ChangedAt_Id");
    }
}
