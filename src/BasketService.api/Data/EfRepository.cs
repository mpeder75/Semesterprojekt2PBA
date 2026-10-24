using Ardalis.Specification.EntityFrameworkCore;
using BasketService.api.Interfaces;

namespace BasketService.api.Data;

public class EfRepository<T> : RepositoryBase<T>, IReadRepository<T>, IRepository<T> where T : class, IAggregateRoot
{
    public EfRepository(BasketContext dbContext) : base(dbContext)
    {
    }
}
