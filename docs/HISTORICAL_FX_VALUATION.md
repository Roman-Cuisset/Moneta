# Valorisation historique multidevise

Ce document conserve l’étude de mission 10D. Le contrat livré en mission 15 exige une réponse qui atteste la date demandée, arrondit chaque flux avant agrégation et exporte les preuves de calcul hors du dossier financier. [Contrat et qualification du moteur](HISTORICAL_FX_ENGINE.md)

La mission 16 applique la même règle aux budgets et rapports. Les versions de rapport figées conservent leurs preuves à l’intérieur du dossier financier. Les estimations de l’échéancier utilisent une date d’ancrage affichée. [Budgets et rapports](BUDGETS_AND_REPORTS.md)

Dans un rapport, chaque dépense passée utilise le cours applicable à sa date métier. Cette règle vaut pour chaque devise prise en charge, y compris celles ajoutées ultérieurement. La date de consultation est une métadonnée du rapport.

## Calculs par nature de montant

| Objet | Date de valorisation | Traitement |
|---|---|---|
| Flux réalisé | Date métier de chaque mouvement, avec sa provenance | Convertir chaque débit ou crédit dans la devise du rapport avec le cours applicable à cette date. Une consultation ultérieure reproduit le même choix de cours et de politique |
| Stock | Date d’évaluation demandée | Calculer d’abord le solde natif à la date d’arrêté, puis valoriser ce solde à cette date |
| Budget prévisionnel | Date ou scénario déclaré | Si aucun cours futur n’est publié, la valorisation est indisponible par défaut. Un futur scénario assumé devra porter son taux, sa source et son statut de projection |
| Virement exécuté | Date de l’opération pour un affichage indicatif éventuel | Les montants natifs des deux jambes et les frais fondent la comptabilité. Le taux implicite du mouvement provient de ses deux montants. La valorisation d’un rapport reste externe au virement |

Une dépense BYN du 12 janvier, une du 18 et une du 25 prennent leurs cours historiques respectifs dans un rapport EUR consulté en mars. Trois opérations du même jour peuvent partager un cours. La date de consultation intervient dans les métadonnées du rapport.

## Sélection et traçabilité proposées

- Fixer le fournisseur, la devise de rapport, la règle de valorisation et la fenêtre maximale de repli dans une politique versionnée. Appliquer cette politique à toutes les paires prises en charge. La devise de référence implicite vaut 1 par construction du lot. Toute autre devise doit être attestée dans le même lot et à la même date d’effet.
- Pour un jour sans cotation, sélectionner la dernière date d’effet admissible au plus tard à la date demandée, dans une fenêtre documentée. Afficher date d’effet, écart et motif du repli. Tester week-ends, jours fériés, dates d’introduction ou de retrait d’une devise et cours historiques révisés. Le contrôle retient les codes et périodes historiques, avec les distinctions BYR/BYN et RUR/RUB.
- Produire un état explicite « cours indisponible » pour une date hors fenêtre, une paire absente, un lot incohérent ou un fournisseur inaccessible sans archive utilisable. Un total partiel doit donner son périmètre et le nombre de mouvements non valorisés. Un rapport éligible à la reproduction garde sa propre référence de données. Le choix d’un autre fournisseur exige un acte explicite.
- Conserver, pour chaque valorisation, date demandée et effective, fournisseur, devise de base, identifiants et échelles des cotations, valeurs et nominaux exacts, instant de récupération et empreinte du lot. Conserver aussi le montant natif signé, l’identifiant et la révision de la ligne, ainsi que la version des règles d’arrondi. Figer les lots utilisés dans une archive ou un instantané durable du rapport pour garantir le rejeu même après une correction du fournisseur ou une purge de cache.

## Arithmétique

Dans un lot unique, si chaque cotation exprime une valeur en devise de référence pour un nominal donné, la conversion de A en B est `montant × valeur(A) × nominal(B) / (nominal(A) × valeur(B))`. Garder le rapport exact jusqu’à l’étape d’arrondi choisie. Borner les coefficients et réduire les fractions avant addition. Pour un total financier de flux, la proposition est de sommer les conversions exactes puis d’arrondir une fois à l’échelle du rapport avec `ToEven`. Si les lignes affichées sont arrondies individuellement, expliquer et afficher l’écart de présentation entre leur somme et le total.

Éprouver le signe, le zéro, une base implicite, un nominal différent de 1, une conversion croisée, un cas d’arrondi à mi-unité, une paire absente, deux fournisseurs pour la même date et la restitution d’un rapport après modification du cache. Vérifier les frontières de période et une union de flux qui traversent plusieurs dates d’effet. Exprimer les limites de volume de façon vérifiable.

## État Moneta à l’issue de 10D

Les lots officiels, le cache daté, les derniers cours admissibles et le calcul dans un lot existent. L’agrégation de flux à plusieurs dates, les budgets, les rapports statistiques et leur archivage reproductible sont des travaux futurs. [Architecture actuelle des cours](ARCHITECTURE.md)

L’import BankPerfect conserve la date d’opération exacte et la devise native qualifiée. Pour une future valorisation de flux, ces champs apportent une date et une monnaie. Le pointage et les dates supplémentaires restent consultables séparément dans la provenance. Une valeur secondaire de BankPerfect ne suffit pas à certifier un cours officiel ni son effet. Les ouvertures identifiées et les jambes internes devront être distinguées des dépenses ou recettes du périmètre d’un rapport. [Correspondance BankPerfect](BANKPERFECT_MAPPING.md)
