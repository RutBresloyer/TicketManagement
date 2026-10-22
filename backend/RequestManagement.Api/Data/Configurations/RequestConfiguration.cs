using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RequestManagement.Api.Entities;

namespace RequestManagement.Api.Data.Configurations;

public sealed class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    /// <summary>
    /// Case-insensitive, accent-sensitive collation set explicitly on the searchable/filterable text columns,
    /// so exact filters and <c>q</c> search are case-insensitive regardless of the server's default collation.
    /// </summary>
    public const string TextCollation = "Latin1_General_100_CI_AS";

    public void Configure(EntityTypeBuilder<Request> builder)
    {
        builder.ToTable("Requests", table =>
        {
            table.HasCheckConstraint("CK_Requests_Status", "[Status] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_Requests_Priority", "[Priority] BETWEEN 1 AND 3");
            // LEN ignores trailing spaces, so this also rejects whitespace-only values.
            table.HasCheckConstraint("CK_Requests_Title_NotBlank", "LEN([Title]) > 0");
            table.HasCheckConstraint("CK_Requests_OrganizationName_NotBlank", "LEN([OrganizationName]) > 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityColumn();

        builder.Property(r => r.Title).HasMaxLength(200).IsRequired().UseCollation(TextCollation);
        builder.Property(r => r.OrganizationName).HasMaxLength(150).IsRequired().UseCollation(TextCollation);
        builder.Property(r => r.AssignedTo).HasMaxLength(100).UseCollation(TextCollation);
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.Priority).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();

        // Native SQL Server rowversion: EF adds "AND [RowVersion] = @original" to every UPDATE and
        // throws DbUpdateConcurrencyException when no row matches.
        builder.Property(r => r.RowVersion).IsRowVersion();

        // Every index ends with (CreatedAt DESC, Id DESC) because that is the default list order:
        // an equality filter on the leading column can then return the first page as an ordered seek with no sort.
        // See docs/PERFORMANCE.md for the measured plans.
        builder.HasIndex(r => new { r.CreatedAt, r.Id })
            .IsDescending(true, true)
            .HasDatabaseName("IX_Requests_CreatedAt_Id");
        builder.HasIndex(r => new { r.Status, r.CreatedAt, r.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Requests_Status_CreatedAt_Id");
        builder.HasIndex(r => new { r.Priority, r.CreatedAt, r.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Requests_Priority_CreatedAt_Id");
        builder.HasIndex(r => new { r.AssignedTo, r.CreatedAt, r.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Requests_AssignedTo_CreatedAt_Id");
        builder.HasIndex(r => new { r.OrganizationName, r.CreatedAt, r.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Requests_OrganizationName_CreatedAt_Id");
    }
}
