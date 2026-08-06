using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class RelayCommandTests
{
    [TestMethod]
    public void CanExecute_NoPredicate_ReturnsTrue() =>
        Assert.IsTrue(new RelayCommand(() => { }).CanExecute(null));

    [TestMethod]
    public void CanExecute_PredicateFalse_ReturnsFalse() =>
        Assert.IsFalse(new RelayCommand(() => { }, () => false).CanExecute(null));

    [TestMethod]
    public void CanExecute_PredicateTrue_ReturnsTrue() =>
        Assert.IsTrue(new RelayCommand(() => { }, () => true).CanExecute(null));

    [TestMethod]
    public void Execute_InvokesAction()
    {
        var called = false;
        new RelayCommand(() => called = true).Execute(null);

        Assert.IsTrue(called);
    }

    [TestMethod]
    public void Constructor_NullExecute_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => new RelayCommand(null!));
}

[TestClass]
public sealed class RelayCommandOfTTests
{
    [TestMethod]
    public void CanExecute_NoPredicate_ReturnsTrue() =>
        Assert.IsTrue(new RelayCommand<string>(_ => { }).CanExecute("x"));

    [TestMethod]
    public void CanExecute_NoPredicate_IgnoresNullValueTypeParameter() =>
        // With no canExecute predicate, CanExecute short-circuits to true before the value-type null guard.
        Assert.IsTrue(new RelayCommand<int>(_ => { }).CanExecute(null));

    [TestMethod]
    public void CanExecute_PredicateWithValueTypeAndNullParameter_ReturnsFalse() =>
        Assert.IsFalse(new RelayCommand<int>(_ => { }, i => i >= 0).CanExecute(null));

    [TestMethod]
    public void CanExecute_ReferenceTypeWithNullParameter_UsesPredicate() =>
        Assert.IsTrue(new RelayCommand<string?>(_ => { }, s => s == null).CanExecute(null));

    [TestMethod]
    public void CanExecute_PredicateReceivesTypedParameter()
    {
        var command = new RelayCommand<int>(_ => { }, i => i > 5);

        Assert.IsFalse(command.CanExecute(3));
        Assert.IsTrue(command.CanExecute(10));
    }

    [TestMethod]
    public void Execute_ValueTypeWithNullParameter_DoesNotInvoke()
    {
        var called = false;
        new RelayCommand<int>(_ => called = true).Execute(null);

        Assert.IsFalse(called);
    }

    [TestMethod]
    public void Execute_ReferenceTypeWithNullParameter_Invokes()
    {
        var received = "unset";
        new RelayCommand<string?>(s => received = s).Execute(null);

        Assert.IsNull(received);
    }

    [TestMethod]
    public void Execute_PassesTypedParameterThrough()
    {
        var received = 0;
        new RelayCommand<int>(i => received = i).Execute(42);

        Assert.AreEqual(42, received);
    }
}
