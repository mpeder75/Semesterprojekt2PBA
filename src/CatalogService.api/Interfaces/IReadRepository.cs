using Ardalis.Specification;

namespace CatalogService.api.Interfaces;

public interface IReadRepository<T> : IReadRepositoryBase<T> where T : class, IAggregateRoot
{
}
