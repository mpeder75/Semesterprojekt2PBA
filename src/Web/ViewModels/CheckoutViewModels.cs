namespace Microsoft.eShopWeb.Web.ViewModels;

/// <summary>Adressen en checkout sendes til. Feltnavnene følger OrderService' kontrakt.</summary>
public record CheckoutAddress(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Hvad der skete, da Web bad OrderService om at starte en checkout.</summary>
public enum StartCheckoutOutcome
{
    /// <summary>OrderService har taget imod checkouten (nu eller ved et tidligere forsøg med samme nøgle).</summary>
    Accepted,

    /// <summary>Nøglen er brugt med et andet indhold, eller kurven har allerede en checkout i gang.</summary>
    Conflict,

    /// <summary>OrderService afviste requesten som ugyldig.</summary>
    Rejected,

    /// <summary>OrderService kunne ikke nås eller svarede med en fejl. Prøv igen med samme nøgle.</summary>
    Unavailable
}

public record StartCheckoutResult(StartCheckoutOutcome Outcome, Guid? CheckoutId = null, string? Message = null);

/// <summary>Status for en checkout, som OrderService svarer på GET.</summary>
public class CheckoutStatusViewModel
{
    public Guid CheckoutId { get; set; }

    /// <summary>Den simple status: Processing, Completed eller Failed.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Den interne tilstand (til fejlfinding).</summary>
    public string State { get; set; } = string.Empty;

    public string? FailureReason { get; set; }

    /// <summary>Ordrens id, når den er oprettet.</summary>
    public int? OrderId { get; set; }

    public bool IsFinished => Status is "Completed" or "Failed";
}
