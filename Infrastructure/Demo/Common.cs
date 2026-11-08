using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;

namespace AuthSandbox.Infrastructure.Demo;

/// <summary>Postal address. Domestic postcodes must use the "1234 AB" format.</summary>
public class PostalAddress : IValidatableObject
{
    /// <example>Keizersgracht</example>
    [Required, StringLength(100)] public string Street { get; set; } = "";
    /// <example>123</example>
    [Range(1, 99999)] public int HouseNumber { get; set; }
    /// <summary>Huisnummertoevoeging, e.g. "A", "2-hoog", "bis".</summary>
    [StringLength(8)] public string? HouseNumberAddition { get; set; }
    /// <example>1015 CJ</example>
    [Required, StringLength(10)] public string Postcode { get; set; } = "";
    /// <example>Amsterdam</example>
    [Required, StringLength(60)] public string City { get; set; } = "";
    /// <summary>ISO 3166-1 alpha-2.</summary>
    [Required, CountryCode] public string Country { get; set; } = "NL";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Country == "NL" && !Validators.IsValidNlPostcode(Postcode))
            yield return new ValidationResult("Postcode must look like '1234 AB'.", [nameof(Postcode)]);
    }

    public PostalAddress Normalized() => new()
    {
        Street = Street.Trim(),
        HouseNumber = HouseNumber,
        HouseNumberAddition = string.IsNullOrWhiteSpace(HouseNumberAddition) ? null : HouseNumberAddition.Trim(),
        Postcode = Country == "NL" ? Validators.NormalizeNlPostcode(Postcode) : Postcode.Trim(),
        City = City.Trim(),
        Country = Country,
    };
}

public static class Text
{
    /// <summary>Lower-case, strip diacritics and punctuation — used for name matching.</summary>
    public static string NormalizeName(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c) || c == ' ') sb.Append(char.ToLowerInvariant(c));
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    public static string Mask(string value, int visible = 4) =>
        value.Length <= visible ? value : new string('*', value.Length - visible) + value[^visible..];

    private const string ReferenceAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Deterministic 6-char booking reference (PNR-like) derived from a sequence number.</summary>
    public static string Reference(int seed)
    {
        var x = (uint)(seed * 2654435761u);
        var sb = new StringBuilder();
        for (var i = 0; i < 6; i++) { sb.Append(ReferenceAlphabet[(int)(x % (uint)ReferenceAlphabet.Length)]); x /= (uint)ReferenceAlphabet.Length; x ^= (uint)(seed * 97 + i * 31); }
        return sb.ToString();
    }

    public static string Csv(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        static string Esc(object? v)
        {
            var s = v switch
            {
                null => "",
                decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => v.ToString() ?? "",
            };
            return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', header));
        foreach (var r in rows) sb.AppendLine(string.Join(',', r.Select(Esc)));
        return sb.ToString();
    }

    /// <summary>Next TARGET2-style business day (skips weekends; holidays ignored in the sandbox).</summary>
    public static DateOnly NextBusinessDay(DateOnly d)
    {
        do d = d.AddDays(1); while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        return d;
    }
}

/// <summary>Attaches a JSON example to a request/response schema in the OpenAPI document.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SchemaExampleAttribute(string json) : Attribute
{
    public string Json { get; } = json;
}
