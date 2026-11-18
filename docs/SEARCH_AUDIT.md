# Audit de la recherche — mission 12

## Périmètre

L’audit est parti de `ec22812`, synchronisé avec `origin/main`, sur un arbre propre. Revue des critères et de la précision dans `LedgerSearch.cs`, de la requête dans `MonetaFile.Search.cs`, de la localisation dans `MainViewModel.Search.cs`, du cycle asynchrone dans `MainWindow.Search.cs` et `LedgerSearchWindow.xaml.cs`, de l’interface `LedgerSearchWindow.xaml`, ainsi que des tests et du guide de mission 12. Les dépendances vérifiées comprennent le cycle d’enregistrement et de remplacement des dossiers, le registre ancré par UUID, le pointage et les liens de virement.

## Défauts confirmés et corrigés

| Reproduction avant correction | Correction | Vérification de régression |
| --- | --- | --- |
| Un verrou exclusif Windows sur le fichier fait lever une `SqliteException` à l’ouverture, avant le traitement habituel des erreurs de recherche. | L’ouverture en lecture seule traduit cette erreur en `DocumentException`, présentée dans la fenêtre. | Test automatisé rouge avant correction, vert ensuite. |
| Une date calendaire incohérente présente dans une ligne relue déclenche une `FormatException`. | La lecture signale une opération corrompue via `DocumentException`. | Test automatisé sur fichier fictif, avec une corruption injectée uniquement par le test. |
| En laissant les filtres ouverts, changer de compte dans le registre puis lancer une recherche ciblée utilise l’ancien UUID. | Une nouvelle recherche explicite prend le compte sélectionné au moment de l’application. La restitution d’une recherche existante garde ses critères. | Deux comptes EUR homonymes et résultat présent uniquement dans le second. |
| Une demande de page 3 suivie d’un enregistrement avant l’affichage ramenait les résultats en page 1. | Le rafraîchissement réutilise l’index demandé, même pendant le travail en arrière-plan. | Page 3, enregistrement avant publication, puis maintien en page 3. |
| Deux enregistrements pendant le rafraîchissement faisaient perdre une sélection encore présente dans la page. | L’UUID sélectionné reste disponible jusqu’à la publication de la page suivante. Il est restauré si la ligne est présente. | Sélection, deux enregistrements pendant la recherche, puis contrôle du même UUID. |

Les deux tests de lecture sont dans `LedgerSearchTests.cs`. Les trois reproductions de concurrence d’interface ont été exécutées avec les fenêtres Release de Moneta dans une sonde locale sur fichiers fictifs.

## Garanties vérifiées

- Les clauses SQL proviennent du code. Les comptes, devises, dates, états et limites passent par des paramètres SQLite. Les chaînes sont transmises comme arguments ou capturées dans les fonctions de comparaison .NET. Les apostrophes et les signes `%`, `_`, `|` et `/` restent littéraux.
- Les unités mineures signées restent des entiers jusqu’à leur conversion exacte en `decimal` selon l’échelle native. Les tests couvrent le zéro dans les deux sens, les montants négatifs, `Int64.MaxValue` en unités mineures, une devise à 28 décimales et des montants numériques égaux exprimés en devises différentes.
- La normalisation NFC et la comparaison ordinale .NET insensible à la casse ont été vérifiées sur le latin et le cyrillique, les formes accentuées composées et décomposées, les espaces Unicode, une chaîne de 240 caractères et le refus de 241. Les accents restent significatifs.
- Les critères s’appliquent avant la sélection de page. Le comptage et la page partagent un instantané de lecture. Les sept tris dans les deux sens ont été comparés à un ordre de référence calculé hors SQLite sur les mêmes écritures. La vérification passe par toutes les pages du dossier de 1 000 écritures et par des pages autour de 10 et de 100 ainsi que les dernières pages des gros dossiers. Elle couvre les égalités de tri et une dernière page incomplète.
- Le mode multicompte distingue les UUID de comptes homonymes, dont deux en EUR. Les trois mouvements du virement gardent un UUID de virement commun et la jambe de frais reste distincte. Le compte archivé explicitement sélectionné est consultable. Son inclusion dans les résultats multicompte exige l’option d’archives. Les états P et R et les suppressions logiques suivent le contrat enregistré.
- Le chemin normal de recherche utilise une connexion SQLite distincte ouverte en lecture seule. La navigation seule laisse `IsDirty` à faux. Après fermeture des dossiers fictifs, SHA-256, condensat du registre, comptes, soldes natifs et nombres de rapprochements et de virements sont identiques. Les journaux auxiliaires SQLite ne servent pas de preuve de mutation du contenu financier.

## Vérification sous Windows

Les builds Debug et Release passent sans erreur ni avertissement. Chaque configuration réussit 631 tests, dont les 629 existants et les deux nouveaux tests de régression.

Le jeu WPF Release utilise quatre comptes fictifs, EUR, EUR, BYN et RUB, avec homonymes, archive, pointage, relevé rapproché et virement comportant des frais. La navigation utilise les commandes WPF vers le registre original puis le retour aux résultats. Elle retrouve aussi une écriture révisée en brouillon par son UUID et refuse celle dont la suppression est en brouillon. La saisie et le changement de compte sont désactivés pendant la localisation d’une écriture.

Les vérifications portent sur le focus de la recherche rapide, la sélection du texte, les noms UI Automation, les raccourcis F7 et Maj+F7 injectés dans le circuit clavier WPF, le passage à la page suivante et précédente, le tri, les archives, le retour et la réouverture. Un verrou exclusif a affiché une erreur de lecture. Une nouvelle recherche a réussi après libération du fichier, sans exception non gérée. Les captures générées sur le poste restent locales. La qualification est limitée au poste Windows disponible, à l’automatisation et à l’inspection des captures dans les deux thèmes.

## Mesures indépendantes en Release

Trois dossiers fictifs ont été générés pour l’audit, avec quatre comptes EUR, EUR, BYN et RUB. Les mesures utilisent le SSD du poste Windows 11, un Core i5-1235U et 32 Gio de mémoire. Chaque cellule est une médiane de sept exécutions après une exécution de chauffe. La recherche ciblée combine un texte latin et cyrillique, un montant exact et EUR. La recherche large applique un mot fréquent et charge la neuvième page triée par libellé.

Le temps total inclut la connexion et la configuration. La mesure de page regroupe le tri SQLite, la sélection de 100 lignes et leur matérialisation. Le CPU est le cumul des threads du processus et sa résolution est limitée sur les petites mesures. Les allocations rapportées sont celles du thread de requête. La mémoire résidente inclut la sonde, WPF et les jeux générés. Les allocations et la mémoire résidente mesurent deux phénomènes différents.

| Écritures | Scénario | Total ms | Comptage ms | Page et tri ms | CPU ms | Allocations Mio | Résident Mio |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 000 | Ciblée | 5,9 | 0,9 | 1,3 | 15,6 | 0,41 | 181,7 |
| 1 000 | Large | 14,7 | 2,5 | 7,0 | 15,6 | 1,55 | 181,9 |
| 10 000 | Ciblée | 28,3 | 10,3 | 12,1 | 31,2 | 3,87 | 178,2 |
| 10 000 | Large | 87,4 | 28,0 | 55,6 | 93,8 | 10,52 | 178,5 |
| 50 000 | Ciblée | 115,8 | 41,2 | 65,7 | 109,4 | 19,25 | 178,2 |
| 50 000 | Large | 369,4 | 146,9 | 230,9 | 375,0 | 43,27 | 179,5 |

SQLite 3.53.4 embarqué confirme sur une requête simplifiée l’usage de `Transactions_AccountOrder` avec compte et intervalle de dates, puis une structure temporaire pour départager le dernier terme du tri. Les essais larges incluent le coût des sous-chaînes Unicode et du tri. La distribution des textes et les volumes par devise diffèrent de ceux de la mission 12, ce qui limite la comparaison directe des durées. Le moteur conserve les index et le schéma 6.

À 50 000 écritures, la deuxième demande d’un essai WPF est apparue en 363,9 ms, mesurés depuis le clic automatisé jusqu’à la présence du résultat dans la vue. Le recalcul de disposition demandé après publication était sous la précision d’affichage de 0,1 ms. Le minuteur de priorité saisie a continué à s’exécuter, avec 47 déclenchements et un écart maximal de 31,1 ms sur ce parcours. Une annulation SQLite pendant la recherche large a été suivie d’une nouvelle recherche réussie.

## Points non reproduits et limites

Les essais de recherche successive, fermeture de la fenêtre en cours de recherche et remplacement de dossier n’ont pas fait réapparaître une ancienne réponse. Le code vérifie l’identité de la demande à la publication, après l’annulation de la demande remplacée. Le test ne force pas toutes les permutations d’ordonnancement possibles. Aucun incident d’écriture lors de la consultation n’a été constaté.

La recherche porte sur les opérations enregistrées. Les opérations supprimées logiquement restent hors des résultats. Les critères actifs et la page sont en mémoire. La référence consultable est celle du virement. Le changement de page relit l’état enregistré et les résultats peuvent se déplacer si un enregistrement modifie les données entre deux pages. Les règles de tri et les égalités sont stables sur un même instantané.

Une validation au lecteur d’écran, sur plusieurs écrans et avec des stockages différents reste hors de la qualification de ce poste. La mesure de consommation du processus ne correspond pas à une empreinte mémoire de SQLite seul.

## Verdict

La mission 12 est qualifiée sur le périmètre testé. Les cinq défauts reproduits sont corrigés, les suites complètes passent et la consultation conserve les données financières. L’audit est favorable au lancement de la mission 13, sous réserve des limites de qualification Windows indiquées ci-dessus. La mission 13 reste une intervention distincte.
