using AuthSandbox.Infrastructure.Demo;

namespace AuthSandbox.Tests;

public class ValidatorTests
{
    [Theory]
    [InlineData("NL91ABNA0417164300", true)]
    [InlineData("NL91 ABNA 0417 1643 00", true)]
    [InlineData("NL92ABNA0417164300", false)]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("NL91ABNA041716430", false)]
    public void Iban_mod97(string iban, bool valid) => Assert.Equal(valid, Validators.IsValidIban(iban));

    [Fact]
    public void Built_nl_ibans_are_valid()
    {
        Assert.Equal("NL91ABNA0417164300", Validators.BuildNlIban("ABNA", 417164300));
        for (var n = 1; n < 50; n++) Assert.True(Validators.IsValidIban(Validators.BuildNlIban("KNAB", 417164300 + n)));
        Assert.Equal("NL91 ABNA 0417 1643 00", Validators.FormatIban("NL91ABNA0417164300"));
    }

    [Theory]
    [InlineData("1015 CJ", true)]
    [InlineData("1015cj", true)]
    [InlineData("0123 AB", false)]
    [InlineData("1234 SA", false)]
    public void Postcode(string pc, bool valid) => Assert.Equal(valid, Validators.IsValidNlPostcode(pc));

    [Theory]
    [InlineData("NL123456789B01", true)]
    [InlineData("NL123456789", false)]
    [InlineData("DE123456789", true)]
    [InlineData("XX123", false)]
    public void Vat_id(string vat, bool valid) => Assert.Equal(valid, Validators.IsValidVatId(vat));

    [Theory]
    [InlineData("8712345000011", true)]
    [InlineData("8712345000012", false)]
    [InlineData("4006381333931", true)]
    public void Ean13(string ean, bool valid) => Assert.Equal(valid, Validators.IsValidEan13(ean));

    [Theory]
    [InlineData("+31612345678", true)]
    [InlineData("+31 6 1234 5678", true)]
    [InlineData("+31 612345678", true)]
    [InlineData("0612345678", false)]
    [InlineData("+31 6 12ab 5678", false)]
    public void E164(string phone, bool valid) => Assert.Equal(valid, Validators.IsValidE164(phone));
}
