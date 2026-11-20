# Cartographie des commandes et gestes

## Contrat documentaire

**Mission 16.** **Rapports** réunit Synthèse, Flux, Répartition, Graphiques, Soldes à date, Prévisions, Budgets et Rapports figés. **Outils → Budget mensuel** et **Ctrl+B**, **Outils → Totaux par catégories** et **Ctrl+T**, ainsi que les boutons de la barre d’outils conservent les accès familiers. [Budgets et rapports](BUDGETS_AND_REPORTS.md)

**Mission 15.** **Outils → Convertisseur historique…**, **Outils → Valoriser le solde à date…** et **Opérations → Valoriser l’opération enregistrée…** ouvrent la consultation à date. La fenêtre peut travailler sur le cache seul et reproduire une preuve exportée. [Valorisation historique](HISTORICAL_FX_ENGINE.md)

**Mission 14.** **Comptes → Échéancier…**, le bouton **Échéancier** et **Ctrl+E** ouvrent les onglets Occurrences, Programmations et Historique. Le bouton affiche le nombre d’échéances à traiter. L’utilisateur peut préparer une occurrence depuis son formulaire de confirmation et enregistrer depuis l’échéancier avec **Ctrl+S**. [Échéancier et raccourcis](SCHEDULED_TRANSACTIONS.md)

**Mission 13.** **Outils → Options → Sauvegardes** règle l’activation, la cadence, la destination locale et la rétention. **Fichier → Sauvegardes → Historique…** ouvre les versions du dossier en cours, avec vérification et restauration vers un nouveau fichier. Ctrl+S confirme le commit financier avant toute demande de sauvegarde. Les commandes de copie manuelle et restauration directe restent dans Fichier. [Sauvegardes automatiques](AUTOMATIC_BACKUPS.md)

La mission 12 active **Édition → Rechercher** pour le focus du texte au-dessus du registre, **Affichage → Filtres avancés…** et le bouton **Filtres** pour les critères détaillés, ainsi que **Comptes → Registre de tous les comptes…**. **F7** ouvre les filtres et **Maj+F7** les efface. Les résultats offrent **Voir dans le registre** et le registre offre **Retour aux résultats**. Les filtres nommés et Ctrl+F restent des références historiques. [Recherche et filtres](SEARCH_AND_FILTERS.md)

La mission 11 livre **Comptes → Propriétés du compte…**, **Archiver le compte…**, **Réactiver le compte…**, **Monter**, **Descendre** et **Supprimer le compte vierge…**. Les actions sont aussi accessibles au clic droit dans la liste de gauche. La case d’affichage des archives contrôle la liste et le sélecteur de la barre d’outils. Les raccourcis en service sont conservés. [Commandes et comportement](ACCOUNT_MANAGEMENT.md)

La cartographie cible s’appuie sur le manuel et la capture privée de mission 4. Les identifiants CMD restent stables. Le README récapitule les commandes en service. [README](../README.md)

### Lecture des tables

- **Mxx** : chapitre officiel lié dans [les sources de la parité](FUNCTIONAL_PARITY.md#sources-officielles-consultées). **I(famille/fichier)** : déclaration INI déjà recensée dans l'audit, sous `Scripts/` ; le suffixe `.ini` est implicite. **S** : comportement du script lisible déjà analysé par l'audit. **?** : emplacement, détail ou contradiction non résolu. Aucune de ces preuves n'est une observation de fonctionnement.
- **MON** : décision/proposition Moneta, jamais attribuée à BankPerfect. Les décisions de format et de parcours d'ouverture sont acquises ; les nouveaux libellés, sous-écrans et leur disposition restent des propositions.
- Priorités **proposées** : **P0** = dossier, relevé, saisie, intégrité, conversion native prioritaire ; **P1** = parité métier et outils usuels ; **P2** = compléments ou intégrations nécessitant arbitrage. Ce classement n'autorise ni abandon de fonction ni substitution CSV à `.bp`.
- `—` dans la colonne touche = aucun raccourci établi, et non preuve d'absence. Les noms sont fonctionnels et normalisés avec accents ; ce ne sont pas des transcriptions garanties de chaque libellé affiché.
- Un accès indiqué « bouton », « tableau », « saisie » ou « auto » n'est pas un menu. Les écrans cités sont des surfaces documentaires : leur séparation en fenêtres réelles reste à confirmer.

**Ordre principal confirmé par la capture utilisateur de mission 4 : Fichier | Édition | Affichage | Comptes | Opérations | Outils | Plugins | Internet | ?.** Inconnus restants : ordre des sous-commandes, séparateurs, accélérateurs Alt et comportements dépendant du focus. L'image ne montre pas les menus déroulés ; aucune donnée personnelle n'en est retranscrite.

### Présentation Moneta — historique jusqu’à la mission 8

- Barre d'outils : emplacement du dossier/compte, Comptes (ajout), Ouvrir, Enregistrer, Annuler, Refaire, Verrouiller, Budget, Totaux, Graphiques, Filtres, Échéancier, **Virement**. Comptes/Ouvrir/Enregistrer, Virement, Filtres, Échéancier et la sélection sont opérationnels, avec débordement en fenêtre étroite. Le bouton Virement reprend l’emplacement transfert existant, sans nouvelle barre.
- Les sous-menus et indications de touches documentés sont conservés, sans activer des raccourcis financiers inexistants. Quitter sous Fichier et À propos sous ? restent des décisions Moneta, pas des observations de menus ouverts BankPerfect.
- Mission 5 : **Affichage → Thème → Clair / Sombre**, sélecteur supérieur supprimé, préférence locale conservée et contraste Windows prioritaire. **Outils → Options** opérationnel ; les thèmes restent la seule préférence visuelle.
- Mission 6 : **Fichier → Nouveau dossier / Ouvrir / Enregistrer / Enregistrer sous / Sauvegarder le dossier / Restaurer une sauvegarde / Mode démonstration**, et **Comptes → Ajouter un compte**. Dossiers `.moneta` persistants limités aux comptes/devises/métadonnées ; édition/suppression de compte et opérations persistantes non réalisées.
- `WindowCommands` centralise ces commandes ainsi que recherche/suppression/annulations futures, menus et outils communs, avec `CanExecute=false` pour les fonctions absentes. Aucun Ctrl+Z/Y global au détriment du texte. Ctrl+F demandé pour recherche entre en conflit avec Champs automatiques BP : indication Ctrl+F retirée de cette dernière dans le prototype, recherche elle-même désactivée ; arbitrage historique conservé dans [CURRENCIES_AND_SETTINGS.md](CURRENCIES_AND_SETTINGS.md).
- Mission 7A (**choix MON**, pas menu BP prétendument observé) : **Opérations → Nouvelle opération / Modifier / Supprimer**, mêmes commandes que la ligne compacte et le double-clic de modification. Ctrl+Entrée / Maj+Entrée et Suppr / Ctrl+Suppr sont limités au registre ; aucune capture des touches dans les champs texte. Le sens Recette/Dépense est explicite et indépendant du nom du mode. La saisie intégrée directe, F8, les raccourcis de calendrier et le pointage restent futurs.
- Le dialogue prépare un brouillon ; Ctrl+S écrit durablement. La suppression demande confirmation et reste logique dans le fichier. Registre réel paginé par 100, sans filtres effectifs ; états de pointage stockés sans rapprochement bancaire validé. Les anciennes limitations ci-dessus décrivent les missions correspondantes, pas l’état actuel du registre.
- Mission 7B : **Opérations → Pointer / Dépointer / Rapprochement bancaire… / Libérer un rapprochement hérité… / Date de pointage…**, contexte Pointer/Dépointer/Rapprochement, clic colonne de pointage. P/Espace basculent NP/P ; Ctrl+Maj+P date et F11 affichage des dates sont documentés BP. **Maj+P** (dépointer) et **Ctrl+Maj+R** (rapprochement) sont des choix Moneta, limités au registre. Aucune interception des champs texte.
- Le manuel BP décrit NP → P → R → NP et l’option deux états. Moneta distingue NP/P de R formel : pas de bascule manuelle vers R, ni de dépointage silencieux d’une R. Historique et réouverture du dernier relevé avec motif dans le dialogue ; libération héritée explicite dans Opérations. Tout lot doit être enregistré avant autre mutation du registre.
- La [page publique Rapprocher](https://chelly.net/bankperfect/wiki/Rapprocher.html) documente une extension dans Opérations/barre d’outils, numéro/date/solde final, sélection par double-clic/Espace et validation à écart nul. **Installation locale non attestée** ; Magic et pause non implémentés. Les règles d’ouverture de relevé/réouverture Moneta sont explicites dans ARCHITECTURE, pas attribuées à BP.
- Mission 7C : **Opérations → Virement…** et outil **Virement**, même commande ; **Ctrl+Maj+T** historiquement documenté pour les transferts, **limité au DataGrid du registre** dans Moneta. Aucun nouveau raccourci global ni interception des champs texte.
- **Ouvrir le virement lié… / Autre côté du virement / Supprimer le virement entier…**, dans Opérations et contexte, sont des accès **MON** réalisés, pas des menus BP prétendument observés. Modifier/double-clic et Supprimer sur ligne liée ciblent aussi l’ensemble. Le bouton ↔ dans la ligne navigue débit → crédit, crédit/frais → débit, page de 100 ancrée UUID chargée avant sélection.
- Dialogue de virement : comptes actifs distincts, montants natifs positifs sans arrondi ; crédit lié au débit en même devise ; deux montants réels et date/source déclarées non vérifiées sinon. Rapport exact crédit/débit hors frais, reçu pour 1 unité source ; quotient seulement approximatif, aucun cours officiel. Frais séparés sur source/cible/troisième compte, positifs au premier ajout, zéro conservant le mouvement ensuite.
- Comptes principaux et compte de frais déjà ajouté fixes ; correction de compte par suppression entière/recréation. États NP/P/R indépendants ; toute jambe R, frais compris, impose consultation readonly de l’ensemble et refuse édition/suppression. Réouverture/libération puis Ctrl+S préalable. Valider le dialogue prépare un brouillon, Save seul rend durable ; aucune mutation financière isolée d’une jambe.
- Les limites annoncées sous missions 6/7A décrivent leurs livraisons historiques : elles ne signifient plus que le registre, les relevés ou les virements sont absents. Conversion de ligne en transfert et import `.bp` restent non réalisés ; cours/cache sont désormais livrés en mission 8. Parcours Windows **historique 7C** et réserves visuelles dans le README, sans qualification Windows mission 8 déduite.
- Mission 8 : **Outils → Cours de change…** ouvre une fenêtre réelle de cotations, historique local et convertisseur de montant ; **Outils → Actualiser les cours officiels** est aussi actif. Source/date, Consulter le cache, Actualiser, Annuler la requête sont disponibles ; aucun besoin de dossier financier pour consulter les cours.
- **Outils → Options → Devises et taux** : source NBRB par défaut ou CBR explicite, choix persisté ; changer de fournisseur ne consulte que son cache, sans téléchargement ni basculement/mélange automatique. Actualisation au démarrage **désactivée par défaut**, option appliquée au prochain lancement, sans bloquer l’ouverture des comptes ni imposer Internet. Options Mises à jour reste une préférence seule, sans téléchargement/installation.
- Près du solde actuel, **Devise d’affichage** = natif réel ou équivalent estimé, avec natif conservé séparément et source/effet/date demandée/récupération/cache explicités. Devises de présentation du dossier + EUR/BYN/RUB sans enrichissement du catalogue financier ; aucune modification d’opération, de virement ou de l’état modifié.
- Hors ligne : dernier cours d’effet ≤ date demandée, ancien d’au plus **31 jours**, avec date réelle/ancienneté ; sinon Cours indisponible. Futur conservé mais exclu avant effet. Cache disque et mémoire non persistée distingués, erreur de cache visible ; aucune fausse persistance annoncée. Annuler/changer la sélection/fermer annule la demande concernée, sans publication tardive d’une sélection obsolète.
- Automatique : cache du jour récupéré depuis au plus six heures réutilisé, cache historique applicable conservé ; actualisation manuelle forcée. Publication absente : huit dates au plus, délai global 20 s et 10 s par requête ; erreur HTTP/réseau sans retry. HTTPS officiel fixe, date publique uniquement, aucun compte/montant envoyé, pas de cookies/redirections, XML sans DTD/résolution externe ; SHA-256 du contenu non assimilé à une signature officielle.



## Fichier, échanges et ouverture native Moneta

| ID | Commande / fonction | Emplacement établi ou inconnu | Touche | Écran(s) | Comportement et limite | Priorité | Source / état |
|---|---|---|---|---|---|---|---|
| CMD-001 | Créer un nouveau fichier de comptes | **Comptes** > Créer un nouveau fichier de comptes, pas Fichier d'après M02 | Ctrl+N | SCREEN-004, SCREEN-003 | BP demande nom et solde initial ; Moneta crée un dossier natif `.moneta`, pas un `.bp` | P0 | M02/M18 ; adaptation MON |
| CMD-002 | Ouvrir | Fichier > Ouvrir | Ctrl+O | SCREEN-004, SCREEN-050 | Même entrée Moneta pour `.moneta` et `.bp` ; branchement détaillé ci-dessous | P0 | M02/M18 ; décision MON |
| CMD-003 | Enregistrer | Fichier > Enregistrer | Ctrl+S | SCREEN-001, SCREEN-004, SCREEN-011 | **Réalisé.** Commit SQLite après validation. Sauvegarde après enregistrement selon la politique Moneta | P0 | M02/M06/M18 ; MON |
| CMD-004 | Enregistrer sous | Groupe exact à relever ; Fichier non garanti par M18 seul | F12 | SCREEN-004 | **Réalisé dans Fichier** : nouvelle destination exclusive, même UUID de dossier, original intact ; emplacement historique BP non prouvé | P0 | M18 ; MON pour protection BP |
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
| CMD-040 | Total des comptes | Comptes | — | SCREEN-029 | **Interface non réalisée** ; API mission 8 `ConvertTotal` disponible, somme exacte puis arrondi unique, échec global si une devise manque ; règles virtuel/désactivé BP à relever | P1 | I(Totals/totals), S ; API MON ≠ parité UI |
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
| CMD-067 | Transfert de compte à compte | Opérations + barre d'outils ; Moneta : **Virement… / Virement** | Ctrl+Maj+T **dans le registre Moneta** | SCREEN-011 | **Réalisé 7C** : deux principaux natifs liés et frais séparés, correction/suppression d’ensemble, consultation seule dès qu’une jambe est R ; garanties multidevises BP non établies | P0 | M03 ; I(Transfer/transfer) ; conventions MON explicites |
| CMD-068 | Rejoindre l'autre ligne du transfert | BP : icône dans le tiers ; Moneta : bouton ↔, Opérations et contexte **Autre côté du virement** | Clic ; aucun raccourci supplémentaire | SCREEN-001, SCREEN-011 | **Réalisé 7C** : débit → crédit, crédit/frais → débit ; chargement page de 100 et sélection UUID ; commandes liées Moneta non attribuées à BP | P0 | M03 ; S liens croisés ; MON pour accès/garanties |
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
| CMD-078 | Afficher monnaie principale / secondaire | BP : deux boutons de barre d’outils ; Moneta : sélecteur **Devise d’affichage** près du solde actuel | — | SCREEN-001, SCREEN-046 | **Réalisé mission 8 selon convention MON** : natif réel + estimation, source/date d’effet/récupération/cache, cours absent explicite ; aucune réécriture ni modification de l’état du dossier | P1 | M15 ; MON pour sélecteur/valorisation |
| CMD-079 | Options | Outils > Options | — | SCREEN-040 | **Opérationnel Moneta.** Cinq rubriques, dont **Devises et taux** et **Sauvegardes** avec activation explicite | P0 | M17 ; rubriques MON distinctes |
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
| CMD-102 | Conversion(s) de devises | BP : **Outils**, « Conversion de devises » ; Moneta : **Outils → Cours de change…** | Ctrl+D historique BP, **non activé Moneta** | SCREEN-046 | **Réalisé mission 8** : convertisseur de montant français avec source/destination, cotations officielles et arrondi final au pair ; résultat indicatif, aucune écriture financière | P1 | M15/M18 ; I(Currencies/currencies), S ; MON |
| CMD-103 | Ajouter une devise au convertisseur | BP : bouton dans SCREEN-046 ; Moneta : choix source/destination dans Cours de change | — | SCREEN-046 | Choix de présentation = devises du dossier + EUR/BYN/RUB ; pas d’éditeur d’ajout libre ni d’ajout au catalogue financier en mission 8 | P1 | M15 ; limite MON explicite |
| CMD-104 | Télécharger les cotations de devises | Bouton **Actualiser** dans Cours de change | — | SCREEN-046 | **Réalisé mission 8** avec flux HTTPS officiels ; BP utilise une page HTTP historique non réputée encore fonctionnelle ; commande manuelle force la demande | P1 | M15 ; S ; MON pour acquisition |
| CMD-105 | Inverser les monnaies | Outils historique BP | — | SCREEN-046, SCREEN-001 | **Non réalisé Moneta** : BP réécrit toutes les lignes et ajoute ajustement ; pas de réécriture silencieuse, arbitrage requis | P1 | M15 ; MON arbitrage |
| CMD-106 | Choisir date, monnaies et provenance du taux | **Réalisé mission 8** dans Cours de change : fournisseur/date, Consulter le cache ; Options → Devises et taux pour la source persistée | — | SCREEN-046 | NBRB défaut/BYN, CBR explicite/RUB ; changement = cache uniquement. Effet réel ≤ date, âge ≤ 31 jours, nominaux/date/source visibles, aucun mélange ni repli de fournisseur | P1 | MON, différent de BP |
| CMD-107 | Consulter historique des taux | Onglet **Historique local** de Cours de change | — | SCREEN-052 | **Réalisé mission 8** : 100 publications récentes au plus, dates effet/requête/récupération et SHA-256 ; archives plus anciennes consultables par date, sans purge automatique ; futur exclu avant effet | P1 | MON |
| CMD-108 | Saisir / corriger un taux manuel | Sous-fonction **future**, non présente dans la fenêtre actuelle | — | SCREEN-053 | **Non réalisé** ; date, paire, unité, valeur et provenance utilisateur restent à développer. La saisie actuelle est celle d’un montant à convertir, pas d’un taux | P1 | MON, exigence conservée |
| CMD-109 | Consulter l’état du cache hors ligne | État et dates visibles dans **Cours de change** | — | SCREEN-055 | **Réalisé mission 8** sans fenêtre cache autonome : source/effet/récupération, disque ou mémoire non persistée, erreurs et cours absent ; pas de conversion au-delà de 31 jours ni taux fictif | P1 | MON |
| CMD-110 | Actualiser les taux officiels | **Outils → Actualiser les cours officiels**, ou bouton Actualiser dans Cours de change | — | SCREEN-046, SCREEN-055 | **Réalisé mission 8** : même source/date, publication absente jusqu’à sept dates antérieures, requête annulable, aucun retry HTTP/réseau ni repli NBRB/CBR ; option démarrage OFF par défaut dans Options → Devises et taux | P1 | MON ; clients officiels réels |
| CMD-111 | Afficher une valorisation consolidée EUR | Action **future** dans totaux/relevé, place provisoire | — | SCREEN-029, SCREEN-030 | **Interface non réalisée** : API d’agrégat exact disponible en mission 8 ; ne pas confondre avec l’estimation opérationnelle du seul compte sélectionné ; date/source et données manquantes requis | P1 | MON |
| CMD-112 | Examiner les montants d'un transfert multidevise | **Réalisé 7C** dans le même dialogue Virement | **Ctrl+Maj+T dans le registre** reste l'entrée création | SCREEN-011 | Deux montants natifs réels, rapport exact hors frais et aperçu approximatif, date/source déclarées non vérifiées, frais séparés ; aucune substitution par un convertisseur ou cours officiel | P0 | MON ; garanties BP inconnues |

La fenêtre **Cours de change mission 8** regroupe les sous-fonctions livrées sans nouveau menu principal ; saisie/correction de taux manuel reste future. Le dialogue **Virement 7C** demeure séparé : deux montants réels figés, aucun cours officiel suggéré ou automatiquement appliqué. **Ctrl+F reste la référence historique Champs automatiques** (conflit recherche Moneta conservé ci-dessus), **Ctrl+D Conversion(s) de devises** est historique et non activé Moneta, **Ctrl+Maj+T Transfert** reste actif seulement dans le registre. Aucun nouvel accélérateur Alt ni raccourci global n’est déclaré sans preuve. Le [README](../README.md#preuves-acquises-de-mission-8) consigne les endpoints NBRB/CBR effectivement vérifiés avec le client .NET, la documentation CBR consultée et le timeout de lecture de l’aide NBRB ; aucun contrat complet de disponibilité ni parcours Windows non observé n’en est déduit.

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
| CMD-147 | Retourner de l’historique / saisie manuelle / cache au convertisseur | Moneta mission 8 : onglets Cotations / Historique local dans la même fenêtre, convertisseur toujours visible ; saisie de taux manuel absente | — | SCREEN-046, SCREEN-052, SCREEN-053, SCREEN-055 | **Partiel** : navigation locale réalisée sans fenêtre d’historique/cache autonome ; pas de nouvelle commande BP ni touche globale, aucun écran de taux manuel livré | P1 | MON |
| CMD-148 | Ouvrir le nouveau `.moneta` après conversion | Choix final **proposé**, place provisoire | — | SCREEN-051, SCREEN-001 | Dans ce parcours natif, uniquement un résultat **complet vérifié et publié**, pas un diagnostic partiel ; original BP conservé et jamais supprimé | P0 | MON, décision parcours |

## Couverture des déclarations et groupes inconnus

Les **42 déclarations de scripts** recensées par l'audit sont représentées : 39 déclarations à accès de menu non vide (dont `auto_fields` également automatique), deux automatiques `notes_auto` / `scheduler_auto`, une déclaration `previsions` à menu vide. `mapping.ini`, 43e INI de Scripts, est une configuration de correspondance OFX (CMD-134), pas un script supplémentaire. Les trois déclenchements ajout/modification/ouverture restent des mécanismes, pas des entrées artificielles. `globalcolors.ini` relève des styles (CMD-083), pas d'une commande d'extension.

**Groupe « emplacement inconnu / non établi » à conserver dans la spécification, sans le transformer en menu visible :** Enregistrer sous (CMD-004), Imprimer (005), Fermer (006), Quitter (007), Vider la saisie (046), ouverture de la saisie séparée hors barre (047), Annuler/Refaire (062–063), Annuler les filtres (075), Aide (129), détails Mise à jour (130). Les chemins contradictoires Remplacer (066), Reçu (096), FTP/F9 (114–115) sont distincts de l'absence de preuve. Prévisions (089) reste **menu vide déclaré**, pas commande cachée supposée. Le signe `?` du menu Manuel est, lui, une valeur déclarée de groupe.

Aucun menu principal n'est affirmé vide : une déclaration à menu vide ne renseigne pas le contenu d'un menu principal. Aucun sous-menu « Bourse », « Prévisions », « Recherche », ni groupe autonome de taux n'est créé par cette cartographie. Les extensions du catalogue public (Documents, Rapprocher, KDiff, Trader, OFX2, Accueil, Suite budgétaire…) ne deviennent pas des commandes installées sans preuve locale.

## Raccourcis : contrat de conservation

Toutes les touches de [FUNCTIONAL_PARITY.md](FUNCTIONAL_PARITY.md#raccourcis-à-préserver) ont un rattachement ci-dessus : F1/ Ctrl+F1 (129/128), F2–F12 (064/071/072/035/070/074/047/114/048/059/004), Maj+F7 (075), Maj+F9 (115), Ctrl+B/D/E/F/G/H/L/M/N/O/P/R/S/T (091/102/084/097/022/076/120/065/001/002/005/066/003/094), Ctrl+Alt+T/Q (095/016), Ctrl+Y/Z (063/062), Ctrl+0 (046), Ctrl+Suppr (045), Ctrl+Entrée (042), Maj+Entrée (044), Ctrl+Maj+C/N/P/T (124/100/058/067). Les gestes contextuels P/Espace, Entrée dans Montant, flèches dans Date, PgDn/PgUp hors Date, Suppr puis Entrée dans Date sont CMD-057/042/051/052/053.

Cette correspondance ne prouve pas leur fonctionnement local. Les captures utilisateur devront préciser le focus, la sélection et les options actives avant/après ; aucun Alt n'est inventé. Les écarts de sécurité, simulation et réécriture des monnaies devront être arbitrés avant gel de l'interface, pas dissimulés sous une prétendue fidélité.
