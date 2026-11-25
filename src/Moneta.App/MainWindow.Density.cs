using System.Windows;
using System.Windows.Controls;

namespace Moneta.App;

public partial class MainWindow
{
    private static readonly DependencyPropertyKey IsCompactRegisterPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCompactRegister), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsCompactRegisterProperty = IsCompactRegisterPropertyKey.DependencyProperty;
    public bool IsCompactRegister => (bool)GetValue(IsCompactRegisterProperty);

    private void RegisterDensity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string density }) return;
        var compact = density == "Compact";
        if (compact != App.Settings.Current.CompactRegister)
            App.SaveSettings(App.Settings.Current with { CompactRegister = compact }, this);
        ApplyRegisterDensity();
    }

    private void ApplyRegisterDensity()
    {
        var compact = App.Settings.Current.CompactRegister;
        SetValue(IsCompactRegisterPropertyKey, compact);
        TransactionsGrid.MinRowHeight = compact ? 30 : 0;
        TransactionsGrid.RowHeight = compact ? double.NaN : 42;
        StandardRegisterItem.IsChecked = !compact;
        CompactRegisterItem.IsChecked = compact;
    }
}
