# Audit statique de BankPerfect

## 1. Périmètre, méthode et limites

Audit du 28 octobre 2026, en lecture seule. Référence : `reference-bankperfect/`. Sources complémentaires : [manuel officiel](https://chelly.net/bankperfect/wiki/manuel.html) et ses 18 chapitres, identifiés M01–M18 dans [FUNCTIONAL_PARITY.md](FUNCTIONAL_PARITY.md#sources-officielles-consultées).

- La référence était déjà présente. Comparaison avec l'installation originale : **128 fichiers, 39 sous-répertoires, 1 649 599 octets ; tous les SHA-256 identiques ; aucun fichier ni répertoire manquant ; aucun fichier supplémentaire**. La copie existante a donc été conservée, sans déplacement ni réécriture.
- Les ressources de version Windows de `bankperfect.exe`, lues sans lancer le fichier, indiquent **FileVersion 8.0.0.373, ProductVersion 8.0, ProductName BankPerfect**. L'index officiel annonce cette version au 22 novembre 2015. Une version déclarée ne prouve pas qu'une fonction réseau historique marche encore.
- Inventaire des noms, lecture des textes/configurations et analyse statique des scripts lisibles. Les textes historiques emploient notamment Windows-1252 : un refus de lecture UTF-8 ne signifie pas que `history.txt` ou un `.py` est un binaire. Aucun fichier de référence n'a été réencodé.
- Aucun lancement de BankPerfect, de désinstalleur ou de script ; aucun chargement de DLL/PYC ; aucune décompilation ni aucun désassemblage. La lecture des ressources de version n'est pas une exécution du programme.
- `identity.dat` est traité comme opaque et potentiellement sensible : contenu non consulté, non reproduit. Pas de valeurs de comptes, d'identifiants ou de secrets dans ce rapport.
- Aucun fichier de comptes `.bp` ou sauvegarde `.bp0` n'apparaît dans cet inventaire. Cela ne permet pas de conclure à l'absence de données personnelles dans tous les fichiers opaques. Toute la référence reste exclue de Git.

**Force des preuves :** « local » signifie présence/déclaration ou lecture statique ; « manuel » signifie documentation officielle ancienne ; « à confirmer » signifie absence de preuve suffisante, divergence ou besoin d'observation. Aucun comportement n'a été vérifié par exécution de BankPerfect.

## 2. Inventaire intégral des fichiers

### Répartition

| Extension | Nombre | Taille totale en octets | Nature identifiée |
|---|---:|---:|---|
| `.exe` | 2 | 738 748 | Application et désinstalleur, non exécutés |
| `.dll` | 1 | 339 456 | `python23.dll`, non chargé |
| `.pyc` | 2 | 9 468 | Modules Python compilés, non décompilés |
| `.py` | 42 | 418 336 | Scripts lisibles, non exécutés |
| `.ini` | 44 | 6 625 | 43 déclarations/correspondances dans Scripts et un fichier de thèmes |
| `.slk` | 6 | 7 209 | Modèles tableur SYLK ; ce ne sont pas des fichiers XLSX |
| `.png` | 29 | 73 162 | Logos bancaires, non réutilisables sans droits |
| `.txt` | 1 | 56 326 | Historique |
| `.dat` | 1 | 269 | Fichier opaque potentiellement sensible |
| **Total** | **128** | **1 649 599** | **8 fichiers racine + 91 dans Scripts + 29 logos** |

Les 39 sous-répertoires sont `Scripts/`, ses 37 familles, et `logos/`.

### Racine : huit fichiers

| Fichier | Traitement / rôle |
|---|---|
| `bankperfect.exe` | Version lue comme métadonnée Windows uniquement ; pas d'exécution |
| `Uninstall.exe` | Inventorié uniquement |
| `python23.dll` | Nom suggérant le runtime Python historique ; pas de chargement ni d'analyse binaire |
| `copy_reg.pyc`, `types.pyc` | Inventoriés uniquement |
| `history.txt` | Historique de versions, source documentaire locale |
| `globalcolors.ini` | Styles/couleurs ; M17 décrit son rôle dans les options |
| `identity.dat` | Quarantaine documentaire, contenu non lu |

Le fichier de configuration utilisateur `bp.ini` est décrit par M17 mais **absent de cette référence**. Ne pas supposer les réglages d'un utilisateur réel.

### Scripts : 37 familles, 91 fichiers

Notation d'inventaire : `nom.{ini,py}` désigne exactement `nom.ini` et `nom.py` ; de même pour `slk`. Chaque ligne est relative à `Scripts/`. Les menus et raccourcis proviennent des INI de la famille, notamment des clés `Name`, `Path`, `Menu`, `Shortcut`, `Index` et des déclencheurs `ExecuteWith...`. Ce sont des **déclarations vérifiées**, pas un relevé visuel des menus complets. « Contexte » correspond à `context`, « barre » à `toolbar` ; les deux ne sont pas des menus principaux.

| Famille | Fichiers inventoriés | Fonction déclarée | Emplacement / raccourci déclaré |
|---|---|---|---|
| `AllAccountsView/` | `all_accounts_view.{ini,py}` | Relevé multi-comptes | Comptes |
| `Auto Fields/` | `auto_fields.{ini,py}` | Calcul automatique de champs | Outils ; Ctrl+F ; ajout et modification de ligne |
| `BackupMail/` | `backup_mail.{ini,py}` | Sauvegarde par courriel | Internet ; F9 |
| `Backups/` | `backups.{ini,py}` | Liste/restauration des sauvegardes | Comptes |
| `Budget/` | `budget.{ini,py,slk}` | Budget | Outils + barre ; Ctrl+B |
| `BudgetYear/` | `budget_year.{ini,py}` | Budget annuel | Outils |
| `Calc/` | `calc.{ini,py}` | Calculatrice | Outils ; Ctrl+Maj+C |
| `CategsTotals/` | `categs_totals.{ini,py,slk}` | Totaux par catégorie | Outils + barre ; Ctrl+T |
| `Charts/` | `charts.{ini,py}` | Graphiques | Outils + barre |
| `Currencies/` | `currencies.{ini,py}` | Conversion de devises | Outils ; Ctrl+D |
| `Export CSV/` | `export_csv.{ini,py}` | Export CSV | Exporter ; parent Fichier documenté par M13 |
| `Export HTML/` | `export_html.{ini,py}` | Export HTML | Exporter ; parent Fichier selon M13 |
| `Export QIF/` | `export_qif.{ini,py}` | Export QIF | Exporter ; parent Fichier selon M13 |
| `Export XLS/` | `export_xls.{ini,py,slk}` | Export tableur | Exporter ; nom du dossier ≠ garantie d'un vrai XLS |
| `Export XML/` | `export_xml.{ini,py}` | Export XML | Exporter ; parent Fichier selon M13 |
| `ExtraValues/` | `extra_values_edit.{ini,py}` | Champs supplémentaires | Opérations + contexte ; F3 |
| `Filters/` | `setfilters.{ini,py}` | Filtres enregistrés | Affichage + barre déroulante ; F7 |
| `Import CSV/` | `import_csv.{ini,py}` | Import CSV | Importer ; parent Fichier selon M13 |
| `Import OFX/` | `import_ofx.{ini,py}`, `mapping.ini` | Import OFX et correspondances des modes | Importer |
| `Import QIF/` | `import_qif.{ini,py}` | Import QIF | Importer ; Ctrl+Alt+Q |
| `Loan/` | `loan.{ini,py,slk}` | Simulateur de crédit | Outils ; Ctrl+L |
| `Luhn/` | `luhn.{ini,py}` | Vérification mathématique d'une carte | Outils |
| `ManagePlugins/` | `manage_plugins.{ini,py}` | Installer/gérer les extensions | Plugins |
| `Manuel/` | `manuel.{ini,py}` | Téléchargement du manuel | ? ; Ctrl+F1 |
| `ModifyGroup/` | `modifygroup.{ini,py}` | Modification groupée | Opérations + contexte ; Ctrl+M |
| `MultipleInput/` | `multiple_input.{ini,py}` | Saisie multiple | Opérations ; F2 |
| `Notes/` | `notes.{ini,py}`, `notes_auto.{ini,py}` | Notes et chargement à l'ouverture | Outils ; Ctrl+Maj+N ; automatique sans menu |
| `Numerals/` | `numerals.{ini,py}` | Conversions numériques | Outils |
| `PeopleTotals/` | `people_totals.{ini,py,slk}` | Totaux par tiers | Outils ; Ctrl+Alt+T |
| `Receipt/` | `receipt.{ini,py}` | Reçu de remboursements | Outils ; sous-menu Rapports non déclaré ici |
| `Replace/` | `search_replace.{ini,py}` | Remplacement de texte | Édition ; Ctrl+R |
| `Rib/` | `rib.{ini,py}` | Impression d'un RIB | Fichier |
| `Scheduler/` | `scheduler.{ini,py}`, `scheduler_add.{ini,py}`, `scheduler_auto.{ini,py}`, `previsions.{ini,py}` | Échéancier, ajout, insertion automatique et prévisions | Comptes + barre, Ctrl+E ; ajout Opérations + contexte ; insertion à l'ouverture ; menu de prévisions vide |
| `Totals/` | `totals.{ini,py,slk}` | Total des comptes | Comptes |
| `Transfer/` | `transfer.{ini,py}`, `convert_line_to_transfer.{ini,py}` | Virements et conversion d'une ligne en transfert | Opérations + barre, Ctrl+Maj+T ; conversion Opérations + contexte |
| `UpdateStock/` | `update_stock.{ini,py}` | Télécharger les cours de titres | Internet |
| `Ventilate/` | `ventilate.{ini,py}` | Ventilation | Opérations + contexte ; F6 |

Il existe **42 scripts PY mais 43 fichiers INI sous Scripts** : 42 déclarations de scripts et le `mapping.ini` de l'import OFX. Un menu vide ne prouve pas qu'une fonction est accessible par un menu caché. Les scripts automatiques et les fonctions appelées par d'autres écrans doivent être distingués des commandes autonomes.

### Logos : 29 fichiers

Tous sont dans `logos/`, avec suffixe `.png` : `allianz`, `axa_banque`, `banque_de_savoie`, `banque_populaire`, `banque_postale`, `barclays`, `bnp_paribas`, `boursorama_banque`, `caisse_epargne`, `cic`, `cnp`, `credit_agricole`, `credit_cooperatif`, `credit_du_nord`, `credit_maritime`, `credit_municipal`, `credit_mutuel`, `dexia`, `groupama`, `hsbc`, `ing`, `lcl`, `monabanq`, `societe_generale`, `societe_marseillaise_de_credit`, `ubs`, `visa_cb`, `visa_infinite`, `visa_premier`.

Les noms et comptes sont inventoriés ; aucun logo n'a été copié dans le futur code ou dans les rapports.

## 3. Fonctions identifiées par le manuel et les déclarations locales

| Domaine demandé | Faits établis | Limite de preuve |
|---|---|---|
| Comptes | Fichier `.bp` multi-comptes, plusieurs fichiers, solde initial, monnaie, informations bancaires facultatives, mot de passe, RIB (M02) | Format natif et mécanisme du mot de passe inconnus |
| Opérations | Date, mode/sens, tiers, détail, catégorie, montant ; modification, suppression, saisie multiple, calcul dans le montant, chèques (M03 et INI) | Types de stockage et règles internes non établis |
| Pointage | Non pointé/pointé/rapproché, date de pointage ; option sans rapprochement (M03/M09/M17) | Protection des lignes rapprochées à relever |
| Catégories | Catégories/sous-catégories ordonnées, renommage, déplacement, promotion lors de suppression, import/export texte (M10) | Références par nom/index et propagation aux opérations à vérifier |
| Tiers | Liste de modèles d'opérations, auto-complétion, chargement depuis liste et historique (M03/M11) | Priorité exacte et effets du changement de compte à observer |
| Échéances | Ajout automatique à l'ouverture, rattrapage des dates manquées, ajout depuis une ligne, application manuelle, simulation (M08 et Scheduler) | Fin de mois, années bissextiles, doublons et prévisions détaillées à vérifier |
| Budgets | Budget mensuel comparé au réel, compte/tous comptes, catégories/sous-catégories, périodes et méthodes (M12) ; budget annuel déclaré localement | Définition exacte de tous les calculs annuels non vérifiée |
| Rapports | Graphiques dépendant des filtres, totaux mensuels annuels par catégorie/tiers, exports HTML/tableur, total des comptes, relevé multi-comptes (M07 et INI) | Consolidation de comptes de devises différentes non garantie |
| Import/export | Imports CSV/QIF/OFX ; exports CSV/QIF/HTML/XML/tableur ; correspondances et doublons (M13 et INI) | Aucun export réel/synthétique exécuté dans cette mission |
| Devises | Monnaie principale/secondaire, affichage alternatif, convertisseur, cotations Internet, inversion avec réécriture et ajustement d'arrondis (M15) | Aucune preuve d'une couverture complète EUR/BYN/RUB, d'un historique officiel ou de garanties multidevises |
| Virements/ventilation | Deux lignes liées et navigation réciproque pour les transferts (M03) ; ventilation remplaçant une ligne par plusieurs (M15) | Atomicité, frais, taux et conservation des liens lors d'un export non prouvés |
| Bourse | Mode Titre, quantité/nom dans tiers, ISIN dans détail, prix unitaire ; téléchargement Yahoo historique (M16 et UpdateStock) | Service actuel non testé ; ne pas confondre cours courant et prix de transaction |
| Compléments | Champs supplémentaires, règles automatiques, notes, calculatrice, crédit/amortissement, Luhn, conversions numériques, reçus et gestionnaire d'extensions | Pas d'autorisation implicite de réutiliser les scripts ou de les exécuter dans Moneta |
| Personnalisation/navigation | Saisie en bas ou séparée, champs filtrants, une/deux lignes, une/deux/trois colonnes, couleurs/police, annuler/refaire, raccourcis (M01/M17/M18) | Tabulation, accélérateurs Alt, ordre exact, séparateurs et états désactivés non observés |

Les fonctions et touches sont détaillées sans statut ambigu dans [FUNCTIONAL_PARITY.md](FUNCTIONAL_PARITY.md). Toutes les réalisations Moneta y restent « à développer ».

## 4. Menus : ce qui est établi et ce qui ne l'est pas

Les INI déclarent les groupes `fichier`, `edition`, `comptes`, `operations`, `affichage`, `outils`, `internet`, `plugins`, `?`, les groupes `importer`/`exporter` et les points d'insertion `toolbar`/`context`. M13 situe les imports/exports sous Fichier. Ce relevé **ne donne pas l'ordre gauche-droite des menus natifs**.

Principales divergences :

| Sujet | Preuves | Conclusion |
|---|---|---|
| Remplacer | M05 : Opérations ; `Scripts/Replace/search_replace.ini:6` : `edition` | Préférer la déclaration pour documenter la référence, mais attendre la capture pour figer Moneta |
| Reçu | M15 : Outils > Rapports > Reçu ; `Scripts/Receipt/receipt.ini:4` : `outils` | Sous-menu Rapports non prouvé localement |
| Totaux par tiers | M07 écrit Ctrl+T ; M18 et `Scripts/PeopleTotals/people_totals.ini:7` indiquent Ctrl+Alt+T | Ctrl+Alt+T retenu comme raccourci déclaré, à vérifier visuellement |
| F9 / FTP / courriel | M17 associe Sauvegarder en ligne à F9 ; M06 et M18 distinguent FTP Maj+F9 et courriel F9 ; `BackupMail/backup_mail.ini:7` confirme F9 pour courriel | Ne pas confondre les deux actions ni leurs libellés |
| Sauvegardes locales | M06 décrit une modification du 7 mars 2016 portant le cycle de 20 à 200 ; la référence déclare 8.0.0.373 de 2015 | Ne pas attribuer automatiquement la limite 200 à cette installation |
| Lignes inutiles | M03 parle des lignes rapprochées avant le mois courant ; M04 décrit une frontière liée à la plus ancienne ligne non pointée | Algorithme exact à établir sur données fictives |
| Portée des exports | M13 généralise compte actif/lignes visibles ; XML local parcourt tout ; CSV offre un mode tous comptes | Lire le périmètre de chaque format, ne pas appliquer la règle du manuel à tous les exports |
| Ventilation | M15 décrit la saisie dans la fenêtre principale ; `Scripts/Ventilate/ventilate.py:77-117` construit un dialogue dédié | Parcours local à capturer avant de reproduire l'écran |

La capture `https://www.chelly.net/images/wiki/interface.png` liée depuis M01 renvoie **HTTP 404**, y compris avec l'hôte sans `www`. Aucun ordre de menus n'a été déduit de cette image indisponible.

## 5. Formats et migration : contraintes déjà établies

| Format | Preuve | Utilité et risque de migration |
|---|---|---|
| `.bp` | M02 : fichier natif regroupant des comptes | Pas de schéma natif établi ni de lecteur implémenté ; aucun échantillon de comptes examiné |
| `.bp0` | M06 : sauvegardes locales numérotées et courante | À préserver comme originaux ; ne pas supposer une base SQLite ni un simple CSV |
| CSV | `Scripts/Export CSV/export_csv.py:22-78,161-186` ; import `import_csv.py:12-110,165-232` | Export configurable ; compte courant/lignes visibles **ou tous comptes/toutes lignes**, un fichier par compte. Colonnes, séparateur, catégories et pointage configurables ; possibilité de retirer les numéros de chèques. Pas d'UID/liens/champs supplémentaires. L'export ne fournit pas une citation générale des cellules ; l'import accepte pourtant guillemets et multilignes puis normalise certains textes. Fidélité à tester |
| QIF | `Scripts/Export QIF/export_qif.py:8-17,28-53` ; import `import_qif.py:106-228` | Export compte actif/lignes visibles, montants à deux décimales, catégories parent/enfant ; pas de pointage, UID ou structure de ventilation émise. Import vers le compte courant, pointage initial zéro, années à deux chiffres placées dans les années 2000 ; risque de pertes ou de remappage |
| OFX | `Scripts/Import OFX/import_ofx.py:122-197,200-268,275-384`, `mapping.ini:1-15` | Import bancaire/cartes avec correspondances de comptes et modes ; ne lit pas FITID ni les devises OFX dans le traitement examiné ; catégorie absente et pointage initial zéro. Découpage textuel : compatibilité générale des dialectes non prouvée. OFX2 public est distinct |
| XML | `Scripts/Export XML/export_xml.py:22-40` | **Tous les comptes et toutes les opérations**, sans filtre dans les boucles lues. Métadonnées bancaires, deux monnaies/taux, indicateur virtuel ; opérations date/mode/tiers/détail/catégorie textuelle/montant/pointage. **Pas d'UID, date de pointage, champs supplémentaires, liens, échéances ou budgets**. Candidat plus riche, mais pas une sauvegarde complète |
| HTML | `Scripts/Export HTML/export_html.py:66-70,87-125` | Compte courant/lignes visibles, montants à deux décimales ; présentation et contrôle humain. [INFERENCE] L'absence d'échappement général dans la boucle peut altérer les textes contenant du balisage |
| SLK / « XLS » | `Scripts/Export XLS/export_xls.py:5-12,36-72` ; six modèles `.slk` | Écrit du SYLK sous le nom `bankperfect.xls`, pas un classeur BIFF/XLSX. Compte actif/lignes visibles, montants à quatre décimales et pointage ; métadonnées avancées absentes. Tester guillemets, retours à la ligne et précision |
| TXT de catégories | M10 | Liste de catégories, pas des opérations ; règle de taille minimale à préserver lors d'un éventuel import |
| INI / DAT / PYC | Inventaire et M17 | Configuration, données auxiliaires opaques et modules compilés ne constituent pas une spécification financière |
| `.bpa` | Aucune occurrence établie dans les textes/scripts ni fichier de ce nom dans l'inventaire | Extension exclue de Git par précaution, **aucune signification de format attribuée** |

**Le manuel M13 est trop général sur les filtres : les sources locales montrent des exceptions XML et CSV.** Pour QIF/HTML/tableur et le mode CSV courant, un export avec filtres peut être incomplet. Pour XML et le mode CSV tous comptes, les boucles portent sur tout le dossier. Le protocole de migration doit vérifier le périmètre choisi et comparer le nombre d'opérations et les soldes ; ne pas se fier au seul nom du format.

Les imports CSV/QIF comparent date + mode normalisé + montant à un instantané des opérations existantes (`Scripts/Import CSV/import_csv.py:203-218,601-625` ; `Scripts/Import QIF/import_qif.py:297-320,350-371`). OFX emploie compte + date + mode + montant ramené à deux décimales (`Scripts/Import OFX/import_ofx.py:200-246`). Tiers/détail ne suffisent pas à distinguer les paiements. [INFERENCE] Des paiements légitimes identiques peuvent être rejetés et des doublons internes au même fichier peuvent passer, l'index n'étant pas enrichi dans les boucles lues. Moneta ne devra supprimer aucune ligne sans justification et arbitrage.

Données dont le transport doit être démontré séparément : liste des comptes et soldes initiaux ; monnaie principale/secondaire ; états et dates de pointage ; modes personnalisés ; ordre et identité des catégories ; modèles des tiers ; détails et numéros de chèques ; liens de transferts ; ventilation ; échéancier et sa prochaine date ; budgets ; champs supplémentaires ; notes ; portefeuilles et quantités/prix ; éventuelles pièces jointes et données d'extensions. Un CSV d'opérations ne prouve pas cette couverture.

### Données non couvertes par les exports et sécurité de lecture

| Ensemble | Stockage observable dans les sources | Conséquence |
|---|---|---|
| Échéancier | Données de script intégrées au fichier de comptes, sérialisation `cPickle`, ancien `scheduler.dat` en repli (`Scripts/Scheduler/scheduler.py:21-56`) | Les définitions et prochaines échéances ne sont pas les seules opérations déjà créées |
| Champs automatiques, budgets, filtres, vue multicomptes, abréviations | Espaces de données de scripts distincts (`Auto Fields/auto_fields.py:249-291,376-398`, `Budget/budget.py:69-74,109-119`, `BudgetYear/budget_year.py:72-82`, `Filters/setfilters.py:5-16`, `AllAccountsView/all_accounts_view.py:81-91`, `MultipleInput/multiple_input.py:395-405`, sous `Scripts/`) | À inventorier séparément ; règles, ordre et indicateurs ne se déduisent pas de l'historique des opérations |
| Notes | `Scripts/Notes/notes.dat` prévu par `notes.py:76-91` et `notes_auto.py:14-20` | Fichier auxiliaire non présent dans cette référence, pas nécessairement contenu dans `.bp` |
| Profils de crédit | `loan.dat` dans le dossier applicatif renvoyé par l'hôte (`Scripts/Loan/loan.py:13,68-90`) | Profils séparés du dossier financier |
| Paramètres des reçus | `Scripts/Receipt/receipt.dat`, sérialisation (`receipt.py:72-94`) | Donnée auxiliaire à demander si cette fonction est utilisée |
| Réglages du convertisseur | Réglages INI dédiés (`Scripts/Currencies/currencies.py:20-38`) | Distincts des monnaies des comptes |

Les noms de fichiers ci-dessus proviennent du **code source lisible**, pas d'une découverte de données personnelles : aucun de ces fichiers auxiliaires DAT n'est présent dans l'inventaire, hors l'opaque `identity.dat` qui n'a pas été lu.

**Ne jamais charger un pickle non fiable ni évaluer une règle historique pour l'analyser.** Certaines règles emploient des expressions évaluables (`Scripts/Auto Fields/auto_fields.py:249-260,419-545`). La lecture documentaire n'a exécuté ni désérialisé ces données. Le futur protocole devra obtenir des représentations neutres avec l'utilisateur et isoler toute conversion nécessaire.

### Devises et intégrité des liens

- Le convertisseur local interroge une page HTML HTTP de `xe.com`, pas une API officielle structurée (`Scripts/Currencies/currencies.py:41-48`). Les taux mémorisés ne comportent pas de date dans les structures lues ; l'échec d'ajout d'une monnaie peut conduire à un taux de repli égal à un (`:20-38,51-66,150-158`). Conversion en flottants et affichage jusqu'à huit décimales (`:68-89,176-186`). Ces choix **ne satisfont pas** les garanties Moneta ; aucun appel à ce service ancien n'a été effectué.
- Les transferts directs utilisent des identifiants et des champs supplémentaires croisés `#sisterAccUid`/`#sisterLineUid` (`Scripts/Transfer/transfer.py:85-121` ; conversion d'une ligne `convert_line_to_transfer.py:21-56`). Les champs préfixés `#` peuvent être masqués dans l'interface (`history.txt:45-48`) : ne pas les jeter comme « sans valeur ».
- Les branches de transfert de l'échéancier créent des opérations opposées sans écrire ces liens dans les sections lues (`Scripts/Scheduler/scheduler.py:510-523`, `scheduler_auto.py:129-145`). [INFERENCE] Tous les virements historiques ne sont donc pas forcément identifiables par la même méthode ; ne pas les apparier seulement par montant/date.
- La ventilation locale remplace la ligne d'origine, arrondit les parts et le reliquat à deux décimales et crée des lignes non pointées ; aucun transfert de ses champs supplémentaires/UID n'apparaît dans la fonction (`Scripts/Ventilate/ventilate.py:3-45,63-75`). [INFERENCE] Le regroupement devra parfois être reconstruit ou signalé comme indémontrable.
- Certaines totalisations additionnent directement les montants renvoyés par l'hôte (`Scripts/Totals/totals.py:59-74`, `BudgetYear/budget_year.py:84-96`, `AllAccountsView/all_accounts_view.py:169-190`). Leur unité interne reste inconnue : aucune preuve d'une consolidation multidevise correcte ne peut en être déduite.

### Échéances, rapports et jalons historiques complémentaires

- Récurrences en jours/mois/années, prochaine date/date de fin, insertion automatique et report possible selon week-ends/jours fériés français (`Scripts/Scheduler/scheduler.py:669-704,761-789`, `scheduler_auto.py:92-106,108-150,180-196`). La simulation manuelle ne modifie pas les définitions, **mais insère des opérations** (`scheduler.py:479-531`) : Moneta devra présenter clairement cette différence avant arbitrage.
- Les noms `auto_insert` et `autoinsert` divergent entre un filtre manuel et les définitions (`scheduler.py:482-487,679-681`). [INFERENCE] Un mauvais sous-ensemble pourrait être sélectionné ; pas de bug exécuté ni confirmé ici.
- Le budget mensuel propose deux méthodes de mensualisation ; le budget annuel permet des tableaux nommés, douze mois Budget/Réel/Écart, des comptes choisis et des lignes associées à plusieurs catégories (`Scripts/Budget/budget.py:179-203,223-262,384-458`, `BudgetYear/budget_year.py:172-205,475-558`). Les totaux par tiers peuvent normaliser accents/casse et retirer dates/nombres (`PeopleTotals/people_totals.py:13-25,60-87`) : un libellé agrégé n'est pas un identifiant de tiers.
- `history.txt:1-26` confirme le build de 2015, des évolutions du filtrage du pointage, les liens robustes des transferts, une séparation visuelle par mois et la possibilité de désactiver un compte. `:65-83` mentionne identifiants uniques d'opérations/comptes et précision décimale ; `:309-312` l'intégration de l'échéancier/règles au fichier ; `:394-400,608-616` des ruptures de compatibilité anciennes. Ces jalons ne sont pas une spécification du format natif.


## 6. Extensions additionnelles et droits

Le manuel mentionne notamment Documents, Rapprocher, KDiff, Trader, Import OFX2, Export tableur multi-compte et Prévisions+Budget/Suite budgétaire. La [page publique Accueil](https://chelly.net/bankperfect/wiki/Accueil.html) décrit aussi une synthèse de comptes, rappels et mémo. **Les dossiers correspondants ne sont pas présents sous ces noms dans la référence inventoriée.** Les fonctions de même thème déjà fournies localement ne prouvent pas que ces extensions publiques sont installées.

Le gestionnaire distingue les plugins par `plugin.ini`, consulte un catalogue HTTP, récupère un installateur EXE et prévoit son lancement (`Scripts/ManagePlugins/manage_plugins.py:13-73,154-162`). Aucun contrôle de signature/hachage n'apparaît dans la fonction lue ; un contrôle par l'hôte reste inconnu. Aucun téléchargement, installation, suppression ni lancement n'a été effectué. Cette chaîne historique ne doit pas être reprise implicitement dans Moneta.

Gratuit ne signifie pas libre : conserver une implémentation Moneta originale. Aucun code Python, binaire, logo ou modèle propriétaire n'est intégré dans les fichiers suivis du projet. La possibilité juridique de réutiliser un actif n'est pas présumée.

## 7. Inconnus et captures nécessaires

L'utilisateur devra produire les captures avec **un fichier entièrement fictif**, ou masquer irréversiblement noms, comptes, soldes réels, chemins personnels et identifiants. Ne pas capturer un onglet contenant des secrets. Le présent audit ne vaut pas autorisation d'exécuter BankPerfect.

| Capture / échantillon à demander | Question à résoudre |
|---|---|
| Fenêtre principale entière et À propos | Version réellement utilisée, colonnes, zones, ordre des menus et barres |
| Tous les menus déroulés, sous-menus et clic droit | Libellés exacts, séparateurs, emplacements, raccourcis et commandes désactivées |
| Gestion des comptes et paramètres de monnaies | Devises, soldes initiaux, compte principal/secondaire, choix affichés |
| Saisie intégrée et séparée, sélection multiple, pointage | Navigation clavier, comportement d'Entrée/Tab, états et dates |
| Catégories, tiers et modes de paiement | Hiérarchie, ordre, modèles et personnalisations |
| Transfert même devise puis deux devises, conversion de ligne | Champs du dialogue, sens du taux, arrondis, frais et liens |
| Ventilation avec plusieurs catégories | Remplacement/regroupement, signes, reliquat et annulation |
| Échéancier, ajout, application/simulation, prévisions | Récurrences, fins de mois, retard et absence de doublons |
| Budget mensuel/annuel et rapports | Choix de période/compte, filtres, calculs et exports |
| Options, sauvegardes/restauration et liste d'extensions | Rétention réelle, règles de sauvegarde et plugins effectivement utilisés ; sans secrets |
| Portefeuille et mise à jour de cotations, si utilisés | Quantité, acquisition, valorisation, sources de prix et conservation de l'historique |
| Jeu fictif exporté compte par compte en XML/CSV/QIF | Couverture réelle des champs et formats, comparaison des comptes/soldes/états/liens |

**Prochaine décision :** valider ces observations, compléter la spécification de fidélité et les critères de migration, puis seulement autoriser un premier lot de programmation. Les mesures de fiabilité empêchant la réécriture rétroactive, le FTP en clair ou l'envoi de données financières sans protection doivent faire l'objet d'arbitrages explicites, pas d'un changement de comportement caché.
