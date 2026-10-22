using BasketService.api.Entities.BasketAggregate;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace BasketService.api.Data
{
    public class BasketContext : DbContext
    {
        #pragma warning disable CS8618
        public BasketContext(DbContextOptions<BasketContext> options) : base(options) { }

        public DbSet<Basket> Baskets {  get; set; }
        public DbSet<BasketItem> BasketItems { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }
}
