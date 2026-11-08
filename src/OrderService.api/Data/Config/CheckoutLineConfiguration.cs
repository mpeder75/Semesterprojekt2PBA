using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.api.Entities.OrderAggregate;

namespace OrderService.api.Data.Config;

public class CheckoutLineConfiguration : IEntityTypeConfiguration<CheckoutLine>
{
    public void Configure(EntityTypeBuilder<CheckoutLine> builder)
    {
        builder.Property(l => l.UnitPrice)
            .HasColumnType("decimal(18,2)");
    }
}
