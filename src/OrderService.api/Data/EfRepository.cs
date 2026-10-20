using Ardalis.Specification.EntityFrameworkCore;
using OrderService.api.Interfaces;

namespace OrderService.api.Data;

public class EfRepository<T> : RepositoryBase<T>, IReadRepository<T>, IRepository<T> where T : class, IAggregateRoot
{
    public EfRepository(OrderDbContext dbContext) : base(dbContext)
    {
    }
}
