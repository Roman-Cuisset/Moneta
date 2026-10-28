# Fidélité fonctionnelle à BankPerfect

## Portée et légende

État au 28 octobre 2026 : **aucune fonction Moneta n'est développée**. Cette matrice définit la cible, pas des capacités disponibles.

- **Vérifié — local** : présence ou déclaration dans les fichiers lisibles de la référence ; ne prouve pas le bon fonctionnement à l'exécution.
- **Vérifié — manuel** : comportement ou chemin explicitement décrit par le manuel officiel ; présence exacte dans l'installation locale encore à confirmer visuellement.
- **À confirmer** : contradiction, absence de preuve ou détail uniquement observable en fonctionnement.
- **À développer** : statut de réalisation dans Moneta, distinct du statut de la preuve BankPerfect.

Les noms de menus sont normalisés avec accents dans ce document. Les clés INI restent parfois sans accents. Les `Index` des extensions ne suffisent pas à reconstruire leur position parmi les commandes natives. **L'ordre des menus principaux, l'ordre complet des commandes et les séparateurs ne sont pas établis.** Ne pas les inventer.

L'inventaire exhaustif des fichiers et familles d'extensions figure dans [BANKPERFECT_AUDIT.md](BANKPERFECT_AUDIT.md). Les références M01–M18 ci-dessous désignent les pages officielles consultées, recensées en fin de document.

## Correspondance fonctionnelle

| Fonction BankPerfect | Emplacement connu / preuve | État de la preuve | Cible Moneta | Réalisation |
|---|---|---|---|---|
| Fichier regroupant plusieurs comptes, plusieurs fichiers possibles | M02 ; fichier `.bp` | Vérifié — manuel | Un dossier financier indépendant avec plusieurs comptes ; conserver la logique ouvrir/enregistrer | À développer |
| Nouveau fichier, solde initial | M02 : Comptes > Créer un nouveau fichier de comptes | Vérifié — manuel ; emplacement local à confirmer | Même commande et parcours | À développer |
| Ouvrir, enregistrer, enregistrer sous, imprimer | M02, M18 ; Fichier pour ouvrir/enregistrer ; détails des autres chemins à relever | Vérifié — manuel ; arborescence partielle | Mêmes gestes et raccourcis ; sauvegarde durable explicite | À développer |
| Gestion et changement de compte | M02 : Comptes > Gestion des comptes ; liste sous les menus | Vérifié — manuel | Créer, modifier, supprimer selon règles de sécurité ; changement de compte au même endroit | À développer |
| Informations bancaires, monnaie du compte, RIB | M02 ; `Scripts/Rib/rib.ini:2-6` : Fichier | Vérifié — manuel et local | Métadonnées facultatives, contrôle RIB et impression | À développer |
| Mot de passe d'un fichier | M02 : Comptes > Mot de passe | Vérifié — manuel ; protection cryptographique inconnue | Protection à concevoir, pas de simple imitation d'un verrou non étudié | À développer |
| Agencement principal | M01 : menus, liste des comptes, barre d'outils, informations, filtres, tableau, saisie, état | Vérifié — manuel ; capture officielle indisponible | Même hiérarchie visuelle, modernisation légère | À développer |
| Saisie intégrée ou fenêtre séparée | M03, M17 | Vérifié — manuel | Deux modes, mêmes champs et navigation clavier | À développer |
| Opération : date, mode, tiers, détail, catégorie, montant | M03 | Vérifié — manuel | Saisie, modification, suppression explicites et calcul exact | À développer |
| Modes de paiement natifs et personnalisés | M03 : Comptes > Modes de paiements | Vérifié — manuel | Sens débiteur/créditeur et modes personnalisés | À développer |
| Numérotation des chèques | M03 : numéro dans le détail, progression lors d'une nouvelle saisie | Vérifié — manuel | Reprendre le parcours, stockage structuré sans perdre le détail importé | À développer |
| Calculatrice dans le montant | M03 : expressions et priorité multiplication/division | Vérifié — manuel | Analyse arithmétique sûre en décimal, sans exécution de code | À développer |
| Auto-complétion et chargement d'un tiers | M03, M11 : Opérations > Charger un tiers | Vérifié — manuel | Réutiliser les modèles de tiers et l'historique selon le comportement à relever | À développer |
| Pointage et rapprochement | M03 : menu contextuel, dernière colonne, cycle non pointé/pointé/rapproché | Vérifié — manuel | Trois états et option deux états ; ne pas confondre solde total et pointé | À développer |
| Date de pointage | M09 : Opérations > Date de pointage ; Affichage pour la bascule des dates | Vérifié — manuel | Date métier distincte de la date d'opération | À développer |
| Annuler/refaire | M17, M18 ; emplacement natif exact non établi | Vérifié — manuel pour les commandes | Annulation contrôlée, compatible avec persistance et historique de corrections | À développer |
| Copier/coller ou déplacer des lignes entre comptes | M05 : Édition > Copier/Coller | Vérifié — manuel | Préserver devises, références et invariants ; règles de déplacement à confirmer | À développer |
| Saisie multiple | `Scripts/MultipleInput/multiple_input.ini:2-7` : Opérations | Vérifié — local | Saisie en série fidèle | À développer |
| Modification groupée | M05 ; `Scripts/ModifyGroup/modifygroup.ini:2-8` : Opérations + contexte | Vérifié — manuel et local | Modification prévisualisée d'une sélection | À développer |
| Remplacer du texte | M05 : Opérations > Remplacer ; `Scripts/Replace/search_replace.ini:2-7` : Édition | **À confirmer — chemins contradictoires** | Ne pas choisir le menu définitif sans capture locale | À développer |
| Ventilation par catégories | M15 ; `Scripts/Ventilate/ventilate.ini:2-6` : Opérations + contexte | Vérifié — manuel et local ; détails de regroupement à confirmer | Ventilation équilibrée, total inchangé ; vue fidèle malgré modèle interne plus sûr | À développer |
| Virement entre comptes et navigation entre ses lignes | M03 ; `Scripts/Transfer/transfer.ini:2-9` : Opérations + barre d'outils | Vérifié — manuel et local | Deux jambes liées et atomiques, navigation réciproque | À développer |
| Convertir une ligne en transfert | `Scripts/Transfer/convert_line_to_transfer.ini:2-8` : Opérations + contexte | Vérifié — local | Conversion atomique avec contrôle des lignes déjà existantes | À développer |
| Filtres par colonne, date, texte et montant | M04 : ligne d'en-tête du relevé | Vérifié — manuel | Reprendre la grammaire documentée, pas seulement une recherche générique | À développer |
| Filtres enregistrés et périodes dynamiques | M04 ; `Scripts/Filters/setfilters.ini:2-9` : Affichage + barre d'outils déroulante | Vérifié — manuel et local | Périodes glissantes, application et réinitialisation rapides | À développer |
| Cacher les lignes inutiles | M03/M04 : Affichage ; critères décrits différemment selon les pages | **À confirmer — règle de frontière** | Masquer sans supprimer ; valider la règle exacte avec jeu synthétique | À développer |
| Solde tenant compte ou non des filtres | M04, M17 : Outils > Options | Vérifié — manuel | Différencier clairement solde du compte et total filtré ; fidélité du mode à valider | À développer |
| Affichage une/deux lignes ; une/deux/trois colonnes de montant | M01, M17 | Vérifié — manuel | Même densité et choix de présentation | À développer |
| Gestion des catégories et sous-catégories | M10 : Comptes > Gestion des catégories | Vérifié — manuel | Ordre, renommage, déplacement, tri et conversion de niveau | À développer |
| Suppression d'une catégorie sans perdre ses sous-catégories | M10 : promotion de la première sous-catégorie | Vérifié — manuel | Reprendre après validation des références d'opérations | À développer |
| Import/export de catégories texte | M10 : boutons de la gestion des catégories ; `.txt` | Vérifié — manuel | Prévisualisation et contrôles de correspondance | À développer |
| Tiers comme modèles d'opérations | M11 : Comptes > Gestion des Tiers | Vérifié — manuel | Montant, mode, détails et catégorie réutilisables | À développer |
| Champs supplémentaires par opération | M09 ; `Scripts/ExtraValues/extra_values_edit.ini:2-8` : Opérations + contexte | Vérifié — manuel et local | Champs nommés, édition et affichage dans les informations | À développer |
| Calcul automatique des champs | `Scripts/Auto Fields/auto_fields.ini:2-10` : Outils ; déclencheurs ajout/modification | Vérifié — local | Étudier les règles existantes et proposer une expression sûre, sans reprendre le moteur Python | À développer |
| Échéancier : création, modification, application, rattrapage | M08 ; `Scripts/Scheduler/scheduler.ini:2-9` : Comptes + barre d'outils | Vérifié — manuel et local | Récurrences, application manuelle, rattrapage sans doublons | À développer |
| Ajouter une opération à l'échéancier | M08 ; `Scripts/Scheduler/scheduler_add.ini:2-7` : Opérations + contexte | Vérifié — manuel et local | Reprendre une opération comme modèle | À développer |
| Insertion automatique à l'ouverture | M08 ; `Scripts/Scheduler/scheduler_auto.ini:2-4` : déclencheur fichier | Vérifié — manuel et local | Insertion idempotente et message explicite, jamais un traitement silencieux incontrôlé | À développer |
| Simulation d'échéances et prévision de solde | M08 ; `Scripts/Scheduler/scheduler.py:479-531`, `previsions.ini:2-7` | Vérifié — local : simulation manuelle insère des opérations sans avancer les définitions ; accès aux prévisions à confirmer | Simulation isolée proposée, **écart à valider** ; ne jamais confondre aperçu et écritures réelles | À développer |
| Budget mensuel comparé au réalisé | M12 ; `Scripts/Budget/budget.ini:2-8` : Outils + barre d'outils | Vérifié — manuel et local | Catégories/sous-catégories, comptes, période et méthodes de calcul | À développer |
| Budget annuel | `Scripts/BudgetYear/budget_year.ini:2-7`, `budget_year.py:172-205,475-558` : Outils | Vérifié — local | Tableaux nommés, douze mois Budget/Réel/Écart, comptes choisis et lignes associées à plusieurs catégories | À développer |
| Graphiques filtrés, modèles et impression | M07 ; `Scripts/Charts/charts.ini:2-7` : Outils + barre d'outils | Vérifié — manuel et local | Mêmes périmètres de calcul, graphes lisibles | À développer |
| Totaux mensuels par catégorie et par tiers | M07 ; `Scripts/CategsTotals/categs_totals.ini`, `Scripts/PeopleTotals/people_totals.ini` : Outils | Vérifié — manuel et local ; erreur de raccourci dans M07 | Rapports annuels mensuels, HTML et tableur selon parité | À développer |
| Total des comptes | `Scripts/Totals/totals.ini:2-6` : Comptes | Vérifié — local | Totalisation, sans addition de monnaies différentes sans conversion | À développer |
| Relevé multi-comptes | `Scripts/AllAccountsView/all_accounts_view.ini:2-7` : Comptes | Vérifié — local | Vue transversale et filtre des comptes | À développer |
| Comptes virtuels et désactivation de compte | XML local `Scripts/Export XML/export_xml.py:27-30` ; `history.txt:12-14` ; `Scripts/Totals/totals.py:59-74` | Vérifié — local ; dialogue et effets complets à confirmer | Distinguer compte virtuel, compte désactivé et suppression ; reproduire les règles de totalisation après observation | À développer |
| Import CSV, QIF, OFX | M13 ; INI des dossiers `Import CSV`, `Import QIF`, `Import OFX` | Vérifié — manuel et local ; OFX2 public distinct | Prévisualisation, correspondances, anomalies et arbitrage des doublons | À développer |
| Export CSV, QIF, HTML, XML et tableur | M13 : Fichier > Exporter ; `Scripts/Export XML/export_xml.py:22-40`, `Scripts/Export CSV/export_csv.py:161-186` et INI | Vérifié — local : **XML tous comptes/toutes lignes ; CSV mode courant ou tous comptes** ; M13 généralise à tort | Périmètre explicite par format, contrôle de couverture ; tableur SYLK sous extension XLS, pas BIFF/XLSX | À développer |
| Sauvegardes locales et externes | M06 ; Fichier > Enregistrer sur disque externe | Vérifié — manuel ; rétention locale dépendante de version | Sauvegardes automatiques vérifiables et destination externe | À développer |
| Liste et restauration de sauvegardes | M06 ; `Scripts/Backups/backups.ini:2-6` : Comptes | Vérifié — manuel et local | Vérifier avant restauration, préserver l'état actuel | À développer |
| Sauvegarde par courriel et FTP | M06 ; `Scripts/BackupMail/backup_mail.ini:2-8` : Internet pour le courriel | Vérifié — manuel et déclaration locale ; libellés contradictoires | Parité à arbitrer : pas de FTP clair ni de données envoyées sans consentement/protection | À développer |
| Deux monnaies, bascule d'affichage | M15 ; boutons de barre d'outils | Vérifié — manuel | Conserver les repères tout en distinguant monnaie comptable et valorisation | À développer |
| Inverser les monnaies avec conversion de toutes les lignes | M15 : Outils > Inverser les monnaies | Vérifié — manuel | **Arbitrage requis** : ne pas reproduire une réécriture rétroactive silencieuse | À développer |
| Convertisseur de devises | M15 ; `Scripts/Currencies/currencies.ini:2-7`, `currencies.py:20-89,150-186` : Outils | Vérifié — local : taux mémorisés sans dates, page HTML HTTP xe.com et calcul flottant ; aucun service actuel testé | Convertisseur EUR/BYN/RUB extensible, source officielle, historique/cache ; pas de taux de repli fictif | À développer |
| Simulateur de crédit et amortissement | M15 ; `Scripts/Loan/loan.ini:2-8` : Outils | Vérifié — manuel et local | Capital, durée, taux, échéance, profils et amortissement exact | À développer |
| Calculatrice séparée | `Scripts/Calc/calc.ini:2-8` : Outils | Vérifié — local | Outil au même emplacement | À développer |
| Conversions numériques | `Scripts/Numerals/numerals.ini:2-6` : Outils | Vérifié — local | Préciser les conversions proposées avant implémentation | À développer |
| Vérifier une carte bancaire | `Scripts/Luhn/luhn.ini:2-6` : Outils | Vérifié — local | Contrôle mathématique Luhn, pas une validation bancaire | À développer |
| Reçu d'une liste de remboursements | M15 : Outils > Rapports > Reçu ; `Scripts/Receipt/receipt.ini:2-6` : Outils | **À confirmer — sous-menu non prouvé localement** | Impression du récapitulatif et du reste à payer | À développer |
| Notes et chargement automatique | `Scripts/Notes/notes.ini`, `notes_auto.ini` : Outils et déclencheur fichier | Vérifié — local | Notes distinctes des opérations, portée à confirmer | À développer |
| Titres : quantité, ISIN, prix unitaire, cours | M16 ; `Scripts/UpdateStock/update_stock.ini:2-7` : Internet | Vérifié — manuel et local ; source réseau historique non validée | Séparer transactions et valorisations ; ne jamais écraser un prix d'acquisition | À développer |
| Options générales, sauvegarde, affichage, couleurs/police | M17 : Outils > Options ; `globalcolors.ini` | Vérifié — manuel et présence locale | Même regroupement des réglages ; lisibilité moderne | À développer |
| Installation/gestion d'extensions | `Scripts/ManagePlugins/manage_plugins.ini:2-7` : Plugins | Vérifié — local | Inventorier les usages réels ; aucune exécution des anciens scripts ni installation distante implicite | À développer |
| Aide, manuel, mise à jour | M14, M18 ; `Scripts/Manuel/manuel.ini:2-8` : ? ; Internet pour la mise à jour | Vérifié — manuel et local partiellement | Aide locale utilisable sans Internet ; politique de mise à jour consentie à concevoir | À développer |
| Import natif BankPerfect complet | Aucun spécimen de fichier de comptes examiné ; format interne non établi | **À confirmer** | Migration contrôlée ; ne pas promettre un lecteur `.bp` avant preuve | À développer |

**Contraintes de migration découvertes dans les scripts :** XML n'exporte ni UID, ni liens de virements, ni date de pointage, ni champs supplémentaires, ni données d'échéancier/budget. Des notes et profils résident dans des fichiers auxiliaires. Les liens des virements directs diffèrent des branches d'échéancier examinées. Aucune désérialisation `pickle`, évaluation de règle ou exécution d'ancien plugin ne sera admise comme simple lecture d'un fichier importé. Voir les preuves et périmètres précis dans l'audit.

## Ajouts demandés pour Moneta

Ces capacités ne sont **pas attribuées à BankPerfect** sans preuve.

| Besoin | État BankPerfect | Cible Moneta | Statut |
|---|---|---|---|
| EUR, BYN, RUB et autres codes monétaires | Le convertisseur et les monnaies de comptes sont vérifiés ; support BYN/RUB complet non établi | Registre extensible, précision par monnaie et dates de validité | À développer |
| Cours officiels NBRB, unités et dates d'effet | Non établi | Fournisseur officiel, normalisation par échelle, provenance et erreurs visibles | À développer |
| Historique des cours et cache hors ligne | Non établi pour les exigences demandées | Historique conservé, consultation et valorisation hors ligne | À développer |
| Soldes convertis en euros | Totalisation et deux monnaies documentées ; équivalence au besoin non prouvée | Total EUR avec date/source du cours et absence de cours explicite | À développer |
| Transferts entre monnaies différentes | Transferts vérifiés ; garanties multidevises non établies | Deux montants natifs figés, taux effectif, frais et transaction atomique | À développer |
| Restauration et migration financièrement vérifiées | Restauration documentée ; contrôles internes inconnus | Vérifier intégrité, comptes, soldes, pointages, liens et anomalies | À développer |

## Raccourcis à préserver

Source par défaut : M18. « Local » désigne la déclaration INI correspondante dans l'inventaire d'audit. Tous sont **à développer dans Moneta**. La priorité des touches lorsque le curseur est dans une cellule ou une zone de texte reste à relever en situation.

| Touche | Action | Preuve / réserve |
|---|---|---|
| F1 | Aide | M18 |
| Ctrl+F1 | Télécharger le manuel | M18 + local Manuel |
| F2 | Saisie multiple | M18 + local MultipleInput |
| F3 | Champs supplémentaires | M18 + local ExtraValues |
| F4 | Une/deux lignes | M01, M18 |
| F5 | Gestion des tiers | M11, M18 |
| F6 | Ventiler | M18 + local Ventilate |
| F7 | Filtres automatiques | M18 + local Filters |
| Maj+F7 | Annuler les filtrages | M04, M18 |
| F8 | Saisie séparée | M03, M18 |
| F9 | Sauvegarder par e-mail | M18 + local BackupMail ; M17 ambigu |
| Maj+F9 | Sauvegarder par FTP | M06, M18 ; arbitrage de sécurité requis |
| F10 | Charger un tiers | M03, M11, M18 |
| F11 | Afficher les dates de pointage | M09, M18 |
| F12 | Enregistrer sous | M18 |
| Ctrl+B | Budget | M18 + local Budget |
| Ctrl+D | Conversion de devises | M18 + local Currencies |
| Ctrl+E | Échéancier | M18 + local Scheduler |
| Ctrl+F | Calcul automatique des champs | M18 + local Auto Fields ; ne pas le remplacer par recherche |
| Ctrl+G | Gestion des comptes | M02, M18 |
| Ctrl+H | Cacher/afficher les lignes inutiles | M18 ; règle à confirmer |
| Ctrl+L | Simulateur de crédit | M18 + local Loan |
| Ctrl+M | Modifier un groupe | M18 + local ModifyGroup |
| Ctrl+N | Nouveau fichier de comptes | M02, M18 |
| Ctrl+O | Ouvrir | M02, M18 |
| Ctrl+P | Imprimer | M18 |
| Ctrl+R | Remplacer | M18 + local Replace ; menu contradictoire |
| Ctrl+S | Enregistrer | M02, M18 |
| Ctrl+T | Totaux par catégories | M18 + local CategsTotals |
| Ctrl+Alt+T | Totaux par tiers | M18 + local PeopleTotals ; M07 indique aussi Ctrl+T, contradictoire |
| Ctrl+Alt+Q | Importer QIF | Local `Import QIF/import_qif.ini:7` ; absent de M18 |
| Ctrl+Y / Ctrl+Z | Refaire / Annuler | M18 |
| Ctrl+0 | Vider la saisie | M18 ; zéro, pas lettre O |
| Ctrl+Suppr | Supprimer des opérations | M03, M18 |
| Ctrl+Entrée | Ajouter l'opération | M03, M18 |
| Maj+Entrée | Modifier l'opération sélectionnée | M03, M18 |
| Ctrl+Maj+C | Calculatrice | M18 + local Calc |
| Ctrl+Maj+N | Notes | M18 + local Notes |
| Ctrl+Maj+P | Modifier la date de pointage | M18 |
| Ctrl+Maj+T | Transfert de compte à compte | M03, M18 + local Transfer |
| P / Espace | Pointer | M03 ; contexte du tableau, pas de la saisie |
| Entrée dans Montant | Ajouter l'opération | M03 ; contexte à préserver |
| Flèche bas / haut dans Date | Avancer / reculer d'un jour | M03 |
| PgDn / PgUp hors Date | Avancer / reculer la date de saisie | M03 |
| Suppr puis Entrée dans Date | Date du jour | M03 ; ne pas confondre avec suppression d'opération |

## Arbitrages et preuve visuelle attendue

1. Capturer chaque menu déroulé, sous-menu, séparateur, accélérateur Alt et menu contextuel sur **l'installation réellement utilisée**, sans donnée réelle. Comparer ses extensions aux 37 familles de la référence.
2. Résoudre Remplacer, Reçu, raccourci des totaux par tiers, F9/FTP et rétention des sauvegardes avant de figer les menus.
3. Observer sur données fictives le filtrage des soldes, la frontière des lignes inutiles, la ventilation, le rattrapage d'échéances et les virements entre deux devises.
4. Faire valider les écarts imposés par la fiabilité : écritures durables, corrections traçables, absence de réécriture par taux frais, protection des sauvegardes réseau. La fidélité de navigation n'autorise pas une perte de données.
5. Le catalogue public d'extensions n'est pas l'installation locale. Documents, Rapprocher, KDiff, Trader, OFX2, Accueil et Suite budgétaire ne sont pas réputés installés sans leurs fichiers ou une capture. Leurs usages éventuels sont à demander, pas à remplacer silencieusement par une application générique.

## Sources officielles consultées

| ID | Page |
|---|---|
| M01 | [Fenêtre principale](https://chelly.net/bankperfect/wiki/La%20fenetre%20principale%20de%20BankPerfect.html) |
| M02 | [Gestion des comptes](https://chelly.net/bankperfect/wiki/La%20gestion%20des%20comptes.html) |
| M03 | [Opérations de base](https://chelly.net/bankperfect/wiki/Les%20operations%20de%20base.html) |
| M04 | [Filtrage](https://chelly.net/bankperfect/wiki/Le%20filtrage.html) |
| M05 | [Modifier plusieurs opérations](https://chelly.net/bankperfect/wiki/Modifier%20rapidement%20plusieurs%20operations.html) |
| M06 | [Sauvegardes](https://chelly.net/bankperfect/wiki/Les%20sauvegardes.html) |
| M07 | [Rapports](https://chelly.net/bankperfect/wiki/Les%20rapports.html) |
| M08 | [Échéancier](https://chelly.net/bankperfect/wiki/L-echeancier.html) |
| M09 | [Champs supplémentaires](https://chelly.net/bankperfect/wiki/Les%20champs%20supplementaires.html) |
| M10 | [Catégories](https://chelly.net/bankperfect/wiki/La%20gestion%20des%20categories.html) |
| M11 | [Tiers](https://chelly.net/bankperfect/wiki/La%20gestion%20des%20tiers.html) |
| M12 | [Budget](https://chelly.net/bankperfect/wiki/La%20fonction%20Budget.html) |
| M13 | [Import et export](https://chelly.net/bankperfect/wiki/Import-export-donnees.html) |
| M14 | [Introduction](https://chelly.net/bankperfect/wiki/Introduction.html) |
| M15 | [Outils supplémentaires](https://chelly.net/bankperfect/wiki/Outils%20supplementaires.html) |
| M16 | [Fonctions boursières](https://chelly.net/bankperfect/wiki/Les%20fonctions%20boursieres.html) |
| M17 | [Options](https://chelly.net/bankperfect/wiki/Options.html) |
| M18 | [Raccourcis clavier](https://chelly.net/bankperfect/wiki/Recapitulatif%20des%20raccourcis%20clavier.html) |

Index : [manuel officiel](https://chelly.net/bankperfect/wiki/manuel.html). Pages d'âges différents, principalement 2010–2016 ; elles ne constituent pas une capture de la version locale.
