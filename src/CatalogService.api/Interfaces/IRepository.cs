using Ardalis.Specification;

namespace CatalogService.api.Interfaces;

public interface IRepository<T> : IRepositoryBase<T> where T : class, IAggregateRoot
{
}
