using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
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
            ThemeHelper.Brush("AcrylicInAppFillColorDefaultBrush")));
        style.Setters.Add(new Setter(FlyoutPresenter.BorderBrushProperty,
            ThemeHelper.Brush("CardStrokeColorDefaultBrush")));
        style.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(1)));
        return style;
    }

    public static Border CreateSeparator()
    {
        return new Border
        {
            Height = 1,
            Background = ThemeHelper.Brush("DividerStrokeColorDefaultBrush"),
            Margin = new Thickness(6, 1, 6, 1)
        };
    }

    public static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
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
            foreground = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush");
        else
            foreground = ThemeHelper.Brush("TextFillColorPrimaryBrush");

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
                Background = ThemeHelper.Brush("ControlFillColorDefaultBrush"),
                Child = new TextBlock
                {
                    Text = key,
                    FontSize = 10,
                    Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
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

    // ── SVG / link row helpers ───────────────────────────────────

    /// <summary>
    /// Renders an SVG path data string into a Viewbox of the given size,
    /// coloured with the primary text foreground.
    /// </summary>
    public static FrameworkElement CreateSvgIcon(string pathData, double size, bool isFilled = true)
    {
        var path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), pathData),
            Stretch = Stretch.Uniform
        };
        if (isFilled)
        {
            path.Fill = ThemeHelper.Brush("TextFillColorPrimaryBrush");
        }
        else
        {
            path.Stroke = ThemeHelper.Brush("TextFillColorPrimaryBrush");
            path.StrokeThickness = 1.5;
            path.StrokeLineJoin = Microsoft.UI.Xaml.Media.PenLineJoin.Round;
            path.StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
            path.StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
        }
        return new Viewbox { Width = size, Height = size, Child = path, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>
    /// A button row with a custom icon element on the left, a label, and an
    /// external-link arrow on the right — for clickable hyperlink-style rows.
    /// </summary>
    public static Button CreateLinkRow(FrameworkElement icon, string label, Action handler)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(4),
            MinHeight = 0,
            Tag = label
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var externalIcon = new FontIcon
        {
            Glyph = "\uE8A7",   // Link / open-in-new
            FontSize = 10,
            Opacity = 0.4,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(externalIcon, 2);
        grid.Children.Add(externalIcon);

        btn.Content = grid;
        btn.Click += (_, _) => handler();
        return btn;
    }

    // ── Cascade sub-menu pattern ─────────────────────────────────

    /// <summary>
    /// Button with label, current value, and chevron that opens a sub-flyout to the right.
    /// </summary>
    public static Button CreateCascadeButton(string label, string currentValue, Flyout subMenu)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(4),
            MinHeight = 0,
            Flyout = subMenu,
            Tag = label + " " + currentValue
        };

        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        if (!string.IsNullOrEmpty(currentValue))
        {
            var value = new TextBlock { Text = currentValue, FontSize = 11, Opacity = 0.45, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
        }

        var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 9, Opacity = 0.4, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);

        btn.Content = grid;
        return btn;
    }

    /// <summary>
    /// Button with label and chevron that triggers a navigation action (replaces flyout content).
    /// </summary>
    public static Button CreateNavigateButton(string label, Action handler)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(4),
            MinHeight = 0,
            Tag = label
        };

        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 9, Opacity = 0.4, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(chevron);

        btn.Content = grid;
        btn.Click += (_, _) => handler();
        return btn;
    }

    /// <summary>
    /// Creates a sub-flyout with radio-style check items, positioned to the right.
    /// </summary>
    public static Flyout CreateRadioSubMenu(
        (string key, string label)[] options, string currentKey, Action<string> onSelected)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedTop,
            FlyoutPresenterStyle = CreateFlyoutPresenterStyle(180, 260)
        };

        var panel = new StackPanel { Spacing = 0 };
        foreach (var (key, label) in options)
        {
            var k = key;
            var btn = CreateCheckItem(label, key == currentKey, () => onSelected(k));
            panel.Children.Add(btn);
        }
        flyout.Content = panel;
        return flyout;
    }

    /// <summary>
    /// Item with label and optional checkmark (for radio-button-style selections).
    /// </summary>
    public static Button CreateCheckItem(string label, bool isSelected, Action handler)
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        if (isSelected)
        {
            var check = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 12,
                Opacity = 0.9,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 1);
            grid.Children.Add(check);
        }

        btn.Content = grid;
        btn.Click += (_, _) => handler();
        return btn;
    }

    /// <summary>
    /// Creates a sub-panel header with a back button and title, for navigate-style sub-panels.
    /// </summary>
    public static StackPanel CreateSubPanelWithHeader(string title, Action onBack)
    {
        var panel = new StackPanel { Spacing = 0 };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var backBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(4),
            MinHeight = 0,
            Content = new FontIcon { Glyph = "\uE72B", FontSize = 12 }
        };
        backBtn.Click += (_, _) => onBack();
        Grid.SetColumn(backBtn, 0);

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        Grid.SetColumn(titleText, 1);

        headerGrid.Children.Add(backBtn);
        headerGrid.Children.Add(titleText);
        panel.Children.Add(headerGrid);
        panel.Children.Add(CreateSeparator());

        return panel;
    }
}
