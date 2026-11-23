using System.ComponentModel;

namespace Moneta.Core.Presentation;

public sealed partial class MainViewModel
{
    public IReadOnlyList<BudgetPlan> Budgets => _document?.Budgets ?? [];
    public IReadOnlyList<FrozenReport> FrozenReports => _document?.FrozenReports ?? [];

    public void PutBudget(BudgetPlan value)
    {
        var document = CaptureDocument();
        value.Validate(document);
        value = value with { Accounts = Array.AsReadOnly(value.Accounts.Order().ToArray()), Categories = Array.AsReadOnly(value.Categories.Order().ToArray()) };
        _document = document with { Budgets = [.. Budgets.Where(item => item.Id != value.Id), value] };
        MarkDirty();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Budgets)));
    }

    public void RemoveBudget(Guid id)
    {
        var document = CaptureDocument();
        if (!Budgets.Any(item => item.Id == id)) throw new ArgumentException("Budget introuvable.", nameof(id));
        _document = document with { Budgets = Budgets.Where(item => item.Id != id).ToArray() };
        MarkDirty();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Budgets)));
    }

    public void AddFrozenReport(FrozenReport value)
    {
        var document = CaptureDocument();
        value.Validate();
        if (value.DraftPayload is null || FrozenReports.Any(item => item.Id == value.Id) ||
            value.PredecessorId is { } parent && !FrozenReports.Any(item => item.Id == parent))
            throw new ArgumentException(L.Get("MainViewModel.ProofOfNewReportOrPreviousVersionNot"));
        _document = document with { FrozenReports = [.. FrozenReports, value] };
        MarkDirty();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FrozenReports)));
    }
}
