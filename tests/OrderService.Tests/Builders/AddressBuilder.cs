using OrderService.api.Entities.OrderAggregate;

namespace OrderService.Tests.Builders;

public class AddressBuilder
{
    public string TestStreet => "123 Main St.";
    public string TestCity => "Kent";
    public string TestState => "OH";
    public string TestCountry => "USA";
    public string TestZipCode => "44240";

    public Address WithDefaultValues()
    {
        return new Address(TestStreet, TestCity, TestState, TestCountry, TestZipCode);
    }
}
