namespace OrderService.api.Interfaces;

public interface ICheckoutRequestProducer
{
    /// <summary>
    /// Pakker payload ind i den fælles kuvert og lægger den som en persistent besked i køen.
    /// Returnerer først, når RabbitMQ har bekræftet beskeden (publisher confirm).
    /// </summary>
    Task SendAsync(
        string queue,
        string messageType,
        Guid checkoutId,
        Guid correlationId,
        object payload,
        CancellationToken cancellationToken = default);
}
