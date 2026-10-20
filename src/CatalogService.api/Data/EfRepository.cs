using Ardalis.Specification.EntityFrameworkCore;
using CatalogService.api.Interfaces;

namespace CatalogService.api.Data;

public class EfRepository<T> : RepositoryBase<T>, IReadRepository<T>, IRepository<T> where T : class, IAggregateRoot
{
    public EfRepository(CatalogContext dbContext) : base(dbContext)
    {

    }
}
