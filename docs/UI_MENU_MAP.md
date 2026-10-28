# Cartographie des commandes et gestes

## Contrat documentaire

État au 28 octobre 2026. Analyse uniquement : aucune interface Moneta, aucun lecteur `.bp`, aucune action décrite ici n'est implémentée ou exécutée. Ce document étend [l'audit existant](BANKPERFECT_AUDIT.md) et [la parité](FUNCTIONAL_PARITY.md), sans recommencer l'audit. Les identifiants `CMD-001` à `CMD-148` sont stables ; ils identifient des intentions de commande, des actions locales ou des déclencheurs, **pas 148 entrées de menus**. Les écrans `SCREEN-xxx` et les captures attendues sont définis dans [UI_SCREEN_INVENTORY.md](UI_SCREEN_INVENTORY.md). L'identité graphique indépendante relève de [VISUAL_IDENTITY.md](VISUAL_IDENTITY.md).

### Lecture des tables

- **Mxx** : chapitre officiel lié dans [les sources de la parité](FUNCTIONAL_PARITY.md#sources-officielles-consultées). **I(famille/fichier)** : déclaration INI déjà recensée dans l'audit, sous `Scripts/` ; le suffixe `.ini` est implicite. **S** : comportement du script lisible déjà analysé par l'audit. **?** : emplacement, détail ou contradiction non résolu. Aucune de ces preuves n'est une observation de fonctionnement.
- **MON** : décision/proposition Moneta, jamais attribuée à BankPerfect. Les décisions de format et de parcours d'ouverture sont acquises ; les nouveaux libellés, sous-écrans et leur disposition restent des propositions.
- Priorités **proposées** : **P0** = dossier, relevé, saisie, intégrité, conversion native prioritaire ; **P1** = parité métier et outils usuels ; **P2** = compléments ou intégrations nécessitant arbitrage. Ce classement n'autorise ni abandon de fonction ni substitution CSV à `.bp`.
- `—` dans la colonne touche = aucun raccourci établi, et non preuve d'absence. Les noms sont fonctionnels et normalisés avec accents ; ce ne sont pas des transcriptions garanties de chaque libellé affiché.
- Un accès indiqué « bouton », « tableau », « saisie » ou « auto » n'est pas un menu. Les écrans cités sont des surfaces documentaires : leur séparation en fenêtres réelles reste à confirmer.

**Inconnus globaux :** ordre gauche-droite des menus, ordre complet des entrées, séparateurs, accélérateurs Alt, focus initial, Tab/Shift+Tab, états grisés, priorité des raccourcis selon le focus. Les `Index` INI ne permettent pas de les reconstruire. Aucun ordre de table ci-dessous ne vaut ordre d'interface. La capture officielle de la fenêtre principale est indisponible (404, audit mission 1).

## Fichier, échanges et ouverture native Moneta

| ID | Commande / fonction | Emplacement établi ou inconnu | Touche | Écran(s) | Comportement et limite | Priorité | Source / état |
|---|---|---|---|---|---|---|---|
| CMD-001 | Créer un nouveau fichier de comptes | **Comptes** > Créer un nouveau fichier de comptes, pas Fichier d'après M02 | Ctrl+N | SCREEN-004, SCREEN-003 | BP demande nom et solde initial ; Moneta crée un dossier natif `.moneta`, pas un `.bp` | P0 | M02/M18 ; adaptation MON |
| CMD-002 | Ouvrir | Fichier > Ouvrir | Ctrl+O | SCREEN-004, SCREEN-050 | Même entrée Moneta pour `.moneta` et `.bp` ; branchement détaillé ci-dessous | P0 | M02/M18 ; décision MON |
| CMD-003 | Enregistrer | Fichier > Enregistrer | Ctrl+S | SCREEN-001, SCREEN-004 | BP sauvegarde et produit une sauvegarde locale selon options ; durabilité Moneta à concevoir sur SQLite direct | P0 | M02/M06/M18 |
| CMD-004 | Enregistrer sous | Groupe exact à relever ; Fichier non garanti par M18 seul | F12 | SCREEN-004 | Nouvelle destination ; ne pas écraser un original BP | P0 | M18 ; MON pour protection BP |
| CMD-005 | Imprimer | Menu exact inconnu | Ctrl+P | SCREEN-054 | Périmètre et dialogue natif à capturer, ne pas assimiler à l'export | P1 | M18 ; ? |
| CMD-006 | Fermer le dossier courant | Emplacement inconnu | — | SCREEN-004 | M02 décrit proposition d'enregistrer les modifications ou option automatique ; libellé exact inconnu | P0 | M02 ; ? |
| CMD-007 | Quitter | Emplacement inconnu | — | SCREEN-004 | Même demande de sauvegarde documentée ; politique native Moneta à préciser | P0 | M02 ; ? |
| CMD-008 | Imprimer un RIB | Fichier | — | SCREEN-006 | Vérifier clé et titulaire ; M02 décrit page PDF de trois RIB ; données exclusivement fictives en capture | P1 | M02 ; I(Rib/rib) |
| CMD-009 | Enregistrer sur disque externe | Fichier | — | SCREEN-041 | Destination externe selon options ; ne pas confondre copie et conversion | P0 | M06/M17 |
| CMD-010 | Exporter CSV | Fichier > Exporter | — | SCREEN-035 | Options et compte courant/visible **ou tous comptes/toutes lignes** ; un fichier par compte dans ce dernier mode | P1 | M13 ; I(Export CSV/export_csv), S audit §5 |
| CMD-011 | Exporter QIF | Fichier > Exporter | — | SCREEN-036 | Compte courant, lignes visibles ; pertes et précision signalées dans l'audit | P1 | M13 ; I(Export QIF/export_qif), S |
| CMD-012 | Exporter HTML | Fichier > Exporter | — | SCREEN-037 | Compte courant, lignes visibles ; présentation, pas sauvegarde complète | P1 | M13 ; I(Export HTML/export_html), S |
| CMD-013 | Exporter XML | Fichier > Exporter | — | SCREEN-038 | **Tous comptes/toutes opérations**, mais sans UID/liens/dates de pointage/champs supplémentaires/échéances/budgets | P1 | M13 corrigé par I(Export XML/export_xml), S |
| CMD-014 | Exporter tableur | Groupe Exporter ; parent Fichier M13 | — | SCREEN-039 | SYLK sous nom `.xls`, pas BIFF/XLSX ; compte courant, lignes visibles | P1 | I(Export XLS/export_xls), S |
| CMD-015 | Importer CSV | Fichier > Importer | — | SCREEN-031, SCREEN-034 | Dates, plage de lignes, colonnes, doublons et rapport ; pas équivalent à la lecture native BP | P1 | M13 ; I(Import CSV/import_csv) |
| CMD-016 | Importer QIF | Fichier > Importer | Ctrl+Alt+Q | SCREEN-032, SCREEN-034 | Correspondances par lettres, dates, doublons, rapport ; raccourci local absent de M18 | P1 | M13 ; I(Import QIF/import_qif) |
| CMD-017 | Importer OFX | Groupe Importer ; parent Fichier à confirmer pour ce plugin local | — | SCREEN-033, SCREEN-034 | Comptes et modes ; ne pas attribuer OFX2 du catalogue à cette installation | P1 | I(Import OFX/import_ofx) ; S |
| CMD-018 | Proposer conversion `.bp` | **Dans Fichier > Ouvrir**, aucune nouvelle entrée obligatoire | — | SCREEN-050 | Expliquer création d'un **nouveau** `.moneta` ; original inchangé et jamais supprimé ; annulation possible | P0 | MON, décision utilisateur |
| CMD-019 | Examiner l'aperçu de conversion | Bouton/étape de SCREEN-050 proposé, place provisoire | — | SCREEN-050 | Inventaire reconnu, comptes, totaux de contrôle et limites ; couverture complète/partielle ou blocage clairement distingués ; aucune écriture financière avant accord | P0 | MON, proposition ; schéma BP inconnu |
| CMD-020 | Confirmer la conversion native | Bouton/étape de SCREEN-050 proposé | — | SCREEN-050, SCREEN-051 | Destination nouvelle, contrôles et création native selon architecture ; refus explicite des données non interprétables, pas de fausse réussite | P0 | MON ; non implémenté |
| CMD-021 | Consulter le rapport de conversion | Depuis fin/échec de SCREEN-050 ; réouverture future à placer, pas menu BP | — | SCREEN-051 | États **complet / partiel / bloqué** distincts ; couverture et contrôles. Dans le parcours natif, seul un résultat complet vérifié et publié peut être ouvert ; un diagnostic partiel ne produit pas une fausse réussite | P0 | MON, proposition |

### Contrat d'ouverture `.moneta` / `.bp`

`CMD-002` conserve son accès **Fichier > Ouvrir / Ctrl+O**. Le sélecteur distingue les formats :

1. `.moneta` : vérifier identité, version et intégrité du SQLite natif indépendant puis ouvrir une version compatible. Une migration d'ancien schéma relève du consentement et des contrôles de FILE_FORMATS, pas de la conversion BP. Ne pas déplacer le dossier dans un stockage opaque imposé.
2. `.bp` : proposer `CMD-018`, avec choix **annuler**, **examiner l'aperçu**, puis **confirmer vers un nouveau `.moneta`**. Après traitement, proposer **consulter le rapport** et **ouvrir le nouveau fichier**. Aucun choix « modifier le BP », suppression ou réenregistrement sous le même original. Le dossier de destination peut être choisi ; aucune destination silencieuse imposée.
3. Le schéma BP reste inconnu, aucun lecteur n'existe et aucun spécimen fictif n'a été fourni. Ce parcours est une exigence à développer, pas une capacité disponible. En cas de format non reconnu, annoncer le blocage ; ne pas renommer un BP en SQLite, ni lancer BP, charger ses plugins, désérialiser un pickle ou évaluer ses règles.
4. CSV/QIF/OFX restent des imports distincts avec leur propre couverture ; ils ne remplacent pas la priorité native `.bp`. Aucun rapport ne prétendra à une migration complète sans preuve pour chaque domaine.

## Comptes et référentiels

| ID | Commande / fonction | Emplacement établi ou inconnu | Touche | Écran(s) | Comportement et limite | Priorité | Source / état |
|---|---|---|---|---|---|---|---|
| CMD-022 | Gestion des comptes | Comptes | Ctrl+G | SCREEN-003 | Liste, création, modification, suppression ; monnaies, métadonnées facultatives | P0 | M02/M18 |
| CMD-023 | Changer de compte | Liste sous les menus, pas menu autonome établi | — | SCREEN-001 | Afficher le relevé choisi ; propagation de sélection/filtres à observer | P0 | M01/M02 |
| CMD-024 | Créer/modifier/supprimer un compte | Boutons de la gestion des comptes | — | SCREEN-003 | M02 documente les actions ; virtuel/désactivé distincts de suppression, interface exacte inconnue | P0 | M02 ; S audit §3/5 |
| CMD-025 | Mot de passe | Comptes | — | SCREEN-005 | M02 décrit saisie en clair sans confirmation ; **pas une spécification de sécurité Moneta**, mécanisme BP inconnu | P1 | M02 ; arbitrage MON |
| CMD-026 | Gestion des catégories | Comptes | — | SCREEN-007 | Catégories et sous-catégories ordonnées | P0 | M10 |
| CMD-027 | Insérer catégorie / sous-catégorie | Boutons de SCREEN-007 | — | SCREEN-007 | Avant sélection ou en fin de liste | P0 | M10 |
| CMD-028 | Renommer catégorie | Bouton de SCREEN-007 | — | SCREEN-007 | Ne pas confondre renommage du référentiel et remplacement dans les opérations | P0 | M10 |
| CMD-029 | Supprimer catégorie / sous-catégorie | Bouton de SCREEN-007 | — | SCREEN-007 | Première sous-catégorie promue, suivantes conservées ; références à contrôler | P0 | M10 |
| CMD-030 | Monter / descendre une catégorie | Boutons de SCREEN-007 | — | SCREEN-007 | Option de déplacement avec sous-catégories | P1 | M10 |
| CMD-031 | Changer catégorie en sous-catégorie ou inversement | Bouton de SCREEN-007 | — | SCREEN-007 | Effet sur références à relever | P1 | M10 |
| CMD-032 | Trier les catégories | Bouton de SCREEN-007 | — | SCREEN-007 | Tri alphabétique immédiat documenté | P1 | M10 |
| CMD-033 | Importer catégories texte | Bouton de SCREEN-007, pas Fichier > Importer établi | — | SCREEN-007, SCREEN-004 | M10 refuse une liste plus courte que la liste courante | P1 | M10 |
| CMD-034 | Exporter catégories texte | Bouton de SCREEN-007 | — | SCREEN-007, SCREEN-004 | Liste `.txt`, pas export d'opérations | P1 | M10 |
| CMD-035 | Gestion des tiers | Comptes | F5 | SCREEN-008 | Modèles d'opérations, liste et champs | P0 | M11/M18 |
| CMD-036 | Ajouter / modifier / supprimer un tiers | Boutons/champs de SCREEN-008 | — | SCREEN-008 | Modification directe des champs ; pas simple carnet d'adresses | P0 | M11 |
| CMD-037 | Charger depuis la liste des tiers | Recherche puis validation dans SCREEN-008 | Entrée dans ce contexte | SCREEN-008, SCREEN-002 | Charge la saisie sans prouver une insertion automatique | P0 | M11 |
| CMD-038 | Modes de paiements | Comptes | — | SCREEN-009 | Liste native et modes personnalisés | P0 | M03 |
| CMD-039 | Ajouter / modifier / supprimer un mode | Boutons/champs de SCREEN-009 | — | SCREEN-009 | Nom et mode créditeur ; modes natifs non supprimables | P0 | M03 |
| CMD-040 | Total des comptes | Comptes | — | SCREEN-029 | Règles virtuel/désactivé à relever ; ne pas sommer des monnaies sans valorisation explicite Moneta | P1 | I(Totals/totals), S |
| CMD-041 | Relevé multi-comptes | Comptes | — | SCREEN-030 | Choix de comptes et vue transversale ; unité des totaux BP inconnue | P1 | I(AllAccountsView/all_accounts_view), S |

## Tableau, saisie, pointage et édition

| ID | Commande / fonction | Emplacement établi ou inconnu | Touche / geste | Écran(s) | Comportement et limite | Priorité | Source / état |
|---|---|---|---|---|---|---|---|
| CMD-042 | Ajouter l'opération | Bouton de saisie ; menu inconnu | Ctrl+Entrée ; Entrée **dans Montant** | SCREEN-001, SCREEN-002 | Valide date/mode/tiers/détail/catégorie/montant ; sens porté par mode BP | P0 | M03/M18 |
| CMD-043 | Charger une ligne dans la saisie | Tableau | Clic ; double-clic si saisie séparée | SCREEN-001, SCREEN-002 | Clic charge la ligne en intégré ; double-clic ouvre modification en séparé | P0 | M03 |
| CMD-044 | Modifier l'opération sélectionnée | Saisie ; clic droit sur bouton de validation en intégré, bouton en séparé | Maj+Entrée | SCREEN-001, SCREEN-002 | Modifie plutôt qu'ajoute ; vérifier état de sélection | P0 | M03/M18 |
| CMD-045 | Supprimer des opérations | Opérations > Supprimer | Ctrl+Suppr | SCREEN-001 | Sélection de lignes ; confirmation et protection rapprochées à observer | P0 | M03/M18 |
| CMD-046 | Vider la saisie | Accès menu inconnu, action de saisie | Ctrl+0 (zéro) | SCREEN-002 | Pas Ctrl+O ; date et focus après remise à zéro à relever | P0 | M18 |
| CMD-047 | Afficher la saisie séparée | Barre d'outils documentée ; menu inconnu | F8 | SCREEN-002 | Option préalable Outils > Options ; ne pas confondre ouverture et configuration | P0 | M03/M17/M18 |
| CMD-048 | Charger un tiers | Opérations | F10 | SCREEN-002 | M03 : modèles d'abord, puis historique du compte par date décroissante ; détails effectifs à capturer | P0 | M03/M11/M18 |
| CMD-049 | Auto-compléter tiers / détail | Saisie, mécanisme dépendant des options | Frappe, sans raccourci global | SCREEN-002 | Ne pas présenter comme nouvelle entrée de menu | P0 | M03/M17 |
| CMD-050 | Calculer une expression dans Montant | Champ Montant | Saisie arithmétique | SCREEN-002 | Priorité multiplication/division ; futur analyseur sûr, jamais évaluation Python historique | P0 | M03 ; MON sécurité |
| CMD-051 | Date de saisie : jour suivant / précédent | Champ Date | Flèche bas / haut **dans Date** | SCREEN-002 | Avance/recul d'un jour | P0 | M03 |
| CMD-052 | Date de saisie : jour suivant / précédent hors Date | Saisie, hors champ Date | PgDn / PgUp | SCREEN-002 | Priorité face à navigation du tableau à relever | P0 | M03 |
| CMD-053 | Date du jour | Champ Date | Suppr puis Entrée **dans Date** | SCREEN-002 | Ne supprime pas les opérations | P0 | M03 |
| CMD-054 | Choisir une date au calendrier | Bouton adjacent à Date | Clic, touche inconnue | SCREEN-002 | Mois courant et flèches de mois ; calendrier exact non observé | P0 | M03 |
| CMD-055 | Numéroter le chèque suivant | Saisie, mode chèque et détail | Choix du mode | SCREEN-002 | Préfixe `n°` sans espace, progression documentée ; ce n'est pas une commande de menu | P1 | M03 |
| CMD-056 | Sélectionner plusieurs lignes | Tableau | Glisser sur les lignes | SCREEN-001 | Prépare suppression, pointage et édition groupée ; Ctrl/Shift de sélection non établis | P0 | M03 |
| CMD-057 | Pointer / rapprocher / dépointer | Menu contextuel > Pointer ; dernière colonne | P / Espace **dans tableau** ; clic dernière colonne | SCREEN-001, SCREEN-010 | Cycle trois états, option deux états ; états reconnaissables sans couleur seule dans Moneta | P0 | M03/M17 |
| CMD-058 | Modifier la date de pointage | Opérations > Date de pointage | Ctrl+Maj+P | SCREEN-010 | Date distincte de l'opération | P0 | M09/M18 |
| CMD-059 | Afficher dates de pointage | Affichage, libellé exact à relever | F11 | SCREEN-001, SCREEN-010 | Bascule d'affichage, ne change pas les dates | P0 | M09/M18 |
| CMD-060 | Copier les lignes | Édition > Copier | — | SCREEN-001 | Raccourci non établi par la matrice ; ne pas inventer Ctrl+C | P0 | M05 |
| CMD-061 | Coller / déplacer vers un autre compte | Édition > Coller ; parcours déplacement à préciser | — | SCREEN-001 | M05 décrit copie/déplacement ; suppression source et devises à observer | P0 | M05 ; ? |
| CMD-062 | Annuler | Groupe menu inconnu | Ctrl+Z | SCREEN-001 | Portée des opérations et nombre réglable ; compatibilité persistance à concevoir | P0 | M17/M18 |
| CMD-063 | Refaire | Groupe menu inconnu | Ctrl+Y | SCREEN-001 | Disponibilité/grisage non observés | P0 | M18 |
| CMD-064 | Saisie multiple | Opérations | F2 | SCREEN-014 | Saisie en série ; détails et abréviations à capturer | P1 | I(MultipleInput/multiple_input), M18 |
| CMD-065 | Modifier un groupe | Opérations + contexte | Ctrl+M | SCREEN-015 | Appliquer changements à une sélection ; aperçu proposé Moneta | P1 | M05 ; I(ModifyGroup/modifygroup) |
| CMD-066 | Remplacer du texte | **Édition INI / Opérations M05 : conflit** | Ctrl+R | SCREEN-016 | Ne pas figer le menu tant que non capturé | P1 | I(Replace/search_replace), M05/M18 ; ? |
| CMD-067 | Transfert de compte à compte | Opérations + barre d'outils | Ctrl+Maj+T | SCREEN-011 | Deux lignes liées ; invariants multidevises non établis pour BP | P0 | M03 ; I(Transfer/transfer) |
| CMD-068 | Rejoindre l'autre ligne du transfert | Icône dans le tiers de la ligne | Clic | SCREEN-001, SCREEN-011 | Navigation réciproque, pas nouveau menu | P0 | M03 ; S liens croisés |
| CMD-069 | Convertir une ligne en transfert | Opérations + contexte | — | SCREEN-012 | Contrôler l'autre compte et les éventuelles lignes déjà présentes | P1 | I(Transfer/convert_line_to_transfer), S |
| CMD-070 | Ventiler l'opération | Opérations + contexte | F6 | SCREEN-013 | Local : dialogue dédié remplaçant une ligne par plusieurs ; M15 décrit saisie intégrée, conflit de parcours | P1 | I(Ventilate/ventilate), S ; M15 ; ? |
| CMD-071 | Champs supplémentaires | Opérations + contexte | F3 | SCREEN-018 | Noms/valeurs et panneau d'informations ; préserver champs internes utiles sans exécuter de contenu | P1 | M09 ; I(ExtraValues/extra_values_edit) |

## Affichage, filtres et options

| ID | Commande / fonction | Emplacement établi ou inconnu | Touche | Écran(s) | Comportement et limite | Priorité | Source / état |
|---|---|---|---|---|---|---|---|
| CMD-072 | Afficher sur une / deux lignes | Affichage > Affichage sur 2 lignes | F4 | SCREEN-001 | Détail sous tiers et sous-catégorie sous catégorie en deux lignes | P0 | M01/M18 |
| CMD-073 | Filtrer par colonne | En-têtes de filtre au-dessus du relevé | Saisie, pas touche globale établie | SCREEN-001, SCREEN-017 | Date, texte, montant ; grammaire M04 à préserver | P0 | M04 |
| CMD-074 | Filtres enregistrés / automatiques | Affichage + barre déroulante | F7 | SCREEN-017 | Périodes dynamiques et filtres nommés ; libellés exacts à relever | P1 | M04 ; I(Filters/setfilters) |
| CMD-075 | Annuler les filtrages | Menu exact inconnu ; geste établi | Maj+F7 | SCREEN-001, SCREEN-017 | Effet sur filtres et soldes à relever | P0 | M04/M18 |
| CMD-076 | Cacher / afficher les lignes inutiles | Affichage | Ctrl+H | SCREEN-001 | Deux règles de frontière contradictoires M03/M04 ; masque sans supprimer | P0 | M03/M04/M18 ; ? |
| CMD-077 | Trier le tableau | Tableau, accès exact à relever | — | SCREEN-001 | Colonne/sens mémorisables par option ; geste sur en-tête non confirmé ici | P0 | M17 ; ? |
| CMD-078 | Afficher monnaie principale / secondaire | Deux boutons de barre d'outils | — | SCREEN-001, SCREEN-046 | Bascule de présentation, calcul BP en monnaie principale selon M15 | P1 | M15 |
| CMD-079 | Options | Outils > Options | — | SCREEN-040 | Général / Sauvegarde / Affichage / Couleurs et police | P0 | M17 |
| CMD-080 | Configurer soldes filtrés, saisie et pointage | Onglet Général de SCREEN-040 | — | SCREEN-040 | Ignorer filtre pour solde, séparé, autocomplete, deux états, décimales, centimes, tri/filtres mémorisés, nombre d'annulations | P0 | M17 |
| CMD-081 | Configurer sauvegardes | Onglet Sauvegarde de SCREEN-040 | — | SCREEN-040, SCREEN-041 | Fermeture automatique, répertoires, externe ; champs réseau historiques à ne pas renseigner avec secrets | P0 | M17 |
| CMD-082 | Configurer dates, colonnes de montants et infobulles | Onglet Affichage de SCREEN-040 | — | SCREEN-040 | Une/deux/trois colonnes, format date, infobulles | P0 | M17 |
| CMD-083 | Police / couleurs / styles : créer, charger, supprimer | Onglet Couleurs et police de SCREEN-040 | — | SCREEN-040 | Styles utilisateur ; fichier historique de thèmes non repris graphiquement | P1 | M17 ; MON identité indépendante |

## Échéancier, budgets, rapports et automatismes

| ID | Commande / fonction | Emplacement établi ou inconnu | Touche | Écran(s) | Comportement et limite | Priorité | Source / état |
|---|---|---|---|---|---|---|---|
| CMD-084 | Échéancier | Comptes + barre d'outils | Ctrl+E | SCREEN-020 | Liste des échéances | P1 | M08 ; I(Scheduler/scheduler) |
| CMD-085 | Ajouter / modifier / supprimer une échéance | Boutons de SCREEN-020 | — | SCREEN-020, SCREEN-021 | Champs de récurrence et modèle ; suppression confirmée selon M08 | P1 | M08 ; S |
| CMD-086 | Ajouter l'opération à l'échéancier | Opérations + contexte | — | SCREEN-021 | Modèle repris d'une ligne sélectionnée | P1 | M08 ; I(Scheduler/scheduler_add) |
| CMD-087 | Appliquer les échéances jusqu'à une date | Bouton Appliquer de SCREEN-020 | — | SCREEN-022 | Date limite ; écritures réelles | P1 | M08 ; S |
| CMD-088 | Insérer pour simulation | Case de SCREEN-022, pas commande de menu établie | — | SCREEN-022 | **BP insère des opérations**, sans avancer les définitions ; Moneta propose simulation isolée, écart à arbitrer | P1 | M08 ; S audit §5 ; MON proposition |
| CMD-089 | Prévisions de solde | **Menu INI vide ; accès autonome inconnu** | — | SCREEN-023 | Ne pas inventer Comptes > Prévisions ni assimiler à CMD-088 | P1 | I(Scheduler/previsions), S ; ? |
| CMD-090 | Insertion / rattrapage d'échéances à l'ouverture | **Auto, sans menu** | — | SCREEN-020, SCREEN-001 | Déclencheur fichier ; récurrences manquées ; idempotence à garantir dans Moneta | P1 | M08 ; I(Scheduler/scheduler_auto), S |
| CMD-091 | Budget mensuel | Outils + barre d'outils | Ctrl+B | SCREEN-024 | Budget/réalisé, comptes, catégories, périodes et deux méthodes | P1 | M12 ; I(Budget/budget), S |
| CMD-092 | Budget annuel | Outils | — | SCREEN-025 | Tableaux nommés, douze mois Budget/Réel/Écart, comptes, catégories multiples par ligne | P1 | I(BudgetYear/budget_year), S |
| CMD-093 | Graphiques | Outils + barre d'outils | — | SCREEN-026 | Périmètre filtré, modèles et impression | P1 | M07 ; I(Charts/charts) |
| CMD-094 | Totaux par catégories | Outils + barre d'outils | Ctrl+T | SCREEN-027 | Totaux mensuels annuels, sorties HTML/tableur selon parité | P1 | M07/M18 ; I(CategsTotals/categs_totals) |
| CMD-095 | Totaux par tiers | Outils | Ctrl+Alt+T | SCREEN-028 | M07 indique aussi Ctrl+T, faute/contradiction non résolue visuellement ; normalisations de libellés à capturer | P1 | M18 ; I(PeopleTotals/people_totals), S ; ? M07 |
| CMD-096 | Reçu | **Outils INI / Outils > Rapports > Reçu M15** | — | SCREEN-048 | Remboursements et reste dû ; ne pas créer le sous-menu Rapports sur cette seule contradiction | P2 | I(Receipt/receipt), M15 ; ? |
| CMD-097 | Calcul automatique des champs | Outils | Ctrl+F | SCREEN-019 | Règles historiques ; aucun déplacement vers recherche ; conception sûre distincte du Python | P1 | I(Auto Fields/auto_fields), M18 ; S |
| CMD-098 | Calcul automatique à l'ajout | **Auto, même INI que CMD-097** | — | SCREEN-019, SCREEN-002 | Déclencheur ajout de ligne, pas entrée de menu additionnelle | P1 | I(Auto Fields/auto_fields) |
| CMD-099 | Calcul automatique à la modification | **Auto, même INI que CMD-097** | — | SCREEN-019, SCREEN-002 | Déclencheur modification, pas évaluation de règles lors d'une lecture BP | P1 | I(Auto Fields/auto_fields) |
| CMD-100 | Notes | Outils | Ctrl+Maj+N | SCREEN-047 | Notes distinctes des opérations ; portée/fichier auxiliaire à confirmer | P1 | I(Notes/notes), S |
| CMD-101 | Charger les notes à l'ouverture | **Auto, sans menu** | — | SCREEN-047 | `notes.dat` prévu mais absent de la référence ; ne pas inventer un contenu | P1 | I(Notes/notes_auto), S |

## Devises et compléments Moneta

| ID | Commande / fonction | Emplacement établi ou inconnu | Touche | Écran(s) | Comportement et limite | Priorité | Source / état |
|---|---|---|---|---|---|---|---|
| CMD-102 | Conversion(s) de devises | **Outils** ; M15 : « Conversion de devises » ; cible Moneta « Conversions de devises » | Ctrl+D | SCREEN-046 | Même point d'entrée ; BP : taux mémorisés non datés, service historique non testé ; Moneta : datés et sourcés | P1 | M15/M18 ; I(Currencies/currencies), S ; MON |
| CMD-103 | Ajouter une devise au convertisseur | Bouton dans SCREEN-046 | — | SCREEN-046 | Sélection dans une liste et saisie de montant ; pas de nouvelle entrée de menu établie | P1 | M15 |
| CMD-104 | Télécharger les cotations de devises | Bouton dans SCREEN-046 | — | SCREEN-046 | BP utilise une page HTTP historique ; aucune garantie de fonctionnement actuel | P1 | M15 ; S |
| CMD-105 | Inverser les monnaies | Outils | — | SCREEN-046, SCREEN-001 | BP réécrit toutes les lignes et ajoute ajustement ; **pas de réécriture silencieuse Moneta**, arbitrage requis | P1 | M15 ; MON arbitrage |
| CMD-106 | Choisir date, monnaies et provenance du taux | Sous-fonction **proposée** dans SCREEN-046, place provisoire | — | SCREEN-046 | EUR/BYN/RUB extensible ; date d'effet, source et unité/nominal visibles ; choix date exacte ou dernier disponible ≤ date explicite, aucune valeur inventée | P1 | MON |
| CMD-107 | Consulter historique des taux | Sous-fonction **proposée** de Outils > Conversions de devises | — | SCREEN-052 | Par monnaie, date, source et nominal ; distinguer officiel et manuel ; cache et date de récupération visibles sans faire du cache un fournisseur | P1 | MON |
| CMD-108 | Saisir / corriger un taux manuel | Sous-fonction **proposée** du même outil | — | SCREEN-053 | Date, paire, unité, valeur, provenance manuelle explicite ; ne pas modifier les écritures historiques | P1 | MON |
| CMD-109 | Consulter l'état du cache hors ligne | Sous-fonction **proposée** du même outil | — | SCREEN-055 | Couverture des dates, date de récupération, source, absent/périmé ; pas de taux fictif de repli | P1 | MON |
| CMD-110 | Actualiser les taux officiels | Bouton **proposé** du même outil | — | SCREEN-046, SCREEN-055 | NBRB prioritaire ; CBR sélection explicite, aucun mélange ni repli silencieux ; source/date/nominal et erreur visibles. Aucun connecteur Moneta implémenté ; les GET documentaires sont recensés dans ARCHITECTURE | P1 | MON |
| CMD-111 | Afficher une valorisation consolidée EUR | Action **proposée** dans totaux/relevé, place provisoire | — | SCREEN-029, SCREEN-030 | Montrer date/source et données manquantes ; pas modification des montants natifs | P1 | MON |
| CMD-112 | Examiner les montants d'un transfert multidevise | Extension **proposée** du dialogue transfert existant | **Ctrl+Maj+T reste l'entrée transfert** | SCREEN-011 | Deux montants natifs figés, taux effectif et frais ; aucune substitution par le convertisseur | P0 | MON ; garanties BP inconnues |

Les sous-fonctions Moneta restent rattachées au même outil de devises ; elles ne justifient ni nouveau menu principal ni redistribution des touches. **Ctrl+F reste Champs automatiques, Ctrl+D reste Conversion(s) de devises, Ctrl+Maj+T reste Transfert.** Aucun nouvel accélérateur Alt ni raccourci global n'est attribué. NBRB est prioritaire et CBR une alternative choisie explicitement ; aucun mélange de sources, repli silencieux ni substitution de date n'est permis. Le choix date exacte/dernier cours disponible ≤ date, la date effectivement retenue, la source, l'unité/nominal et l'état du cache restent visibles. Voir [ARCHITECTURE.md](ARCHITECTURE.md) pour les règles techniques, distinctes du convertisseur BP.

## Sauvegardes, Internet, outils, plugins et aide

| ID | Commande / fonction | Emplacement établi ou inconnu | Touche | Écran(s) | Comportement et limite | Priorité | Source / état |
|---|---|---|---|---|---|---|---|
| CMD-113 | Lister / restaurer les sauvegardes | Comptes | — | SCREEN-041 | Liste puis restauration ; Moneta doit préserver l'état courant et vérifier la copie | P0 | M06 ; I(Backups/backups) |
| CMD-114 | Sauvegarder par courriel | Internet | F9 | SCREEN-042 | Consentement et protection à arbitrer ; ne pas envoyer de données dans cette mission | P2 | I(BackupMail/backup_mail), M06/M18 ; ? M17 |
| CMD-115 | Sauvegarder par FTP / en ligne | Internet > Sauvegarder en ligne selon M17 ; libellé local non établi | Maj+F9 | SCREEN-042 | M17 associe F9 à cette action, conflit avec courriel ; aucun FTP clair à reproduire sans arbitrage | P2 | M06/M18 vs M17 ; ? |
| CMD-116 | Sauvegarde locale lors d'enregistrement | **Auto, sans commande INI autonome** | Ctrl+S déclenche enregistrement, pas touche distincte | SCREEN-041 | Rétention 20/200 liée à versions ; 200 non attribuable au build 2015 sans preuve | P0 | M06 ; audit §4 |
| CMD-117 | Enregistrer automatiquement à la fermeture | **Auto, option**, sans nouvelle entrée de menu | — | SCREEN-040, SCREEN-004 | Option globale BP ; futur comportement Moneta annoncé explicitement | P0 | M02/M17 |
| CMD-118 | Télécharger les cours des titres | Internet | — | SCREEN-043 | Source Yahoo historique non validée ; ne pas écraser prix d'acquisition | P2 | M16 ; I(UpdateStock/update_stock), S |
| CMD-119 | Saisir un titre | Saisie, mode Titre | — | SCREEN-043, SCREEN-002 | Quantité/nom en tiers, ISIN en détail et prix unitaire d'après M16 ; pas menu « Bourse » prouvé | P2 | M16 |
| CMD-120 | Simulateur de crédit | Outils | Ctrl+L | SCREEN-044 | Capital, taux, durée, mensualité et amortissement | P1 | M15 ; I(Loan/loan) |
| CMD-121 | Calculer la quatrième valeur d'un crédit | Contrôles de SCREEN-044 | — | SCREEN-044 | Trois valeurs saisies ou variation d'une avec deux fixes | P1 | M15 |
| CMD-122 | Enregistrer / charger / supprimer un profil de crédit | Boutons et liste de SCREEN-044 | — | SCREEN-044 | `loan.dat` prévu mais absent ; aucune donnée réelle à demander | P1 | M15 ; S |
| CMD-123 | Décaler le tableau d'amortissement | Ligne de SCREEN-044 | Double-clic sur ligne | SCREEN-044 | Reprend capital et durée restante | P1 | M15 |
| CMD-124 | Calculatrice séparée | Outils | Ctrl+Maj+C | SCREEN-045 | Outil distinct du calcul dans Montant ; dispositions inconnues | P1 | I(Calc/calc), M18 |
| CMD-125 | Conversions numériques | Outils | — | SCREEN-056 | Types de conversions exacts non établis dans l'audit ; ne pas inventer les options | P2 | I(Numerals/numerals) ; ? |
| CMD-126 | Vérifier une carte bancaire (Luhn) | Outils | — | SCREEN-057 | Contrôle mathématique seulement ; aucune carte réelle, fictive dédiée si capture | P2 | I(Luhn/luhn), S |
| CMD-127 | Installer / gérer les extensions | Plugins | — | SCREEN-049 | Inventaire des usages ; aucune exécution/installation d'ancien script dans Moneta | P2 | I(ManagePlugins/manage_plugins) |
| CMD-128 | Télécharger le manuel | ? (nom du groupe INI, pas marque d'incertitude) | Ctrl+F1 | SCREEN-058 | Action distincte de l'aide ; téléchargement par BankPerfect non exécuté | P1 | I(Manuel/manuel), M18 |
| CMD-129 | Aide | Groupe exact inconnu | F1 | SCREEN-058 | Aide locale cible Moneta ; écran BP non observé | P0 | M18 ; MON pour aide locale |
| CMD-130 | Rechercher / appliquer une mise à jour | Internet, arborescence incomplète | — | SCREEN-058 | M14 évoque mises à jour ; libellés et consentement à relever, politique Moneta à concevoir | P2 | M14 ; ? |

## Actions locales de dialogue documentées

Ces lignes complètent les accès précédents sans supposer des menus supplémentaires. Les boutons génériques OK/Annuler de chaque dialogue ne sont pas comptés comme des commandes différentes par fenêtre.

| ID | Commande / fonction | Emplacement établi ou inconnu | Touche | Écran(s) | Comportement et limite | Priorité | Source / état |
|---|---|---|---|---|---|---|---|
| CMD-131 | Choisir options / lancer export CSV | Dialogue de l'export | — | SCREEN-035 | Colonnes, séparateur, catégories, pointage, retrait numéros de chèques ; périmètre visible ou tous comptes | P1 | S audit §5 |
| CMD-132 | Préparer correspondances import CSV | Dialogue d'import | — | SCREEN-031 | Date obligatoire, montant unique ou débit/crédit, limites de lignes et doublons | P1 | M13 |
| CMD-133 | Préparer correspondances import QIF | Dialogue d'import | — | SCREEN-032 | Lettres des champs et ordre des dates | P1 | M13 |
| CMD-134 | Choisir comptes et modes import OFX | Dialogue d'import ; configuration `mapping.ini` | — | SCREEN-033 | `mapping.ini` est une correspondance, **pas une commande ni un menu** | P1 | S audit §5 ; I Import OFX/mapping |
| CMD-135 | Arbitrer les doublons puis importer | Dialogues d'import | — | SCREEN-031, SCREEN-032, SCREEN-033, SCREEN-034 | Ignorer/importer/demander selon M13 ; heuristiques imparfaites explicites, pas suppression silencieuse Moneta | P1 | M13 ; S audit §5 ; MON |
| CMD-136 | Consulter rapport d'import | Après import, placement précis à relever | — | SCREEN-034 | Lignes importées/refusées/erronées ; ne prouve pas couverture native BP | P1 | M13 |
| CMD-137 | Choisir période, comptes, catégories du budget | Dans budget mensuel | — | SCREEN-024 | Méthodes de mensualisation distinctes, effet de filtre à capturer | P1 | M12 ; S audit §5 |
| CMD-138 | Gérer tableaux et lignes du budget annuel | Dans budget annuel | — | SCREEN-025 | Tableaux nommés, comptes et association d'une ligne à plusieurs catégories selon audit ; boutons exacts inconnus | P1 | S audit §5 ; ? |
| CMD-139 | Choisir modèle / imprimer un graphique | Dans Graphiques | — | SCREEN-026, SCREEN-054 | Périmètre dépend du filtrage ; boutons exacts à relever | P1 | M07 |
| CMD-140 | Exporter un rapport HTML / tableur | Dans rapports catégories/tiers et autres rapports selon disponibilité à relever | — | SCREEN-027, SCREEN-028, SCREEN-054 | Ne pas confondre avec export du relevé ; formats disponibles par écran à confirmer | P1 | M07 ; parité ; ? |
| CMD-141 | Choisir comptes de la vue multi-comptes | Dans relevé multi-comptes | — | SCREEN-030 | Sélection persistée dans données de script, pas extrapolation d'un menu | P1 | S audit §5 |
| CMD-142 | Éditer / appliquer des règles de champs automatiques | Dans Champs automatiques | — | SCREEN-019 | Éditeur historique à relever ; importer la description ne doit jamais exécuter une règle | P1 | S audit §5 |
| CMD-143 | Éditer / conserver les notes | Dans Notes | — | SCREEN-047 | Portée et commandes exactes inconnues ; pas présumer insertion dans `.bp` | P1 | S audit §5 ; ? |
| CMD-144 | Paramétrer / imprimer le reçu | Dans Reçu | — | SCREEN-048, SCREEN-054 | Compte dédié, crédit initial pointé, débits de remboursements ; fichier annexe prévu | P2 | M15 ; S |
| CMD-145 | Confirmer / annuler une action de dialogue | Contrôles locaux, existence exacte par écran à capturer | — | SCREEN-004, SCREEN-050, SCREEN-051 | MON : distinguer aperçu, confirmation et abandon sans mutation ; les validations BP n'en démontrent pas les garanties | P0 | M02/M03/M08 pour exemples BP ; MON pour conversion |
| CMD-146 | Examiner un échec ou élément non couvert de conversion | Dans rapport proposé | — | SCREEN-051 | Ne pas masquer une perte ; détail intelligible sans secrets ni données financières réelles dans les captures | P0 | MON |
| CMD-147 | Retourner de l'historique / saisie manuelle / cache au convertisseur | Navigation locale **proposée**, place provisoire | — | SCREEN-046, SCREEN-052, SCREEN-053, SCREEN-055 | Conserver paire/date choisies ; pas nouveau menu BP ni touche globale | P1 | MON |
| CMD-148 | Ouvrir le nouveau `.moneta` après conversion | Choix final **proposé**, place provisoire | — | SCREEN-051, SCREEN-001 | Dans ce parcours natif, uniquement un résultat **complet vérifié et publié**, pas un diagnostic partiel ; original BP conservé et jamais supprimé | P0 | MON, décision parcours |

## Couverture des déclarations et groupes inconnus

Les **42 déclarations de scripts** recensées par l'audit sont représentées : 39 déclarations à accès de menu non vide (dont `auto_fields` également automatique), deux automatiques `notes_auto` / `scheduler_auto`, une déclaration `previsions` à menu vide. `mapping.ini`, 43e INI de Scripts, est une configuration de correspondance OFX (CMD-134), pas un script supplémentaire. Les trois déclenchements ajout/modification/ouverture restent des mécanismes, pas des entrées artificielles. `globalcolors.ini` relève des styles (CMD-083), pas d'une commande d'extension.

**Groupe « emplacement inconnu / non établi » à conserver dans la spécification, sans le transformer en menu visible :** Enregistrer sous (CMD-004), Imprimer (005), Fermer (006), Quitter (007), Vider la saisie (046), ouverture de la saisie séparée hors barre (047), Annuler/Refaire (062–063), Annuler les filtres (075), Aide (129), détails Mise à jour (130). Les chemins contradictoires Remplacer (066), Reçu (096), FTP/F9 (114–115) sont distincts de l'absence de preuve. Prévisions (089) reste **menu vide déclaré**, pas commande cachée supposée. Le signe `?` du menu Manuel est, lui, une valeur déclarée de groupe.

Aucun menu principal n'est affirmé vide : une déclaration à menu vide ne renseigne pas le contenu d'un menu principal. Aucun sous-menu « Bourse », « Prévisions », « Recherche », ni groupe autonome de taux n'est créé par cette cartographie. Les extensions du catalogue public (Documents, Rapprocher, KDiff, Trader, OFX2, Accueil, Suite budgétaire…) ne deviennent pas des commandes installées sans preuve locale.

## Raccourcis : contrat de conservation

Toutes les touches de [FUNCTIONAL_PARITY.md](FUNCTIONAL_PARITY.md#raccourcis-à-préserver) ont un rattachement ci-dessus : F1/ Ctrl+F1 (129/128), F2–F12 (064/071/072/035/070/074/047/114/048/059/004), Maj+F7 (075), Maj+F9 (115), Ctrl+B/D/E/F/G/H/L/M/N/O/P/R/S/T (091/102/084/097/022/076/120/065/001/002/005/066/003/094), Ctrl+Alt+T/Q (095/016), Ctrl+Y/Z (063/062), Ctrl+0 (046), Ctrl+Suppr (045), Ctrl+Entrée (042), Maj+Entrée (044), Ctrl+Maj+C/N/P/T (124/100/058/067). Les gestes contextuels P/Espace, Entrée dans Montant, flèches dans Date, PgDn/PgUp hors Date, Suppr puis Entrée dans Date sont CMD-057/042/051/052/053.

Cette correspondance ne prouve pas leur fonctionnement local. Les captures utilisateur devront préciser le focus, la sélection et les options actives avant/après ; aucun Alt n'est inventé. Les écarts de sécurité, simulation et réécriture des monnaies devront être arbitrés avant gel de l'interface, pas dissimulés sous une prétendue fidélité.
