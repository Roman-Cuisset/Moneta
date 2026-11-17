# Fidélité fonctionnelle à BankPerfect

## Portée et légende

La mission 10E ajoute des preuves de reprise, de concurrence et de charge pour l’import EUR V5 qualifié. Les états de pointage et les dates source sont documentés avec les variantes BP8, le registre Moneta et la provenance de l’import. [Qualification avancée](BANKPERFECT_QUALIFICATION_10E.md)

État **mission 8** : cours officiels NBRB/CBR, historique/cache hors ligne, convertisseur indicatif de montant et estimation du solde sélectionné réalisés. Dossiers/comptes, registre, pointage/rapprochements, virements liés et sauvegarde/restauration du graphe complet conservés ; **format financier schéma 5 et Ctrl+S inchangés**. Total multicomptes dans l’interface, taux manuel, import natif `.bp`, budgets, échéancier et installation de mises à jour restent absents. Les preuves BankPerfect sont distinctes des conventions Moneta ; aucune parité parfaite n’est déduite des fonctionnalités livrées.

**Qualification mission 9 :** fonctionnalités et conventions financières conservées ; corrections des onglets clairs, de la petite hauteur du rapprochement et des noms UI Automation. Vérification contrôlée de huit surfaces en deux thèmes/quatre DPI WPF, sans prétendre à quatre configurations matérielles ni à une qualification complète par lecteur d'écran. [Preuves et limites](PERFORMANCE_AUDIT.md). Cette consolidation ne clôt aucune ligne de parité encore absente et ne démontre pas le format natif `.bp`.

**Missions 10A et 10B.** Quatre fixtures produites par BP8 8.0.0.373 et comparées au lecteur Rust BankPerfect 9.13.0 MIT figé à `990f5c7f712dab07c7fefc2921cbf60beee7f6b1` et au lecteur C# diagnostique. Celui-ci conserve les UID, dates, liens et montants sources, dont **0,0001 EUR** non représentable dans la cible. La qualification concerne la lecture de ce corpus. [Recherche](BANKPERFECT_FORMAT_RESEARCH.md) · [Mapping](BANKPERFECT_MAPPING.md) · [Matrice](BANKPERFECT_COMPATIBILITY_MATRIX.md) · [Vérités terrain](BANKPERFECT_FIXTURES.md)

**Mission 10C.** Le parcours natif V5 qualifié permet de créer un dossier Moneta séparé, avec aperçu, veto sur les anomalies bloquantes, provenance intégrée et vérification de sauvegarde/restauration. [Périmètre](BANKPERFECT_COMPATIBILITY_MATRIX.md#périmètre-de-conversion-10c)

**Mission 10D.** La conversion EUR est qualifiée sur une ouverture négative, une date de pointage saisie avant la date comptable et des virements distincts de même montant le même jour. L’audit local lie chaque jambe à son mouvement et à son compte cible. La valorisation des budgets et rapports par cours historiques fait l’objet d’une proposition. [Qualification 10D](BANKPERFECT_COMPATIBILITY_MATRIX.md#extension-qualifiée-en-mission-10d) · [Règles de valorisation proposées](HISTORICAL_FX_VALUATION.md)


- **Vérifié — local** : présence ou déclaration dans les fichiers lisibles de la référence ; ne prouve pas le bon fonctionnement à l'exécution.
- **Vérifié — manuel** : comportement ou chemin explicitement décrit par le manuel officiel ; présence exacte dans l'installation locale encore à confirmer visuellement.
- **À confirmer** : contradiction, absence de preuve ou détail uniquement observable en fonctionnement.
- **À développer** : statut de réalisation dans Moneta, distinct du statut de la preuve BankPerfect.

Les libellés documentaires restent normalisés. La capture privée fournie en mission 4 confirme l'ordre principal **Fichier | Édition | Affichage | Comptes | Opérations | Outils | Plugins | Internet | ?**, pas l'ordre des commandes dans les menus fermés ni leurs séparateurs. Les `Index` INI ne suffisent toujours pas à reconstruire les inconnus.

L'inventaire exhaustif des fichiers et familles d'extensions figure dans [BANKPERFECT_AUDIT.md](BANKPERFECT_AUDIT.md). Les références M01–M18 ci-dessous désignent les pages officielles consultées, recensées en fin de document.

**Décisions utilisateur acquises :** format natif `.moneta` sur SQLite, code indépendant, C#/.NET 10 LTS/WPF/MVVM léger et ressources graphiques **toutes créées spécialement pour Moneta**. La compatibilité concerne les fonctions et les repères, pas la réutilisation de logos/icônes. Aucun logo définitif n'est créé ici.

La [cartographie des commandes](UI_MENU_MAP.md) détaille fonction, fenêtre, contexte clavier, priorité et état documentaire ; l'[inventaire des écrans](UI_SCREEN_INVENTORY.md) définit les preuves visuelles encore nécessaires. La [stratégie d'importation](BANKPERFECT_IMPORT_STRATEGY.md) confronte les données à reprendre aux domaines de cette matrice ; [FILE_FORMATS.md](FILE_FORMATS.md) définit le cycle de vie `.moneta` et [VISUAL_IDENTITY.md](VISUAL_IDENTITY.md) les interdictions graphiques.

## Correspondance fonctionnelle

| Fonction BankPerfect | Emplacement connu / preuve | État de la preuve | Cible Moneta | Réalisation |
|---|---|---|---|---|
| Fichier regroupant plusieurs comptes, plusieurs fichiers possibles | M02 ; fichier `.bp` | Vérifié — manuel | Un dossier financier indépendant **`.moneta` SQLite** avec plusieurs comptes ; conserver la logique ouvrir/enregistrer | Réalisé pour comptes, registre, rapprochements et virements persistants, dossiers isolés ; autres domaines encore absents |
| Nouveau fichier, solde initial | M02 : Comptes > Créer un nouveau fichier de comptes | Vérifié — manuel ; emplacement local à confirmer | Même commande et parcours | Partiel : nouveau dossier vierge nommé, solde initial par compte ajouté ; pas de solde initial global au dialogue de création |
| Ouvrir, enregistrer, enregistrer sous, imprimer | M02, M18 ; Fichier pour ouvrir/enregistrer ; détails des autres chemins à relever | Vérifié — manuel ; arborescence partielle | Mêmes gestes et raccourcis ; **Fichier > Ouvrir** ouvre `.moneta` ou propose la conversion d'un `.bp` ; aucun enregistrement vers `.bp` | Dossiers Moneta et conversion V5 compatible disponibles. Impression à développer. |
| Gestion et changement de compte | M02 : Comptes > Gestion des comptes ; liste sous les menus | Vérifié — manuel | Créer, modifier, supprimer selon règles de sécurité ; changement de compte au même endroit | Partiel : sélection et ajout persistant sans plafond produit ; modification/suppression non proposées, drapeau archivé seulement conservé |
| Informations bancaires, monnaie du compte, RIB | M02 ; `Scripts/Rib/rib.ini:2-6` : Fichier | Vérifié — manuel et local | Métadonnées facultatives, contrôle RIB et impression | À développer |
| Mot de passe d'un fichier | M02 : Comptes > Mot de passe | Vérifié — manuel ; protection cryptographique inconnue | Protection à concevoir, pas de simple imitation d'un verrou non étudié | À développer |
| Agencement principal | M01 et capture privée mission 4 : menus, outils, informations à gauche, filtres, tableau, saisie, état | Vérifié — manuel et disposition visible ; sous-menus non observés | Mêmes repères, identité moderne originale | Partiel : structure conservée, thèmes Light/Dark, actions de saisie ; filtres indisponibles |
| Saisie intégrée ou fenêtre séparée | M03, M17 | Vérifié — manuel | Deux modes, mêmes champs et navigation clavier | Partiel : dialogue opérationnel, ligne compacte de prévisualisation/actions ; saisie intégrée directe future |
| Opération : date, mode, tiers, détail, catégorie, montant | M03 ; champs visibles sur capture privée mission 4 | Vérifié — manuel et présentation | Saisie, modification, suppression explicites et calcul exact | Réalisé pour recettes/dépenses simples, notes/sous-catégories, UUID/révision et solde exact ; suppression logique confirmée, commit Ctrl+S |
| Modes de paiement natifs et personnalisés | M03 : Comptes > Modes de paiements | Vérifié — manuel | Sens débiteur/créditeur et modes personnalisés | Partiel : mode personnalisé à UUID réutilisé ; sens Recette/Dépense explicite, pas déduit du nom du mode ; gestion avancée absente |
| Numérotation des chèques | M03 : numéro dans le détail, progression lors d'une nouvelle saisie | Vérifié — manuel | Reprendre le parcours, stockage structuré sans perdre le détail importé | À développer |
| Calculatrice dans le montant | M03 : expressions et priorité multiplication/division | Vérifié — manuel | Analyse arithmétique sûre en décimal, sans exécution de code | À développer |
| Auto-complétion et chargement d'un tiers | M03, M11 : Opérations > Charger un tiers | Vérifié — manuel | Réutiliser les modèles de tiers et l'historique selon le comportement à relever | À développer |
| Pointage et rapprochement | M03 : contexte, dernière colonne, P/Espace et cycle NP/P/R ; [plugin public Rapprocher](https://chelly.net/bankperfect/wiki/Rapprocher.html), non attesté installé localement | Vérifié — manuel/public ; détails locaux à confirmer | Trois états distincts et relevé contrôlé, solde pointé explicite | Réalisé : NP/P individuel/multiple, totaux natifs, relevé/écart/invariants, R protégées, réouverture avec motif/historique. Écart MON : pas de cycle manuel vers/hors R ; pas de Magic/pause |
| Date de pointage | M09 : Opérations > Date de pointage ; Affichage pour la bascule, Ctrl+Maj+P/F11 | Vérifié — manuel | Date métier distincte, inconnue préservée | Réalisé : date de vérification P modifiable, première colonne basculable ; dates inconnues héritées non fabriquées et nouveaux pointages futurs refusés |
| Annuler/refaire | M17, M18 ; emplacement natif exact non établi | Vérifié — manuel pour les commandes | Annulation contrôlée, compatible avec persistance et historique de corrections | À développer |
| Copier/coller ou déplacer des lignes entre comptes | M05 : Édition > Copier/Coller | Vérifié — manuel | Préserver devises, références et invariants ; règles de déplacement à confirmer | À développer |
| Saisie multiple | `Scripts/MultipleInput/multiple_input.ini:2-7` : Opérations | Vérifié — local | Saisie en série fidèle | À développer |
| Modification groupée | M05 ; `Scripts/ModifyGroup/modifygroup.ini:2-8` : Opérations + contexte | Vérifié — manuel et local | Modification prévisualisée d'une sélection | À développer |
| Remplacer du texte | M05 : Opérations > Remplacer ; `Scripts/Replace/search_replace.ini:2-7` : Édition | **À confirmer — chemins contradictoires** | Ne pas choisir le menu définitif sans capture locale | À développer |
| Ventilation par catégories | M15 ; `Scripts/Ventilate/ventilate.ini:2-6` : Opérations + contexte | Vérifié — manuel et local ; détails de regroupement à confirmer | Ventilation équilibrée, total inchangé ; vue fidèle malgré modèle interne plus sûr | À développer |
| Virement entre comptes et navigation entre ses lignes | M03 ; `Scripts/Transfer/transfer.ini:2-9` : Opérations + barre d'outils | Vérifié — manuel et local | Deux jambes liées et atomiques, navigation réciproque | **Réalisé 7C** : Opérations → Virement et outil existant, Ctrl+Maj+T dans le registre ; navigation UUID hors page comprise, correction/suppression d’ensemble, frais optionnels. Commandes liées explicites et protections Moneta ; détails BP non observés non déclarés identiques |
| Convertir une ligne en transfert | `Scripts/Transfer/convert_line_to_transfer.ini:2-8` : Opérations + contexte | Vérifié — local | Conversion atomique avec contrôle des lignes déjà existantes | À développer |
| Filtres par colonne, date, texte et montant | M04 : ligne d'en-tête du relevé | Vérifié — manuel | Reprendre la grammaire documentée, pas seulement une recherche générique | À développer |
| Filtres enregistrés et périodes dynamiques | M04 ; `Scripts/Filters/setfilters.ini:2-9` : Affichage + barre d'outils déroulante | Vérifié — manuel et local | Périodes glissantes, application et réinitialisation rapides | À développer |
| Cacher les lignes inutiles | M03/M04 : Affichage ; critères décrits différemment selon les pages | **À confirmer — règle de frontière** | Masquer sans supprimer ; valider la règle exacte avec jeu synthétique | À développer |
| Solde tenant compte ou non des filtres | M04, M17 : Outils > Options | Vérifié — manuel | Différencier clairement solde du compte et total filtré ; fidélité du mode à valider | À développer |
| Affichage une/deux lignes ; une/deux/trois colonnes de montant | M01, M17 ; double niveau et montants séparés visibles sur capture privée mission 4 | Vérifié — manuel et disposition visible | Même densité et choix de présentation | Partiel : deux niveaux et débit/crédit fixes ; choix de présentation à développer |
| Gestion des catégories et sous-catégories | M10 : Comptes > Gestion des catégories | Vérifié — manuel | Ordre, renommage, déplacement, tri et conversion de niveau | Partiel : création/réutilisation à la saisie, racine + sous-catégorie et références persistantes ; éditeur avancé à développer |
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
| Total des comptes | `Scripts/Totals/totals.ini:2-6` : Comptes | Vérifié — local | Totalisation, sans addition de monnaies différentes sans conversion | **Interface à développer** : API d’agrégat exact `ConvertTotal` disponible en mission 8, arrondi final unique et refus du total entier si une cotation manque ; aucun écran Total des comptes livré |
| Relevé multi-comptes | `Scripts/AllAccountsView/all_accounts_view.ini:2-7` : Comptes | Vérifié — local | Vue transversale et filtre des comptes | À développer |
| Comptes virtuels et désactivation de compte | XML local `Scripts/Export XML/export_xml.py:27-30` ; `history.txt:12-14` ; `Scripts/Totals/totals.py:59-74` | Vérifié — local ; dialogue et effets complets à confirmer | Distinguer compte virtuel, compte désactivé et suppression ; reproduire les règles de totalisation après observation | À développer |
| Import CSV, QIF, OFX | M13 ; INI des dossiers `Import CSV`, `Import QIF`, `Import OFX` | Vérifié — manuel et local ; OFX2 public distinct | Prévisualisation, correspondances, anomalies et arbitrage des doublons | À développer |
| Export CSV, QIF, HTML, XML et tableur | M13 : Fichier > Exporter ; `Scripts/Export XML/export_xml.py:22-40`, `Scripts/Export CSV/export_csv.py:161-186` et INI | Vérifié — local : **XML tous comptes/toutes lignes ; CSV mode courant ou tous comptes** ; M13 généralise à tort | Périmètre explicite par format, contrôle de couverture ; tableur SYLK sous extension XLS, pas BIFF/XLSX | À développer |
| Sauvegardes locales et externes | M06 ; Fichier > Enregistrer sur disque externe | Vérifié — manuel ; rétention locale dépendante de version | Sauvegardes automatiques vérifiables et destination externe | Manuel réalisé via API SQLite avec opérations/références/tombstones ; automatisation/rétention et supports externes à qualifier |
| Liste et restauration de sauvegardes | M06 ; `Scripts/Backups/backups.ini:2-6` : Comptes | Vérifié — manuel et local | Vérifier avant restauration, préserver l’état actuel | Restauration manuelle vers nouvelle copie rouverte/comparée réalisée ; catalogue/liste de sauvegardes futur |
| Sauvegarde par courriel et FTP | M06 ; `Scripts/BackupMail/backup_mail.ini:2-8` : Internet pour le courriel | Vérifié — manuel et déclaration locale ; libellés contradictoires | Parité à arbitrer : pas de FTP clair ni de données envoyées sans consentement/protection | À développer |
| Deux monnaies, bascule d'affichage | M15 ; boutons de barre d'outils | Vérifié — manuel | Conserver les repères tout en distinguant monnaie comptable et valorisation | **Réalisé mission 8 selon convention Moneta** : sélecteur Devise d’affichage près du solde, natif réel + équivalent estimé, source/effet/récupération/cache ; pas de réécriture des lignes ni identité d’interface BP revendiquée |
| Inverser les monnaies avec conversion de toutes les lignes | M15 : Outils > Inverser les monnaies | Vérifié — manuel | **Arbitrage requis** : ne pas reproduire une réécriture rétroactive silencieuse | À développer |
| Convertisseur de devises | M15 ; `Scripts/Currencies/currencies.ini:2-7`, `currencies.py:20-89,150-186` : Outils | Vérifié — local : taux mémorisés sans dates, page HTML HTTP xe.com et calcul flottant ; aucun service actuel testé | Convertisseur EUR/BYN/RUB extensible, source officielle, historique/cache ; pas de taux de repli fictif | **Réalisé mission 8** : Outils → Cours de change, montant français et devises, cotations officielles NBRB/CBR avec nominaux ; arrondi final au pair, résultat indicatif. Taux manuel non réalisé, Ctrl+D historique non activé |
| Simulateur de crédit et amortissement | M15 ; `Scripts/Loan/loan.ini:2-8` : Outils | Vérifié — manuel et local | Capital, durée, taux, échéance, profils et amortissement exact | À développer |
| Calculatrice séparée | `Scripts/Calc/calc.ini:2-8` : Outils | Vérifié — local | Outil au même emplacement | À développer |
| Conversions numériques | `Scripts/Numerals/numerals.ini:2-6` : Outils | Vérifié — local | Préciser les conversions proposées avant implémentation | À développer |
| Vérifier une carte bancaire | `Scripts/Luhn/luhn.ini:2-6` : Outils | Vérifié — local | Contrôle mathématique Luhn, pas une validation bancaire | À développer |
| Reçu d'une liste de remboursements | M15 : Outils > Rapports > Reçu ; `Scripts/Receipt/receipt.ini:2-6` : Outils | **À confirmer — sous-menu non prouvé localement** | Impression du récapitulatif et du reste à payer | À développer |
| Notes et chargement automatique | `Scripts/Notes/notes.ini`, `notes_auto.ini` : Outils et déclencheur fichier | Vérifié — local | Notes distinctes des opérations, portée à confirmer | À développer |
| Titres : quantité, ISIN, prix unitaire, cours | M16 ; `Scripts/UpdateStock/update_stock.ini:2-7` : Internet | Vérifié — manuel et local ; source réseau historique non validée | Séparer transactions et valorisations ; ne jamais écraser un prix d'acquisition | À développer |
| Options générales, sauvegarde, affichage, couleurs/police | M17 : Outils > Options ; `globalcolors.ini` | Vérifié — manuel et présence locale | Même regroupement des réglages ; lisibilité moderne | Partiel : cinq rubriques Moneta, préférences locales/thèmes et **Devises et taux** opérationnels ; mise à jour = préférence seule, sauvegardes manuelles ; autres options BP non couvertes |
| Installation/gestion d'extensions | `Scripts/ManagePlugins/manage_plugins.ini:2-7` : Plugins | Vérifié — local | Inventorier les usages réels ; aucune exécution des anciens scripts ni installation distante implicite | À développer |
| Aide, manuel, mise à jour | M14, M18 ; `Scripts/Manuel/manuel.ini:2-8` : ? ; Internet pour la mise à jour | Vérifié — manuel et local partiellement | Aide locale utilisable sans Internet ; politique de mise à jour consentie à concevoir | À développer |
| Ouverture et conversion natives BankPerfect | Lecteurs Rust figé et C# comparés sur quatre fichiers BP8 natifs, [résultats 10B](BANKPERFECT_FIXTURES.md#qualification-c--mission-10b) | **Diagnostic V5 sur F00..F03** | **P0** : consentement, lecture seule, mapping et nouveau `.moneta` vérifié | Conversion 10C des cas compatibles. BP-F03 est bloqué pour précision EUR. |

**Contraintes de migration découvertes dans les scripts :** XML n'exporte ni UID, ni liens de virements, ni date de pointage, ni champs supplémentaires, ni données d'échéancier/budget. Des notes et profils résident dans des fichiers auxiliaires. Les liens des virements directs diffèrent des branches d'échéancier examinées. Aucune désérialisation `pickle`, évaluation de règle ou exécution d'ancien plugin ne sera admise comme simple lecture d'un fichier importé. Voir les preuves et périmètres précis dans l'audit.

## Conventions de mission 7B à valider avec l’utilisateur

- Manuel et fichiers lisibles confirment les trois états, pointage de sélection, clic dernière colonne, P/Espace et date de pointage. `history.txt:305,317,533` signale verrou R, avertissement au dépointage et Ctrl+Maj+P ; `Scripts/Totals/totals.py:58-69` distingue états >0 et non pointés. Ce sont des preuves documentaires, pas une exécution du binaire.
- Le plugin public décrit relevé daté/numéroté, solde final et sélection à écart nul ; sa présence locale et ses règles de correction ne sont pas établies. Moneta ne copie pas son code et ne revendique ni Magic, ni pause/sauvegarde de session inachevée.
- R Moneta nouveau = relevé validé et conservé ; réouverture du dernier actif avec motif. P corrigée conserve date/état avec confirmation financière. Ouverture incluse une fois dans le solde pointé, mouvements futurs inclus dans actuel mais nouveau pointage futur refusé. Ces règles BP exactes restent à observer ; conventions Moneta explicites, pas compatibilité parfaite annoncée.
- Premier relevé part de l’ouverture du compte ; suivants de la dernière clôture active. Des anciens non rapprochés peuvent être candidats. Un R hérité sans session reste non attesté ; libération motivée auditable, pas de relevé inventé.
- Brouillon de validation/réouverture/libération à enregistrer avant autre mutation du registre. F11/Ctrl+Maj+P/P/Espace conservés ; Maj+P et Ctrl+Maj+R sont des choix Moneta, non des raccourcis historiques prétendument vérifiés.

## Conventions de virements livrées en mission 7C

- Deux comptes actifs distincts du même dossier ; principal strictement positif en devise native exacte, égalité hors frais si même devise, deux montants réels libres sinon. Magnitudes stockées avec sens séparé ; impact −débit/+crédit/−frais, sans valorisation d’affichage ni somme implicite de monnaies différentes.
- Rapport exact **crédit/débit principal**, hors frais, reçu pour **1 unité source**, conservé par les deux montants. Le quotient décimal n’est qu’un aperçu **approximatif**, omis s’il déborde ou devient zéro. Date du change et source déclarée/non vérifiée, défaut « Montants saisis par l’utilisateur » ; aucun taux officiel ou conversion imposée.
- Frais séparés sur source/cible/troisième compte actif dans sa devise native. Premier ajout positif ; frais déjà ajouté, brouillon compris, ramenable à zéro en conservant UUID/compte/lien. Comptes principaux fixes dès création, compte des frais fixe dès ajout ; correction de compte par suppression entière puis recréation, pas de remplacement de jambe.
- Pointage indépendant pour chaque jambe ; une seule R, frais compris et hors écran, rend l’ensemble consultable seulement et interdit toute correction/suppression financière. Réouverture/libération puis Ctrl+S obligatoire. Correction P conservant état/date avec confirmation ; aucune mutation financière isolée d’une jambe, même par l’API de stockage.
- Tout reste brouillon jusqu’au Save manuel, y compris sur dossier neuf ; SaveAs, concurrence, révisions et transaction couvrent l’ensemble. Suppression avant premier Save retire les brouillons ; suppression persistée conserve toutes les traces/liens ; frais nouveaux non enregistrés abandonnés si le principal persistant est supprimé.
- Schéma 5 additif, `Transaction` et instantanés V1 historiques inchangés ; migrations 1..4 sur copie et certificats originaux conservés. Digest/restauration couvrent entités, liens, frais, tombstones et historique, pas seulement des soldes égaux.
- **Ouvrir le virement lié… / Autre côté du virement / Supprimer le virement entier…** sont des commandes Moneta, dans Opérations/contexte ; bouton ↔ dans la ligne. Débit → crédit ; crédit/frais → débit, page de 100 chargée et sélectionnée par UUID. Ni nouveaux emplacements BP prétendus observés, ni transfert inter-dossiers, ni conversion d’une opération ordinaire en virement.
- **Preuves historiques 7C** : parcours Windows synthétique clair/sombre avec création/correction/frais, P/R indépendant, consultation/refus R, fermeture/réouverture, sauvegarde à chaud puis restauration ouverte ; 329 tests Debug/Release et mesures datées dans le README. Ces chiffres et parcours ne constituent pas la qualification de mission 8.
- La provenance BP reste prospective : UUID stables pour une future correspondance démontrée, sans faux identifiant source, faux jeu BP ou import annoncé. Les cours officiels sont désormais séparément livrés en mission 8 ; conversion de ligne et autres parités absentes restent explicitement à développer.


## Conventions de cours et valorisation — mission 8

- **Outils → Cours de change…** ouvre table officielle, choix de fournisseur/date, consultation du cache, actualisation/annulation, historique et convertisseur de montant. **Outils → Actualiser les cours officiels** reste actif. **Outils → Options → Devises et taux** règle source persistée et actualisation au prochain démarrage.
- NBRB par défaut (référence BYN), CBR explicitement sélectionné (référence RUB). Changer de banque consulte seulement son cache : **pas de téléchargement implicite, mélange ni repli automatique**. Le démarrage ne fait aucune requête de cours par défaut ; l’option automatique, désactivée initialement, ne rend pas Internet obligatoire pour les comptes.
- Date demandée distincte de l’effet réel et de la récupération. Dernier cours d’effet ≤ date, ancien d’au plus 31 jours calendaires ; sinon **Cours indisponible**, sans estimation fictive. Publications futures conservées sans emploi avant effet, historique non purgé, 100 publications récentes affichées et archives plus anciennes consultables par date.
- Cache du jour demandé récupéré depuis ≤ 6 h ou cache historique applicable réutilisés par l’automatique ; actualisation manuelle forcée. Publication absente : jusqu’à sept dates antérieures (huit au total), délai global 20 s et 10 s par requête ; pas de retry HTTP/réseau. Annulation et choix plus récent empêchent une réponse obsolète de reprendre la main.
- Le solde natif demeure réel et indiqué séparément de l’estimation ; montants, opérations, pointage/rapprochements et état modifié inchangés. Choix de présentation = devises financières du dossier + EUR/BYN/RUB, sans enrichissement du catalogue financier. Conversion exacte avec nominaux puis arrondi final au pair ; API d’agrégat disponible, **pas de total multicomptes dans l’interface**.
- La saisie d’un **montant** à convertir n’est ni la saisie d’un **taux manuel**, ni une proposition appliquée à un virement. Les deux montants réels, frais, rapport effectif et source déclarée du virement restent figés.
- Cache public SQLite indépendant du `.moneta` schéma 5 ; erreur de persistance visible, cours validés conservés en mémoire sans promesse de survie au redémarrage. HTTPS fixe sans données financières, sans redirections/cookies ; limites de réponse et XML sans DTD/résolution externe. SHA-256 du contenu, **pas de signature officielle du flux** ; aucune télémétrie ni installation de mise à jour.
- **Preuves courantes bornées :** Debug/Release sans erreur/avertissement, **516 tests réussis par configuration**, 0 échec/ignoré, dont les 329 cas antérieurs ; clients .NET avec HTTP 200 NBRB/CBR pour 03/04 et 03/11/2026, XML Windows-1251. Parcours Windows clair/sombre, source persistée, natif/estimations, cache après redémarrage, réseau inaccessible puis rétabli, clavier/annulation pendant une connexion réellement retenue et CBR 05/04 → effet 04/04. Anciennes références refusées : NBRB avant 01/07/2016, CBR avant 01/01/1998, sans conversion automatique. SHA-256 financier entier inchangé après les conversions. [Documentation XML CBR](https://www.cbr.ru/development/SXML/) consultée ; [aide NBRB](https://www.nbrb.by/apihelp/exrates) en timeout de lecture, donc non réputée lue. Aucun quota/disponibilité/contrat exhaustif déduit ; mesures et limites visuelles détaillées dans le README.

## Ajouts demandés pour Moneta

Ces capacités ne sont **pas attribuées à BankPerfect** sans preuve.

| Besoin | État BankPerfect | Cible Moneta | Statut |
|---|---|---|---|
| EUR, BYN, RUB et autres codes monétaires | Le convertisseur et les monnaies de comptes sont vérifiés ; support BYN/RUB complet non établi | Registre extensible, précision par monnaie et dates de validité | Registre/précision et comptes natifs réalisés ; conversion indicative mission 8 pour les devises de présentation disponibles/cotées ; historique des périodes monétaires non réalisé |
| Cours officiels NBRB et CBR, unités et dates d’effet | Non établi dans BankPerfect ; contrats tiers étudiés dans ARCHITECTURE | NBRB prioritaire ; CBR alternative explicite, échelles variables, aucune substitution ou combinaison cachée | **Réalisé mission 8** pour les flux quotidiens : nominaux/date/source, cache indépendant et client sécurisé ; réserve documentaire NBRB/conditions complètes maintenue |
| Historique, cache hors ligne et taux manuel | Non établi pour les exigences demandées | Cours datés/sourcés, date demandée distincte de l’effet ; dernier antérieur signalé ; manuel étiqueté utilisateur | **Historique/cache réalisés mission 8**, ≤ 31 jours pour valorisation, futur exclu avant effet, mémoire non persistée distinguée ; **taux manuel à développer** |
| Soldes convertis en euros | Totalisation et deux monnaies documentées ; équivalence au besoin non prouvée | Total EUR avec date/source du cours et absence de cours explicite | **Solde du compte sélectionné réalisé mission 8**, natif + estimation et provenance ; API d’agrégat exacte disponible, **total multicomptes dans l’interface à développer** |
| Transferts entre monnaies différentes | Transferts vérifiés ; garanties multidevises non établies | Deux montants natifs figés, rapport effectif, frais et transaction atomique | **Réalisé 7C et conservé mission 8**, source déclarée non vérifiée, frais séparés et rapport exact crédit/débit ; aucune suggestion officielle ni application automatique de change |
| Restauration et migration financièrement vérifiées | Restauration documentée ; contrôles internes inconnus | Vérifier intégrité, comptes, soldes, pointages, liens et anomalies | Réalisé sur `.moneta` connu 1..5 : graphe complet et historique, certificats 1..4 conservés ; aucune compatibilité `.bp` déduite |
| Format indépendant `.moneta` et association Windows | Sans équivalent BankPerfect revendiqué | SQLite versionné ; identité vérifiée au-delà de l'extension ; association `.moneta` consentie, jamais prise de contrôle de `.bp` | Format schéma 5 réalisé ; association Windows non réalisée |
| Identité visuelle originale | Les ressources BankPerfect restent en référence uniquement | Aucune réutilisation de logo, icône, image, illustration ou ressource extraite ; navigation fidèle mais graphisme original à valider séparément | Ressources Moneta originales et thèmes réalisés ; identité définitive et qualification visuelle complète non acquises |

## Raccourcis à préserver

Source par défaut : M18. « Local » désigne la déclaration INI correspondante dans l'inventaire d'audit. La table conserve la preuve historique, pas une promesse que toutes ces touches fonctionnent dans Moneta. Sont notamment opérationnels Ctrl+N/O/S, F12, Ctrl+Entrée/Maj+Entrée, Suppr/Ctrl+Suppr, P/Espace, Ctrl+Maj+P, F11 et **Ctrl+Maj+T** ; les gestes de registre restent limités à son DataGrid, sans interception des champs texte. État détaillé dans UI_MENU_MAP.

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
| Ctrl+Maj+T | Transfert de compte à compte | M03, M18 + local Transfer ; **réalisé Moneta 7C, uniquement dans le registre** |
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
