namespace Bookstore.Core;

public enum OrderStatus { New, Paid, Shipped, Cancelled }

public sealed class Cart
{
  private readonly List<(Book Book, int Copies)> _lines = new();

  public IReadOnlyList<(Book Book, int Copies)> Lines => _lines;

  public void Add(Book book, int copies = 1)
  {
    if (copies <= 0) throw new ArgumentOutOfRangeException(nameof(copies));
    var index = _lines.FindIndex(l => l.Book.Isbn == book.Isbn);
    if (index >= 0) _lines[index] = (book, _lines[index].Copies + copies);
    else _lines.Add((book, copies));
  }

  public void Remove(string isbn) => _lines.RemoveAll(l => l.Book.Isbn == isbn);

  public decimal Total(PriceCalculator calculator) => _lines.Sum(l => calculator.Total(l.Book, l.Copies));
}

public sealed class Order
{
  public OrderStatus Status { get; private set; } = OrderStatus.New;

  public void Pay() => Move(OrderStatus.New, OrderStatus.Paid);
  public void Ship() => Move(OrderStatus.Paid, OrderStatus.Shipped);

  public void Cancel()
  {
    if (Status == OrderStatus.Shipped) throw new InvalidOperationException("A shipped order can't be cancelled");
    Status = OrderStatus.Cancelled;
  }

  private void Move(OrderStatus from, OrderStatus to)
  {
    if (Status != from) throw new InvalidOperationException($"Can't go from {Status} to {to}");
    Status = to;
  }
}
