using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Infrastructure;

namespace MiHome.WinUI.Dialogs;

public sealed partial class UpdateAvailableDialog : ContentDialog
{
    public UpdateAvailableDialog() => InitializeComponent();

    internal void SetRelease(UpdateRelease release, double availableHeight)
    {
        Title = UiText.Format("UpdateAvailable", release.Version);
        PrimaryButtonText = UiText.Get(release.Download is null ? "DownloadPage" : "DownloadInstall");
        Notes.Content = DocumentPresenter.Build(release.Notes, compact: true);
        NotesScroll.MaxHeight = Math.Max(120, Math.Min(460, availableHeight - 220));
    }
}
