# Compatibilité BankPerfect — qualification missions 10A à 10D

## Extension qualifiée en mission 10D

| Cas testé | Décision | Étendue de la preuve |
|---|---|---|
| BP-F04 native | Conversion | Ouverture négative R, même montant qu’un débit NP daté antérieurement, chaque ligne reprise une fois et solde exact de −500 EUR |
| BP-F05 native | Conversion avec avertissement historique | P sur la recette du 15/01/2024, date du 31/12/2023 conservée en provenance, solde 1 087,67 EUR |
| BP-F06 native | Conversion | Trois virements distincts du même montant, dont deux à la même date, leurs références réciproques et leurs UUID cibles |
| BP-F07 native | Conversion | 37 catégories, trois homonymes dans des emplacements distincts, accents et parents conservés |
| Variantes synthétiques | Conversion ou blocage selon les invariants | Date comptable future, bissextile, états P/R distincts, protection d’une paire par une jambe R, références ambiguës ou cassées, mutation du candidat avant publication |
| BP-F03 native | Refus confirmé | Fraction de 0,0001 EUR, cible absente |

Builds Debug et Release réussis, sans avertissement ni erreur. **594 tests réussis** par configuration, dont neuf cas supplémentaires. Les 78 cas ciblés BankPerfect ont réussi. Sous Windows, les aperçus de F05 et de F03 ont été vérifiés en clair et en sombre dans la vraie fenêtre WPF, sur un poste et une échelle. La création et la vérification de chaque cible native ont été exercées par les API de conversion utilisées par WPF.

Le champ de date supplémentaire reste réservé à la provenance. Le résultat F05 atteste une saisie manuelle du pointage sur ce build. Les autres formats historiques, les comptes non EUR, les dates de valeur, les ouvertures de signature inconnue et les virements non attestés restent hors du périmètre qualifié. [Production native, empreintes et variantes restantes](BANKPERFECT_FIXTURES.md#qualification-native--mission-10d)

## Périmètre de conversion 10C

| Corpus BP8 8.0.0.373 | Décision | Contrôles observés |
|---|---|---|
| BP-F00 | Conversion du registre minimal | Une ligne R héritée à zéro, UID de ligne et de compte absents en provenance |
| BP-F01 | Conversion du registre EUR | Trois lignes, solde 1 087,67 EUR, recette et dépense exactes et textes accentués |
| BP-F02 | Conversion multicomptes EUR | Quatre lignes, soldes 850,00 et 350,00 EUR, lien réciproque UID 3 et 4 et extension conservée |
| BP-F03 | Blocage de la conversion | Diagnostic V5 exploitable, aucun dossier cible pour le montant à 0,0001 EUR |

Refus probants : V6, fichiers tronqués détectables, comptes ou monnaies hors périmètre, date obligatoire invalide, UID et catégories ambigus, jambes cassées, ouverture indéterminée, protection ou extensions et champs inconnus de portée financière. Le V5 étant dépourvu de contrôle d’intégrité, une altération qui produit une structure valide et cohérente peut passer la détection. [Politique et vérification](BANKPERFECT_IMPORT_STRATEGY.md#état-courant--mission-10c)

Les trois dossiers convertis ont été rouverts via `MonetaFile.Open`. La sauvegarde et la restauration de contrôle ont vérifié leurs lignes, référentiels, virements et provenance. Les quatre SHA-256 d’origine concordent avant et après l’épreuve native. Les builds Debug et Release ont réussi sans avertissement, avec **585 tests réussis chacun** dont 24 nouveaux cas, sans fichiers BankPerfect suivis dans Git.

Parcours Windows observé sur le registre et la fenêtre d’import en rendu logiciel : commande Fichier → Importer → BankPerfect, BP-F01 converti en clair, instance fermée puis relancée et dossier rouvert en sombre, BP-F02 converti en sombre et navigation entre les deux jambes, refus de BP-F03 sans bouton de création. Les deux conversions WPF ont exécuté la sauvegarde et la restauration de contrôle avant affichage du résultat. Il s’agit d’un parcours sur les deux thèmes, avec un seul poste et une seule échelle d’affichage.

Une seconde sauvegarde de BP-F02 après publication a été restaurée au moyen des API normales. L’ouverture de cette copie dans la build WPF finale a confirmé l’ordre des lignes à date égale, le passage entre les jambes et les soldes de 350,00 et 850,00 EUR.

## Contrat de lecture

Version du logiciel, version du conteneur et version du schéma SQLite Moneta sont **trois dimensions distinctes**. « Code présent » ne signifie ni lecture exacte, ni compatibilité BP8 validée, ni import disponible.

Référence amont : **BankPerfect 9.13.0**, commit **`990f5c7f712dab07c7fefc2921cbf60beee7f6b1`**, MIT. Les sources, lignes, épreuves et limites sont détaillées dans [BANKPERFECT_FORMAT_RESEARCH.md](BANKPERFECT_FORMAT_RESEARCH.md).

| Famille / version | Preuve actuelle | Capacité du lecteur étudié | Qualification locale / Moneta |
|---|---|---|---|
| V5, marqueur décodé `BP-FILE-5` | Code, 6 tests codec/texte et quatre fichiers natifs BP8 | Décodage CP1252, grammaire à 7/9 champs, montants, références et réparations identifiées | **Lecteur C# confronté à F00..F03** et aux entrées négatives synthétiques. Portée limitée à ces preuves |
| BankPerfect **8.0.0.373**, produit 8.0 | Métadonnées/hash identiques entre référence et EXE officiel, exécution isolée | Quatre fixtures créées/sauvées par BP8 et comparées au lecteur figé | **Compatibilité de lecture partielle démontrée**, différences/limites documentées ; aucun import vers Moneta |
| Autres builds BP5/BP6/BP7/BP8 | Mention/commentaires et inventaire Delphi 2019 | Chemin V5 susceptible de s'appliquer si format correspondant | Aucune de ces versions réellement testée ; ne pas annoncer « tous les BP8 » |
| `.bp0` / rotations `.bp01`… | Nommage historique documenté, tests `legacy_files` filtrant `.bp`/`.bp0` | Extension seule non suffisante ; contenu à détecter | Aucun exemplaire testé ; les rotations ne sont pas toutes énumérées par le filtre amont |
| Vieux UCL/NRV2E « BPXENCODE » | Inventaire public §11, pas `detect_format` V5/V6 | Aucun chemin de détection correspondant dans la façade étudiée | Non pris en charge par le périmètre de lecteur proposé pour 10B |
| Vieux Shuffle/ISAAC | Inventaire public §11 | Même limite | Non qualifié ; aucune tentative de récupération ou contournement |
| BP9 V6, `BPFILE6` + octet NUL | Code `format_v6`, `file::open_bytes` | Conteneur zstd/chiffrement, payload MessagePack distinct | **Signature reconnue et refusée par le chemin V5** |
| `.moneta` SQLite schéma 5 | Contrat Moneta existant, 516 tests par configuration | Ni V5 ni V6, pas de renommage/conversion implicite | Debug/Release conservés ; aucun changement de format financier en 10A |

## Matrice sémantique historique des missions 10A et 10B

Statuts de la **cible et des transformations possibles**, pas certification d'un import : compatible = représentation disponible sous conditions ; partiel = informations/règles manquantes ; absent = modèle/fonction non livré ; inconnu = preuve source insuffisante. **L'import est absent pour toutes les lignes.**

| Domaine | Source établie | Cible / statut | Porte de validation |
|---|---|---|---|
| Comptes et monnaies natives | Champs positionnels, symbole pas nécessairement ISO | **Partiel** : comptes/devises livrés, mapping et provenance absents | Symbole→ISO/échelle approuvé, aucun défaut EUR/BYN/RUB ni BYR→BYN implicite |
| Débits/crédits simples | Montant signé, quatre décimales dans le modèle amont | **Compatible sous conditions** : sens/magnitude exacts ; non représentable bloque | Précision, limites, zéro, conservation du brut et somme exacte |
| Ouverture | Pas de champ séparé dans `Account` ; ligne décrite dans Delphi | **Partiel** : `opening_minor` et transactions existent, classification non prouvée | Une seule prise en compte, états P/R compris |
| Dates d'opération | Jours entiers depuis 1899-12-30 dans le lecteur | **Compatible sous conditions** avec DateOnly | Bornes, bissextiles, pas de substitution aujourd'hui |
| Champ `mark_date` | F03 : NP/date opération → P/date du jour → date de pointage manuelle, sans mutation financière | **Partiel** : `MarkedOn` disponible pour P/R selon règle approuvée, provenance des autres valeurs absente | P datée prouvée pour ce build ; NP, champ absent et autres versions sans date inventée |
| Catégories/sous-catégories | IDs et indentation, parent précédent | **Partiel** : un niveau cible, ordre/provenance et collisions à traiter | Références exactes et catégories inutilisées conservées |
| Tiers de lignes, modes | Texte/codes ; modes reconstruits par le parseur | **Partiel** : catalogues simples présents | Aucun défaut Carte, aucune fusion silencieuse de noms |
| Modèles de tiers | `whoN` porte aussi montant/mode/catégorie/détail | **Absent** comme modèle complet | Ne pas les réduire à des noms de tiers |
| NP/P/R | Codes lus ; le lecteur accepte des valeurs inconnues comme NP | **Partiel** : états et R héritées disponibles | Aucun code inconnu normalisé, aucune session inventée |
| Relevés / historique bancaire | Un R seul ne porte pas une preuve de session | **Inconnu** côté source ; modèle formel cible livré | Sessions/membres/soldes réellement présents et attestés |
| Virements | Références Extra réciproques attendues | **Partiel** : modèle cible livré, résolution/import absents | Graphe exact, dates compatibles, devises, montants et R indépendants |
| Frais / change | Relation source explicite à prouver | **Inconnu** côté source ; cible disponible | Aucun frais déduit d'une différence ; montants natifs figés |
| Coordonnées bancaires, virtuel/désactivé | Champs source explicites | **Partiel/absent** : archive cible ≠ virtuel/désactivé, métadonnées bancaires incomplètes | Pas de simple entassement dans Notes présenté comme reprise structurée |
| Extra / provenance d'import | Champs bruts accessibles | **Absent** comme stockage générique complet | Nouveau contrat de provenance avant conversion financière |
| Ventilations, échéances, budgets, titres et annexes | Structures/scripts évoqués, couverture source variable | **Absent ou partiel** selon domaine, voir mapping détaillé | Présence détectée interdit le statut « complet » sans prise en charge |

## Bilan exact des épreuves

| Épreuve | Réalisé / résultat | Ce qu'elle ne prouve pas |
|---|---|---|
| Build Moneta Debug | 0 erreur, 0 avertissement | Import absent |
| Tests Moneta Debug | **516 réussis, 0 échoué, 0 ignoré** | Compatibilité native BP8 |
| Build Moneta Release | 0 erreur, 0 avertissement | Qualification d'un convertisseur |
| Tests Moneta Release | **516 réussis, 0 échoué, 0 ignoré** | Corpus fictif BP8 |
| Tests Rust codec V5 / texte V5 / modèle | **3 + 3 + 5 réussis**, 0 échec/ignoré, profil debug | Compatibilité de tous les fichiers historiques |
| Sonde négative de texte, hors Moneta | Replis date/0/NP, renumérotation et deux panics observés ; détail dans la recherche | Fichier réellement écrit par BP8, tests release de la sonde |
| Fixtures natives 10A F00/F01/F02/F03 | **Quatre produites et comparées**, données fictives ; mêmes hashes avant/après lecture | Couverture limitée, pas tout BP8 ; F00 contient un UID généré signalé ; F03 contient 0,0001 EUR non représentable en centimes |
| Parcours Moneta WPF spécifique à l'import | **Non exécuté : aucune interface d'import développée** | Les anciennes campagnes WPF restent historiques |

TRX locaux ignorés : `tests/Moneta.Tests/TestResults/mission10a-debug.trx` et `mission10a-release.trx`. Les 516 cas sont la suite existante conservée ; les 11 cas Rust ne sont pas ajoutés au compteur Moneta.

## Épreuves de lecture et épreuves restantes

La mission 10B exerce les signatures et versions, quotas, encodage, troncatures, champs invalides, dates non représentables, limites décimales, UID maximum et doublons, références cassées, réciprocité et conservation des champs inconnus. Les autres épreuves du tableau restent à qualifier sur leurs cas natifs ou au stade financier. [Cas et résultats 10B](BANKPERFECT_FIXTURES.md#qualification-c--mission-10b)

| Entrée / événement | Attendu du futur lecteur/importeur |
|---|---|
| Fichier vide, bruit, signature invalide, extension trompeuse | Refus borné et explicite, aucune destination financière publiée |
| Troncatures à plusieurs longueurs, bloc absent/supplémentaire, séparateur dans un champ | Diagnostic structurel, pas de disparition silencieuse de ligne/compte |
| Version inconnue, UCL/Shuffle/V6 hors périmètre | Refus distinct de « corruption », aucune tentative permissive ou récupération cachée |
| CP1252, caractères non ASCII, page ANSI différente/inconnue, fins LF/CRLF/CR | Politique explicite, brut préservé, aucun remplacement destructeur silencieux |
| Date invalide/extrême, nombre mal formé, précision > devise, bornes Currency/Int64 | Blocage avant mutation ; jamais aujourd'hui/zéro/arrondi de repli |
| Clé UTF-8 multioctet, UID max, tailles/profondeurs excessives | Erreur contrôlée, limites temps/mémoire, aucune panic ou allocation non bornée |
| UID absent/dupliqué, deux lignes identiques légitimes | Conserver cardinalité et ambiguïté ; identité d'import distincte de l'identité historique |
| Catégorie inconnue, compte manquant, lien asymétrique/cassé/partagé | Refus ou rapport bloquant ; aucun lien reconstruit par ressemblance |
| État inconnu, R sans session, champ de date ambigu | Brut conservé, R héritée non attestée, aucune session/date fictive |
| Payload de script/pickle, règle ou chemin externe | Données inertes ; aucune exécution ni ouverture de chemin/réseau fourni par le fichier |
| Échec/annulation/interruption de conversion, destination existante, import répété | Original inchangé, destination exclusive, pas de lot partiel publié ni doublons cachés |
| Sauvegarde/restauration du résultat futur | Comparaison exhaustive des lignes/référentiels/liens/provenance, pas seulement solde final |

**Décision de passage :** le diagnostic C# est qualifié sur quatre cas BP8 réels. La portée financière dépend encore des règles d'ouverture, de provenance, des domaines manquants et du traitement des fractions EUR.
