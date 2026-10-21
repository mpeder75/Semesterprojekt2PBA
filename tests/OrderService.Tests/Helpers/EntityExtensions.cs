using OrderService.api.Entities;

namespace OrderService.Tests.Helpers;

public static class EntityExtensions
{
    // BaseEntity.Id har protected setter (EF sætter den). I tests sætter vi den med reflection.
    public static T WithId<T>(this T entity, int id) where T : BaseEntity
    {
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);
        return entity;
    }
}
