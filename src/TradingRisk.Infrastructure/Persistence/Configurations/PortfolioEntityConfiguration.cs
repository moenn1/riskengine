using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingRisk.Infrastructure.Persistence.Entities;

namespace TradingRisk.Infrastructure.Persistence.Configurations;

internal sealed class PortfolioEntityConfiguration : IEntityTypeConfiguration<PortfolioEntity>
{
    public void Configure(EntityTypeBuilder<PortfolioEntity> builder)
    {
        builder.ToTable("Portfolios");
        builder.HasKey(portfolio => portfolio.Id);

        // Portfolio IDs come from Domain, not from a database-generated value.
        builder.Property(portfolio => portfolio.Id).ValueGeneratedNever();
        builder.Property(portfolio => portfolio.Name).HasMaxLength(100).IsRequired();
        builder.Property(portfolio => portfolio.BaseCurrency).HasMaxLength(3).IsRequired();

        builder.HasIndex(portfolio => portfolio.Name);
        builder.HasIndex(portfolio => portfolio.BaseCurrency);

        builder
            .HasMany(portfolio => portfolio.Positions)
            .WithOne(position => position.Portfolio)
            .HasForeignKey(position => position.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
