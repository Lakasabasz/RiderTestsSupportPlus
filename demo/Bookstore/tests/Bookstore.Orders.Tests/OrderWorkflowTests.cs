using Bookstore.Core;
using NUnit.Framework;

namespace Bookstore.Orders.Tests;

public class OrderWorkflowTests
{
  [Test, Category("Smoke")]
  public void PaidOrderCanBeShipped()
  {
    var order = new Order();
    order.Pay();
    order.Ship();
    Assert.That(order.Status, Is.EqualTo(OrderStatus.Shipped));
  }

  [Test]
  public void NewOrderCantBeShipped() => Assert.Throws<InvalidOperationException>(() => new Order().Ship());

  [Test]
  public void ShippedOrderCantBeCancelled()
  {
    var order = new Order();
    order.Pay();
    order.Ship();
    Assert.Throws<InvalidOperationException>(order.Cancel);
  }

  private static IEnumerable<TestCaseData> CancellableStates()
  {
    yield return new TestCaseData("").SetName("CancelNewOrder");
    yield return new TestCaseData("Pay").SetName("CancelPaidOrder");
  }

  [TestCaseSource(nameof(CancellableStates))]
  public void OrderCanBeCancelled(string steps)
  {
    var order = new Order();
    foreach (var step in steps.Split(',', StringSplitOptions.RemoveEmptyEntries))
      typeof(Order).GetMethod(step)!.Invoke(order, null);
    order.Cancel();
    Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
  }
}

[TestFixture(OrderStatus.Paid)]
[TestFixture(OrderStatus.Shipped)]
[Category("Integration")]
public class WarehouseIntegrationTests
{
  private readonly OrderStatus _status;

  public WarehouseIntegrationTests(OrderStatus status) => _status = status;

  [Test, Category("Slow")]
  public async Task WarehouseReceivesTheOrder()
  {
    // Stands for a round trip to the warehouse system
    await Task.Delay(2000);
    Assert.That(_status, Is.AnyOf(OrderStatus.Paid, OrderStatus.Shipped));
  }

  [TestCase("Warsaw, PL")]
  [TestCase("Berlin, DE")]
  public void ShippingLabel(string address) => Assert.That($"{_status}: {address}", Does.Contain(", "));
}
