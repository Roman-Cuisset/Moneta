using Moneta.Core;

namespace Moneta.Infrastructure;

public static class DemoData
{
    public static DemoPortfolio Create()
    {
        var currencies = new CurrencyRegistry();
        var euro = currencies.Get("EUR");
        var belarusianRuble = currencies.Get("BYN");
        var russianRuble = currencies.Get("RUB");
        Account[] accounts =
        [
            new(new Guid("10000000-0000-0000-0000-000000000001"), "Courant EUR — démo", euro, 1650.25m),
            new(new Guid("10000000-0000-0000-0000-000000000002"), "Courant BYN — démo", belarusianRuble, 845.10m),
            new(new Guid("10000000-0000-0000-0000-000000000003"), "Courant RUB — démo", russianRuble, -1200.50m),
            new(new Guid("10000000-0000-0000-0000-000000000004"), "Épargne EUR — démo", euro, 5000.00m)
        ];
        Payee[] payees =
        [
            new("Atelier des nuages (fictif)"),
            new("Marché des lucioles (fictif)"),
            new("Café des étoiles (fictif)"),
            new("Tram des collines (fictif)"),
            new("Librairie des comètes (fictif)"),
            new("Maison des loisirs (fictif)")
        ];
        Category[] categories =
        [
            new("Revenus fictifs"),
            new("Alimentation") { Subcategory = "Courses hebdomadaires" },
            new("Restauration") { Subcategory = "Pause café" },
            new("Transport") { Subcategory = "Transports urbains" },
            new("Culture") { Subcategory = "Livres" },
            new("Loisirs")
        ];
        var transactions = new List<Transaction>(3212);
        var start = new DateOnly(2024, 8, 20);
        for (var index = 0; index < 3200; index++)
        {
            var isCredit = index % 12 == 0;
            var categoryIndex = isCredit ? 0 : 1 + index % 5;
            var amount = isCredit
                ? (index % 120 == 0 ? 1850.00m : 125.00m + index % 17)
                : 5.25m + (index * 137 % 6840) / 100m;
            transactions.Add(new Transaction(
                TransactionId(index + 1), accounts[0].Id, start.AddDays(index / 4),
                isCredit ? "Recette de démonstration" : $"Achat de démonstration · {categories[categoryIndex].Name}",
                payees[categoryIndex], categories[categoryIndex], amount, euro,
                isCredit ? TransactionDirection.Credit : TransactionDirection.Debit,
                (ReconciliationStatus)(index % 3),
                $"Opération fictive n° {index + 1}. Jeu de démonstration en mémoire ; aucune donnée bancaire réelle.")
            {
                PaymentMode = isCredit ? "Virement" : categoryIndex == 3 ? "Prélèvement" : "Carte"
            });
        }

        var otherDate = new DateOnly(2026, 10, 20);
        AddOtherLedger(accounts[1], [120.45m, 450.00m, 36.20m, 18.05m], "BYN");
        AddOtherLedger(accounts[2], [275.30m, 6200.00m, 1499.99m, 850.00m], "RUB");
        AddOtherLedger(accounts[3], [50.00m, 250.00m, 12.35m, 100.00m], "épargne EUR");
        return new DemoPortfolio(Array.AsReadOnly(accounts), transactions.AsReadOnly());

        void AddOtherLedger(Account account, decimal[] amounts, string description)
        {
            for (var index = 0; index < amounts.Length; index++)
            {
                var isCredit = index == 1 || (account.Id == accounts[3].Id && index == 2);
                var categoryIndex = isCredit ? 0 : index + 1;
                transactions.Add(new Transaction(
                    TransactionId(transactions.Count + 1), account.Id, otherDate.AddDays(index / 2),
                    $"Démonstration {description} · {(isCredit ? "crédit" : "débit")}",
                    payees[categoryIndex], categories[categoryIndex], amounts[index], account.Currency,
                    isCredit ? TransactionDirection.Credit : TransactionDirection.Debit,
                    (ReconciliationStatus)(index % 3),
                    $"Écriture entièrement fictive du compte {account.Name}. Aucun taux ni conversion appliqué.")
                {
                    PaymentMode = isCredit || account.Id == accounts[3].Id
                        ? "Virement"
                        : categoryIndex == 3 ? "Prélèvement" : "Carte"
                });
            }
        }
    }

    private static Guid TransactionId(int sequence) => new(sequence, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
}
