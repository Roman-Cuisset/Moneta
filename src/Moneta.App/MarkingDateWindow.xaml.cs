using System.Globalization;
using System.Windows;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class MarkingDateWindow : Window
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
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
        MarkingDate.Text = (currentDate ?? DateOnly.FromDateTime(DateTime.Today)).ToString("dd/MM/yyyy", French);
        Loaded += (_, _) => { MarkingDate.Focus(); MarkingDate.SelectAll(); };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting)
            return;
        if (!DateOnly.TryParseExact(MarkingDate.Text.Trim(), "dd/MM/yyyy", French, DateTimeStyles.None, out var date)
            || date == default || date > DateOnly.FromDateTime(DateTime.Today))
        {
            ErrorText.Text = "Saisissez une date valide au format jj/mm/aaaa, au plus tard aujourd’hui.";
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
            ErrorText.Text = "La date n’a pas été modifiée. Votre saisie est conservée.\n" + error.Message;
            _submitting = false;
            ConfirmMarkingDate.IsEnabled = true;
        }
    }
}
