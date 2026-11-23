using System.Globalization;
using System.Windows;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class MarkingDateWindow : Window
{
    private static CultureInfo DisplayCulture => L.Culture;
    private readonly MainViewModel _model;
    private readonly TransactionSelection _selection;
    private bool _submitting;

    public MarkingDateWindow(MainViewModel model, TransactionSelection selection, DateOnly? currentDate)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selection);
        _model = model;
        _selection = selection;
        InitializeComponent();
        MarkingDate.Text = (currentDate ?? DateOnly.FromDateTime(DateTime.Today)).ToString(L.DatePattern, DisplayCulture);
        Loaded += (_, _) => { MarkingDate.Focus(); MarkingDate.SelectAll(); };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting)
            return;
        if (!DateOnly.TryParseExact(MarkingDate.Text.Trim(), L.DatePattern, DisplayCulture, DateTimeStyles.None, out var date)
            || date == default || date > DateOnly.FromDateTime(DateTime.Today))
        {
            ErrorText.Text = L.Get("MarkingDateWindow.EnterAValidDateInMmDdYyyy");
            MarkingDate.Focus();
            return;
        }
        _submitting = true;
        ConfirmMarkingDate.IsEnabled = false;
        try
        {
            _model.ChangeMarkingDate(_selection, date);
            DialogResult = true;
        }
        catch (Exception error)
        {
            ErrorText.Text = L.Get("MarkingDateWindow.TheDateHasNotBeenChangedYourEntry") + error.Message;
            _submitting = false;
            ConfirmMarkingDate.IsEnabled = true;
        }
    }
}
