using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MiHome.WinUI.Dialogs;

public sealed partial class TrayDevicesDialog : ContentDialog
{
    internal ListView ChoiceList => Choices;
    public TrayDevicesDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += (_, args) =>
        {
            if (Choices.SelectedItems.Count <= 12) return;
            args.Cancel = true;
            Error.Text = Infrastructure.UiText.Get("TrayPinLimit"); Error.Visibility = Visibility.Visible;
        };
    }
}
