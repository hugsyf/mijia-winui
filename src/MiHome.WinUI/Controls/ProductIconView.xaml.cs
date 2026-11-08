using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHome.WinUI.ViewModels;
namespace MiHome.WinUI.Controls;
public sealed partial class ProductIconView : UserControl
{
    public static readonly DependencyProperty TileProperty=DependencyProperty.Register(nameof(Tile),typeof(DeviceTile),typeof(ProductIconView),new PropertyMetadata(null,Changed));
    public static readonly DependencyProperty IconSizeProperty=DependencyProperty.Register(nameof(IconSize),typeof(double),typeof(ProductIconView),new PropertyMetadata(40d,Changed));
    public DeviceTile? Tile {get=>(DeviceTile?)GetValue(TileProperty);set=>SetValue(TileProperty,value);}
    public double IconSize {get=>(double)GetValue(IconSizeProperty);set=>SetValue(IconSizeProperty,value);}
    private int imageVersion;
    public ProductIconView() {InitializeComponent();Loaded+=(_,_)=>UpdateIcon();}
    private static void Changed(DependencyObject owner,DependencyPropertyChangedEventArgs args)=>((ProductIconView)owner).UpdateIcon();
    private void UpdateIcon()
    {
        if(ProductImage is null)return;
        var version=++imageVersion;Width=Height=IconSize;
        ProductImage.Source=null;ProductImage.Opacity=0;Fallback.Visibility=Visibility.Visible;
        Fallback.Content=Tile is {} tile?DeviceUi.Icon(tile.IconGeometry,this,Math.Min(IconSize,28)):null;
        if(Tile is {} current)_=LoadImageAsync(current,version);
    }
    private async Task LoadImageAsync(DeviceTile tile,int version)
    {
        try
        {
            if(await ProductImageCatalog.ResolveAsync(tile) is not {} path)return;
            var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var stream=await file.OpenReadAsync();
            var bitmap=new Microsoft.UI.Xaml.Media.Imaging.BitmapImage {DecodePixelWidth=128};
            await bitmap.SetSourceAsync(stream);
            if(version!=imageVersion)return;
            ProductImage.Source=bitmap;ProductImage.Opacity=1;Fallback.Visibility=Visibility.Collapsed;
        }
        catch { /* Keep the category graphic when a product image is unavailable. */ }
    }
    private void OnImageOpened(object sender,RoutedEventArgs args) {ProductImage.Opacity=1;Fallback.Visibility=Visibility.Collapsed;}
    private void OnImageFailed(object sender,ExceptionRoutedEventArgs args) {ProductImage.Opacity=0;Fallback.Visibility=Visibility.Visible;}
}
