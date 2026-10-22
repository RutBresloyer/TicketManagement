using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RequestManagement.Api.Entities;

namespace RequestManagement.Api.Data.Configurations;

public sealed class DemoSeedInfoConfiguration : IEntityTypeConfiguration<DemoSeedInfo>
{
    public void Configure(EntityTypeBuilder<DemoSeedInfo> builder)
    {
        builder.ToTable("DemoSeedInfo");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SeedVersion).HasMaxLength(20).IsRequired();
    }
}
