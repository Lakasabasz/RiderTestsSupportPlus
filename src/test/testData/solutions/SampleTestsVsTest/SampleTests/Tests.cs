using NUnit.Framework;
namespace Sample.Ns;
[TestFixture("A")]
[TestFixture("b.c")]
public class ParamFixture
{
    private readonly string _p;
    public ParamFixture(string p) { _p = p; }
    [TestCase(1)]
    [TestCase(2)]
    [TestCase("x.y", Category = "Slow")]
    public void Method(object x) { }
    [Test] public void Plain() { }
}
public class Plain
{
    [TestCase("a,b", 1.5)]
    [TestCase("q\"uote", 2)]
    public void Two(string s, double d) { }
    [Test, Category("Fast")] public void Simple() { }
}
