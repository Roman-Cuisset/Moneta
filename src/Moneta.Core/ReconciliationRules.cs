namespace Moneta.Core;

public static class ReconciliationRules
{
    // Reference names and currency display preferences are mutable catalog presentation,
    // not the financial identity attested by a statement snapshot.
    public static bool FinancialPayloadEquals(Transaction left, Transaction right) =>
        left.Id == right.Id && left.AccountId == right.AccountId && left.Date == right.Date &&
        left.Label == right.Label && left.Amount == right.Amount &&
        left.Currency.Code == right.Currency.Code && left.Currency.DecimalPlaces == right.Currency.DecimalPlaces &&
        left.Direction == right.Direction && left.Details == right.Details &&
        left.PayeeId == right.PayeeId && left.CategoryId == right.CategoryId &&
        left.SubcategoryId == right.SubcategoryId && left.PaymentMethodId == right.PaymentMethodId &&
        left.Valuation == right.Valuation && left.IsDeleted == right.IsDeleted && left.CreatedUtc == right.CreatedUtc;

    public static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new ArgumentException("Un motif non vide, limité à 1 000 caractères, est obligatoire.", nameof(reason));
    }

    public static void ValidatePointing(Transaction value, bool pointed, DateOnly markedOn, DateOnly today)
    {
        if (value.IsDeleted || value.Status == ReconciliationStatus.Reconciled)
            throw new InvalidOperationException("Une opération rapprochée ou supprimée ne peut pas être pointée ou dépointée. Rouvrez d’abord son rapprochement.");
        if (pointed && (markedOn == default || markedOn > today))
            throw new ArgumentException("La date de pointage doit être renseignée et ne peut pas être future.", nameof(markedOn));
        if (pointed && value.Status == ReconciliationStatus.Unmarked && value.Date > today)
            throw new InvalidOperationException("Une opération future ne peut pas être pointée.");
    }

    // Historical validation deliberately has no today check: rewinding the clock must
    // not make a previously valid, durable statement unreadable.
    public static void ValidateSession(ReconciliationSession session, Account account,
        ReconciliationSession? previous, IReadOnlyList<ReconciliationEntry> entries)
    {
        if (session.Id == Guid.Empty || session.AccountId != account.Id || session.StatementDate == default ||
            session.StatementNumber is null || session.StatementNumber.Length > 120 ||
            session.ValidatedUtc == default || session.ValidatedUtc.Offset != TimeSpan.Zero ||
            !Enum.IsDefined(session.State))
            throw new ArgumentException("Relevé de rapprochement invalide.", nameof(session));
        _ = ExactMoney.ToMinorUnits(session.OpeningBalance, account.Currency);
        _ = ExactMoney.ToMinorUnits(session.ExpectedClosingBalance, account.Currency);
        _ = ExactMoney.ToMinorUnits(session.CalculatedBalance, account.Currency);
        if (previous is null)
        {
            if (session.PreviousSessionId is not null || session.OpeningBalance != account.OpeningBalance)
                throw new ArgumentException("Le premier relevé doit partir du solde initial du compte.");
        }
        else if (previous.AccountId != account.Id ||
            session.PreviousSessionId != previous.Id || session.OpeningBalance != previous.ExpectedClosingBalance ||
            session.StatementDate <= previous.StatementDate)
            throw new ArgumentException("La chaîne des relevés actifs est incohérente.");
        if (session.State == ReconciliationSessionState.Validated)
        {
            if (session.Revision != 1 || session.ReopenedUtc is not null || session.ReopenReason is not null)
                throw new ArgumentException("État du relevé validé incohérent.");
        }
        else
        {
            if (session.Revision != 2 || session.ReopenedUtc is not { } reopened ||
                reopened.Offset != TimeSpan.Zero || reopened < session.ValidatedUtc)
                throw new ArgumentException("État du relevé rouvert incohérent.");
            ValidateReason(session.ReopenReason!);
        }
        var ids = new HashSet<Guid>();
        var calculated = session.OpeningBalance;
        foreach (var entry in entries)
        {
            var value = entry.Original;
            if (entry.SessionId != session.Id || value.Id == Guid.Empty || !ids.Add(value.Id) ||
                value.AccountId != account.Id || value.IsDeleted || value.Date == default || value.Date > session.StatementDate ||
                value.Status is not (ReconciliationStatus.Unmarked or ReconciliationStatus.Marked) ||
                value.ReconciliationId is not null || value.Currency.Code != account.Currency.Code ||
                value.Currency.DecimalPlaces != account.Currency.DecimalPlaces || value.Amount < 0 ||
                value.Revision < 1 || !Enum.IsDefined(value.Direction) ||
                (value.Status == ReconciliationStatus.Unmarked && value.MarkedOn is not null) || value.MarkedOn == default(DateOnly))
                throw new ArgumentException("Une opération du relevé est invalide, dupliquée ou inéligible.");
            _ = ExactMoney.ToMinorUnits(value.Amount, value.Currency);
            calculated = checked(calculated + FinancialRules.SignedAmount(value));
        }
        if (calculated != session.CalculatedBalance || calculated != session.ExpectedClosingBalance)
            throw new ArgumentException("L’écart du relevé doit être exactement nul.");
    }
}
