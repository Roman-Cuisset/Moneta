using System.Security.Cryptography;
using System.Text;
using Moneta.BankPerfect;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Infrastructure.BankPerfect;

internal static class BankPerfectPlan
{
    internal static BankPerfectImportPreview Prepare(string path, V5ReadResult read, CancellationToken cancellationToken)
    {
        var source = read.Document;
        var issues = read.Report.Diagnostics
            .Where(diagnostic => diagnostic.Finding != V5Finding.Read)
            .Select(diagnostic => ToImportIssue(diagnostic, source)).ToList();
        var accounts = new List<BankPerfectImportAccount>();
        if (source is not null)
        {
            var linkedRows = source.Transfers.Where(link => link.Status == V5TransferStatus.Linked)
                .Select(link => link.Source).ToHashSet();
            foreach (var account in source.Accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (account.MonetaCurrency != "EUR")
                    issues.Add(new("currency-scope", "La devise native du compte n’est pas qualifiée pour cet import.", true, account.Source));
                if (account.Fields.VirtualMarker is "virtual" || account.Fields.DisabledMarker is "disabled" ||
                    account.Fields.Name?.StartsWith('_') == true)
                    issues.Add(new("account-scope", "Compte virtuel, désactivé ou spécial hors du périmètre qualifié.", true, account.Source));
                var opening = FindOpening(account);
                if (account.Operations.Count != 0 && opening is null)
                    issues.Add(new("opening-uncertain", "L’ouverture historique de ce registre ne peut pas être distinguée avec suffisamment de certitude.", true, account.Source));
                else if (opening is not null)
                    issues.Add(new("opening-probable",
                        "Une ligne correspond à une ouverture historique probable. Le solde de départ Moneta est nul et chaque ligne est reprise une seule fois.",
                        false, opening) { Kind = BankPerfectImportIssueKind.HistoricalUncertainty });
                foreach (var row in account.Operations)
                    if (!linkedRows.Contains(row.Source) && row.Payee is { } payee &&
                        (payee.StartsWith("Transfert vers le compte ", StringComparison.Ordinal) ||
                         payee.StartsWith("Transfert depuis le compte ", StringComparison.Ordinal)))
                        issues.Add(new("transfer-missing-links", "Signature d’une jambe de transfert sans références réciproques.", true, row.Source));
                var canSum = account.LedgerSum.HasValue && account.Operations.All(row => row.Pointing != V5Pointing.Unknown);
                accounts.Add(new(account.Fields.Name ?? "", account.MonetaCurrency, account.Operations.Count, account.LedgerSum,
                    canSum ? account.Operations.Where(row => row.Pointing != V5Pointing.NP).Sum(row => row.Amount.Value!.Value) : null,
                    canSum ? account.Operations.Where(row => row.Pointing == V5Pointing.NP).Sum(row => row.Amount.Value!.Value) : null,
                    opening));
                if (account.Operations.Any(row => row.AdditionalDate.Present && row.AdditionalDate.Date.HasValue))
                    issues.Add(new("additional-date-provenance",
                        "Les dates supplémentaires restent dans la provenance. Leur rôle peut dépendre de la version de BankPerfect.",
                        false, account.Source) { Kind = BankPerfectImportIssueKind.ProvenanceOnly });
                if (account.Operations.Any(row => row.Pointing == V5Pointing.R))
                    issues.Add(new("legacy-reconciliation", "Les états R sont repris comme états hérités protégés, sans session de rapprochement.", false, account.Source));
            }
        }
        if (read.SourceSha256 is null || source is null || accounts.Count == 0)
            issues.Add(new("source-unavailable", "Aucun registre V5 complet ne peut être préparé.", true));

        var report = new BankPerfectImportReport(read.SourceSha256, read.Report, accounts.AsReadOnly(), issues.AsReadOnly());
        if (report.CanImport)
        {
            // Use stable draft identities for validation. Execution allocates a fresh namespace for the new document.
            var draftId = StableId(Guid.Empty, report.SourceSha256!);
            try
            {
                var plan = Build(source!, report, draftId, DateTimeOffset.UnixEpoch, cancellationToken);
                _ = BankPerfectAudit.Encode(plan, source!, report);
            }
            catch (PlanException error) { issues.Add(new(error.Code, error.Message, true, error.Location)); }
            catch (Exception error) when (error is DocumentException or ArgumentException or OverflowException)
            {
                issues.Add(new("target-invariant", error.Message, true));
            }
        }
        return new(path, source, report);
    }

    private static BankPerfectImportIssue ToImportIssue(V5Diagnostic diagnostic, V5Document? source)
    {
        var kind = diagnostic.Code switch
        {
            "missing-document-id" or "missing-account-uid" or "missing-operation-uid" or "missing-additional-date" or
                "earlier-additional-date" => BankPerfectImportIssueKind.HistoricalUncertainty,
            "logo" => BankPerfectImportIssueKind.ProvenanceOnly,
            "extension-data" when source?.Extensions.Any(extension => extension.Header.Source == diagnostic.Source &&
                extension.Header.Key == "bp_script_data_fabiobptransfer") == true => BankPerfectImportIssueKind.ProvenanceOnly,
            _ => BankPerfectImportIssueKind.Blocker
        };
        return new(diagnostic.Code, diagnostic.Message, kind == BankPerfectImportIssueKind.Blocker, diagnostic.Source) { Kind = kind };
    }

    private static V5Location? FindOpening(V5Account account)
    {
        if (account.OpeningCandidates.Count != 1) return null;
        var row = account.Operations.Single(operation => operation.Source == account.OpeningCandidates[0]);
        // Two BP8 signatures corroborated by the native corpus. A candidate with another payload
        // or a marked date that differs from the operation cannot be qualified by this rule.
        if (row.Extra.Any(extra => extra.Key != "UId") ||
            row.AdditionalDate.Date is { } additional && additional != row.Date.Date) return null;
        return (row.Payee, row.RawMode, row.Amount.Value) switch
        {
            ("Création du compte courant", "40", >= 0) or
            ("Création du compte courant", "23", < 0) or
            ("Solde initial", "42", > 0) => row.Source,
            _ => null
        };
    }

    internal static BankPerfectTarget Build(V5Document source, BankPerfectImportReport report,
        Guid documentId, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        var registry = new CurrencyRegistry();
        var categoryBySource = new Dictionary<uint, (LedgerCategory Category, LedgerCategory? Parent)>();
        var categoriesByPosition = new Dictionary<int, LedgerCategory>();
        var payees = new Dictionary<string, LedgerPayee>(StringComparer.Ordinal);
        var methods = new Dictionary<string, PaymentMethod>(StringComparer.Ordinal);
        var objects = new List<BankPerfectObjectMapping>();
        var accounts = new Account[source.Accounts.Count];
        var operations = new Dictionary<V5Location, (V5Operation Source, Transaction Target)>();
        var transfers = new List<TransferChange>();
        // Moneta resolves same-day ties with CreatedUtc. Preserve the physical source
        // order within each account using technical ticks up to the import time.
        var started = timestamp.AddTicks(-report.Reader.Operations);
        var operationOrdinal = 0;

        Guid Id(string key) => StableId(documentId, key);
        void Add(string type, Guid target, V5Location? location, string? uid, IReadOnlyList<string?> fields, bool opening = false)
            => objects.Add(new(type, target, location, uid, fields, opening));

        var name = Header(source, "utilisateur");
        var historicalId = Header(source, "guid");
        Add("document", documentId, null, historicalId, [name]);
        foreach (var category in source.Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = category.Source.Item!.Value;
            var parent = category.ParentItem is { } parentPosition ? categoriesByPosition[parentPosition] : null;
            var target = new LedgerCategory(Id(FormattableString.Invariant($"category/{position}")), category.Name, parent?.Id);
            categoriesByPosition.Add(position, target);
            categoryBySource.Add((category.Id ?? category.LegacyPositionId)!.Value, (target, parent));
            Add("category", target.Id, category.Source, category.RawId, [category.Raw]);
        }
        foreach (var mode in source.Modes)
        {
            var method = new PaymentMethod(Id("mode/" + mode.Name), mode.Name);
            if (!methods.TryAdd(mode.Name, method))
                throw new PlanException("payment-name-collision", "Deux modes de paiement source partagent le même nom.", mode.Source);
            Add("payment-method", method.Id, mode.Source, null, [mode.Raw]);
        }
        var templateNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in source.PayeeTemplates)
        {
            if (string.IsNullOrWhiteSpace(template.Payee) || !templateNames.Add(template.Payee))
                throw new PlanException("payee-template",
                    "Le modèle de tiers ne désigne pas un tiers distinct représentable.", template.Source);
            var payee = Payee(template.Payee, template.Source);
            _ = Method(template.Mode!, template.Source);
            Add("payee-template", payee!.Id, template.Source, null, template.Fields);
        }

        foreach (var account in source.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currency = registry.Get(account.MonetaCurrency!);
            var target = new Account(Id(FormattableString.Invariant($"account/{account.Index}")), account.Fields.Name ?? "", currency, 0)
                { CreatedUtc = started, ModifiedUtc = timestamp };
            accounts[account.Index] = target;
            Add("account", target.Id, account.Source, account.Fields.Uid, account.Fields.Raw);
            var opening = report.Accounts[account.Index].Opening;
            foreach (var row in account.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Mode == "Titre")
                    throw new PlanException("security-operation", "Les opérations de titres sont hors du périmètre qualifié.", row.Source);
                var payee = Payee(row.Payee, row.Source);
                var method = Method(row.Mode!, row.Source);
                var (category, parent) = row.CategoryId is { } id ? categoryBySource[id] : default;
                var amount = row.Amount.Value!.Value;
                var operationTime = started.AddTicks(operationOrdinal++);
                var label = !string.IsNullOrWhiteSpace(row.Payee) ? row.Payee
                    : row.Details is { Length: <= 240 } details && !string.IsNullOrWhiteSpace(details) ? details : row.Mode!;
                var value = new Transaction(Id(FormattableString.Invariant($"operation/{account.Index}/{row.Source.Line}")),
                    target.Id, row.Date.Date!.Value, label, new(payee?.Name ?? ""),
                    new(parent?.Name ?? category?.Name ?? "") { Subcategory = parent is null ? null : category?.Name },
                    decimal.Abs(amount), currency, amount < 0 ? TransactionDirection.Debit : TransactionDirection.Credit,
                    row.Pointing switch
                    {
                        V5Pointing.NP => ReconciliationStatus.Unmarked,
                        V5Pointing.P => ReconciliationStatus.Marked,
                        V5Pointing.R => ReconciliationStatus.Reconciled,
                        _ => throw new PlanException("pointing-state", "État de pointage non représentable.", row.Source)
                    }, row.Details!)
                {
                    CreatedUtc = operationTime, ModifiedUtc = operationTime, PayeeId = payee?.Id,
                    CategoryId = parent?.Id ?? category?.Id, SubcategoryId = parent is null ? null : category?.Id,
                    PaymentMode = method.Name, PaymentMethodId = method.Id
                };
                operations.Add(row.Source, (row, value));
                var isOpening = row.Source == opening;
                objects.Add(new("operation", value.Id, row.Source, RawUid(row), row.Fields, isOpening)
                {
                    OpeningConfidence = isOpening ? account.Opening : null
                });
            }
        }

        var document = new FinancialDocument(documentId, string.IsNullOrWhiteSpace(name) ? "Import BankPerfect" : name,
            started, timestamp, registry.Currencies.ToArray(), source.AccountOrder.Select(index => accounts[index]).ToArray())
        {
            SelectedAccountId = accounts[source.DefaultAccountIndex ?? source.AccountOrder[0]].Id,
            Categories = categoriesByPosition.Values.ToArray(), Payees = payees.Values.ToArray(), PaymentMethods = methods.Values.ToArray()
        };
        // The storage APIs validate against the full catalog repeatedly. Bound that work in preflight.
        if ((long)operations.Count * (accounts.Length + categoriesByPosition.Count + payees.Count + methods.Count) > 50_000_000)
            throw new PlanException("catalog-work-limit", "Le registre et son catalogue dépassent la capacité de validation exhaustive de cet import.", null);
        MonetaFile.ValidateSnapshot(document);
        foreach (var item in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { FinancialRules.ValidateTransaction(item.Value.Target, document); }
            catch (ArgumentException error) { throw new PlanException("operation-invariant", error.Message, item.Key); }
        }

        var links = source.Transfers.ToDictionary(link => link.Source);
        foreach (var link in source.Transfers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (debitRow, debit) = operations[link.Source];
            if (debit.Direction != TransactionDirection.Debit) continue;
            var (creditRow, credit) = operations[link.Target!];
            var transfer = new Transfer(Id(FormattableString.Invariant($"transfer/{link.Source.Block}/{link.Source.Line}")),
                debit.Id, credit.Id, null, debit.Date, "", debit.Details, null, null, timestamp, timestamp);
            try { TransferRules.Validate(new(transfer, debit, credit, null), document); }
            catch (ArgumentException error) { throw new PlanException("transfer-invariant", error.Message, link.Source); }
            transfers.Add(new(transfer, 0));
            objects.Add(new("transfer", transfer.Id, link.Source, RawUid(debitRow),
                [link.SourceAccountUid, link.RawTargetAccountUid, link.RawTargetOperationUid])
            {
                Transfer = new(Leg(debit, debitRow, link), Leg(credit, creditRow, links[creditRow.Source]))
            });
        }
        if (source.Transfers.Count != 2 * transfers.Count)
            throw new PlanException("transfer-count", "Les références de virement ne forment pas des paires attestées.", null);
        return new(document, operations.Values.Select(value => new TransactionChange(value.Target, 0)).ToArray(),
            transfers.AsReadOnly(), objects.AsReadOnly());

        LedgerPayee? Payee(string? value, V5Location location)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (payees.TryGetValue(value, out var known)) return known;
            var target = new LedgerPayee(Id("payee/" + value), value);
            payees.Add(value, target);
            Add("payee", target.Id, location, null, [value]);
            return target;
        }

        PaymentMethod Method(string value, V5Location location)
        {
            if (methods.TryGetValue(value, out var known)) return known;
            var target = new PaymentMethod(Id("mode/" + value), value);
            methods.Add(value, target);
            Add("payment-method", target.Id, location, null, [value]);
            return target;
        }
    }

    private static string? RawUid(V5Operation row) => row.Extra.FirstOrDefault(extra => extra.Key == "UId")?.RawValue;

    private static BankPerfectTransferLeg Leg(Transaction target, V5Operation source, V5Transfer link) =>
        new(target.AccountId, target.Id, source.Source, link.SourceAccountUid, RawUid(source),
            link.RawTargetAccountUid, link.RawTargetOperationUid);

    private static string? Header(V5Document source, string key) => source.Header
        .FirstOrDefault(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static Guid StableId(Guid namespaceId, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(namespaceId.ToString("N") + "/" + key));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes.AsSpan(0, 16), bigEndian: true);
    }

    internal sealed class PlanException(string code, string message, V5Location? location) : Exception(message)
    {
        public string Code { get; } = code;
        public V5Location? Location { get; } = location;
    }
}
