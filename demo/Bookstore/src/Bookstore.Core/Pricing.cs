namespace Bookstore.Core;

public sealed record Book(string Isbn, string Title, decimal NetPrice, bool IsEbook = false);

public sealed class PriceCalculator
{
  private static readonly Dictionary<string, (decimal Print, decimal Ebook)> VatRates = new()
  {
    ["PL"] = (0.05m, 0.05m),
    ["DE"] = (0.07m, 0.07m),
    ["UK"] = (0.00m, 0.00m),
  };

  private readonly (decimal Print, decimal Ebook) _vat;

  public PriceCalculator(string country)
  {
    if (!VatRates.TryGetValue(country, out _vat))
      throw new ArgumentException($"Unsupported country {country}", nameof(country));
    Country = country;
  }

  public string Country { get; }

  public decimal GrossPrice(Book book) =>
    decimal.Round(book.NetPrice * (1 + (book.IsEbook ? _vat.Ebook : _vat.Print)), 2);

  /// <summary>Bulk discount: 5% from 3 copies, 10% from 10 copies.</summary>
  public decimal Total(Book book, int copies)
  {
    if (copies <= 0) throw new ArgumentOutOfRangeException(nameof(copies));
    var discount = copies >= 10 ? 0.10m : copies >= 3 ? 0.05m : 0m;
    return decimal.Round(GrossPrice(book) * copies * (1 - discount), 2);
  }
}

public static class Isbn
{
  /// <summary>Validates an ISBN-13, ignoring hyphens and spaces.</summary>
  public static bool IsValid(string? isbn)
  {
    if (isbn is null) return false;
    var digits = isbn.Where(c => c is not ('-' or ' ')).ToArray();
    if (digits.Length != 13 || !digits.All(char.IsDigit)) return false;
    var sum = digits.Take(12).Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
    return (10 - sum % 10) % 10 == digits[12] - '0';
  }
}
