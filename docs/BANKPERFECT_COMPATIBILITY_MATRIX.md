# Compatibilité BankPerfect — qualification mission 10A

## Contrat de lecture

Version du logiciel, version du conteneur et version du schéma SQLite Moneta sont **trois dimensions distinctes**. « Code présent » ne signifie ni lecture exacte, ni compatibilité BP8 validée, ni import disponible.

Référence amont : **BankPerfect 9.13.0**, commit **`990f5c7f712dab07c7fefc2921cbf60beee7f6b1`**, MIT. Les sources, lignes, épreuves et limites sont détaillées dans [BANKPERFECT_FORMAT_RESEARCH.md](BANKPERFECT_FORMAT_RESEARCH.md).

| Famille / version | Preuve actuelle | Capacité du lecteur étudié | Qualification locale / Moneta |
|---|---|---|---|
| V5, marqueur décodé `BP-FILE-5` | Code, 6 tests codec/texte et quatre fichiers natifs BP8 | Décodage CP1252, grammaire à 7/9 champs, montants, références et réparations identifiées | **Qualifié sur F00..F03**, pas tous les fichiers historiques ; lecteur Moneta absent |
| BankPerfect **8.0.0.373**, produit 8.0 | Métadonnées/hash identiques entre référence et EXE officiel, exécution isolée | Quatre fixtures créées/sauvées par BP8 et comparées au lecteur figé | **Compatibilité de lecture partielle démontrée**, différences/limites documentées ; aucun import vers Moneta |
| Autres builds BP5/BP6/BP7/BP8 | Mention/commentaires et inventaire Delphi 2019 | Chemin V5 susceptible de s'appliquer si format correspondant | Aucune de ces versions réellement testée ; ne pas annoncer « tous les BP8 » |
| `.bp0` / rotations `.bp01`… | Nommage historique documenté, tests `legacy_files` filtrant `.bp`/`.bp0` | Extension seule non suffisante ; contenu à détecter | Aucun exemplaire testé ; les rotations ne sont pas toutes énumérées par le filtre amont |
| Vieux UCL/NRV2E « BPXENCODE » | Inventaire public §11, pas `detect_format` V5/V6 | Aucun chemin de détection correspondant dans la façade étudiée | Non pris en charge par le périmètre de lecteur proposé pour 10B |
| Vieux Shuffle/ISAAC | Inventaire public §11 | Même limite | Non qualifié ; aucune tentative de récupération ou contournement |
| BP9 V6, `BPFILE6` + octet NUL | Code `format_v6`, `file::open_bytes` | Conteneur zstd/chiffrement, payload MessagePack distinct | **Hors import V5 proposé** ; aucune compatibilité descendante BP8 déduite |
| `.moneta` SQLite schéma 5 | Contrat Moneta existant, 516 tests par configuration | Ni V5 ni V6, pas de renommage/conversion implicite | Debug/Release conservés ; aucun changement de format financier en 10A |

## Couverture sémantique du lecteur versus cible

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

## Épreuves futures obligatoires — non exécutées dans Moneta

Sans parseur Moneta livré, **toutes** les lignes ci-dessous sont des critères 10B, pas des résultats obtenus :

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

**Décision de passage :** quatre cas BP8 réels qualifiés pour la lecture, pas une compatibilité universelle ou un import financier. Le choix d'ouverture, la provenance, les domaines manquants et la fraction EUR 0,0001 restent des portes de conception/validation. Lecteur diagnostic C# proposé seulement après accord utilisateur ; aucun démarrage automatique de 10B.
