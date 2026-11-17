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
        var picker = new OpenFileDialog { Title = "Choisir un fichier BankPerfect", Filter = "BankPerfect (*.bp)|*.bp", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) await AnalyzeAsync(picker.FileName);
    }

    private async Task AnalyzeAsync(string path)
    {
        preview = null;
        Result = null;
        SourceText.Text = Path.GetFileName(path);
        SourceText.ToolTip = path;
        ReportText.Clear();
        await RunAsync("Analyse en lecture seule…", async token =>
        {
            preview = await Task.Run(() => BankPerfectImporter.Prepare(path, token), token);
            ReportText.Text = Format(preview.Report);
            StatusText.Text = preview.Report.CanImport
                ? "Rapport prêt. Choisissez une destination neuve pour confirmer la création."
                : "Import bloqué. Les causes sont détaillées dans le rapport.";
        });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (preview is not { Report.CanImport: true } approved) return;
        var picker = new SaveFileDialog
        {
            Title = "Confirmer la création d’un nouveau dossier Moneta",
            Filter = "Dossier Moneta (*.moneta)|*.moneta", DefaultExt = ".moneta", AddExtension = true,
            CheckPathExists = true, OverwritePrompt = false,
            FileName = Path.GetFileNameWithoutExtension(SourceText.Text) + ".moneta"
        };
        if (picker.ShowDialog(this) != true) return;
        await RunAsync("Vérification du plan approuvé…", async token =>
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var result = await Task.Run(() => BankPerfectImporter.Import(approved, picker.FileName, token, progress), token);
            Result = result;
            ReportText.Text = $"Dossier créé et vérifié\r\n{result.Destination}\r\n" +
                $"UUID Moneta {result.DocumentId:D}\r\nProvenance SHA-256 {result.ProvenanceSha256}\r\n" +
                $"Sauvegarde et restauration de contrôle vérifiées.\r\n\r\n{Format(result.Report)}";
            StatusText.Text = "Importation terminée.";
        });
    }

    private async Task RunAsync(string title, Func<CancellationToken, Task> action)
    {
        if (work is not null) return;
        using var cancellation = new CancellationTokenSource();
        work = cancellation;
        StatusText.Text = title;
        UpdateButtons();
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { StatusText.Text = "Opération annulée avant publication."; }
        catch (Exception error) when (error is DocumentException or IOException or UnauthorizedAccessException or ArgumentException)
        { StatusText.Text = error.Message; }
        finally { work = null; UpdateButtons(); }
    }

    private void UpdateButtons()
    {
        BrowseButton.IsEnabled = work is null;
        ImportButton.Visibility = preview?.Report.CanImport == true && Result is null ? Visibility.Visible : Visibility.Collapsed;
        ImportButton.IsEnabled = work is null;
        OpenButton.Visibility = Result is null ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Content = work is null ? "Fermer" : "Annuler";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Open_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (work is null) return;
        e.Cancel = true;
        work.Cancel();
        StatusText.Text = "Annulation demandée…";
    }

    private static string Format(BankPerfectImportReport report)
    {
        var text = new StringBuilder();
        text.AppendLine(report.CanImport ? "Rapport préalable · compatible" : "Rapport préalable · bloqué");
        text.AppendLine($"Format {report.Reader.Format ?? "indéterminé"} · {Count(report.Reader.Operations, "ligne", "lignes")} · " +
                        $"{Count(report.Reader.LinkedTransfers, "virement lié", "virements liés")} · " +
                        Count(report.Reader.Categories, "catégorie", "catégories"));
        text.AppendLine($"États source  NP {report.Reader.NP}  /  P {report.Reader.P}  /  R {report.Reader.R}");
        foreach (var account in report.Accounts)
            text.AppendLine($"• {account.Name} · {Count(account.OperationCount, "ligne", "lignes")} · solde {Money(account.Balance, account.Currency)}" +
                            $" · pointé {Money(account.PointedBalance, account.Currency)}");
        if (report.Issues.Count != 0) text.AppendLine();
        foreach (var issue in report.Issues.OrderByDescending(issue => issue.Blocking))
        {
            text.Append(issue.Kind switch
            {
                BankPerfectImportIssueKind.Blocker => "Bloquant · ",
                BankPerfectImportIssueKind.HistoricalUncertainty => "Historique incertain · ",
                BankPerfectImportIssueKind.ProvenanceOnly => "Provenance · ",
                _ => "Information · "
            });
            if (issue.Source is { } source) text.Append($"ligne {source.Line} · ");
            text.AppendLine(issue.Message);
        }
        return text.ToString();
    }

    private static string Count(int count, string singular, string plural) => $"{count} {(count > 1 ? plural : singular)}";

    private static string Money(decimal? amount, string? code) => amount is { } exact
        ? $"{exact.ToString("#,0.00##########################", CultureInfo.GetCultureInfo("fr-FR"))} {code}"
        : "indéterminé";
}
