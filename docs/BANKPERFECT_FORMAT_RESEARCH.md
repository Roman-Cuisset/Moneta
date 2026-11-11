# Recherche du format BankPerfect — mission 10A

## Conclusion et portée

**Le lecteur V5 public est étudié, compilé et confronté à quatre fixtures réellement produites par notre BankPerfect 8.0.0.373.** Qualification limitée à ces cas, pas à tous les BP8. Aucun importeur Moneta, décodeur Moneta, changement de schéma ou traitement de fichier personnel ajouté.

La mission 9 avait identifié la révision `ea0624c408797acea6cdd58d59a0bb19560908fa` par lecture statique. La mission 10A étudie son successeur **`990f5c7f712dab07c7fefc2921cbf60beee7f6b1`**, daté du **10 novembre 2026**, dans une copie séparée du dépôt Moneta. La version déclarée du workspace est **9.13.0**, édition Rust 2024 ; outils réellement employés : rustc/cargo **1.98.1**.

Le [site de l'auteur](https://chelly.net/bankperfect9/) renvoie au [dépôt public](https://github.com/fabiochelly/bankperfect). Sa [licence à la révision étudiée](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/LICENSE) est **MIT**, copyright 2026 Fabio Chelly : maintien de la notice pour une future reprise substantielle, absence de garantie. Cela ne requalifie pas les anciens binaires, scripts, logos ou dépendances en composants librement redistribuables. Aucun code tiers n'est incorporé à Moneta.

### Niveaux de preuve

- **C — confirmé par le code** : comportement de cette révision Rust, pas automatiquement celui du binaire Delphi 2015.
- **F — confirmé par fichier** : BP-F00..BP-F03 réellement produits par BP8 et confrontés aux saisies/UI, aux octets et au lecteur figé ; portée et écarts dans [les fiches](BANKPERFECT_FIXTURES.md).
- **H — hypothèse / documentation historique** : description publiée des sources Delphi de mars 2019 ou règle proposée, non prouvée sur notre binaire.
- **I — inconnu** : information insuffisante ; jamais remplacée par une valeur inventée.

Les tests sur du texte construit pour une sonde sont identifiés séparément : ils prouvent une réaction du lecteur, **pas la production d'un `.bp` par BankPerfect**.

## Sources figées et symboles étudiés

Tous les liens suivants sont épinglés au même commit, sans dépendance au futur contenu de `main`.

| Source | Symboles / lignes utiles |
|---|---|
| [Cargo.toml](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/Cargo.toml#L1-L12) | Workspace, version 9.13.0, licence MIT |
| [lib.rs](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/lib.rs#L41-L57) | `FileFormat`, `detect_format` : V6 puis V5, sinon erreur |
| [codec_v5.rs](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/codec_v5.rs#L13-L103) | `HEADER`, `decode`, `decode_with_line_fallback`, `decrypt`, `line_ending` |
| [v5_text.rs](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/v5_text.rs#L12-L253) | `parse`, `Header`, `simple_decode`, `parse_modes`, `parse_categories` |
| [v5_text.rs — comptes/opérations](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/v5_text.rs#L256-L519) | `parse_accounts`, `parse_payees`, `OperationParser::parse_line`, `parse_mode`, `assign_missing_uids` |
| [model.rs — dates/montants](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/model.rs#L8-L220) | `Date`, `Amount`, parsing, formatage et opérateurs |
| [model.rs — entités](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/model.rs#L365-L502) | `Tack`, `Operation`, `Account`, `Account::balance`, `Category` |
| [file.rs](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/file.rs#L36-L109) | `requires_password`, `open`, `open_bytes`, `to_bytes` |
| [split.rs](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/split.rs#L293-L309) | `link` écrit les deux références réciproques |
| [edit.rs](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/edit.rs#L111-L115) | La modification du pointage dans BP9 met `mark_date` à `today` |
| [Inventaire publié du format](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/docs/inventaire/02-format-fichier-bp.md) | Sources annoncées : Delphi mars 2019 et `Exemple.bp`, pas notre binaire 2015. Les références Delphi ne sont pas des fichiers examinés ici |

## Conteneur et grammaire V5

| Élément | Règle établie et limite | Preuve |
|---|---|---|
| Signature | Marqueur logique de **9 octets** `BP-FILE-5`, après décodage. Ce n'est pas une signature physique en clair à tester directement au début du fichier | C, `codec_v5.rs:13,45-49` |
| Substitution | Alphabet de 77 caractères ; pour l'octet d'indice zéro `j`, décalage inverse par `deltas[(j+1)%100]` et longueur totale `L`, modulo 77. Les autres octets sont inchangés ; les tables sont dans le source, non reproduites ici | C, `codec_v5.rs:15-35,68-80` |
| Fins de ligne | Premier essai sur les octets originaux ; second essai avec transformation LF/CRLF selon le résultat. Pas de réduction préalable des lignes vides dans ce Rust. La description Delphi publiée prévoit, elle, une réduction : les deux règles ne sont pas identiques | C, `codec_v5.rs:54-103` ; H, inventaire §2.6 |
| Encodage | Le Rust impose **Windows-1252** après retrait du marqueur. Le document historique décrit une page ANSI système, CP1252 sur Windows français. Une variante produite sous une autre page ANSI reste à qualifier ; pas de garantie Unicode/cyrillique déduite du seul parseur | C, `codec_v5.rs:45-49` ; H, inventaire §2.1 |
| Compression | Aucune décompression dans ce chemin V5 ; substitution ≠ chiffrement authentifié. Les formats historiques UCL/NRV2E « BPXENCODE » et Shuffle sont documentés séparément mais **non sélectionnés par `detect_format`** | C, codec / `lib.rs:49-57` ; H, inventaire §11 |
| Séparateur | `TNSLPPBNTSOGPTQBC280673`, **23 caractères**, sépare l'en-tête et les blocs de comptes ; peut être accolé à la première opération, ce n'est pas nécessairement une ligne autonome | C, `v5_text.rs:12,26-58` |
| En-tête | Lignes `clé=valeur`, découpage au premier `=`, recherche ASCII insensible à la casse, premier doublon retenu ; lignes sans `=` ignorées | C, `v5_text.rs:98-125` |
| Comptes/blocs | `compte0`, `compte1`… contigus ; arrêt au premier absent. Appariement positionnel compte/bloc. Blocs excédentaires ignorés avec avertissement ; dernier compte sans bloc conservé vide | C, `v5_text.rs:38-58,256-293` |
| Opérations | Une ligne, **7 à 9 champs** séparés simplement par `;`, sans syntaxe CSV de guillemets ; hors cardinalité, ligne ignorée avec avertissement | C, `v5_text.rs:361-437` |
| Intégrité | Reconnaissance de marqueur et parsing, pas de checksum/authentification V5 identifié. Aucun contrôle complet de graphe/références n'est prouvé par une ouverture réussie | C pour les contrôles lus ; I pour d'autres garanties du binaire BP8 |
| Nouveau V6 | Magic `BPFILE6\0`, conteneur version 1, compression zstd, protection XChaCha20-Poly1305 ; format **BP9**, distinct du V5 et du schéma 5 SQLite Moneta | C, [format_v6.rs:1-67](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/format_v6.rs#L1-L67) |

`file::open` lit le fichier entier. `codec_v5` et `v5_text` allouent des vecteurs/chaînes sans les quotas d'import exigés par Moneta. La présence de protections propres au V6 ne doit pas être attribuée au lecteur V5.

### Comptes : champs positionnels, indices base zéro

Confirmé **C** par `parse_accounts:256-293` et **F** pour les comptes EUR des quatre fixtures ; autres builds, pages ANSI et variantes monétaires encore **I**.

| Indice | Donnée |
|---|---|
| 0 / 1 / 2 | Banque / agence / adresse |
| 3 / 4 | Nom du compte / numéro |
| 5 / 6 | Taux principal / devise principale |
| 7 / 8 | Taux secondaire / devise secondaire |
| 9 / 10 / 11 | Codes banque / agence / BIC |
| 12 / 13 | Nom de logo / IBAN — logo jamais intégré à Moneta |
| 14 / 15 / 16 | Marqueur `virtual` / UID opaque / marqueur `disabled` |

Champs absents → chaînes vides ; UID absent → identifiant généré. Les taux sont analysés en **`f64`**, avec valeurs de repli 1 et 6,55957 : ce chemin ne convient pas à une migration monétaire exacte. Préserver leur texte brut ; aucune assimilation à un cours officiel NBRB/CBR.

### Opérations : champs, indices base zéro

| Indice | Lecture Rust actuelle | Exigence future Moneta |
|---|---|---|
| 0 | Date entière Delphi ; erreur → aujourd'hui + avertissement | Date bornée valide ou diagnostic bloquant, jamais remplacement |
| 1 | `P` / `1` → P ; `R` → R ; tout autre → NP | Codes connus explicites, brut conservé ; inconnu signalé |
| 2 | Code ou nom de mode ; plusieurs replis/recréations | Aucun remplacement silencieux par Carte ; conserver code/nom/sens |
| 3 / 4 | Tiers / détail bruts | Texte exact ; libellé cible à définir explicitement |
| 5 | ID catégorie ; invalide/inconnu → absence | Ne pas perdre une référence source sans diagnostic |
| 6 | Montant signé ; invalide → zéro + avertissement | Parsing exact strict ; zéro valide distinct d'un échec |
| 7 | `mark_date` ; absent/invalide → date d'opération | Préserver le brut ; F03 confirme la date de pointage pour 8.0.0.373, pas pour toutes les variantes |
| 8 | Extra : `<\|\|>` → `;`, paires `clé=valeur`, `UId` séparé | Préserver toutes les clés, les doublons et les relations avant transformation |

Extra décode également `<fab_pv>` en `;` et `<fab_cr>` en LF (`v5_text.rs:408-420,486-489`). Données de scripts : préfixe `bp_script_data_`, décodage d'octets `_hh` (`:128-188`). **Jamais de `pickle.loads`, `eval`, expression ou plugin exécuté à l'import** ; décoder des octets n'est pas encore comprendre le contenu métier.

## Montants, dates et ouverture : décisions non définitives

- **C** : `Amount(i64)`, échelle **10 000** ; signe intrinsèque. Le parseur arrondit les décimales supplémentaires au pair à quatre places ; le formatage arrondit aux places demandées en s'éloignant de zéro. Il expose aussi `as_f64`, à ne pas utiliser pour Moneta.
- **H** : l'inventaire Delphi décrit Currency signé 64 bits × 10⁻⁴ et une sauvegarde de précision configurable 0..4, défaut 2. Sa section U1 reconnaît l'absence de vérification binaire de l'arrondi historique. **Aucun arrondi BP8 n'est déclaré mesuré.** Un chiffre déjà perdu lors de la sauvegarde n'est pas récupérable.
- **F** : F03, option native de précision **4** vérifiée, sauvegarde et lecture exacte de **0,0001 EUR** et d'un solde **1 087,6701 EUR**. La précision 2 était observée auparavant. Les règles d'arrondi des demi-unités restent non mesurées ; ne pas extrapoler ce test.
- Cible : `decimal` puis unités mineures exactes. Une fraction 0,0001 EUR n'est pas représentable en centimes : blocage, pas arrondi ou devise artificiellement modifiée. Vérifier aussi la magnitude minimale négative historique et les dépassements ; tous les calculs et conversions doivent être contrôlés.
- **C** : `Date(i32)` = jours depuis **1899-12-30**, sans fuseau ; les tests vérifient notamment 39444 = 2007-12-28. Une valeur i32 n'assure pas qu'elle soit représentable par le calendrier cible.
- **Différence documentaire circonscrite par F03** : l'inventaire Delphi §6.8 nomme le champ 7 « valeur » ; BP9 et les gestes exercés sur **8.0.0.373** l'utilisent pour le pointage. Recette du 2024-01-15 : NP avec date supplémentaire 2024-01-15 → clic P avec 2026-11-11 → **Opérations → Date de pointage** avec 2024-01-31, montant/date d'opération inchangés. Mapping P envisageable pour ce périmètre approuvé ; autres versions/usages et dates NP restent à distinguer, brut conservé.
- **C + F** : pas d'ouverture séparée dans `Account`, somme des opérations ; F00/F01 portent « Création du compte courant »/Versement et F02 porte aussi « Solde initial »/Virement reçu, déjà R sans relevé. F00 zéro contient une ligne technique nulle à sept champs, sans UID ni date supplémentaire. Ne classifier ni sur le seul libellé/mode, ni sur la première ligne.
- **Proposition, à valider** : conserver chaque ligne historique une fois, ouverture cible zéro et provenance explicite. Extraction vers `opening_minor` seulement après preuve et règle approuvée, en retirant exactement une fois son effet du registre et en contrôlant également les soldes P/R. Aucun choix définitif ni transformation programmé.

## Identités, relations et données avancées

UID de compte : chaîne opaque, pas nécessairement UUID ; identifiant de fichier : peut contenir un ancien chemin personnel selon la description Delphi, donc **pas de journalisation brute**. `UId` d'opération est un `u64`. Le Rust réattribue les UID absents ou dupliqués (`assign_missing_uids:495-519`), ce qui ne prouve pas une réparation correcte des liens préexistants.

Les transferts référencent `#sisterAccUid` et `#sisterLineUid` dans les deux sens. **F02 confirme** deux jambes UID 3/4, −150/+150 EUR, deux comptes et références réciproques exactes, sauvegardées par le plugin historique examiné et lues sans avertissement. L'ouverture de 200 EUR est un autre Virement reçu sans lien : le mode ne suffit pas. Liens asymétriques/cassés ou UID réparés restent à diagnostiquer, jamais à reconstruire par similitude.

Le parseur V5 conserve les données de scripts brutes dans `feature_data`, mais cela ne signifie pas qu'échéances, budgets, règles ou rapprochements formels soient migrés. Un R seul ne prouve pas une session/relevé. Ventilation, coordonnées bancaires, modèles de tiers, dates de valeur et provenance n'ont pas tous une cible complète dans Moneta schéma 5 : voir [mapping](BANKPERFECT_MAPPING.md).

## Épreuves réellement exécutées

Commandes dans la copie tierce séparée ; profil Rust debug, dépendances verrouillées :

| Épreuve | Résultat observé |
|---|---|
| `cargo test --locked --offline -p bp-core --lib codec_v5::tests::` | Premier essai bloqué par dépendance `directories` absente du cache : aucun test exécuté |
| Même filtre, `cargo test --locked` après accès normal au registre Cargo | **3 réussis**, 0 échec/ignoré : roundtrip construit, conversion CRLF, rejet de bruit |
| `cargo test --locked --offline -p bp-core --lib v5_text::tests::` | **3 réussis**, 0 échec/ignoré : texte d'exemple inline, séparateur manquant, identifiants générés |
| `cargo test --locked --offline -p bp-core --lib model::tests::` | **5 réussis**, 0 échec/ignoré : dates, montants, formatage, codes/noms des modes |
| Sonde temporaire appelant le parseur sur texte fictif, hors Moneta | Observations ci-dessous ; ce n'est ni un fichier BP8 ni un nouveau test de l'application |

Soit **11 tests unitaires amont ciblés**, pas une exécution de tous les tests BP9 ni une qualification des fichiers legacy. Sources des cas : [codec:129-147](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/codec_v5.rs#L129-L147), [parseur:556-674](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/v5_text.rs#L556-L674), [modèle:670-761](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/src/model.rs#L670-L761).

### Limites démontrées de la lecture permissive

Sonde de texte synthétique, sans aucun `.bp` personnel ni écriture financière :

| Entrée / invariant | Observation |
|---|---|
| Date/montant invalides et état `X` | Ligne conservée avec date du jour, montant 0, état None ; trois avertissements au total, dont attribution d'UID |
| `0.0001` | `Amount.0 = 1`, précision de quatre décimales effectivement conservée |
| `1-2` | Accepté comme 12, soit `Amount.0 = 120000` |
| `-922337203685477.5808` | `Amount::parse` renvoie `None` malgré la borne négative du Currency historique |
| Deux UID 7 | Résultat 7 et 8, avertissement ; pas une preuve de préservation des anciennes relations |
| Clé d'en-tête fictive `compté0` | **Panic** à `v5_text.rs:154` : découpage UTF-8 au milieu d'un caractère |
| UID `u64::MAX` | **Panic en debug**, addition débordante dans `assign_missing_uids` (`:496-502`) |

Les deux panics ont été capturées dans la sonde afin d'observer les deux cas ; le lecteur n'a pas été corrigé ou incorporé. Le comportement release de ces dépassements n'a pas été mesuré. Autres risques **statiques** : opérateurs `Amount` non contrôlés, conversion calendrier sur dates extrêmes et absence de quotas V5 ; ne pas les présenter comme des tests exécutés.

Les [tests legacy sur fichiers](https://github.com/fabiochelly/bankperfect/blob/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/crates/bp-core/tests/legacy_files.rs#L1-L135) recherchent des `.bp`/`.bp0` dans un répertoire extérieur au crate. Leurs boucles peuvent réussir **sans fichier**, et le test `Exemple.bp` retourne si ce fichier manque. Les fichiers protégés ne sont parfois testés que pour le refus sans mot de passe. **Suite non exécutée ici et aucun succès de migration déduit de ces tests conditionnels.** Un roundtrip conserve éventuellement un modèle déjà réparé : il ne démontre pas la fidélité à la source.

## BankPerfect 8 local et corpus

Lecture de métadonnées, sans modification : version EXE **8.0.0.373**, produit 8.0. SHA-256 :

`e81fa1973c2dc26c70dd596181b00208711917144b13d9dbd43ab2bf8216a0c6`

Recherche dans la référence : **aucun `.bp`/sauvegarde `.bpN`**, aucun corpus utilisateur recherché ailleurs. Les exports CSV/QIF/XML existants restent ceux de l'audit ; leurs limites ne sont pas annulées par la découverte du lecteur Rust. Aucun export n'a pu être produit lors de cette mission.

**Exécution isolée réalisée** : Windows Sandbox vérifié sous `WDAGUtilityAccount`, culture fr-FR/page ANSI 1252 ; réseau, clipboard et redirections sensibles désactivés, seuls dossiers de recherche dédiés mappés. La copie minimale sans `identity.dat` a d'abord échoué. Le fichier opaque de référence n'a été ni lu ni utilisé.

Paquet 8.0 du [site officiel](https://chelly.net/bankperfect/), [téléchargement HTTPS](https://chelly.net/bankperfect/bp.exe), SHA-256 `d122cc9bb17140cf37c75c756001b471ea7798bc0ca1fde4f0cf9351eeb42665` : deux essais silencieux ont renvoyé 2, puis **l'installation interactive dans le seul invité a réussi**. L'EXE extrait a exactement le hash de notre référence ; la ressource `identity.dat` de distribution a pu être copiée sans lecture de son contenu, sans ressource privée. Aucun installateur sur l'hôte ni protection Windows désactivée. Signature Authenticode hôte non qualifiée, module PowerShell indisponible.

**Quatre fixtures produites et comparées**, enregistrements natifs uniquement. F00 : 1 compte/1 ouverture nulle, aucun mouvement métier ; F01 : ouverture + recette/dépense, 1 087,67 EUR ; F02 : deux comptes, virement et liens réciproques, 850/350 EUR ; F03 : NP/P/R, date manuelle, hiérarchie et 0,0001 EUR, solde 1 087,6701. Tailles/hashes, étapes, écarts de noms/parent réellement observés et limites : [fiches](BANKPERFECT_FIXTURES.md), [matrice](BANKPERFECT_COMPATIBILITY_MATRIX.md). Les quatre empreintes sont inchangées après lecture Rust et fermeture de BP8 ; invité propriétaire arrêté. Aucun fichier/capture/binaire ajouté au dépôt.

## Passage vers 10B

1. Revoir les quatre fiches et élargir le corpus aux autres domaines/versions nécessaires ; les cas budgets/échéances/ventilations, protections et BP multidevise restent non exercés.
2. Garder les fixtures closes/stables, leurs hashes et la lecture Rust **sans réenregistrement V6** ; compléter les épreuves négatives, répétitions et variantes encore absentes.
3. Approuver ouverture, dates, monnaies, identités et provenance ; décider explicitement le **blocage de la fraction EUR 0,0001 non représentable en centimes**.
4. Après validation utilisateur seulement : lecteur C# diagnostic borné/passif puis mapping représentable. **Mission 10B non commencée ; aucun import complet ni conversion financière livré.**
