using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace MiHome.WinUI.Controls;
public sealed partial class CodeBlockView : UserControl
{
    public CodeBlockView() => InitializeComponent();
    internal void SetCode(string language, string code) { CodeLanguage.Text=language.ToUpperInvariant(); Code.Text=code; }
    private void OnCopy(object sender, RoutedEventArgs args) => DocumentPresenter.Copy(Code.Text);
}
