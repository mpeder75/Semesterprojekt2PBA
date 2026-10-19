using AutoMapper;
using CatalogService.api.EndPoints.CatalogBrandEndpoints;
using CatalogService.api.EndPoints.CatalogItemEndpoints;
using CatalogService.api.EndPoints.CatalogTypeEndpoints;
using CatalogService.api.Entities;

namespace CatalogService.api;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<CatalogItem, CatalogItemDto>();
        CreateMap<CatalogType, CatalogTypeDto>()
            .ForMember(dto => dto.Name, options => options.MapFrom(src => src.Type));
        CreateMap<CatalogBrand, CatalogBrandDto>()
            .ForMember(dto => dto.Name, options => options.MapFrom(src => src.Brand));
    }
}
