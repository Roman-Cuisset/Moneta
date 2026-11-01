using System.Windows;

namespace Moneta.App;

public partial class ReopenReasonWindow : Window
{
    public string Reason { get; private set; } = string.Empty;

    public ReopenReasonWindow(string title, string notice)
    {
        InitializeComponent();
        Title = title;
        ReasonNotice.Text = notice;
        Loaded += (_, _) => ReopenReason.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var reason = ReopenReason.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
        {
            ErrorText.Text = "Saisissez un motif non vide de 1 000 caractères maximum.";
            ReopenReason.Focus();
            return;
        }
        Reason = reason;
        DialogResult = true;
    }
}
