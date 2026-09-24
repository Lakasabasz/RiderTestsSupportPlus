using Bookstore.Core;
using NUnit.Framework;

namespace Bookstore.Core.Tests;

// A parametrized fixture: Rider shows PriceCalculatorTests("PL").GrossPrice(...) etc.,
// the plugin saves and resolves these by name
[TestFixture("PL", 0.05)]
[TestFixture("DE", 0.07)]
[TestFixture("UK", 0.0)]
public class PriceCalculatorTests
{
  private readonly PriceCalculator _calculator;
  private readonly decimal _vat;

  public PriceCalculatorTests(string country, double vat)
  {
    _calculator = new PriceCalculator(country);
    _vat = (decimal)vat;
  }

  [TestCase(10.00)]
  [TestCase(39.99)]
  [TestCase(0.01, Category = "EdgeCase")]
  [Category("Smoke")]
  public void GrossPrice(double net)
  {
    var book = new Book("978-0-306-40615-7", "Clean Code", (decimal)net);
    Assert.That(_calculator.GrossPrice(book), Is.EqualTo(decimal.Round((decimal)net * (1 + _vat), 2)));
  }

  [TestCase(1, 0.00)]
  [TestCase(3, 0.05)]
  [TestCase(10, 0.10)]
  public void BulkDiscount(int copies, double discount)
  {
    var book = new Book("978-3-16-148410-0", "Refactoring", 20m);
    var expected = decimal.Round(_calculator.GrossPrice(book) * copies * (1 - (decimal)discount), 2);
    Assert.That(_calculator.Total(book, copies), Is.EqualTo(expected));
  }

  [Test]
  public void EbookHasTheSameRateAsPrint()
  {
    var print = new Book("978-0-596-52068-7", "The Pragmatic Programmer", 30m);
    Assert.That(_calculator.GrossPrice(print with { IsEbook = true }), Is.EqualTo(_calculator.GrossPrice(print)));
  }

  [Test, Category("Slow")]
  public async Task PriceListSync()
  {
    // Stands for a call to an external price list
    await Task.Delay(1500);
    Assert.That(_calculator.Country, Is.Not.Empty);
  }
}

public class PriceCalculatorCountryTests
{
  [TestCase("FR")]
  [TestCase("")]
  [TestCase("pl")]
  public void UnsupportedCountryIsRejected(string country) =>
    Assert.Throws<ArgumentException>(() => new PriceCalculator(country));

  [Test]
  public void ZeroCopiesAreRejected() =>
    Assert.Throws<ArgumentOutOfRangeException>(() => new PriceCalculator("PL").Total(new Book("x", "y", 1m), 0));
}
