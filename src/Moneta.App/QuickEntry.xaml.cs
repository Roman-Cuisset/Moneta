using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Moneta.Core;
using Moneta.Core.Presentation;

namespace Moneta.App;

public partial class QuickEntry : UserControl
{
    private sealed record Buffer(TransactionEntry Fields, TransactionEntry Baseline, CultureInfo Culture);
    private readonly Dictionary<Guid, Buffer> _buffers = new();
    private MainViewModel? _model;
    private Account? _account;
    private IReadOnlyList<LedgerCategory> _categories = [];
    private TransactionEntry _baseline = BlankEntry();
    private CultureInfo _inputCulture = L.Culture;
    private bool _loading;
    private bool _staging;

    public QuickEntry()
    {
        InitializeComponent();
        EntryCategory.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(Category_TextChanged));
    }

    internal bool HasUnvalidatedInput => _buffers.Count > 0 || _account is not null && ReadFields() != _baseline;

    internal void SetDocument(MainViewModel? model)
    {
        if (_model is not null) PropertyChangedEventManager.RemoveHandler(_model, ModelChanged, string.Empty);
        _model = model;
        _account = null;
        _buffers.Clear();
        if (model is not null) PropertyChangedEventManager.AddHandler(model, ModelChanged, string.Empty);
        if (model is not { IsDemo: false, SelectedAccount: not null }) SetFields(_baseline = BlankEntry());
        RefreshContext();
    }

    internal void RefreshLanguage()
    {
        if (_account is not null)
        {
            EntryDate.Text = TranslateDate(EntryDate.Text, _inputCulture);
            _baseline = _baseline with { Date = TranslateDate(_baseline.Date, _inputCulture) };
        }
        _inputCulture = L.Culture;
        ErrorText.Text = string.Empty;
        RefreshLabels();
    }

    internal void RefreshSuggestions()
    {
        if (_account is not null) SetFields(ReadFields());
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainViewModel.SelectedAccount) or nameof(MainViewModel.PendingReconciliations))) return;
        if (Dispatcher.CheckAccess()) RefreshContext();
        else Dispatcher.BeginInvoke(RefreshContext);
    }

    private void RefreshContext()
    {
        var selected = _model is { IsDemo: false } ? _model.SelectedAccount?.Account : null;
        IsEnabled = selected is { IsArchived: false } && _model!.PendingReconciliations.IsEmpty;
        if (selected?.Id == _account?.Id)
        {
            _account = selected;
            RefreshLabels();
            return;
        }

        if (_account is not null && ReadFields() is { } fields && fields != _baseline)
            _buffers[_account.Id] = new(fields, _baseline, _inputCulture);
        _account = selected;
        _inputCulture = L.Culture;
        _baseline = BlankEntry();
        var next = _baseline;
        if (selected is not null && _buffers.Remove(selected.Id, out var buffer))
        {
            _baseline = buffer.Baseline with { Date = TranslateDate(buffer.Baseline.Date, buffer.Culture) };
            next = buffer.Fields with { Date = TranslateDate(buffer.Fields.Date, buffer.Culture) };
        }
        SetFields(next);
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        AmountCaption.Text = _account is null ? L.Get("QuickEntry.Amount") : L.Format("QuickEntry.NativeAmount", _account.Currency.Code);
        Notice.Visibility = _buffers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Notice.Text = _buffers.Count > 0 ? L.Format("QuickEntry.OtherAccounts", _buffers.Count) : string.Empty;
    }

    private static TransactionEntry BlankEntry() => new(DateOnly.FromDateTime(DateTime.Today).ToString(L.DatePattern, L.Culture),
        0, "", "", "", "", "", "");

    private static string TranslateDate(string text, CultureInfo previous) =>
        previous.Name != L.Culture.Name && DateOnly.TryParseExact(text.Trim(), TransactionEntry.DatePattern(previous),
            previous, DateTimeStyles.None, out var date) ? date.ToString(L.DatePattern, L.Culture) : text;

    private TransactionEntry ReadFields() => new(EntryDate.Text, EntryDirection.SelectedIndex, EntryAmount.Text,
        EntryLabel.Text, EntryMode.Text, EntryPayee.Text, EntryCategory.Text, EntrySubcategory.Text);

    private void SetFields(TransactionEntry fields)
    {
        _loading = true;
        try
        {
            var document = _model is { IsDemo: false } ? _model.CaptureDocument() : null;
            _categories = document?.Categories ?? [];
            EntryMode.ItemsSource = TransactionEntry.Names(document?.PaymentMethods.Select(item => item.Name) ?? []);
            EntryPayee.ItemsSource = TransactionEntry.Names(document?.Payees.Select(item => item.Name) ?? []);
            EntryCategory.ItemsSource = TransactionEntry.Names(_categories.Where(item => item.ParentId is null).Select(item => item.Name));
            EntryDate.Text = fields.Date;
            EntryDirection.SelectedIndex = fields.Direction;
            EntryAmount.Text = fields.Amount;
            EntryLabel.Text = fields.Label;
            EntryMode.Text = fields.PaymentMode;
            EntryPayee.Text = fields.Payee;
            EntryCategory.Text = fields.Category;
            EntrySubcategory.ItemsSource = TransactionEntry.Subcategories(_categories, fields.Category);
            EntrySubcategory.Text = fields.Subcategory;
            ErrorText.Text = string.Empty;
        }
        finally { _loading = false; }
    }

    private void Category_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        var typed = EntrySubcategory.Text;
        EntrySubcategory.ItemsSource = TransactionEntry.Subcategories(_categories,
            e.OriginalSource is TextBox textBox ? textBox.Text : EntryCategory.Text);
        EntrySubcategory.Text = typed;
    }

    private void Entry_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || e.Key is not (Key.Enter or Key.Escape) ||
            EntryMode.IsDropDownOpen || EntryPayee.IsDropDownOpen || EntryCategory.IsDropDownOpen ||
            EntrySubcategory.IsDropDownOpen || EntryDirection.IsDropDownOpen) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        if (e.Key == Key.Enter) Stage();
        else if (ReadFields() != _baseline && MessageBox.Show(Window.GetWindow(this), L.Get("QuickEntry.DiscardEntry"),
                     L.Get("QuickEntry.Name"), MessageBoxButton.YesNo, MessageBoxImage.Question,
                     MessageBoxResult.No) == MessageBoxResult.Yes)
            SetFields(_baseline);
    }

    private void Stage_Click(object sender, RoutedEventArgs e) => Stage();

    private void Stage()
    {
        if (_staging || !IsEnabled || _model is null || _account is null || _model.SelectedAccountId != _account.Id) return;
        var fields = ReadFields();
        if (fields == _baseline) return;
        _staging = true;
        try
        {
            var draft = fields.CreateDraft(_account.Currency, _inputCulture);
            _model.AddTransaction(draft);
            _baseline = BlankEntry() with { Date = fields.Date, Direction = fields.Direction };
            SetFields(_baseline);
            EntryMode.Focus();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or DocumentException or OverflowException)
        {
            ErrorText.Text = L.Get("Common.TheDraftCouldNotBeValidatedYourFields") + error.Message;
        }
        finally { _staging = false; }
    }
}
