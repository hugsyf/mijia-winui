using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class AdapterCardPreview : UserControl
{
    internal DeviceCardView DeviceCard => Card;
    internal TextBlock FeedbackLabel => Feedback;
    public AdapterCardPreview() => InitializeComponent();
}
