using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;

namespace MiHome.WinUI.Controls;

public sealed partial class CardResizeThumb : UserControl
{
    public Thumb Handle => ResizeHandle;
    public CardResizeThumb()
    {
        InitializeComponent();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast);
        Handle.PointerPressed += (_, _) => Handle.Focus(FocusState.Pointer);
        PointerEntered += (_, _) => Opacity = 1;
        PointerExited += (_, _) => Opacity = FocusState == FocusState.Unfocused ? .45 : 1;
        GotFocus += (_, _) => Opacity = 1;
        LostFocus += (_, _) => Opacity = .45;
        Tapped += (_, e) => e.Handled = true;
        DoubleTapped += (_, e) => e.Handled = true;
    }
}
