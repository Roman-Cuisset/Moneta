using System.Text.Json;
using Moneta.BankPerfect;
using Moneta.Core;
using Moneta.Infrastructure.Storage;

namespace Moneta.Infrastructure.BankPerfect;

internal static class BankPerfectAudit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static byte[] Encode(BankPerfectTarget plan, V5Document source, BankPerfectImportReport report)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            format = "BankPerfect V5",
            documentId = plan.Document.Id,
            sourceSha256 = report.SourceSha256,
            sourceByteLength = report.Reader.ByteLength,
            lineRecovery = report.Reader.Recovery.ToString(),
            source.Header,
            report.Issues,
            plan.Objects,
            rules = new Dictionary<string, string>
            {
                ["document"] = "Nom de groupe source ou libellé technique, nouveau UUID et horodatages d’import.",
                ["account"] = "Nom et devise natifs, ordre et compte par défaut, ouverture technique nulle. Coordonnées et taux historiques en provenance.",
                ["operation"] = "Date, détail, tiers, mode, catégorie, état et montant exact. Signe séparé de la magnitude. Libellé issu du tiers, du détail ou du mode. Date supplémentaire et Extra en provenance.",
                ["opening"] = "La ligne qualifiée est conservée une fois et repérée par opening. Elle garde son état de pointage hérité.",
                ["category"] = "Nom et parent physiques. Identifiant explicite ou position historique. Aucune fusion de catégories.",
                ["payee-template"] = "Le tiers a une référence Moneta. Les autres champs du modèle restent en provenance.",
                ["payment-method"] = "Mode rattaché au catalogue. Définition et code bruts conservés.",
                ["transfer"] = "UUID du virement et deux UUID de lignes. Références sœurs et champs bruts conservés."
            }
        }, Json);
        if (payload.Length > MonetaFile.MaxBankPerfectProvenanceBytes)
            throw new BankPerfectPlan.PlanException("provenance-size", "La provenance complète dépasse la taille prise en charge.", null);
        return payload;
    }
}
