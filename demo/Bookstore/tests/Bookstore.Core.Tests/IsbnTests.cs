using Bookstore.Core;
using NUnit.Framework;

namespace Bookstore.Core.Tests;

public class IsbnTests
{
  // Arguments with hyphens, spaces, dots and quotes: names such as IsValid("978-0-306-40615-7")
  // have to survive saving and loading a session
  [TestCase("978-0-306-40615-7")]
  [TestCase("978 3 16 148410 0")]
  [TestCase("9780596520687")]
  [Category("Smoke")]
  public void ValidIsbn(string isbn) => Assert.That(Isbn.IsValid(isbn), Is.True);

  [TestCase("978-0-306-40615-8", TestName = "WrongCheckDigit")]
  [TestCase("978.0.306.40615.7")]
  [TestCase("\"9780306406157\"")]
  [TestCase("97803064061")]
  [TestCase(null)]
  public void InvalidIsbn(string? isbn) => Assert.That(Isbn.IsValid(isbn), Is.False);
}
