using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Audiomatic;

/// <summary>
/// Raycast-style action panel helpers — reusable across windows.
/// </summary>
public static class ActionPanel
{
    public static Style CreateFlyoutPresenterStyle(double minWidth = 240, double maxWidth = 300)
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(3)));
        style.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
        style.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, minWidth));
        style.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, maxWidth));
        style.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty,
            (Brush)Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"]));
        style.Setters.Add(new Setter(FlyoutPresenter.BorderBrushProperty,
            (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]));
        style.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(1)));
        return style;
    }

    public static Border CreateSeparator()
    {
        return new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
            Margin = new Thickness(6, 1, 6, 1)
        };
    }

    public static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(8, 4, 8, 3),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    public static Button CreateButton(string glyph, string label, string[] keys, Action handler,
        bool isDestructive = false, bool isActive = false)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4),
            MinHeight = 0,
            Tag = label
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Brush foreground;
        if (isDestructive)
            foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 99, 99));
        else if (isActive)
            foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
        else
            foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

        var icon = new FontIcon { Glyph = glyph, FontSize = 12, Foreground = foreground };
        Grid.SetColumn(icon, 0);

        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);

        var keysPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var key in keys)
        {
            keysPanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0, 4, 0),
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                Child = new TextBlock
                {
                    Text = key,
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                }
            });
        }
        Grid.SetColumn(keysPanel, 2);

        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(keysPanel);

        btn.Content = grid;
        btn.Click += (_, _) => handler();

        return btn;
    }
}
