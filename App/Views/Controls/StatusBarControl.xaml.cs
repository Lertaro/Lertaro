using UserControl = System.Windows.Controls.UserControl;
using Border = System.Windows.Controls.Border;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Lertaro.App;

public partial class StatusBarControl : UserControl
{
    public StatusBarControl() => InitializeComponent();

    public Border StatusBarBorder => StatusBar;
    public System.Windows.Shapes.Ellipse StatusDot => DotStatus;
    public TextBlock StatusInfoTextBlock => TxtStatusInfo;
}
