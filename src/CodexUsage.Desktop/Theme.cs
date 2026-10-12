using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexUsage.Desktop;

internal static class Theme
{
    internal static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    internal static readonly SolidColorBrush Background = Brush("#10151D"), Card = Brush("#19222E"), Line = Brush("#2C3747"), Text = Brush("#F1F5FA"), Muted = Brush("#94A4B9"), Accent = Brush("#69DCC1"), Warning = Brush("#F4C36B");
    internal static TextBlock Label(string text, double size = 13, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Text, TextWrapping = TextWrapping.Wrap };
    internal static Border Box(UIElement child, Thickness? padding = null) => new() { Child = child, Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = padding ?? new Thickness(16), Margin = new Thickness(0,0,0,12) };
    internal static Button Button(string label, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(13,9,13,9), Cursor = System.Windows.Input.Cursors.Hand, FontSize = 12, FontWeight = FontWeights.SemiBold, MinHeight = 36, BorderThickness = new Thickness(1), BorderBrush = primary ? Accent : Line, Background = primary ? Accent : Card, Foreground = primary ? Background : Text };
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "buttonBorder";
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty,new System.Windows.TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty,new System.Windows.TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty,new System.Windows.TemplateBindingExtension(Control.BorderThicknessProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.MarginProperty,new System.Windows.TemplateBindingExtension(Control.PaddingProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(content); template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty,.82));
        template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.45)); template.Triggers.Add(disabled);
        button.Template = template;
        System.Windows.Automation.AutomationProperties.SetName(button,label);
        return button;
    }
}
