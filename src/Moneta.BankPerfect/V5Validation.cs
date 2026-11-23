namespace Moneta.BankPerfect;

internal static class V5Validation
{
    private sealed record Row(V5Account Account, V5Operation Operation, V5Extra[] TargetAccounts, V5Extra[] TargetLines)
    {
        public string? AccountUid => Account.Fields.Uid;
        public ulong? Uid => Operation.Uid;
        public bool HasReference => TargetAccounts.Length + TargetLines.Length > 0;
        public bool DuplicateReference => TargetAccounts.Length > 1 || TargetLines.Length > 1;
        public string? TargetAccount => TargetAccounts.FirstOrDefault()?.Value;
        public string? TargetLine => TargetLines.FirstOrDefault()?.RawValue;
    }

    public static IReadOnlyList<V5Transfer> Validate(List<V5Account> accounts, V5Diagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < accounts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accounts[index] = CheckLedger(accounts[index], diagnostics);
        }

        var byAccount = accounts.Where(account => !string.IsNullOrWhiteSpace(account.Fields.Uid))
            .ToLookup(account => account.Fields.Uid!, StringComparer.Ordinal);
        foreach (var duplicate in byAccount.Where(group => group.Count() > 1))
        foreach (var account in duplicate)
            diagnostics.Add("duplicate-account-uid", V5Finding.Uncertain, "UID de compte présent plusieurs fois.", account.Source with { Field = 15 });

        var rows = accounts.SelectMany(account => account.Operations.Select(operation =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new Row(account, operation, Other(operation, "#sisterAccUid"), Other(operation, "#sisterLineUid"));
        })).ToArray();
        var byUid = rows.Where(row => row.Uid.HasValue).ToLookup(row => row.Uid!.Value);
        foreach (var duplicate in byUid.Where(group => group.Count() > 1))
        foreach (var row in duplicate)
            diagnostics.Add("duplicate-operation-uid", V5Finding.Uncertain, "UID d’opération répété dans le dossier.",
                row.Operation.Extra.First(extra => extra.Key == "UId").Source);

        // Audit competing claims only when a row could use the omitted-account rule.
        Dictionary<ulong, int>? claims = null;
        if (rows.Any(row => row.TargetAccounts.Length == 0 && row.TargetLines.Length == 1))
        {
            claims = new();
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var reference in row.TargetLines)
                    if (V5Values.TryUid(reference.RawValue, out var uid))
                        claims[uid] = claims.GetValueOrDefault(uid) + 1;
            }
        }
        return rows.Where(row => row.HasReference)
            .Select(row =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Resolve(row, byAccount, byUid, claims, diagnostics);
            }).ToList().AsReadOnly();
    }

    private static V5Extra[] Other(V5Operation operation, string key) =>
        operation.Extra.Where(extra => extra.Key == key).ToArray();

    private static V5Account CheckLedger(V5Account account, V5Diagnostics diagnostics)
    {
        if (!account.HasBlock)
        {
            diagnostics.Add("missing-account-block", V5Finding.Uncertain, "Compte source sans bloc positionnel.", account.Source);
            return account;
        }
        // Même à la limite du nombre de lignes, la somme des Currency V5 tient
        // exactement dans un decimal. Les valeurs non qualifiées excluent la somme.
        decimal? sum = account.Operations.All(operation =>
            operation.Amount.Value is { } amount && V5Values.IsHistoricalAmount(amount))
            ? account.Operations.Sum(operation => operation.Amount.Value!.Value)
            : null;
        if (sum is null)
            diagnostics.Add("ledger-sum-unavailable", V5Finding.Uncertain,
                "Somme des opérations indisponible : au moins un montant ou une ligne non qualifiée.", account.Source);
        var candidates = account.Operations.Where(operation =>
            operation.Pointing == V5Pointing.R && operation.Date.Date.HasValue && operation.Amount.Value.HasValue &&
            operation.Payee is "Création du compte courant" or "Solde initial" &&
            operation.Mode is "Versement" or "Prélèvement" or "Virement reçu" &&
            operation.RawCategory == "-1" && operation.Details == "" &&
            !operation.Extra.Any(extra => extra.Key is "#sisterAccUid" or "#sisterLineUid"))
            .Select(operation => operation.Source).ToList().AsReadOnly();
        return account with
        {
            LedgerSum = sum,
            Opening = candidates.Count == 1 ? V5OpeningConfidence.Probable : V5OpeningConfidence.Indeterminate,
            OpeningCandidates = candidates
        };
    }

    private static V5Transfer Resolve(Row row, ILookup<string, V5Account> byAccount,
        ILookup<ulong, Row> byUid, Dictionary<ulong, int>? claims, V5Diagnostics diagnostics)
    {
        var validTargetUid = V5Values.TryUid(row.TargetLine, out var targetUid);
        var link = new V5Transfer(row.Operation.Source, row.AccountUid, row.Uid, row.TargetAccounts.FirstOrDefault()?.RawValue,
            row.TargetLine, validTargetUid ? targetUid : null, null, false, V5TransferStatus.Incomplete);

        V5Transfer WithStatus(V5TransferStatus status, string code, string message)
        {
            diagnostics.Add(code, V5Finding.Uncertain, message, row.Operation.Source);
            return link with { Status = status };
        }

        if (row.DuplicateReference)
            return WithStatus(V5TransferStatus.Ambiguous, "transfer-ambiguous", "Références sœurs répétées dans Extra.");
        if (claims is not null && validTargetUid && RecoverMissingAccount(row, targetUid, byAccount, byUid, claims) is { } recovered)
            return link with
            {
                Target = recovered.Operation.Source,
                Reciprocal = true,
                Status = V5TransferStatus.Linked,
                RecoveredAccountReference = row.TargetAccounts.Length == 0
            };
        if (string.IsNullOrWhiteSpace(row.TargetAccount))
            return WithStatus(V5TransferStatus.Incomplete, "transfer-incomplete", "UID du compte de la ligne sœur absent.");
        if (string.IsNullOrWhiteSpace(row.TargetLine))
            return WithStatus(V5TransferStatus.Incomplete, "transfer-incomplete", byAccount.Contains(row.TargetAccount)
                ? "Compte de la ligne sœur référencé, mais UID de l’opération sœur absent."
                : "Compte de la ligne sœur introuvable et UID de l’opération sœur absent.");
        if (!validTargetUid)
            return WithStatus(V5TransferStatus.Inconsistent, "transfer-invalid-uid", "UID de l’opération sœur invalide.");
        var targetAccounts = byAccount[row.TargetAccount];
        var targetLines = byUid[targetUid];
        if (!targetAccounts.Any() || !targetLines.Any())
            return WithStatus(V5TransferStatus.MissingTarget, "transfer-missing-target", "Compte ou opération sœur introuvable.");
        if (targetAccounts.Count() != 1 || targetLines.Count() != 1)
            return WithStatus(V5TransferStatus.Ambiguous, "transfer-ambiguous", "Compte ou UID de ligne sœur non unique.");
        var target = targetLines.Single();
        if (target.Account.Index != targetAccounts.Single().Index)
            return WithStatus(V5TransferStatus.Inconsistent, "transfer-wrong-account", "L’opération sœur existe dans un autre compte que celui référencé.");
        link = link with { Target = target.Operation.Source };

        if (string.IsNullOrWhiteSpace(row.AccountUid) || !row.Uid.HasValue)
            return WithStatus(V5TransferStatus.Incomplete, "transfer-missing-source-id", "UID de la ligne ou du compte de départ manquant ou invalide.");
        if (target.DuplicateReference || byAccount[row.AccountUid].Count() > 1 || byUid[row.Uid.Value].Count() > 1)
            return WithStatus(V5TransferStatus.Ambiguous, "transfer-ambiguous", "Retour ou identifiants de départ non uniques.");
        if (!string.Equals(row.AccountUid, target.TargetAccount, StringComparison.Ordinal) ||
            !V5Values.TryUid(target.TargetLine, out var returnedUid) || returnedUid != row.Uid)
            return WithStatus(V5TransferStatus.Inconsistent, "transfer-nonreciprocal", "Lien de virement non réciproque.");

        link = link with { Reciprocal = true };
        if (row.Account.Index == target.Account.Index)
            return WithStatus(V5TransferStatus.Inconsistent, "transfer-same-account", "Les deux références visent le même compte.");
        if (row.Operation.Amount.Value is { } left && target.Operation.Amount.Value is { } right)
        {
            var sameCurrency = row.Account.MonetaCurrency is { } currency && currency == target.Account.MonetaCurrency ||
                !string.IsNullOrEmpty(row.Account.Fields.Currency) && row.Account.Fields.Currency == target.Account.Fields.Currency;
            if (left == 0 || right == 0 || Math.Sign(left) == Math.Sign(right) || sameCurrency && left != -right)
                return WithStatus(V5TransferStatus.Inconsistent, "transfer-amounts", "Montants incohérents pour les deux lignes liées.");
        }
        return link with { Status = V5TransferStatus.Linked };
    }

    private static Row? RecoverMissingAccount(Row row, ulong targetUid, ILookup<string, V5Account> byAccount,
        ILookup<ulong, Row> byUid, Dictionary<ulong, int> claims)
    {
        if (row.Uid is not { } uid || byUid[uid].Count() != 1 || byUid[targetUid].Count() != 1)
            return null;
        var target = byUid[targetUid].Single();
        if (target.DuplicateReference || row.TargetAccounts.Length + target.TargetAccounts.Length != 1 ||
            !V5Values.TryUid(target.TargetLine, out var returnedUid) || returnedUid != uid ||
            claims.GetValueOrDefault(uid) != 1 || claims.GetValueOrDefault(targetUid) != 1)
            return null;
        if (row.Account.Index == target.Account.Index ||
            row.AccountUid is not { Length: > 0 } accountUid || target.AccountUid is not { Length: > 0 } targetAccountUid ||
            byAccount[accountUid].Count() != 1 || byAccount[targetAccountUid].Count() != 1 ||
            (row.TargetAccounts.Length == 0 ? target.TargetAccount != accountUid : row.TargetAccount != targetAccountUid))
            return null;

        // Date, amount and labels corroborate two explicit line identities. Never use them to select a line.
        var left = row.Operation;
        var right = target.Operation;
        if (!left.ValidLayout || !right.ValidLayout || left.Date.Date is null || left.Date.Date != right.Date.Date ||
            left.Details != right.Details || row.Account.MonetaCurrency is not { } currency || target.Account.MonetaCurrency != currency ||
            left.Amount.Compatibility != V5AmountCompatibility.Exact || right.Amount.Compatibility != V5AmountCompatibility.Exact ||
            left.Amount.Value is not { } amount || amount == 0 || right.Amount.Value != -amount ||
            !HasTransferLabel(row, target, amount) || !HasTransferLabel(target, row, -amount))
            return null;
        return target;
    }

    private static bool HasTransferLabel(Row row, Row target, decimal amount) =>
        target.Account.Fields.Name is { Length: > 0 } name &&
        (amount < 0
            ? row.Operation.Mode == "Virement émis" && row.Operation.Payee == "Transfert vers le compte " + name
            : row.Operation.Mode == "Virement reçu" && row.Operation.Payee == "Transfert depuis le compte " + name);
}
