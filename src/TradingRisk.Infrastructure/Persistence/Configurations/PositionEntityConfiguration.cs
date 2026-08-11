using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingRisk.Infrastructure.Persistence.Entities;

namespace TradingRisk.Infrastructure.Persistence.Configurations;

internal sealed class PositionEntityConfiguration : IEntityTypeConfiguration<PositionEntity>
{
    public void Configure(EntityTypeBuilder<PositionEntity> builder)
    {
        builder.ToTable("Positions");
        builder.HasKey(position => position.Id);

        builder.Property(position => position.InstrumentId)
            .HasMaxLength(32)
            .IsRequired();

        // Precision documents the relational contract. SQLite stores decimal values using
        // its provider mapping; PostgreSQL/SQL Server would enforce this precision directly.
        builder.Property(position => position.Quantity).HasPrecision(38, 18);
        builder.Property(position => position.Price).HasPrecision(38, 18);

        // Domain forbids duplicate instruments; the unique index also protects the invariant
        // against other writers and race conditions.
        builder.HasIndex(position => new
        {
            position.PortfolioId,
            position.InstrumentId
        })
            .IsUnique();
        builder.HasIndex(position => position.InstrumentId);
    }
}
