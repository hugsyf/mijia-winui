using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
namespace MiHome.WinUI.Controls;

internal sealed record ComponentLicense(string Name, string Version, string License, string Source, string File, string Group)
{
    public string Label => string.IsNullOrEmpty(Version) ? Name : $"{Name} {Version}";
}

public sealed partial class LicensesView : UserControl
{
    private static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Licenses");
    private readonly List<ComponentLicense> catalog = LoadCatalog();
    private bool wide, detailRequested, filtering, directEntry;
    public event Action? BackRequested;
    internal static List<ComponentLicense> LoadCatalog() => JsonSerializer.Deserialize<List<ComponentLicense>>(
        File.ReadAllText(Path.Combine(DirectoryPath, "index.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    internal static string ReadTerms(ComponentLicense component)
    {
        if (Path.GetFileName(component.File) != component.File) throw new InvalidDataException("Invalid license filename.");
        return File.ReadAllText(Path.Combine(DirectoryPath, component.File));
    }
    public LicensesView()
    {
        InitializeComponent();
        components.ItemsSource = catalog;
        components.SelectedIndex = 0;
        ShowComponent();
        components.SelectionChanged += (_, _) =>
        {
            if (filtering || components.SelectedItem is null) return;
            if (wide) detailRequested = true;
            ShowComponent(); UpdateLayoutMode();
        };
        components.ItemClick += (_, e) =>
        {
            components.SelectedItem = e.ClickedItem;
            detailRequested = true; ShowComponent(); UpdateLayoutMode();
            if (!wide) copy.Focus(FocusState.Programmatic);
        };
        search.TextChanged += (_, _) => Filter();
        back.Click += (_, _) => GoBack();
        var backKey = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Left, Modifiers = Windows.System.VirtualKeyModifiers.Menu };
        backKey.Invoked += (_, args) => { GoBack(); args.Handled = true; };
        back.KeyboardAccelerators.Add(backKey);
        copy.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(title.Text + "\n" + license.Text + "\n\n" + terms.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        };
        SizeChanged += (_, _) => UpdateLayoutMode();
        UpdateLayoutMode();
    }
    private void GoBack()
    {
        if (!wide && detailRequested && !directEntry) { detailRequested = false; UpdateLayoutMode(); components.Focus(FocusState.Programmatic); }
        else BackRequested?.Invoke();
    }
    public void OpenComponent(string name)
    {
        directEntry = true;
        search.Text = "";
        components.SelectedItem = catalog.FirstOrDefault(c => c.Name == name);
        detailRequested = true; ShowComponent(); UpdateLayoutMode();
    }
    public void Enter()
    {
        directEntry = false;
        detailRequested = false;
        UpdateLayoutMode();
        search.Focus(FocusState.Programmatic);
    }
    private void Filter()
    {
        var selected = components.SelectedItem as ComponentLicense;
        var query = search.Text.Trim();
        var matches = catalog.Where(c => (c.Label + " " + c.Group + " " + c.License).Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        filtering = true;
        components.ItemsSource = matches;
        components.SelectedItem = selected is not null && matches.Contains(selected) ? selected : matches.FirstOrDefault();
        filtering = false;
        empty.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowComponent(); UpdateLayoutMode();
    }
    private void ShowComponent()
    {
        var item = components.SelectedItem as ComponentLicense;
        title.Text = item?.Label ?? "请选择组件";
        license.Text = item is null ? "" : item.Group + " · " + item.License;
        source.NavigateUri = item is not null && Uri.TryCreate(item.Source, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri : null;
        source.Visibility = source.NavigateUri is null ? Visibility.Collapsed : Visibility.Visible;
        terms.Text = item is null ? "" : ReadTerms(item);
        copy.IsEnabled = item is not null;
        reader.ChangeView(null, 0, null, true);
    }
    private void UpdateLayoutMode()
    {
        wide = ActualWidth >= 760;
        listPane.Visibility = wide || !detailRequested ? Visibility.Visible : Visibility.Collapsed;
        reader.Visibility = wide || detailRequested ? Visibility.Visible : Visibility.Collapsed;
        panes.ColumnDefinitions[0].Width = wide ? new GridLength(280) : new GridLength(1, GridUnitType.Star);
        panes.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(reader, wide ? 1 : 0);
        back.Content = !wide && detailRequested && !directEntry ? "所有组件" : "返回开源许可";
    }
    internal void SelectForDiagnostic(int index) { components.SelectedIndex = index; detailRequested = true; ShowComponent(); UpdateLayoutMode(); }
}
