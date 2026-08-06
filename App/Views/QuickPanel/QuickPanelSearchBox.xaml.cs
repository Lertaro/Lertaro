namespace Lertaro.App.Views.QuickPanel;

// See QuickPanelSearchBox.xaml for why this is a control of its own. UserControl is spelled out in full
// because System.Windows.Forms is also in scope in this project.
public partial class QuickPanelSearchBox : System.Windows.Controls.UserControl
{
    public QuickPanelSearchBox() => InitializeComponent();

    /// <summary>Puts the keyboard in the box, which is where a summon leaves it.</summary>
    /// <remarks>
    /// Both calls: Focus() sets WPF's logical focus within this control, Keyboard.Focus is what actually
    /// routes keystrokes to it. The panel comes up without activation (ShowActivated="False") and only
    /// takes it back through the foreground-thread attach in QuickPanelWindow.ActivateAndFocus, so one
    /// without the other leaves a caret that blinks and a keystroke that goes somewhere else.
    /// </remarks>
    public void FocusInput()
    {
        TxtFilter.Focus();
        System.Windows.Input.Keyboard.Focus(TxtFilter);
    }
}
