using System.ComponentModel.DataAnnotations;

namespace AuthSandbox.Infrastructure.Demo;

/// <summary>Base for string format validators; null/empty is left to [Required].</summary>
public abstract class FormatAttribute(Func<string, bool> isValid, string message) : ValidationAttribute(message)
{
    public override bool IsValid(object? value) => value is not string s || s.Length == 0 || isValid(s);
}

public sealed class IbanAttribute() : FormatAttribute(Validators.IsValidIban, "Must be a valid IBAN (ISO 13616, mod-97 check).");
public sealed class BicAttribute() : FormatAttribute(Validators.IsValidBic, "Must be a valid BIC (ISO 9362), e.g. KNABNL2H.");
public sealed class NlPostcodeAttribute() : FormatAttribute(Validators.IsValidNlPostcode, "Must be a postcode, e.g. '1012 AB'.");
public sealed class E164Attribute() : FormatAttribute(Validators.IsValidE164, "Must be an E.164 phone number, e.g. +31612345678 or +31 6 1234 5678.");
public sealed class CountryCodeAttribute() : FormatAttribute(Validators.IsValidCountry, "Must be an ISO 3166-1 alpha-2 country code, e.g. DE.");
public sealed class CurrencyCodeAttribute() : FormatAttribute(Validators.IsValidCurrency, "Must be an ISO 4217 currency code, e.g. EUR.");
public sealed class Ean13Attribute() : FormatAttribute(Validators.IsValidEan13, "Must be a 13-digit GTIN/EAN with a valid check digit.");
public sealed class KvkAttribute() : FormatAttribute(Validators.IsValidKvk, "Must be an 8-digit KvK number.");
public sealed class VatIdAttribute() : FormatAttribute(Validators.IsValidVatId, "Must be an EU VAT id, e.g. NL123456789B01.");
