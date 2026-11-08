using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MiHome.WinUI.Controls;
using MiHome.WinUI.Infrastructure;
namespace MiHome.WinUI;

public sealed partial class MainWindow
{
    private AppPreferences preferences = new();
    private bool loadingPreferences, choosingSettings;
    private readonly Pages.AppearancePage appearancePage = new();
    private readonly Pages.AccountPage accountPage = new();
    private readonly Pages.AboutPage aboutPage=new();
    private ListView settingsNavigation => SettingsView.NavigationList;
    private SelectorBar settingsTabs => SettingsView.NavigationTabs;
    private ContentControl settingsToolbar => SettingsView.ToolbarHost;
    private ScrollViewer settingsScroll => SettingsView.ScrollHost;
    private ContentControl settingsContent => SettingsView.ContentHost;
    private readonly Dictionary<string, FrameworkElement> settingsPages = [];
    private string settingsSection = "appearance";
    private void InitializeSettings()
    {
        settingsNavigation.SelectionChanged += (_, _) => { if (!choosingSettings && settingsNavigation.SelectedItem is ListViewItem item) SelectSettings(item.Tag.ToString()!); };
        settingsTabs.SelectionChanged += (_, _) => { if (!choosingSettings && settingsTabs.SelectedItem is { } item) SelectSettings(item.Tag.ToString()!); };
        appearancePage.Changed += () => OnTheme(this, null!);
        appearancePage.DesktopChanged += OnDesktopPreferencesChanged;
        appearancePage.StartupChanged += enabled =>
        {
            if (loadingPreferences || !ready || App.StartupDiagnostic) return;
            try { StartupRegistration.SetEnabled(enabled); }
            catch { ShowMessage(UiText.Get("DesktopSaveFailed")); }
            LoadDesktopPreferences();
        };
        appearancePage.TrayDevicesRequested += () => App.OpenQuickPanel(true);
        accountPage.SyncRequested += async () => await RunAsync(LoadHomesAsync);
        accountPage.LogoutRequested += OnLogout;
        var about=aboutPage;
        about.AutomaticUpdatesChanged+=enabled=>{if(loadingPreferences)return;preferences=preferences with{CheckUpdates=enabled};if(!App.StartupDiagnostic)preferences.Save(DataDirectory);};
        appearancePage.LanguageChanged+=language=>{if(loadingPreferences)return;preferences=preferences with{Language=language};if(!App.StartupDiagnostic)preferences.Save(DataDirectory);ShowMessage(UiText.Get("RestartLanguage"),InfoBarSeverity.Informational);};
        about.CommandRequested += async command =>
        {
            switch (command)
            {
                case "updates": await CheckForUpdatesAsync();break;
                case "changes": await ShowInformationAsync(MiHome.WinUI.Infrastructure.UiText.Get("Sde6811e73d40"), File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Changelog.txt"))); break;
                case "privacy": await ShowInformationAsync(MiHome.WinUI.Infrastructure.UiText.Get("S9d06d61ee41f"), File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Privacy.txt"))); break;
                case "diagnostics": await RunAsync(ExportDiagnosticsAsync); break;
                case "licenses": SelectSettings("licenses"); break;
            }
        };
        settingsPages["appearance"] = appearancePage;
        settingsPages["account"] = accountPage;
        settingsPages["about"] = about;
        settingsPages["licenses"] = new Pages.LicenseCatalogPage();
        InitializeAdapters();
        SelectSettings("appearance");
    }
    private void SelectSettings(string section)
    {
        settingsSection = section; licensesOpen = false;
        if (section == "appearance" && ready && !App.StartupDiagnostic) LoadDesktopPreferences();
        settingsToolbar.Visibility=Visibility.Collapsed;settingsToolbar.Content=null;
        choosingSettings = true;
        settingsNavigation.SelectedItem = settingsNavigation.Items.Cast<ListViewItem>().First(i => i.Tag.ToString() == section);
        settingsTabs.SelectedItem = settingsTabs.Items.First(i => i.Tag.ToString() == section);
        choosingSettings = false;
        settingsContent.Content = settingsPages[section];
        settingsScroll?.ChangeView(null,0,null,true);
        if(section=="adapters") _=RefreshAdapterCatalogAsync();
        accountPage.FamilyName = (Homes.SelectedItem as Core.Home)?.Name ?? "尚未选择家庭";
        ShowCurrentView();
    }
    private void OpenLicenseReader(string name)
    {
        if (licensesView is null)
        {
            licensesView = new LicensesView(); licensesView.BackRequested += CloseLicenses; LicensesPage.Content = licensesView;
        }
        licensesOpen = true; ShowCurrentView(); licensesView.OpenComponent(name);
    }
    private void ApplyPreferences()
    {
        Root.RequestedTheme = preferences.Theme switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        timer.Interval = TimeSpan.FromSeconds(Math.Max(10, preferences.RefreshSeconds));
        if (preferences.RefreshSeconds == 0) timer.Stop(); else if (detailOpen) timer.Start();
        browser.CompactLayout = false;
        if (ready) { ApplyFilter(); ApplyDetailPresentation(); UpdateTitleBarTheme(); }
    }
    internal void OpenDesktopSettings()
    {
        Navigation.SelectedItem = Navigation.SettingsItem;
        SelectSettings("appearance");
    }
    internal void ShowDesktopMessage(string text) => ShowMessage(text);
    private void OnDesktopPreferencesChanged()
    {
        if (loadingPreferences || !ready || App.StartupDiagnostic) return;
        try
        {
            preferences = preferences with { StartHidden = appearancePage.StartHidden, CloseToTray = appearancePage.CloseToTray };
            preferences.Save(DataDirectory);
        }
        catch { ShowMessage(UiText.Get("DesktopSaveFailed")); }
        LoadDesktopPreferences();
    }
    private void LoadDesktopPreferences()
    {
        loadingPreferences = true;
        appearancePage.StartupEnabled = StartupRegistration.Enabled;
        appearancePage.StartHidden = preferences.StartHidden;
        appearancePage.CloseToTray = preferences.CloseToTray;
        appearancePage.SetStartupBlocked(StartupRegistration.DisabledByWindows);
        loadingPreferences = false;
    }
    private ScrollViewer DialogContent(UIElement content,double maximumHeight=560) =>
        DeviceUi.Scroll(content,Math.Max(120,Math.Min(maximumHeight,Root.XamlRoot.Size.Height-220)));
    private async Task ShowInformationAsync(string title, string text, bool markdown=false)
    {
        var body=markdown?DocumentPresenter.Build(text,compact:true):(FrameworkElement)DocumentPresenter.Plain(text);
        var dialog = new Dialogs.MessageDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = title, CloseButtonText = MiHome.WinUI.Infrastructure.UiText.Get("S3fd47edce45b") };
        dialog.SetBody(body, Math.Max(120, Math.Min(560, Root.XamlRoot.Size.Height - 220)));
        await dialog.ShowAsync();
    }
    private void OpenAdapterGuide()
    {
        var markdown=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"backend","adapter-agent-guide.md"));
        var bar = new GuideToolbar { Markdown = markdown };
        bar.BackRequested += () => SelectSettings("adapters");
        settingsToolbar.Content = bar; settingsToolbar.Visibility = Visibility.Visible;
        settingsContent.Content = DocumentPresenter.Build(markdown, skipTitle: true);
        settingsScroll.ChangeView(null, 0, null, true); bar.FocusBack();
    }
    private async Task ExportDiagnosticsAsync()
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "MiHome-diagnostics" };
        picker.FileTypeChoices.Add("文本文件", new[] { ".txt" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        var text = $"米家 WinUI {typeof(App).Assembly.GetName().Version}\n.NET {Environment.Version}\nWindows {Environment.OSVersion.Version}\n";
        var stage = Path.Combine(DataDirectory, "startup-stage.log");
        if (File.Exists(stage)) text += File.ReadAllText(stage);
        await Windows.Storage.FileIO.WriteTextAsync(file, text);
    }
}
