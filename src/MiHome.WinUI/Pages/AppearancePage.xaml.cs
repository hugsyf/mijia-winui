using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Pages;
public sealed partial class AppearancePage : UserControl
{
    public event Action? Changed;
    internal event Action<string>? LanguageChanged;
    internal string SelectedLanguage {get=>(LanguageOptions.SelectedItem as SelectorBarItem)?.Tag as string??"system";set=>LanguageOptions.SelectedItem=LanguageOptions.Items.Cast<SelectorBarItem>().FirstOrDefault(item=>(string)item.Tag==value);}
    private void OnLanguageChanged(SelectorBar sender,SelectorBarSelectionChangedEventArgs args)=>LanguageChanged?.Invoke(SelectedLanguage);
    internal int ThemeIndex { get => ThemeOptions.Items.IndexOf(ThemeOptions.SelectedItem); set => ThemeOptions.SelectedItem = value >= 0 && value < ThemeOptions.Items.Count ? ThemeOptions.Items[value] : null; }
    internal int RefreshIndex { get => RefreshOptions.SelectedIndex; set => RefreshOptions.SelectedIndex = value; }
    public AppearancePage() {InitializeComponent();RefreshOptions.ItemsSource=new[]{Infrastructure.UiText.Get("S69194bd7c9fd"),Infrastructure.UiText.Get("S7928ca287c7a"),Infrastructure.UiText.Get("S52a0cbef7917"),Infrastructure.UiText.Get("S46e152919d00")};}
    private void OnThemeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => Changed?.Invoke();
    private void OnRefreshChanged(object sender, SelectionChangedEventArgs args) => Changed?.Invoke();
}
