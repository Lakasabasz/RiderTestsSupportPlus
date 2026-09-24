using Bookstore.Core;
using NUnit.Framework;

namespace Bookstore.Core.Tests;

public class CartTests
{
  private static readonly Book CleanCode = new("978-0-306-40615-7", "Clean Code", 40m);
  private static readonly Book Refactoring = new("978-3-16-148410-0", "Refactoring", 50m);

  [Test, Category("Smoke")]
  public void AddingTheSameBookIncreasesCopies()
  {
    var cart = new Cart();
    cart.Add(CleanCode);
    cart.Add(CleanCode, 2);
    Assert.That(cart.Lines.Single().Copies, Is.EqualTo(3));
  }

  [Test]
  public void RemoveDropsTheLine()
  {
    var cart = new Cart();
    cart.Add(CleanCode);
    cart.Add(Refactoring);
    cart.Remove(CleanCode.Isbn);
    Assert.That(cart.Lines.Select(l => l.Book.Title), Is.EqualTo(new[] { "Refactoring" }));
  }

  [TestCase("PL", 94.50)]
  [TestCase("DE", 96.30)]
  public void TotalOfTwoBooks(string country, double expected)
  {
    var cart = new Cart();
    cart.Add(CleanCode);
    cart.Add(Refactoring);
    Assert.That(cart.Total(new PriceCalculator(country)), Is.EqualTo((decimal)expected));
  }

  [Test]
  public void EmptyCartCostsNothing() => Assert.That(new Cart().Total(new PriceCalculator("UK")), Is.Zero);
}
