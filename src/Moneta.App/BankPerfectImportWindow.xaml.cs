using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Moneta.Core;
using Moneta.Infrastructure.BankPerfect;

namespace Moneta.App;

public partial class BankPerfectImportWindow : Window
{
    private BankPerfectImportPreview? preview;
    private CancellationTokenSource? work;
    public BankPerfectImportResult? Result { get; private set; }

    public BankPerfectImportWindow(string? source = null)
    {
        InitializeComponent();
        if (source is not null)
            Loaded += async (_, _) => await AnalyzeAsync(source);
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = L.Get("BankPerfectImportWindow.ChooseABankperfectFile"), Filter = "BankPerfect (*.bp)|*.bp", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) await AnalyzeAsync(picker.FileName);
    }

    private async Task AnalyzeAsync(string path)
    {
        preview = null;
        Result = null;
        SourceText.Text = Path.GetFileName(path);
        SourceText.ToolTip = path;
        ReportText.Clear();
        await RunAsync(L.Get("BankPerfectImportWindow.ReadOnlyAnalysis"), async token =>
        {
            preview = await Task.Run(() => BankPerfectImporter.Prepare(path, token), token);
            ReportText.Text = Format(preview.Report);
            StatusText.Text = preview.Report.CanImport
                ? L.Get("BankPerfectImportWindow.ReportReadyChooseANewDestinationToConfirm")
                : L.Get("BankPerfectImportWindow.TheCausesAreDetailedInTheReport");
        });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (preview is not { Report.CanImport: true } approved) return;
        var picker = new SaveFileDialog
        {
            Title = L.Get("BankPerfectImportWindow.ConfirmCreationOfANewMonetaDocument"),
            Filter = L.Get("Common.DocumentMonetaMonetaMoneta"), DefaultExt = ".moneta", AddExtension = true,
            CheckPathExists = true, OverwritePrompt = false,
            FileName = Path.GetFileNameWithoutExtension(SourceText.Text) + ".moneta"
        };
        if (picker.ShowDialog(this) == true) await ImportToAsync(approved, picker.FileName);
    }

    private Task ImportToAsync(BankPerfectImportPreview approved, string destination) =>
        RunAsync(L.Get("BankPerfectImportWindow.AuditOfApprovedPlan"), async token =>
        {
            var progress = new Progress<string>(message => StatusText.Text = L.Diagnostic(message));
            var result = await Task.Run(() => BankPerfectImporter.Import(approved, destination, token, progress), token);
            Result = result;
            ReportText.Text = L.Format("BankPerfectImportWindow.DocumentCreatedAndVerified0", (result.Destination)) +
                L.Format("BankPerfectImportWindow.UuidMoneta0DSourceSha2561", (result.DocumentId), (result.ProvenanceSha256)) +
                L.Format("BankPerfectImportWindow.VerifiedBackupAndRestorationOfControl0", (Format(result.Report)));
            StatusText.Text = L.Get("BankPerfectImportWindow.ImportationCompleted");
        });

    private async Task RunAsync(string title, Func<CancellationToken, Task> action)
    {
        if (work is not null) return;
        using var cancellation = new CancellationTokenSource();
        work = cancellation;
        StatusText.Text = title;
        UpdateButtons();
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { StatusText.Text = L.Get("BankPerfectImportWindow.TransactionCancelledBeforePublication"); }
        catch (Exception error) when (error is DocumentException or IOException or UnauthorizedAccessException or ArgumentException)
        { StatusText.Text = L.Diagnostic(error.Message); }
        finally { work = null; UpdateButtons(); }
    }

    private void UpdateButtons()
    {
        BrowseButton.IsEnabled = work is null;
        ImportButton.Visibility = preview?.Report.CanImport == true && Result is null ? Visibility.Visible : Visibility.Collapsed;
        ImportButton.IsEnabled = work is null;
        OpenButton.Visibility = Result is null ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Content = work is null ? "Fermer" : L.Get("Common.Cancel");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Open_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (work is null) return;
        e.Cancel = true;
        work.Cancel();
        StatusText.Text = L.Get("BankPerfectImportWindow.Canceling");
    }

    private static string Format(BankPerfectImportReport report)
    {
        var text = new StringBuilder();
        text.AppendLine(report.CanImport ? L.Get("BankPerfectImportWindow.PreliminaryReportCompatible") : L.Get("BankPerfectImportWindow.PreliminaryReportBlocked"));
        text.AppendLine(L.Format("BankPerfectImportWindow.Format01", (report.Reader.Format ?? "indéterminé"), (Count(report.Reader.Operations, "ligne", "lignes"))) +
                        $"{Count(report.Reader.LinkedTransfers, "virement lié", "virements liés")} · " +
                        Count(report.Reader.Categories, L.Get("BankPerfectImportWindow.Category"), L.Get("BankPerfectImportWindow.Categories")));
        text.AppendLine(L.Format("BankPerfectImportWindow.SourceStatesNp0P1R2", (report.Reader.NP), (report.Reader.P), (report.Reader.R)));
        foreach (var account in report.Accounts)
            text.AppendLine(L.Format("BankPerfectImportWindow.01Balance2", (account.Name), (Count(account.OperationCount, "ligne", "lignes")), (Money(account.Balance, account.Currency))) +
                            L.Format("BankPerfectImportWindow.Cleared0", (Money(account.PointedBalance, account.Currency))));
        if (report.Issues.Count != 0) text.AppendLine();
        foreach (var issue in report.Issues.OrderByDescending(issue => issue.Blocking))
        {
            text.Append(issue.Kind switch
            {
                BankPerfectImportIssueKind.Blocker => "Bloquant · ",
                BankPerfectImportIssueKind.HistoricalUncertainty => L.Get("BankPerfectImportWindow.UncertainHistory"),
                BankPerfectImportIssueKind.ProvenanceOnly => "Provenance · ",
                _ => "Information · "
            });
            if (issue.Source is { } source) text.Append(L.Format("BankPerfectImportWindow.Line0", (source.Line)));
            text.AppendLine(L.Diagnostic(issue.Message));
        }
        return text.ToString();
    }

    private static string Count(int count, string singular, string plural) => $"{count} {(count > 1 ? plural : singular)}";

    private static string Money(decimal? amount, string? code) => amount is { } exact
        ? $"{exact.ToString("#,0.00##########################", L.Culture)} {code}"
        : L.Get("BankPerfectImportWindow.Indeterminate");
}
