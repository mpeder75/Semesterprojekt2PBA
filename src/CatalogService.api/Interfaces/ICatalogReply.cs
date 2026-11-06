namespace CatalogService.api.Interfaces;

public interface ICatalogReply : ICatalogMessage
{ }

public interface ICatalogReply<out TResult> : ICatalogReply 
{ }
