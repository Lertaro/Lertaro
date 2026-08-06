using Lertaro.App.ViewModels;

namespace Lertaro.App.Tests.ViewModels;

[TestClass]
public sealed class ViewModelBaseTests
{
    private sealed class TestViewModel : ViewModelBase
    {
        private int _value;
        public int Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public bool SetPropertyReturn(int newValue) => SetProperty(ref _value, newValue);
    }

    [TestMethod]
    public void SetProperty_DifferentValue_UpdatesStorageAndReturnsTrue()
    {
        var vm = new TestViewModel();

        var changed = vm.SetPropertyReturn(5);

        Assert.IsTrue(changed);
        Assert.AreEqual(5, vm.Value);
    }

    [TestMethod]
    public void SetProperty_SameValue_ReturnsFalseAndDoesNotRaiseEvent()
    {
        var vm = new TestViewModel { Value = 5 };
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        var changed = vm.SetPropertyReturn(5);

        Assert.IsFalse(changed);
        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void SetProperty_DifferentValue_RaisesPropertyChangedWithCallerMemberName()
    {
        var vm = new TestViewModel();
        string? raisedPropertyName = null;
        vm.PropertyChanged += (_, e) => raisedPropertyName = e.PropertyName;

        vm.Value = 10;

        Assert.AreEqual(nameof(TestViewModel.Value), raisedPropertyName);
    }
}
