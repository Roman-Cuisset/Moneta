using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Tests;

[TestClass]
public sealed class LedgerStorageTests
{
    private string directory = null!;
    private string FileName => Path.Combine(directory, "ledger.moneta");
    private static readonly DateTimeOffset Created = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MonetaLedgerTests-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [TestMethod]
    public void SuccessfulCreationDoesNotLeaveUnpublishedRecoveryCompanions()
    {
        using (var file = MonetaFile.Create(FileName, Document()))
        {
            using var reader = Raw(FileName);
            using var command = reader.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            Assert.AreEqual("wal", command.ExecuteScalar());
        }
        Assert.AreEqual(0, Directory.EnumerateFiles(directory, ".*.tmp*").Count(),
            "SQLite must consolidate its own unpublished work before publication.");
    }

    [TestMethod]
    public void CreatePersistsAllFieldsAndGeneratesDetachedMetadata()
    {
        var metadata = Document();
        var value = Operation(metadata) with { Status = ReconciliationStatus.Reconciled, Details = "Notes Unicode 日本語\nseconde ligne" };
        FinancialDocument saved;
        using (var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(value, 0) }))
        {
            Assert.AreEqual(value, file.GetTransaction(value.Id));
            Assert.AreEqual(1L, file.Snapshot.OperationCount);
            Assert.IsNotNull(file.Snapshot.LedgerDigest);
            Assert.AreEqual(87.66m, file.Snapshot.AccountBalances![value.AccountId]);
            saved = file.Snapshot;
        }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual(value, reopened.GetTransaction(value.Id));
        Assert.AreEqual(saved.LedgerDigest, reopened.Snapshot.LedgerDigest);
        Assert.AreEqual(saved.Categories[1], reopened.Snapshot.Categories[1]);
        Assert.AreEqual(saved.Payees[0], reopened.Snapshot.Payees[0]);
        Assert.AreEqual(saved.PaymentMethods[0], reopened.Snapshot.PaymentMethods[0]);
        Assert.AreEqual(saved.LedgerDigest, MonetaFile.Inspect(FileName).LedgerDigest);
    }

    [TestMethod]
    public void PendingInsertEditDeleteArePrivateUntilExplicitSave()
    {
        var metadata = Document();
        var first = Operation(metadata);
        using var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(first, 0) });
        var digest = file.Snapshot.LedgerDigest;
        var added = Operation(metadata, 4.56m, TransactionDirection.Credit) with { Date = first.Date.AddDays(1) };
        var edited = first with { Amount = 8m, Revision = 2, ModifiedUtc = Created.AddMinutes(1) };
        var pending = new[] { new TransactionChange(edited, 1), new TransactionChange(added, 0) };
        var page = file.ReadPage(metadata.Accounts[0], 0, 100, pending);
        Assert.AreEqual(2L, page.TotalCount);
        Assert.AreEqual(96.56m, page.AccountBalance);
        Assert.AreEqual(100m, page.BalanceBeforePage);
        Assert.AreEqual(first, file.GetTransaction(first.Id));
        Assert.IsNull(file.GetTransaction(added.Id));
        Assert.AreEqual(digest, MonetaFile.Inspect(FileName).LedgerDigest);
        var deleted = edited with { IsDeleted = true };
        var deletedPage = file.ReadPage(metadata.Accounts[0], 0, 100,
            new[] { new TransactionChange(deleted, 1), new TransactionChange(added, 0) });
        Assert.AreEqual(1L, deletedPage.TotalCount);
        Assert.AreEqual(104.56m, deletedPage.AccountBalance);
        file.Save(metadata, pending);
        Assert.AreEqual(edited, file.GetTransaction(first.Id));
        Assert.AreEqual(added, file.GetTransaction(added.Id));
        Assert.AreEqual(96.56m, file.Snapshot.AccountBalances![first.AccountId]);
    }

    [TestMethod]
    public void EditAndSoftDeletePreserveIdentityAndTombstoneHash()
    {
        var metadata = Document();
        var first = Operation(metadata);
        string deletedDigest;
        using (var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(first, 0) }))
        {
            var edited = first with { Label = "Libellé corrigé", Amount = 0m, Status = ReconciliationStatus.Marked, MarkedOn = new DateOnly(2026, 2, 3), Revision = 2, ModifiedUtc = Created.AddMinutes(1) };
            var originalDigest = file.Snapshot.LedgerDigest;
            file.Save(file.Snapshot, new[] { new TransactionChange(edited, 1) });
            Assert.AreEqual(edited, file.GetTransaction(first.Id));
            Assert.AreNotEqual(originalDigest, file.Snapshot.LedgerDigest);
            var tombstone = edited with { IsDeleted = true, Revision = 3, ModifiedUtc = Created.AddMinutes(2) };
            file.Save(file.Snapshot, new[] { new TransactionChange(tombstone, 2) });
            Assert.IsNull(file.GetTransaction(first.Id));
            Assert.AreEqual(tombstone, file.GetTransaction(first.Id, includeDeleted: true));
            Assert.AreEqual(1L, file.Snapshot.OperationCount);
            Assert.AreEqual(0L, file.ReadPage(metadata.Accounts[0], 0, 100, Array.Empty<TransactionChange>()).TotalCount);
            Assert.AreEqual(100m, file.Snapshot.AccountBalances![first.AccountId]);
            deletedDigest = file.Snapshot.LedgerDigest!;
            ExpectError(DocumentError.InvalidData, () => file.Save(file.Snapshot, new[] { new TransactionChange(tombstone with { Revision = 4 }, 3) }));
        }
        using var reopened = MonetaFile.Open(FileName);
        Assert.AreEqual(deletedDigest, reopened.Snapshot.LedgerDigest);
        Assert.IsTrue(reopened.GetTransaction(first.Id, true)!.IsDeleted);
    }

    [TestMethod]
    public void LegitimateIdenticalEntriesHaveDistinctStableIds()
    {
        var metadata = Document();
        var first = Operation(metadata);
        var second = first with { Id = Guid.NewGuid() };
        using var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(first, 0), new TransactionChange(second, 0) });
        var page = file.ReadPage(metadata.Accounts[0], 0, 100, Array.Empty<TransactionChange>());
        Assert.AreEqual(2L, page.TotalCount);
        Assert.AreEqual(75.32m, page.AccountBalance);
        CollectionAssert.AreEqual(new[] { first.Id, second.Id }.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray(), page.Transactions.Select(value => value.Id).ToArray());
    }

    [TestMethod]
    public void RevisionFailureRollsBackMetadataReferencesAndEarlierPatchRows()
    {
        var metadata = Document();
        var first = Operation(metadata);
        using var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(first, 0) });
        var saved = file.Snapshot;
        var extraPayee = new LedgerPayee(Guid.NewGuid(), "Nouveau tiers synthétique");
        var changed = saved with { Name = "Nom non commis", Payees = saved.Payees.Append(extraPayee).ToArray() };
        var added = Operation(metadata, 1m);
        ExpectError(DocumentError.InvalidData, () => file.Save(changed, new[]
        {
            new TransactionChange(added, 0),
            new TransactionChange(first with { Revision = 3, ModifiedUtc = Created.AddMinutes(1) }, 2)
        }));
        Assert.AreSame(saved, file.Snapshot);
        Assert.IsNull(file.GetTransaction(added.Id));
        Assert.AreEqual(first, file.GetTransaction(first.Id));
        var actual = MonetaFile.Inspect(FileName);
        Assert.AreEqual(saved.Name, actual.Name);
        Assert.AreEqual(saved.LedgerDigest, actual.LedgerDigest);
        Assert.AreEqual(saved.Payees.Count, actual.Payees.Count);
        file.Save(saved, new[] { new TransactionChange(first with { Revision = 2, ModifiedUtc = Created.AddMinutes(1) }, 1) });
        Assert.AreEqual(2L, file.GetTransaction(first.Id)!.Revision);
    }

    [TestMethod]
    public void UpdatesRejectImmutableAccountCurrencyCreationAndDuplicatePatchIds()
    {
        var metadata = Document();
        var first = Operation(metadata);
        using var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(first, 0) });
        var changed = first with { Revision = 2, ModifiedUtc = Created.AddMinutes(1) };
        foreach (var invalid in new[]
        {
            changed with { AccountId = metadata.Accounts[1].Id },
            changed with { CreatedUtc = Created.AddTicks(1) },
            changed with { Currency = metadata.Currencies[1] },
            changed with { ModifiedUtc = Created.AddTicks(-1) },
            changed with { Revision = 1 }
        }) ExpectError(DocumentError.InvalidData, () => file.Save(file.Snapshot, new[] { new TransactionChange(invalid, 1) }));
        ExpectError(DocumentError.InvalidData, () => file.Save(file.Snapshot, new[] { new TransactionChange(changed, 1), new TransactionChange(changed, 1) }));
        ExpectError(DocumentError.InvalidData, () => file.Save(file.Snapshot, new[] { new TransactionChange(first, 0) }));
        Assert.AreEqual(first, file.GetTransaction(first.Id));
    }

    [TestMethod]
    public void InvalidFinancialValuesReferencesAndDisplayProjectionsAreRejected()
    {
        var metadata = Document();
        var valid = Operation(metadata);
        using var file = MonetaFile.Create(FileName, metadata);
        foreach (var invalid in new[]
        {
            valid with { Amount = -0.01m }, valid with { Amount = 0.001m }, valid with { Amount = 92233720368547758.08m },
            valid with { Date = default }, valid with { Id = Guid.Empty }, valid with { Direction = (TransactionDirection)7 },
            valid with { Status = (ReconciliationStatus)7 }, valid with { Label = " " }, valid with { Label = new string('x', 241) },
            valid with { Details = new string('x', 2001) }, valid with { Revision = 2 },
            valid with { PayeeId = Guid.NewGuid() }, valid with { Payee = new Payee("Nom fantôme") },
            valid with { CategoryId = metadata.Categories[1].Id }, valid with { SubcategoryId = Guid.NewGuid() },
            valid with { PaymentMethodId = Guid.NewGuid() }, valid with { PaymentMode = "Mode fantôme" },
            valid with { CreatedUtc = Created.ToOffset(TimeSpan.FromHours(1)) },
            valid with { Valuation = new MoneyValuation(1m, metadata.Currencies[0], valid.Date, "non pris en charge") }
        }) ExpectError(DocumentError.InvalidData, () => file.Save(file.Snapshot, new[] { new TransactionChange(invalid, 0) }));
        Assert.AreEqual(0L, MonetaFile.Inspect(FileName).OperationCount);
    }

    [TestMethod]
    public void CatalogRejectsUnicodeCaseDuplicatesWrongHierarchyAndRemoval()
    {
        var metadata = Document();
        using var file = MonetaFile.Create(FileName, metadata);
        ExpectError(DocumentError.InvalidData, () => file.Save(metadata with
        {
            Payees = metadata.Payees.Append(new LedgerPayee(Guid.NewGuid(), "  E\u0301PICERIE  ")).ToArray()
        }));
        ExpectError(DocumentError.InvalidData, () => file.Save(metadata with
        {
            Categories = metadata.Categories.Append(new LedgerCategory(Guid.NewGuid(), "Niveau interdit", metadata.Categories[1].Id)).ToArray()
        }));
        ExpectError(DocumentError.InvalidData, () => file.Save(metadata with { Categories = metadata.Categories.Take(1).ToArray() }));
        ExpectError(DocumentError.InvalidData, () => file.Save(metadata with { PaymentMethods = Array.Empty<PaymentMethod>() }));
        var wrongParent = metadata with { Categories = new[] { metadata.Categories[0], metadata.Categories[1] with { ParentId = null } } };
        ExpectError(DocumentError.InvalidData, () => file.Save(wrongParent));
    }

    [TestMethod]
    public void CatalogReorderingAndRenamingUseCanonicalReferenceNames()
    {
        var metadata = Document();
        var value = Operation(metadata);
        using var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(value, 0) });
        var changed = file.Snapshot with
        {
            Categories = file.Snapshot.Categories.Reverse().ToArray(),
            Payees = new[] { metadata.Payees[0] with { Name = "Tiers renommé" } },
            PaymentMethods = new[] { metadata.PaymentMethods[0] with { Name = "Mode renommé" } }
        };
        file.Save(changed);
        Assert.AreEqual("Tiers renommé", file.GetTransaction(value.Id)!.Payee.Name);
        Assert.AreEqual("Mode renommé", file.GetTransaction(value.Id)!.PaymentMode);
        CollectionAssert.AreEqual(changed.Categories.ToArray(), file.Snapshot.Categories.ToArray());
    }

    [DataTestMethod]
    [DataRow("UPDATE Transactions SET amount_minor=amount_minor+1;")]
    [DataRow("UPDATE Transactions SET notes='Modification externe';")]
    [DataRow("UPDATE Transactions SET direction=1;")]
    [DataRow("UPDATE Transactions SET status=1;")]
    [DataRow("UPDATE Transactions SET revision=revision+1;")]
    [DataRow("UPDATE Transactions SET is_deleted=1;")]
    [DataRow("UPDATE Transactions SET payee_id=NULL;")]
    public void FullLedgerBaselineDetectsExternalChangesEvenWithForgedInputDigest(string mutation)
    {
        var metadata = Document();
        var value = Operation(metadata);
        using var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(value, 0) });
        var baseline = file.Snapshot;
        using (var raw = Raw(FileName)) Execute(raw, mutation);
        var external = MonetaFile.Inspect(FileName);
        Assert.AreNotEqual(baseline.LedgerDigest, external.LedgerDigest);
        ExpectError(DocumentError.InvalidData, () => file.Save(baseline with { LedgerDigest = external.LedgerDigest, Name = "Ne pas écraser" }));
        Assert.AreSame(baseline, file.Snapshot);
        Assert.AreEqual(external.LedgerDigest, MonetaFile.Inspect(FileName).LedgerDigest);
    }

    [TestMethod]
    public void TombstoneContentsRemainPartOfBaselineDigest()
    {
        var metadata = Document();
        var value = Operation(metadata);
        using var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(value, 0) });
        file.Save(file.Snapshot, new[] { new TransactionChange(value with { IsDeleted = true, Revision = 2, ModifiedUtc = Created.AddMinutes(1) }, 1) });
        var baseline = file.Snapshot;
        using (var raw = Raw(FileName)) Execute(raw, "UPDATE Transactions SET notes='Tombstone modifié extérieurement';");
        Assert.AreNotEqual(baseline.LedgerDigest, MonetaFile.Inspect(FileName).LedgerDigest);
        ExpectError(DocumentError.InvalidData, () => file.Save(baseline));
        Assert.AreEqual(100m, file.ReadPage(metadata.Accounts[0], 0, 100, Array.Empty<TransactionChange>()).AccountBalance);
    }

    [DataTestMethod]
    [DataRow("UPDATE Payees SET name_key='CLE INCORRECTE';")]
    [DataRow("PRAGMA ignore_check_constraints=ON; UPDATE Transactions SET amount_minor=-1;")]
    [DataRow("PRAGMA ignore_check_constraints=ON; UPDATE Transactions SET date='2026-02-30';")]
    [DataRow("UPDATE Transactions SET created_utc='2026-02-03T04:05:06Z';")]
    [DataRow("UPDATE Transactions SET transaction_id=upper(transaction_id);")]
    public void TamperedRowsAreRejectedWithoutRewritingSource(string mutation)
    {
        var metadata = Document();
        var value = Operation(metadata) with { Id = Guid.Parse("abcdefab-cdef-abcd-efab-cdefabcdefab") };
        using (MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(value, 0) })) { }
        using (var raw = Raw(FileName)) Execute(raw, "PRAGMA ignore_check_constraints=ON;" + mutation);
        var bytes = File.ReadAllBytes(FileName);
        ExpectError(DocumentError.Corrupt, () => MonetaFile.Open(FileName));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(FileName));
    }

    [DataTestMethod]
    [DataRow("UPDATE Transactions SET amount_minor=-1;")]
    [DataRow("UPDATE Transactions SET date='2026-02-30';")]
    [DataRow("UPDATE Transactions SET direction=3;")]
    [DataRow("UPDATE Transactions SET status=3;")]
    [DataRow("UPDATE Transactions SET native_code='JPY';")]
    [DataRow("UPDATE Transactions SET subcategory_id=category_id;")]
    [DataRow("UPDATE Transactions SET payee_id='11111111-1111-1111-1111-111111111111';")]
    public void SqlConstraintsEnforceFinancialIntegrity(string mutation)
    {
        var metadata = Document();
        var value = Operation(metadata);
        using var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(value, 0) });
        using var raw = Raw(FileName);
        Execute(raw, "PRAGMA foreign_keys=ON;");
        Assert.ThrowsException<SqliteException>(() => Execute(raw, mutation));
        Assert.AreEqual(value, file.GetTransaction(value.Id));
    }

    [DataTestMethod]
    [DataRow("DROP INDEX Transactions_AccountOrder;")]
    [DataRow("DROP INDEX Categories_RootNames;")]
    [DataRow("CREATE INDEX UnexpectedLedgerIndex ON Transactions(label);")]
    [DataRow("DROP INDEX Transactions_AccountOrder; CREATE INDEX Transactions_AccountOrder ON Transactions(account_id,date,transaction_id);")]
    public void LedgerIndexWhitelistRejectsMissingAdditionalAndChangedIndexes(string mutation)
    {
        using (MonetaFile.Create(FileName, Document())) { }
        using (var raw = Raw(FileName)) Execute(raw, mutation);
        ExpectError(DocumentError.InvalidFormat, () => MonetaFile.Open(FileName));
    }

    [TestMethod]
    public void PagingUsesDateCreationCanonicalUuidAndExactProgressiveBalances()
    {
        var metadata = Document();
        var rows = Enumerable.Range(0, 253).Select(index => Operation(metadata, index % 7 / 100m,
            index % 2 == 0 ? TransactionDirection.Credit : TransactionDirection.Debit) with
        {
            Date = new DateOnly(2026, 3, 1).AddDays(index / 19),
            CreatedUtc = Created.AddTicks(index % 3), ModifiedUtc = Created.AddTicks(index % 3),
            Id = Guid.Parse(index.ToString("x8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000001")
        }).Reverse().ToArray();
        var ordered = rows.OrderBy(value => value.Date).ThenBy(value => value.CreatedUtc).ThenBy(value => value.Id.ToString("D"), StringComparer.Ordinal).ToArray();
        using var file = MonetaFile.Create(FileName, metadata, rows.Select(value => new TransactionChange(value, 0)).ToArray());
        var first = file.ReadPage(metadata.Accounts[0], 0, 100, Array.Empty<TransactionChange>());
        var second = file.ReadPage(metadata.Accounts[0], 100, 100, Array.Empty<TransactionChange>());
        var third = file.ReadPage(metadata.Accounts[0], 200, 100, Array.Empty<TransactionChange>());
        Assert.AreEqual(253L, first.TotalCount);
        CollectionAssert.AreEqual(ordered.Take(100).ToArray(), first.Transactions.ToArray());
        CollectionAssert.AreEqual(ordered.Skip(100).Take(100).ToArray(), second.Transactions.ToArray());
        CollectionAssert.AreEqual(ordered.Skip(200).ToArray(), third.Transactions.ToArray());
        Assert.AreEqual(100m + ordered.Take(100).Sum(Signed), second.BalanceBeforePage);
        Assert.AreEqual(100m + ordered.Sum(Signed), first.AccountBalance);
        Assert.AreEqual(first.AccountBalance, third.AccountBalance);
        var anchored = file.ReadPage(metadata.Accounts[0], 0, 100, Array.Empty<TransactionChange>(), ordered[212].Id);
        Assert.AreEqual(200, anchored.Offset);
        Assert.IsTrue(anchored.Transactions.Any(value => value.Id == ordered[212].Id));
        Assert.AreEqual(200, file.ReadPage(metadata.Accounts[0], 900, 100, Array.Empty<TransactionChange>()).Offset);
    }

    [TestMethod]
    public void PagingOverlayMovesEditsRemovesDeletesAndAnchorsNewRows()
    {
        var metadata = Document();
        var rows = Enumerable.Range(0, 205).Select(index => Operation(metadata, 1m, TransactionDirection.Credit) with { Date = new DateOnly(2026, 1, 1).AddDays(index) }).ToArray();
        using var file = MonetaFile.Create(FileName, metadata, rows.Select(value => new TransactionChange(value, 0)).ToArray());
        var moved = rows[0] with { Date = new DateOnly(2027, 1, 1), Revision = 2, ModifiedUtc = Created.AddMinutes(1), Amount = 9m };
        var deleted = rows[1] with { IsDeleted = true, Revision = 2, ModifiedUtc = Created.AddMinutes(1) };
        var added = Operation(metadata, 2m, TransactionDirection.Debit) with { Date = new DateOnly(2027, 2, 1) };
        var pending = new[] { new TransactionChange(moved, 1), new TransactionChange(deleted, 1), new TransactionChange(added, 0) };
        var page = file.ReadPage(metadata.Accounts[0], 0, 100, pending, added.Id);
        Assert.AreEqual(200, page.Offset);
        Assert.AreEqual(205L, page.TotalCount);
        Assert.AreEqual(310m, page.AccountBalance);
        Assert.AreEqual(added, page.Transactions[^1]);
        Assert.AreEqual(moved, page.Transactions[^2]);
        Assert.IsFalse(page.Transactions.Any(value => value.Id == deleted.Id));
        Assert.AreEqual(305m, MonetaFile.Inspect(FileName).AccountBalances![metadata.Accounts[0].Id]);
    }

    [TestMethod]
    public void UnsavedAccountPagingAndDocumentIsolationNeverReadOtherAccounts()
    {
        var metadata = Document();
        var value = Operation(metadata);
        using var first = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(value, 0) });
        using var second = MonetaFile.Create(Path.Combine(directory, "other.moneta"), metadata);
        Assert.AreEqual(0L, second.ReadPage(metadata.Accounts[0], 0, 100, Array.Empty<TransactionChange>()).TotalCount);
        Assert.IsNull(second.GetTransaction(value.Id));
        var fresh = metadata.Accounts[0] with { Id = Guid.NewGuid(), OpeningBalance = 7m };
        var added = value with { Id = Guid.NewGuid(), AccountId = fresh.Id, Amount = 2m };
        var page = first.ReadPage(fresh, 0, 100, new[] { new TransactionChange(added, 0) });
        Assert.AreEqual(1L, page.TotalCount);
        Assert.AreEqual(5m, page.AccountBalance);
        Assert.AreEqual(7m, page.BalanceBeforePage);
        Assert.AreEqual(0L, first.ReadPage(metadata.Accounts[1], 0, 100, Array.Empty<TransactionChange>()).TotalCount);
    }

    [TestMethod]
    public void BalancesExceedInt64AggregateWithoutSqlSumOverflowAndRetainScale28()
    {
        var metadata = Document();
        var precise = new Currency("XTS", "Précision synthétique", 28);
        var fineAccount = new Account(Guid.NewGuid(), "Précision synthétique", precise, 0.0000000000000000000000000001m)
        {
            CreatedUtc = metadata.Accounts[0].CreatedUtc,
            ModifiedUtc = metadata.Accounts[0].ModifiedUtc
        };
        metadata = metadata with { Currencies = metadata.Currencies.Append(precise).ToArray(), Accounts = metadata.Accounts.Append(fineAccount).ToArray() };
        var maximum = Operation(metadata, 92233720368547758.07m, TransactionDirection.Credit);
        var second = maximum with { Id = Guid.NewGuid() };
        var fine = Operation(metadata, 0.0000000000000000000000000001m, TransactionDirection.Credit) with { AccountId = fineAccount.Id, Currency = precise };
        using var file = MonetaFile.Create(FileName, metadata, new[] { new TransactionChange(maximum, 0), new TransactionChange(second, 0), new TransactionChange(fine, 0) });
        Assert.AreEqual(184467440737095616.14m, file.Snapshot.AccountBalances![maximum.AccountId]);
        Assert.AreEqual(0.0000000000000000000000000002m, file.Snapshot.AccountBalances![fineAccount.Id]);
        Assert.AreEqual(184467440737095616.14m, file.ReadPage(metadata.Accounts[0], 0, 1, Array.Empty<TransactionChange>()).AccountBalance);
        Assert.AreEqual(fine, file.GetTransaction(fine.Id));
    }

    [TestMethod]
    public void SnapshotFreezesCallerOwnedMetadataAndGeneratedBalances()
    {
        var metadata = Document();
        var categories = metadata.Categories.ToArray(); var payees = metadata.Payees.ToArray(); var methods = metadata.PaymentMethods.ToArray();
        metadata = metadata with { Categories = categories, Payees = payees, PaymentMethods = methods };
        using var file = MonetaFile.Create(FileName, metadata);
        categories[0] = categories[0] with { Name = "Mutation non enregistrée" };
        payees[0] = payees[0] with { Name = "Mutation non enregistrée" };
        methods[0] = methods[0] with { Name = "Mutation non enregistrée" };
        Assert.AreNotEqual(categories[0].Name, file.Snapshot.Categories[0].Name);
        Assert.AreNotEqual(payees[0].Name, file.Snapshot.Payees[0].Name);
        Assert.AreNotEqual(methods[0].Name, file.Snapshot.PaymentMethods[0].Name);
        Assert.ThrowsException<NotSupportedException>(() => ((IDictionary<Guid, decimal>)file.Snapshot.AccountBalances!).Add(Guid.NewGuid(), 1m));
    }

    [TestMethod]
    public void WriterLockRetainsBaselineAndPatchCanBeRetried()
    {
        var metadata = Document();
        var value = Operation(metadata);
        using var file = MonetaFile.Create(FileName, metadata);
        var baseline = file.Snapshot;
        var changes = new[] { new TransactionChange(value, 0) };
        using (var raw = Raw(FileName))
        using (var transaction = raw.BeginTransaction())
        {
            using var command = raw.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE Document SET name='Verrou synthétique';"; command.ExecuteNonQuery();
            ExpectError(DocumentError.Locked, () => file.Save(baseline, changes));
            Assert.AreSame(baseline, file.Snapshot);
            Assert.IsNull(file.GetTransaction(value.Id));
            transaction.Rollback();
        }
        file.Save(baseline, changes);
        Assert.AreEqual(value, file.GetTransaction(value.Id));
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void ActualLegacySchemasMigrateAdditivelyWithoutChangingSourceOrAccountValues(int version)
    {
        var metadata = Document() with { Categories = Array.Empty<LedgerCategory>(), Payees = Array.Empty<LedgerPayee>(), PaymentMethods = Array.Empty<PaymentMethod>() };
        BackupRestoreTests.BuildLegacy(FileName, metadata, version);
        var bytes = File.ReadAllBytes(FileName);
        ExpectError(DocumentError.MigrationRequired, () => MonetaFile.Open(FileName));
        using (var migrated = MonetaBackup.Migrate(FileName, Path.Combine(directory, "migrated.moneta")))
        {
            Assert.AreEqual(5, migrated.Snapshot.SchemaVersion);
            Assert.AreEqual(metadata.Id, migrated.Snapshot.Id);
            Assert.AreEqual(metadata.Name, migrated.Snapshot.Name);
            Assert.AreEqual(metadata.CreatedUtc, migrated.Snapshot.CreatedUtc);
            Assert.AreEqual(metadata.ModifiedUtc, migrated.Snapshot.ModifiedUtc);
            CollectionAssert.AreEqual(metadata.Accounts.ToArray(), migrated.Snapshot.Accounts.ToArray());
            CollectionAssert.AreEqual(metadata.Currencies.ToArray(), migrated.Snapshot.Currencies.ToArray());
            Assert.AreEqual(0L, migrated.Snapshot.OperationCount);
            Assert.IsNotNull(migrated.Snapshot.LedgerDigest);
            Assert.AreEqual(metadata.Accounts[0].OpeningBalance, migrated.Snapshot.AccountBalances![metadata.Accounts[0].Id]);
            var value = Operation(metadata) with { PayeeId = null, CategoryId = null, SubcategoryId = null, PaymentMethodId = null, Payee = new Payee(""), Category = new Category(""), PaymentMode = null };
            migrated.Save(migrated.Snapshot, new[] { new TransactionChange(value, 0) });
            Assert.AreEqual(value, migrated.GetTransaction(value.Id));
        }
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(FileName));
    }

    private static decimal Signed(Transaction value) => value.Direction == TransactionDirection.Credit ? value.Amount : -value.Amount;

    private static FinancialDocument Document()
    {
        var euro = new Currency("EUR", "Euro synthétique", 2);
        var yen = new Currency("JPY", "Yen synthétique", 0);
        var account = new Account(Guid.NewGuid(), "Compte synthétique", euro, 100m) { CreatedUtc = Created, ModifiedUtc = Created };
        var second = account with { Id = Guid.NewGuid(), Name = "Autre compte synthétique", OpeningBalance = 0m };
        var root = new LedgerCategory(Guid.NewGuid(), "Vie quotidienne");
        return new(Guid.NewGuid(), "Registre synthétique", Created, Created, new[] { euro, yen }, new[] { account, second })
        {
            SelectedAccountId = account.Id,
            Categories = new[] { root, new LedgerCategory(Guid.NewGuid(), "Courses", root.Id) },
            Payees = new[] { new LedgerPayee(Guid.NewGuid(), "Épicerie") },
            PaymentMethods = new[] { new PaymentMethod(Guid.NewGuid(), "Carte") }
        };
    }

    private static Transaction Operation(FinancialDocument metadata, decimal amount = 12.34m, TransactionDirection direction = TransactionDirection.Debit)
    {
        var account = metadata.Accounts[0];
        return new(Guid.NewGuid(), account.Id, new DateOnly(2026, 2, 3), "Achat synthétique", new Payee(metadata.Payees.FirstOrDefault()?.Name ?? ""),
            new Category(metadata.Categories.FirstOrDefault()?.Name ?? "") { Subcategory = metadata.Categories.Skip(1).FirstOrDefault()?.Name },
            amount, account.Currency, direction, ReconciliationStatus.Unmarked, "Notes synthétiques")
        {
            PayeeId = metadata.Payees.FirstOrDefault()?.Id, CategoryId = metadata.Categories.FirstOrDefault()?.Id,
            SubcategoryId = metadata.Categories.Skip(1).FirstOrDefault()?.Id, PaymentMethodId = metadata.PaymentMethods.FirstOrDefault()?.Id,
            PaymentMode = metadata.PaymentMethods.FirstOrDefault()?.Name, CreatedUtc = Created, ModifiedUtc = Created
        };
    }

    private static SqliteConnection Raw(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 3 }.ToString());
        connection.Open(); return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private static void ExpectError(DocumentError error, Action action) => Assert.AreEqual(error, Assert.ThrowsException<DocumentException>(action).Error);
}
