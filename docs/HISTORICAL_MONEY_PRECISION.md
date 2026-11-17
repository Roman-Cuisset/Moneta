# Précision des montants historiques

## Contrat actuel

BankPerfect V5 peut conserver `0,0001 EUR` et un solde de `1 087,6701 EUR`. Dans Moneta, EUR, BYN et RUB utilisent deux décimales. Le schéma 5 stocke les montants en unités mineures entières, avec une magnitude et un sens pour une opération. Le lecteur V5 conserve le texte, l’échelle source et la valeur exacte. `ExactMoney` contrôle la conversion en unités mineures avant la création du dossier. BP-F03 reste un cas de refus. [Format Moneta](FILE_FORMATS.md) · [Fixture BP-F03](BANKPERFECT_FIXTURES.md#bp-f03--pointage-hiérarchie-date-et-précision-limites)

SQLite manipule ses entiers comme des valeurs signées sur 64 bits. `REAL` est du flottant binaire. Une déclaration de précision décimale ne fournit pas le contrat d’exactitude d’un registre. Microsoft.Data.Sqlite convertit par défaut les `decimal` en texte pour préserver leur valeur. La somme, le tri et la validation exigent toujours un modèle d’arithmétique explicite. [Types SQLite](https://www.sqlite.org/datatype3.html) · [Types Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types)

## Options

| Option | Solde actif | Coût et risque | Compatibilité |
|---|---|---|---|
| A. Refus contrôlé | Exact pour les dossiers représentables à leur échelle actuelle | Faible. L’utilisateur conserve un aperçu du fichier avec le montant qui bloque | Format 5 et sauvegardes existantes directement utilisables |
| B. Montant comptable exact et échelle de saisie distincte | Exact pour chaque mouvement importé et chaque agrégat, y compris sous le centime | Élevé. Revoir le stockage, les requêtes du registre, la validation des virements, les relevés et les migrations | Nouveau format financier versionné, migration par copie et version de lecteur adaptée |
| C. Registre historique distinct | Exact seulement si chaque ligne historique affecte le solde actif et les opérations de vérification | Deux cheminements à synchroniser. Les transferts, corrections et relevés exigent des règles communes pour éviter des doublons ou des omissions | Une annexe documentaire suffit à l’audit. Un registre actif double exige son propre contrat versionné et des vues financières exactes |

Illustration arithmétique, à quatre décimales. Une recette de `0,0001 EUR` suivie d’une dépense de `0,01 EUR` produit `-0,0099 EUR`. Un solde arrondi ou une annexe qui omet la recette du calcul fausserait le contrôle des lignes suivantes.

## Recommandation

Conserver A en production et qualifier B sur une copie isolée. Pour B, étudier une échelle comptable définie par devise et figée pendant la vie du dossier. Pour une devise qualifiée à quatre décimales comme l’EUR BankPerfect, prendre au minimum l’échelle historique et garder, séparément, la précision de saisie ordinaire. Conserver également le texte et l’échelle d’origine dans la provenance. Cette séparation permet d’exprimer une opération historique puis de continuer à saisir des centimes.

L’augmentation d’échelle multiplie le coefficient d’une opération existante. `long.MaxValue` centimes multiplié par 100 dépasse un entier SQLite. Le minimum Currency historique vaut `-922337203685477,5808`, dont la magnitude à quatre décimales est `2^63`. Un prototype doit couvrir ces deux frontières. Comparer un coefficient signé élargi à largeur canonique, par exemple un `Int128` encodé en BLOB, et une paire coefficient/échelle versionnée. Le premier simplifie les additions au sein d’une devise. La seconde facilite l’évolution de l’échelle mais augmente les conversions, comparaisons et contrôles de normalisation. Un format texte canonique est une autre option de stockage, avec arithmétique applicative et contrôle explicite de l’ordre. Pour les sommes, prévoir des coefficients élargis vérifiés ou `BigInteger` borné et un diagnostic d’échec. Interdire les agrégats SQLite flottants sur des valeurs comptables.

## Conditions d’une future migration

1. Fixer les bornes par montant, solde, cumul et nombre de décimales. Définir le zéro, les signes, les zéros non significatifs et la représentation canonique des valeurs.
2. Versionner le nouveau stockage des lignes, ouvertures, états de rapprochement et instantanés d’audit. Migrer une copie certifiée. Comparer chaque identité, date, montant, lien, état et solde avant publication. Tester la lecture des certificats historiques du schéma 5 et le refus d’ouverture en écriture par un ancien lecteur.
3. Calculer les soldes courants, pointés et historiques depuis la même représentation exacte. Tester les deux jambes et les frais, les ratios multidevises, les rapprochements hérités et validés, le retour à l’état antérieur, les changements de période et les rapports sur de gros volumes.
4. Afficher le montant en entier dès que des chiffres sous l’unité de saisie sont significatifs. Distinguer le total comptable, une présentation arrondie et les unités de saisie. Spécifier les réexportations vers des formats à précision limitée.
5. Vérifier l’atomicité, la concurrence, la copie, le digest et la restauration avec contrôles de bornes et d’égalité exacts. Inclure BP-F03 et des montants positifs, négatifs et nuls, puis confronter le modèle à des fichiers Moneta préexistants proches des bornes.

La mission 10D documente ces choix. Le schéma financier, les échelles courantes et le refus des montants sous l’unité mineure restent ceux de Moneta 10C.
