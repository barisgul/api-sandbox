using System.Numerics;
using System.Text.RegularExpressions;

namespace AuthSandbox.Infrastructure.Demo;

/// <summary>
/// Format and checksum validators for EU identifiers.
/// All generated values are synthetic and only used for demos.
/// </summary>
public static partial class Validators
{
    // ---------- BSN (Burgerservicenummer) — 11-proef ----------
    public static bool IsValidBsn(string? value)
    {
        if (value is null || !BsnRegex().IsMatch(value)) return false;
        var sum = 0;
        for (var i = 0; i < 8; i++) sum += (value[i] - '0') * (9 - i);
        sum -= value[8] - '0';
        return sum % 11 == 0 && value != "000000000";
    }

    // ---------- IBAN (ISO 13616, mod-97) ----------
    public static string NormalizeIban(string value) => value.Replace(" ", "").ToUpperInvariant();

    public static bool IsValidIban(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var iban = NormalizeIban(value);
        if (!IbanRegex().IsMatch(iban)) return false;
        if (iban.StartsWith("NL") && iban.Length != 18) return false;
        return Mod97(iban[4..] + iban[..4]) == 1;
    }

    /// <summary>Builds a valid IBAN: NLkk BBBB 0000000000 (bank code + 10-digit account).</summary>
    public static string BuildNlIban(string bankCode, long accountNumber)
    {
        var bban = bankCode.ToUpperInvariant() + accountNumber.ToString("D10");
        var check = 98 - Mod97(bban + "NL00");
        return $"NL{check:D2}{bban}";
    }

    public static string FormatIban(string iban)
    {
        var n = NormalizeIban(iban);
        return string.Join(' ', Enumerable.Range(0, (n.Length + 3) / 4).Select(i => n.Substring(i * 4, Math.Min(4, n.Length - i * 4))));
    }

    private static int Mod97(string input)
    {
        var digits = string.Concat(input.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));
        return (int)(BigInteger.Parse(digits) % 97);
    }

    // ---------- BIC (ISO 9362) ----------
    public static bool IsValidBic(string? value) => value is not null && BicRegex().IsMatch(value);

    /// <summary>Synthetic demo mapping of bank codes to BICs.</summary>
    public static readonly IReadOnlyDictionary<string, string> NlBankBics = new Dictionary<string, string>
    {
        ["ABNA"] = "ABNANL2A",
        ["INGB"] = "INGBNL2A",
        ["RABO"] = "RABONL2U",
        ["KNAB"] = "KNABNL2H",
        ["SNSB"] = "SNSBNL2A",
        ["BUNQ"] = "BUNQNL2A",
    };

    public static string? BicForIban(string iban)
    {
        var n = NormalizeIban(iban);
        return n.StartsWith("NL") && NlBankBics.TryGetValue(n.Substring(4, 4), out var bic) ? bic : null;
    }

    // ---------- Postcode ----------
    public static bool IsValidNlPostcode(string? value) => value is not null && PostcodeRegex().IsMatch(value.ToUpperInvariant());

    public static string NormalizeNlPostcode(string value)
    {
        var v = value.Replace(" ", "").ToUpperInvariant();
        return $"{v[..4]} {v[4..]}";
    }

    // ---------- E.164 phone ----------
    public static bool IsValidE164(string? value) => value is not null && E164Regex().IsMatch(value);

    // ---------- GTIN / EAN-13 ----------
    public static bool IsValidEan13(string? value)
    {
        if (value is null || !Ean13Regex().IsMatch(value)) return false;
        return value[12] - '0' == Ean13CheckDigit(value[..12]);
    }

    public static int Ean13CheckDigit(string first12)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (first12[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    // ---------- KvK & BTW-id ----------
    public static bool IsValidKvk(string? value) => value is not null && KvkRegex().IsMatch(value);

    /// <summary>EU VAT identification number (country prefix + national format).</summary>
    public static bool IsValidVatId(string? value)
    {
        if (value is null) return false;
        var v = value.Replace(" ", "").ToUpperInvariant();
        if (v.StartsWith("NL")) return NlVatRegex().IsMatch(v);
        return v.Length >= 4 && EuCountries.Contains(v[..2] == "EL" ? "GR" : v[..2]) && GenericVatRegex().IsMatch(v);
    }

    // ---------- ISO 3166 / ISO 4217 ----------
    public static readonly HashSet<string> EuCountries =
    [
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT",
        "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
    ];

    public static readonly HashSet<string> KnownCountries =
    [
        .. EuCountries, "IS", "LI", "NO", "CH", "GB", "US", "CA", "TR", "MA", "AE", "JP", "AU", "BR", "IN", "CN", "SR", "UA",
    ];

    public static bool IsValidCountry(string? value) => value is not null && KnownCountries.Contains(value);

    public static readonly HashSet<string> KnownCurrencies = ["EUR", "USD", "GBP", "CHF", "SEK", "DKK", "NOK", "PLN", "CZK", "HUF", "RON", "BGN"];

    public static bool IsValidCurrency(string? value) => value is not null && KnownCurrencies.Contains(value);

    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"^\d{9}$")] private static partial Regex BsnRegex();
    [GeneratedRegex(@"^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$")] private static partial Regex IbanRegex();
    [GeneratedRegex(@"^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$")] private static partial Regex BicRegex();
    [GeneratedRegex(@"^[1-9]\d{3} ?(?!SA|SD|SS)[A-Z]{2}$")] private static partial Regex PostcodeRegex();
    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")] private static partial Regex E164Regex();
    [GeneratedRegex(@"^\d{13}$")] private static partial Regex Ean13Regex();
    [GeneratedRegex(@"^\d{8}$")] private static partial Regex KvkRegex();
    [GeneratedRegex(@"^NL\d{9}B\d{2}$")] private static partial Regex NlVatRegex();
    [GeneratedRegex(@"^[A-Z]{2}[A-Z0-9]{2,13}$")] private static partial Regex GenericVatRegex();
}
