# Moneta — feuille de route proposée

## Portée et règle de lecture

Le format financier courant utilise le **schéma 6** et le cache public des cours reste indépendant. La conversion `.bp` est qualifiée sur un périmètre V5 EUR. Le total multicomptes dans l’interface, le taux manuel, les budgets, l’échéancier, la sauvegarde automatique et la mise à jour installable restent ouverts.

Les choix et invariants sont décrits dans [ARCHITECTURE.md](ARCHITECTURE.md). Le [manuel officiel](https://chelly.net/bankperfect/wiki/manuel.html) fournit des comportements documentés, pas une démonstration de l'interface locale complète. L'audit et la matrice de parité doivent relier chaque besoin à sa preuve, sa limite et sa validation ; la feuille de route donne l'ordre et les portes de sortie, pas un abandon des fonctions placées plus tard.

**Priorités :** P0 = préalable bloquant / protection des données ; P1 = parcours central ; P2 = complétude fonctionnelle et qualification. Une priorité ne supprime pas un besoin de parité. Les identifiants dans « Dépendances » désignent des résultats **acceptés**, pas simplement des tâches commencées. Chaque ligne dispose de sa validation. Un désaccord de fidélité reste ouvert jusqu'à arbitrage utilisateur explicite ; ne pas le résoudre en changeant discrètement le comportement.

La [stratégie d'importation](BANKPERFECT_IMPORT_STRATEGY.md) et le [format `.moneta`](FILE_FORMATS.md) définissent le périmètre de la conversion native. [UI_MENU_MAP.md](UI_MENU_MAP.md), [UI_SCREEN_INVENTORY.md](UI_SCREEN_INVENTORY.md) et [VISUAL_IDENTITY.md](VISUAL_IDENTITY.md) complètent les critères de parité et de conception.

### Gestion des comptes — mission 11

Propriétés descriptives, ordre persistant, archivage, réactivation, consultation de l’historique et suppression confirmée d’un compte vierge sont disponibles dans Moneta. Les dossiers antérieurs passent au schéma 6 sur copie. Pour la mission 12, une suite possible consiste à étendre la recherche au registre actif, avec pagination et conservation du contexte de compte. [Règles de la mission 11](ACCOUNT_MANAGEMENT.md)

### Première conversion financière — mission 10C

Import V5 EUR borné avec aperçu, consentement et vérification exhaustive avant publication. BP-F00 à F02 constituent le périmètre positif de référence. BP-F03 qualifie le refus pour précision excessive. [Matrice](BANKPERFECT_COMPATIBILITY_MATRIX.md#périmètre-de-conversion-10c)

### Qualification complémentaire — mission 10D

Le corpus BP8 fictif couvre désormais une ouverture négative R, une date de pointage antérieure au jour comptable et plusieurs virements pouvant partager date, comptes et montant. Suivre les limites par cas avant d’élargir la conversion à d’autres builds ou devises. [Matrice de qualification](BANKPERFECT_COMPATIBILITY_MATRIX.md#extension-qualifiée-en-mission-10d)

Pour une mission suivante, compléter les variantes natives encore ouvertes et qualifier les limites de volume et la reprise après interruption. L’évolution pour les montants sous le centime et la valorisation historique des futurs rapports ont des études séparées. [Précision historique](HISTORICAL_MONEY_PRECISION.md) · [Valorisation historique](HISTORICAL_FX_VALUATION.md)

### Robustesse et bilan — mission 10E

La relance après arrêt repart de la source. Le corpus synthétique d’EUR exerce la conversion exhaustive et la restauration jusqu’à 50 000 lignes. La prochaine reprise du développement courant peut s’appuyer sur ce périmètre et un pilote ciblé avec des dossiers fictifs supplémentaires. Priorités restantes pour élargir l’import. Qualifier séparément les autres builds, devises et signatures d’ouverture, et mesurer les reprises sur d’autres systèmes de fichiers.

Les futurs budgets et rapports valoriseront chaque dépense passée avec le cours applicable à la date de cette dépense pour toute devise prise en charge. [Qualification 10E](BANKPERFECT_QUALIFICATION_10E.md) · [Valorisation historique](HISTORICAL_FX_VALUATION.md)

### Lecteur diagnostique C# — mission 10B

B02/B03 disposent d’une bibliothèque .NET 10 et d’une CLI interne. Lecture seule des quatre fixtures BP8 V5, provenance source, décimaux exacts, liens réciproques, diagnostics bornés et couverture JSON. [Résultats](BANKPERFECT_FIXTURES.md#qualification-c--mission-10b)

B04–B06 restent à traiter lors d’une mission ultérieure. Approuver les règles d’ouverture, UID, pointage, monnaies et provenance puis étendre le corpus aux domaines manquants avant de décider d'une conversion. Le cas `0,0001 EUR` exige une décision explicite.

### Étude du format et qualification — mission 10A

| Résultat | Validation / réserve |
|---|---|
| Source amont figée | BankPerfect **9.13.0**, MIT, commit `990f5c7f712dab07c7fefc2921cbf60beee7f6b1`, copie séparée ; aucun code tiers intégré |
| Spécification V5 | Codec/substitution, encodage, en-tête, comptes, opérations, Extra et identités documentés avec symboles/lignes et niveaux de preuve ; V6/UCL/Shuffle distingués |
| Épreuves amont | **11 tests ciblés réussis**, séparés de la confrontation native ; sonde fictive montrant replis/renumérotation et panics Unicode/UID maximal |
| Référence BP8 | **8.0.0.373**, EXE public identique ; installation interactive propre dans l'invité après échecs initiaux, identité privée non lue ; aucune modification de la référence |
| Corpus | **F00..F03 produits et comparés**, hashes inchangés après lecture ; ouverture R, recette/dépense, liens réciproques, NP/P/R/date manuelle, hiérarchie et **0,0001 EUR** ; namespace 10A distinct de F00..F22 |
| Décisions proposées | C# strict/borné, Rust contre-vérification ; ouverture zéro/toutes lignes proposée. Pointage étayé pour ce build ; **fraction sous centime, provenance, autres devises/versions et domaines absents** à arbitrer |
| Non-régression | Builds Debug/Release : zéro erreur/avertissement ; **516 tests réussis par configuration**, aucun échec/ignoré ; moteur financier et SQLite schéma 5 inchangés |

Documents : [recherche](BANKPERFECT_FORMAT_RESEARCH.md), [mapping](BANKPERFECT_MAPPING.md), [matrice](BANKPERFECT_COMPATIBILITY_MATRIX.md), [fiches](BANKPERFECT_FIXTURES.md), [stratégie et trois options](BANKPERFECT_IMPORT_STRATEGY.md#imp-10--choix-dimplémentation-et-passage-vers-10b).

Ce sous-corpus fonde la qualification diagnostique de 10B. Budgets, échéances, ventilation, autres devises et variantes historiques restent à éprouver.

### Consolidation et préparation — mission 9

| Résultat | Validation / réserve |
|---|---|
| Diagnostic CPU | Même PID/TID pour CPU et JIT ; compilation étagée transitoire démontrée, contre-vérification layout/GC et contrôles natifs répétés. Une reprise tardive non entièrement attribuée reste signalée ; pas de commutateur runtime livré |
| Démarrage et volumes | Sans dossier, vide, 12/10 000 opérations et démonstration ; deux thèmes, visibilité/minimisation, réseau isolé et actualisation. Objectif interactif <2 s non garanti ; HWND/UIA distincts du premier pixel composé |
| Corrections WPF | Contenu d'onglet clair lisible, rapprochement défilant à petite hauteur, champs nommés pour UIA ; 64 combinaisons contrôlées, 125 % matériel seulement |
| Fiabilité | 516 tests Debug/Release conservés ; parcours Windows clair/sombre, erreur d'écriture/brouillon/reprise, P/R et virements/frais, sauvegarde/restauration comparées ; aucun changement financier ou de schéma |
| Préparation native | [Source publique BankPerfect 9 MIT et V5](BANKPERFECT_FORMAT_RESEARCH.md) identifiée, [mapping et décisions](BANKPERFECT_MAPPING.md) documentés ; aucun corpus local, lecteur ou conversion réalisé |

[Mesures, méthode et limites](PERFORMANCE_AUDIT.md). B01 progresse grâce à la description publique pertinente ; **B02/B03 ne sont pas clos** : corpus légitime, preuve de version et conventions restent indispensables. La suite Mission 10 doit commencer par BP-F00/F01/F02/F03 et extensions réellement utilisées, puis confronter V5 et données aux attentes. Un lecteur passif futur ne sera pas présenté comme un import complet ; provenance, inconnus et pertes doivent rester explicites. Aucun budget, échéancier ni redesign lancé.


### Livraison des cours et valorisations — mission 8

| Résultat livré | Portée / limite |
|---|---|
| Découverte et acquisition | **Outils → Cours de change…**, table officielle avec nominaux ; source/date, Consulter le cache, Actualiser et Annuler la requête ; ancienne entrée Actualiser les cours officiels active |
| Choix de source | NBRB par défaut, CBR explicite ; préférence persistée dans **Outils → Options → Devises et taux** et dans la fenêtre de cours ; changement de banque = cache uniquement, aucun téléchargement ni repli/mélange automatique |
| Démarrage | Automatique désactivé par défaut ; option appliquée au prochain lancement, sans Internet obligatoire ni blocage des comptes ; cache du jour ≤ 6 h réutilisé, historique applicable réutilisé, actualisation manuelle forcée |
| Dates et absence de cours | Dernière publication d’effet ≤ date de valorisation, âge maximal 31 jours ; ancienneté/date réelle explicites, futur conservé mais non utilisé avant effet, cours manquant/périmé non inventé ; jusqu’à sept dates antérieures interrogées pour publication absente, pas de retry HTTP/réseau |
| Historique hors ligne | Cache SQLite distinct du financier, réponse originale et SHA-256, publications immuables/corrections ajoutées ; 100 entrées récentes affichées, archives plus anciennes consultables par date, aucune purge automatique |
| Montant et solde | Convertisseur de montant français et équivalent estimé du compte sélectionné ; natif toujours réel et visible, source/effet/récupération/cache affichés, arrondi final au pair ; aucune modification des écritures ou de l’état modifié |
| Calcul d’agrégat | API `ConvertTotal` : somme exacte puis arrondi unique, refus du total entier si une cotation manque ; **aucun écran Total des comptes ou relevé multicomptes livré** |
| Erreurs et sécurité | Annulation/génération empêchant une réponse périmée de remplacer le choix courant ; mémoire validée conservée après échec de cache, persistance non confirmée signalée ; HTTPS officiel fixe sans données financières, cookies/redirections, DTD/résolution XML externe interdits |
| Virements et persistance | Deux montants réels, frais, rapport et source déclarée figés ; aucune suggestion de cours appliquée ; schéma financier 5, Save, sauvegardes et restauration conservés |
| Preuves acquises | Debug/Release : 0 erreur/avertissement, **516 tests réussis par configuration**, aucun échec/ignoré ; clients .NET réels HTTP 200 NBRB/CBR, Windows-1251, parcours WPF clair/sombre, redémarrage, cache hors ligne, annulation sous réseau retenu et retour réseau ; refus des anciennes unités BYR/RUB, fichier financier entier inchangé. Mesures et limites dans le README |

La [documentation XML CBR](https://www.cbr.ru/development/SXML/) a été consultée et les deux endpoints publics effectivement vérifiés. La [page d’aide NBRB](https://www.nbrb.by/apihelp/exrates) a expiré dans l’outil de lecture ; réponses API vérifiées ne signifient pas contrat complet, quotas ou horaires garantis. F01 garde cette réserve documentaire. F02 est livré pour cours/cache, convertisseur de montant et solde individuel, **pas** taux manuel ni total d’interface. Le [README](../README.md#preuves-acquises-de-mission-8) sépare les preuves courantes des campagnes historiques 7C/7B ; leur nombre de tests et leurs performances ne sont pas ceux de mission 8.

**Priorité de consolidation avant le prochain pilote :** profiler l’activité CPU visible, élevée et variable au dernier intervalle mesuré ; aucun « CPU nul » ni correctif causal déclaré en mission 8. Qualifier aussi la fenêtre composée, les moniteurs/DPI et les conditions documentaires des fournisseurs. Le corpus `.bp` synthétique peut être préparé séparément ; aucun import, budget ou échéancier n’est commencé implicitement.

### Livraison des virements liés — mission 7C, historique

| Résultat livré | Validation / limite |
|---|---|
| Principal même devise ou multidevises | Deux comptes actifs distincts, montants principaux positifs natifs exacts ; égalité hors frais en même devise, deux montants réellement saisis sinon ; aucune conversion imposée |
| Rapport et source | Crédit/débit hors frais, reçu pour 1 unité source ; opérandes exacts conservés, quotient seulement approximatif ; date/source déclarées non vérifiées, pas de cours officiel |
| Frais séparés | Débit source/cible/troisième compte actif en devise native ; positif au premier ajout, zéro conservant UUID/compte/lien ensuite ; première jambe ajoutable, pas de retrait/remplacement |
| Cycle complet | Brouillons avant premier fichier, Save/SaveAs, révisions d’entité et de jambes indépendantes, refus demi-patch/édition isolée, suppression logique atomique ; comptes existants immuables |
| Interaction 7B | NP/P/R indépendants, correction P conservant date/état ; n’importe quelle jambe R, frais compris, protège l’ensemble ; réouverture/libération puis Save préalable |
| Schéma 5 / archives | Deux tables additives, `Transaction` et instantanés V1 inchangés ; migration 1..4 sur copie, représentation/historique et certificats originaux préservés ; digest/sauvegarde/restauration couvrant tout le graphe |
| Interface | Opérations → Virement et outil existant, Ctrl+Maj+T limité au registre ; ouverture/suppression d’ensemble et navigation UUID vers page de 100, commandes liées explicitement Moneta |
| Qualification | Debug et Release : 329 tests réussis, aucun échec ni ignoré ; parcours Windows synthétique clair/sombre de création/correction/frais, suppression du brouillon entier, P/R indépendants, consultation/refus R, Save, fermeture/réouverture, sauvegarde à chaud/restauration ouverte ; mesures et portée de l’inspection visuelle dans le README |

Le [README](../README.md#vérifications-observées-de-mission-7c) donne chiffres et portée des observations **historiques 7C**. Les **261 tests Debug/Release de 7B** ci-dessous sont historiques ; les 68 cas supplémentaires de 7C les complètent. Restent à qualifier : conventions utilisateur, rendu composé natif/accessibilité/multi-DPI, frontières matérielles de panne et volumes plus importants ; ces réserves ne signifient pas que les virements restent à implémenter.

**Suite recommandée à l’issue de 7C, désormais réalisée en mission 8 pour cours/cache et valorisation individuelle :** F01/F02 et séparation du rafraîchissement T02 ci-dessus. Aucun connecteur réseau ni import BP n’avait été ajouté à 7C ; ce constat historique n’exclut pas les connecteurs mission 8. La future provenance d’import doit relier des UUID Moneta stables à des identifiants source effectivement démontrés, jamais inventer un faux corpus BankPerfect.

### Livraison du pointage et rapprochement — mission 7B

| Résultat livré | Validation / limite |
|---|---|
| Pointage individuel/multiple | NP/P via colonne, P/Espace, commandes partagées ; lot atomique, dates séparées, futurs refusés pour nouveau pointage ; sélection souris/clavier et garde des champs texte exercées |
| Soldes exacts | Actuel = ouverture + actifs ; pointé = ouverture + actifs P/R ; non pointé = actifs NP ; devise native, aucune prévision ni valorisation fictive |
| Relevé bancaire | Ouverture du compte puis clôture précédente, fin cohérente, candidats et écart nul plus invariants de chaque référence/révision |
| Correction traçable | Dernier relevé actif rouvrable avec motif/confirmation ; états antérieurs restaurés, instantanés/événements conservés ; R héritées traitées explicitement sans relevé inventé |
| Frontière de sauvegarde | Lot de validation/réouverture/libération à enregistrer avant toute autre mutation du registre ; échec conserve le brouillon, navigation autorisée |
| Schéma 4 et archives | Migration additive 3 → 4 sur copie, représentation v3 conservée ; concurrence/empreintes et restauration incluent opérations, sessions, snapshots et événements |
| Qualification | 261 tests Debug/Release ; parcours WPF réel et comparaison des restaurations ; inspection des arbres visuels clair/sombre, réserve sur capture composée native et mesures détaillées dans le README |

**Contexte historique avant 7C :** la validation utilisateur des conventions NP/P/R et de la fenêtre composée, ainsi que le coût des candidats sur gros dossier, restaient à qualifier. Les virements liés, y compris multidevises et frais, ont depuis été livrés en 7C, puis cours/cache en mission 8 ; l’import `.bp` reste distinct.

### Livraison du registre financier — mission 7A

| Résultat livré | Validation / limite |
|---|---|
| Schéma 3 normalisé | Opérations/référentiels à UUID stables, précision entière exacte ; migration additive 2 → 3 sur copie et chaîne 1 → 2 → 3 |
| Recettes/dépenses et corrections | Dialogue validant, brouillons conservés après erreur, même UUID et révision attendue ; Ctrl+S seul commit durable |
| Suppression contrôlée | Confirmation, marqueur conservé dans SQLite/sauvegardes, aucune suppression financière depuis un champ texte ; pas encore de corbeille ou historique complet |
| Registre et soldes | Pages réelles de 100, virtualisation conservée, solde initial + mouvements actifs, aucun total de page présenté comme solde du compte |
| Référentiels | Catégories/sous-catégories, tiers et modes personnalisés réutilisables ; gestion avancée/renommage/modèles de tiers encore absents |
| Concurrence et sauvegardes | Tous les champs d’opérations/références/tombstones inclus dans les comparaisons ; SaveAs préserve aussi les opérations non affichées |
| Qualification | 168 tests Debug et Release, 97 cas antérieurs conservés ; parcours Windows réel Light/Dark, fermeture/réouverture, sauvegarde/restauration et comparaison complète ; mesures et limites dans le README |

La mission 7B ci-dessus prolonge ce registre sans remplacer ses invariants. « Rapproché » n’est plus une simple bascule manuelle ; un relevé formel est exigé pour les nouveaux R. Cette convention Moneta réversible reste à valider contre les habitudes utilisateur BankPerfect.

S01/U01 couvrent maintenant registre, rapprochements simples et virements liés 7C, pas ventilations/filtres ni toute la parité. S02 reste manuel ; S03 couvre les schémas connus **1/2/3/4/5** ; S04 conserve ses limites matérielles. Le corpus `.bp` synthétique et sa spécification restent nécessaires indépendamment du registre.

### Livraison de persistance — mission 6

| Résultat livré | Validation / limite |
|---|---|
| Dossier vierge, création et ouverture `.moneta` | Schéma 2, identité contrôlée, UUID et dates stables, isolation ; aucune injection des 3 200 opérations fictives |
| Comptes multidevises persistants | Plusieurs comptes EUR/BYN/RUB et même devise, registre extensible, montant entier exact ; aucune limite produit de cardinalité ; pas encore d'édition/suppression de compte |
| Enregistrement manuel / Enregistrer sous | Ajout en mémoire marqué modifié, commit atomique seulement sur Enregistrer ; nouvelle copie sans écrasement pour Enregistrer sous ; proposition à la fermeture |
| Sauvegarde et restauration manuelles | API SQLite, copie autonome et manifeste final ; restauration vers nouveau chemin, réouverture/comparaison complète, dossier courant conservé |
| Versionnement et erreurs | Migration connue du brouillon interne 1 → 2 sur copie ; refus de schémas/versions inconnus ; conflits externes et fichiers compagnons protégés |
| Résistance observée | Verrous, rollback, checkpoint bloqué puis reprise ; processus tué après commit WAL et pendant transaction non validée, récupération exacte ; pas une coupure électrique matérielle |
| Qualification | Résultats des tests, parcours WPF réel Light/Dark et mesures dans le README ; disque plein forcé et tous supports/moniteurs restent à qualifier |

**Ordre proposé après mission 6, conservé comme historique :** accepter le cycle manuel et le format sur dossiers synthétiques, ajouter les opérations, qualifier les sauvegardes puis change daté et transferts. Depuis, 7A/7B/7C ont livré registre, rapprochements et transferts sans attendre les cours officiels. Obtenir le corpus `.bp` fictif nécessaire à B02/B03 reste indépendant, sans remplacer l’objectif natif par CSV.

**État historique de mission 6 :** S01 couvrait dossiers/devises/comptes ; U01 attendait la saisie d’opérations et S03 une seule transition interne. Les extensions 7A/7B/7C et leur couverture courante sont précisées ci-dessus. S02 reste partiel sans cadence/rétention automatiques, S04 demeure une qualification ouverte malgré les preuves de processus interrompu, S05 n’est pas commencé.

### Consolidation indépendante — mission 6.1

97 tests Debug/Release, trois protections de session confirmées, six cas de sauvegarde/verrou/readonly ajoutés et classification d'erreur de source verrouillé corrigée. Diagnostic CPU : pic attribué au thread de compilation étagée .NET, sans preuve de boucle WPF ; toutes les occurrences prolongées précédentes ne sont pas expliquées avec certitude. Aucun commutateur de désactivation des optimisations ni refonte livré. [Rapport, mesures et réserves](AUDIT_MISSION_6_1.md).

La mission 7A a réalisé le registre simple et le schéma 3 ; 7B a ajouté pointage/rapprochements et schéma 4 ; **7C a livré les virements et leurs règles liées, schéma 5**, sans service de cours à cette date. La mission 8 ajoute ses connecteurs/cache séparés sans migration financière.


### Livraison de préparation fonctionnelle — mission 5

- Sélection de texte distincte de la sélection de ligne : cause WPF adorner reproduite, correction partagée et contrôles Light/Dark actifs/inactifs/readonly/désactivés observés.
- Comptes de démonstration ajoutables/sélectionnables en mémoire, sans plafond produit, plusieurs comptes par devise ; référentiel extensible et affichage sans taux inventé.
- Thèmes déplacés dans Affichage, préférences conservées ; cinq rubriques Options. Mise à jour et cours : choix enregistrables uniquement, aucun service actif.
- 29 tests Debug/Release et scénarios Windows ; performances et témoin de l'ancien commit dans le README. Les tests initiaux sont conservés.
- Cette livraison historique préparait la persistance/sauvegarde désormais livrée partiellement en mission 6. Services NBRB/CBR et mises à jour signées restent des lots séparés ; les préférences seules ne ferment aucun jalon d'importation native ou de durabilité.


### Livraison de présentation — mission 4

Disposition rétablie : ordre des menus confirmé par capture, outils, panneau gauche réglable, registre avec modes et catégories à deux niveaux, filtres désactivés et ligne compacte readonly. Thèmes Light/Dark fonctionnels, contraste Windows prioritaire, pictogrammes originaux et aucun package graphique. Calculs et 12 tests de mission 3 conservés ; aucune persistance, importation ou prévision ajoutée.

Les résultats Windows, conditions de mesure et limites de qualification figurent dans le [README](../README.md). Les essais de mise à l'échelle synthétiques ne remplacent pas une campagne sur plusieurs moniteurs physiques. L'étape suivante reste la validation visuelle utilisateur, puis le lot financier/persistance autorisé séparément ; la refonte ne clôt aucun jalon natif `.bp` ou de durabilité.


### Livraison intermédiaire autorisée — mission 3

| Élément | État vérifié / limite |
|---|---|
| Solution et quatre projets | Moneta.slnx ; App WPF, Core, Infrastructure de données fictives, Tests MSTest ; SDK 10.0.401 présent, aucune dépendance système installée |
| Navigation en mémoire | Quatre comptes EUR/BYN/RUB/épargne, changement réel de relevé, sélection/détail, dates et nombres français ; solde initial et flux calculés en decimal, aucune consolidation de devises |
| Interface | Huit colonnes, clavier, défilement 3 200 lignes EUR, colonnes redimensionnables, détail en lecture seule ; menus futurs désactivés, ordre provisoire annoncé |
| Validation | Debug/Release : zéro erreur/avertissement ; 12 tests réussis dans chaque configuration ; UI Automation et inspection visuelle à 125 %, fenêtre normale/minimale ; fermeture par menu vérifiée |
| Performances | Mesures réelles et limites dans le README ; démarrage à chaud observé sous 2 s, objectif RAM <100 Mo non atteint ; autres facteurs DPI à tester |
| Stockage et migration | Aucun fichier `.moneta`, fournisseur SQLite, lecteur `.bp`, API de taux ou sauvegarde implémenté ; aucun jalon natif/persistance acquis |

Ce lot constitue une **validation partielle des intentions U01**, pas une réalisation du parcours ouvrir/saisir/enregistrer. Il ne clôt ni B03, ni D01–D03, ni S01–S05, ni U01–U04 dans leur totalité. L'autorisation de mission 3 permet ce prototype sans attendre le diagnostic natif ; elle ne contourne pas les portes de sûreté avant utilisation financière réelle.


## 1. Avant toute programmation : preuves et validation utilisateur

| ID / priorité | Livrable proposé | Dépendances | Critère de validation précis |
| --- | --- | --- | --- |
| E01 / P0 | Compléter l'inventaire documentaire avec version, source, fonction, écran/menu/raccourci et niveau de certitude. Séparer manuel, script local, observation utilisateur et proposition Moneta. | Audit et matrice de parité documentaires disponibles. | Chaque besoin possède une preuve liée ou le statut « inconnu » ; pas de menu complet, ordre complet ou écran exact déclaré observé sans capture. Les pages modifiées en 2016 ne sont pas attribuées sans preuve à une version de 2015. |
| E02 / P0 | Demander à l'utilisateur des captures légitimes de son BankPerfect sur un **dossier synthétique**, sans ouvrir ses comptes privés : fenêtre principale, menus, saisie intégrée/séparée, comptes, pointage, filtres, champs supplémentaires, transfert, échéancier, budget, rapports, bourse, sauvegarde/restauration et options pertinentes. | E01. | Captures sans données personnelles, version et scénario notés ; champs, libellés, ordre et états lisibles ; éléments absents de capture conservés comme inconnus. La mission présente ne lance aucun binaire pour les obtenir. |
| E03 / P0 | Définir avec l'utilisateur un jeu synthétique de référence : deux dossiers indépendants ; plusieurs comptes EUR/BYN/RUB ; soldes initiaux connus ; crédits/débits identiques légitimes ; dates égales, accents/séparateurs ; trois états de pointage ; ventilation ; transferts avec/sans frais ; échéances et champs personnalisés ; titres si utilisés. | E01 ; E02 pour les écrans nécessaires. | Liste de données et résultats attendus approuvée, sans identité bancaire réelle ; totaux de contrôle débit/crédit, soldes finaux et soldes pointés/rapprochés par compte/devise ; relations attendues et scénarios d'erreur explicités. Aucun montant de devises différentes additionné sans politique explicite. |
| E04 / P0 | Caractériser les exports neutres du jeu synthétique : CSV/QIF/XML et autres formats nécessaires ; comparer compte actif/visible et mode tous comptes ; inventorier transformations et informations absentes. | E03 ; exports fournis par l'utilisateur. | Pour chaque format/options, tableau champs présents/absents/transformés, compte(s), filtre(s), encodage, dates et nombre de lignes. XML multicomptes et option CSV multicomptes distingués du cas visible ; absence de liens/champs/échéances signalée. Aucun export présumé sauvegarde intégrale. |
| E05 / P0 | Arbitrer divergences documentaires et comportements à risque : sauvegarde explicite/automatique et annulation, FTP/mail/raccourcis, inversion des monnaies, actualisation des titres, simulation d'échéances insérant des lignes, doublons, lignes inutiles, menus divergents. | E01–E04. | Décision utilisateur datée pour chaque écart : comportement historique, proposition sûre, effet visible, raccourcis conservés/modifiés et raison. Les désaccords sur menus Remplacer/Reçu, totaux par tiers et critères de masquage ne sont pas gommés par une supposition. |
| E06 / P0 | Obtenir l'autorisation distincte de commencer la programmation après examen des inconnus ; confirmer modalités de stockage/sauvegarde, arrondis, couverture BP et écarts de parité, pas redemander le choix de stack déjà décidé. | E01–E05 ; diagnostic B03 ; architecture et FILE_FORMATS. | Accord utilisateur explicite sur les parcours et risques restants, licences candidates et lots autorisés. Si B03 est bloqué, aucune promesse de date native ni substitution CSV implicite ; une poursuite séparée doit être décidée. **Sans autorisation, aucun projet ni application provisoire.** |

Les sources de ces portes sont le [fichier multi-comptes](https://chelly.net/bankperfect/wiki/La%20gestion%20des%20comptes.html), les [opérations et trois états de pointage](https://chelly.net/bankperfect/wiki/Les%20operations%20de%20base.html), les [champs supplémentaires](https://chelly.net/bankperfect/wiki/Les%20champs%20supplementaires.html), les [raccourcis](https://chelly.net/bankperfect/wiki/Recapitulatif%20des%20raccourcis%20clavier.html) et les [imports/exports documentés](https://chelly.net/bankperfect/wiki/Import-export-donnees.html). Les exceptions locales sont établies par `reference-bankperfect/Scripts/Export XML/export_xml.py:22-40` et `reference-bankperfect/Scripts/Export CSV/export_csv.py:35-69,177-186`. Ces constats motivent E04 ; ils ne prouvent pas un aller-retour sans perte.

### Voie native `.bp` prioritaire

| ID / priorité | Livrable proposé | Dépendances | Critère de validation |
|---|---|---|---|
| B01 / P0 | Rechercher la documentation publique, exploiter les informations de l'API des scripts déjà audités et confronter les exports/imports, dans cet ordre. | E01 ; audit initial et stratégie d'importation. | Registre des sources/méthodes, faits/inférences/inconnus séparés ; aucune API hôte présentée comme schéma binaire ; limites des exports explicites. L'annonce historique d'une protection ne devient pas un algorithme supposé. |
| B02 / P0 | Recevoir le corpus strictement fictif puis comparer des changements isolés et plusieurs sauvegardes du même état. | B01 ; E03 ; production des fichiers par l'utilisateur ou autorisation distincte d'exécution. | Cas vide/compte/opérations/multicomptes/virement/pointage/catégories/ventilation/échéancier/devises, plus budgets/notes/champs/paramètres, selon le protocole ; SHA-256, tailles, valeurs attendues et différences observées consignés. Sans fichier : « non produit/non observé », jamais un résultat inventé. |
| B03 / P0 | Établir la spécification empirique du format de données et le diagnostic de faisabilité légale/technique, sans programmer le lecteur. | B01–B02 ; E04 comme témoins de contenu. | Versions et grammaire étayées par corpus indépendant, champs/reliquats inconnus et protections décrits. Analyse binaire uniquement de copies fictives, sans exécution ni contournement. Si protection/méthode intrusive requise : arrêt et validation préalable, pas de décodage de force. |
| B04 / P0 | Programmer ultérieurement le lecteur indépendant des seules versions `.bp` démontrées ; staging passif et provenance. | E06 ; B03 ; D01–D03. | Corpus à attentes exactes, fichiers tronqués/versions inconnues/tailles malformées rejetés proprement ; source jamais modifiée ; aucun pickle, règle ou composant BankPerfect exécuté. Chaque objet attendu identifié, pas seulement un nombre d'opérations non nul. |
| B05 / P0 | Réaliser Fichier > Ouvrir `.bp`, proposition de conversion, rapport puis création protégée d'un nouveau `.moneta`. | B04 ; U01–U02 ; S01–S03 ; T01 pour transferts. | Empreintes originales inchangées, stabilité source démontrée, destination sans écrasement ; rapport comptes/opérations/soldes/relations/inconnus, états complet/partiel/bloqué ; annulation/erreur ne publie pas de fausse réussite. Les données dont le module cible manque interdisent un résultat « complet ». |
| B06 / P0 | Qualifier la conversion native de bout en bout sur versions ciblées et jeux indépendants. | B05 ; S04 ; P01–P05 pour toutes les données transportables. | Toutes les rubriques du rapport et de la matrice de données confrontées aux valeurs attendues ; exactitude des soldes et des relations vérifiée ; sauvegarde/restauration et réouverture du `.moneta` réconciliées ; aucune information perdue déclarée reprise. Version non couverte explicitement refusée. |

État 10A : B01 et la spécification sourcée progressent ; **B02/B03 disposent de quatre fixtures comparées**, mais budgets/échéances/ventilation/devises/protections, répétitions et autres versions restent à qualifier. B04–B06 attendent autorisation et règles acceptées. Ni ces quatre succès de lecture ni un export multicomptes ne certifient la migration complète.

### Identité originale, validation séparée

| ID / priorité | Livrable proposé | Dépendances | Critère de validation |
|---|---|---|---|
| G01 / P1 | Étudier ultérieurement des directions graphiques originales et la lisibilité, sans changer l'organisation de navigation. | E02 ; VISUAL_IDENTITY ; validation séparée du travail graphique. | Choix utilisateur de la direction, des conventions et des essais ; aucune ressource BankPerfect ni pack réutilisé ; aucune identité définitive présumée dans cette mission. |
| G02 / P1 | Créer et valider les ressources originales puis vérifier leur provenance dans toute la chaîne de livraison. | G01 ; autorisation du lot graphique ; E06 avant intégration applicative. | Icônes et futures ressources documentées, cohérentes et lisibles ; contraste/focus/High Contrast et DPI 100/125/150/200 % exercés ; aucun actif de la référence dans source, compilation, documentation graphique distribuée ou installateur. |


## 2. Domaine exact et contrat de calcul — après autorisation seulement

| ID / priorité | Livrable proposé | Dépendances | Critère d'acceptation futur |
| --- | --- | --- | --- |
| D01 / P0 | Spécifier montants, sens, devises/périodes, échelles, quantités/prix, taux, bornes et arrondis ; séparer dates métier et horodatages UTC. | E06 ; E03 et arbitrages E05. | Exemples chiffrés approuvés pour 0,1 + 0,2, valeurs positives/négatives, milieu d'arrondi ±0,005, échelles 0/2/3, limites Int64 et decimal, division non finie et valeur non représentable. Montants comptabilisés exacts en unités mineures ; taux/prix textuels invariants exacts ; aucune dépendance à la culture de l'OS. |
| D02 / P0 | Réaliser ultérieurement le domaine et ses tests de calculs indépendants de WPF/SQLite. | D01. | Résultats exacts attendus, débordement et division par zéro visibles, aucune conversion par `double`/`float`, arrondi final explicite et politique identifiée. Toute fraction rejetée est expliquée, pas tronquée ; aller-retour sérialisation décimale sans altération ; culture fr-FR/invariante et dates autour des changements d'heure couvertes. |
| D03 / P1 | Définir opérations, catégories/sous-catégories, tiers modèles, modes, trois états de pointage et ventilation ; cycle de correction/suppression selon décision utilisateur. | D01–D02 ; E03. | Solde initial + crédits − débits = solde attendu par devise ; états et dates préservés ; ventilation totalisant exactement l'origine, sans double comptabilisation ; répartition négative traitée selon cas approuvé ; renommage de référentiel ne casse pas les opérations. Une correction volontaire conserve révision antérieure, date et raison ; aucun recalcul de cours/règle ne modifie implicitement l'historique. |

Fondements techniques : [précision de decimal](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/floating-point-numeric-types), [arrondi explicite](https://learn.microsoft.com/en-us/dotnet/api/system.decimal.round?view=net-10.0), [types SQLite et affinités](https://www.sqlite.org/datatype3.html), [agrégats et débordement](https://www.sqlite.org/lang_aggfunc.html#sumunc). Les critères ci-dessus ne sont pas une revendication de tests déjà exécutés.

## 3. Persistance fiable avant données utilisateur

| ID / priorité | Livrable proposé | Dépendances | Critère d'acceptation futur |
| --- | --- | --- | --- |
| S01 / P0 | Choisir fournisseur/version SQLite corrigés et maintenus ; réaliser format `.moneta`, identité/version, schéma contraint, clés et transactions. | E06 ; D01–D03 ; FILE_FORMATS. | Deux dossiers isolés, plusieurs comptes par dossier ; fichier SQLite arbitraire ou version future non ouvert en écriture ; relecture exacte des montants/taux ; contraintes et références actives ; aucune somme financière flottante. Le correctif WAL requis est vérifié dans le moteur réellement embarqué, pas déduit du seul nom du package. |
| S02 / P0 | Réaliser sauvegarde automatique cohérente par API SQLite, déclenchements/cadence et rétention validés, manifeste minimal, vérification et restauration vers une copie distincte avant bascule. | S01 ; arbitrage E05 sur l'enregistrement. | Déclenchements et échec de destination externe observés, hors ligne compris. Sauvegarde prise alors que des commits existent en WAL, puis restauration réelle réussie ; empreinte, `integrity_check`, `foreign_key_check`, comptes, soldes, dates, champs et liens concordants. Destination inaccessible/disque plein ne détruit ni source ni dernière sauvegarde valide ; fichier partiel jamais annoncé valide. |
| S03 / P0 | Réaliser migrations numérotées et reprise contrôlée, sauvegarde validée avant migration. | S01–S02. | Migration de chaque version supportée avec mêmes comptes/soldes/liaisons ; interruption à chaque étape et manque d'espace sans perte de l'original ; réouverture d'une version future en écriture refusée clairement ; aucun retour arrière implicite ni création de base vide sur erreur. |
| S04 / P0 | Qualifier atomicité et durabilité locale : commit, crash, arrêt brutal, disque plein, erreur d'E/S, verrouillage, ouverture concurrente et croissance WAL. | S01–S03 ; transferts complets T01 pour les scénarios bilatéraux. | À chaque point de panne simulé sur base synthétique : état avant ou après commit, jamais demi-commande ; aucune réussite affichée avant commit ; données validées retrouvées après reprise ; brouillon non sauvegardé distingué ; restauration S02 encore possible. Version SQLite corrigée, mode journal/synchronisation effectifs et environnement de stockage consignés. |
| S05 / P1 | Préparer ultérieurement l'association Windows `.moneta` consentie, après existence d'une application réellement ouvrable. | E06 ; S01 ; U01. | Ouverture d'un chemin avec espaces/Unicode comme un argument unique ; fichier inexistant ou incompatible sans création implicite ; choix par utilisateur respecté, UserChoice non forcé ; `.bp` non réassocié. Aucune clé de registre ni aucun installateur créé dans la mission documentaire. |

L'[Online Backup API](https://www.sqlite.org/backup.html) fournit une copie cohérente ; copier à chaud le seul `.moneta`, ou ses auxiliaires séparément, ne remplace pas cette procédure. Les [contraintes WAL](https://www.sqlite.org/wal.html) imposent un stockage local ; les [contrôles d'intégrité](https://www.sqlite.org/pragma.html#pragma_integrity_check), [références](https://www.sqlite.org/pragma.html#pragma_foreign_key_check) et invariants métier sont distincts. [FILE_FORMATS.md](FILE_FORMATS.md) porte les règles de checkpoint, fermeture, récupération et restauration réellement exercée. S04 qualifie le socle ; les branches peuvent avancer après S01–S03 sans prétendre S04 acquis.

## 4. Écrans, raccourcis et parité du parcours principal

| ID / priorité | Livrable proposé | Dépendances | Critère d'acceptation futur |
| --- | --- | --- | --- |
| U01 / P1 | Réaliser le parcours WPF/MVVM principal à partir des captures approuvées : ouvrir/créer dossier, choisir compte, tableau, saisie intégrée/séparée, ajout/modification/suppression, solde et état sauvegardé. | E02–E06 ; D03 ; S01–S03. | Parcours synthétique complet sans souris lorsque prévu ; focus, ordre de tabulation, validation/annulation, dates et montants identiques aux cas approuvés ; Ctrl+Entrée ajoute et Maj+Entrée modifie sans confusion ; le comportement Ctrl+S/fermeture respecte E05. Aucune capture historique manquante inventée. |
| U02 / P1 | Réaliser modes/tiers/catégories, autocomplétion, pointage/rapprochement, dates de pointage, ventilation et champs supplémentaires. | U01 ; D03. | Les trois états et F11, F3, F6, F10 sont confrontés au scénario approuvé ; saisie/modification des champs sans perte ; sommes ventilées exactes ; déplacement entre lignes conserve la sélection et ne valide pas un brouillon accidentellement. |
| U03 / P1 | Réaliser filtres, masquage, tri, sélection multiple, modification de groupe, recherche/remplacement, annuler/refaire, notes et impression utiles selon inventaire validé. | U01–U02 ; E05 pour les divergences. | Même jeu de lignes attendu pour chaque filtre/composition ; annulation des filtres retrouve toutes les lignes ; aucun masquage assimilé à suppression ; totaux et état du filtre visibles ; raccourcis et menus de la matrice approuvée exercés, y compris interactions avec champs de saisie. Historique annuler/refaire n'abîme ni transferts ni données enregistrées. |
| U04 / P1 | Revue utilisateur écran par écran et inventaire clavier, accessibilité et affichage Windows. | U01–U03 ; T01 et F02 pour les commandes concernées. | Chaque entrée de parité du parcours principal porte résultat, preuve et décision ; tailles de fenêtre, mise à l'échelle, clavier français et absence de souris contrôlés. Aucune commande visible ne reste un bouton sans effet ; pas de raccourci déplacé pour raison de sécurité sans accord E05. |

Le [récapitulatif clavier](https://chelly.net/bankperfect/wiki/Recapitulatif%20des%20raccourcis%20clavier.html) est une base documentaire à confronter aux captures, pas une garantie d'identité avec tous les INI locaux. Les outils historiques comprennent notamment [prêt, conversion, reçu et ventilation](https://chelly.net/bankperfect/wiki/Outils%20supplementaires.html) ; ils sont couverts également par P04/P05, et non implicitement supprimés parce qu'absents du premier écran.

## 5. Imports et migration contrôlée

| ID / priorité | Livrable proposé | Dépendances | Critère d'acceptation futur |
| --- | --- | --- | --- |
| I01 / P1 | Réaliser les lecteurs de données neutres de parité (CSV, QIF, OFX, XML selon contrat), en voie secondaire distincte du natif BP. | E04–E06 ; D01–D03 ; S01–S03. | Cas valides/malformés : dates ambiguës, encodages, séparateurs, décimales, guillemets/multilignes, débit/crédit, champs inconnus et bornes. Chaque ligne a résultat/erreur ; aucune exécution de formule/script ni entité externe ; abandon sans écriture comptable. Ces lecteurs ne satisfont pas B04/B05 par substitution. |
| I02 / P1 | Réaliser provenance, doublons candidats, prévisualisation et validation atomique d'un lot. | I01 ; U01. | Deux opérations légitimement identiques ne sont jamais supprimées automatiquement ; import répété identifiable et expliqué ; décisions « importer / ignorer / différer » consignées ; ligne ignorée/rejetée comptée dans le rapport ; interruption avant/après commit sans lot partiel caché. |
| I03 / P0 | Piloter la migration BankPerfect native en priorité ; documenter séparément toute reprise partielle par export expressément acceptée. | B06 ; E04 ; U02 ; T01 ; P01–P05 pour les données présentes ; I01–I02 seulement pour la voie exports. | Comptes, monnaies, nombres de lignes, soldes initiaux/finaux/pointés/rapprochés, relations, ventilation, tiers modèles, catégories, échéances, budgets, notes, champs et paramètres confrontés au corpus. Empreinte `.bp` inchangée, rapport accepté, nouveau `.moneta` restaurable. Aucun résultat partiel présenté comme complet. |

Le [manuel](https://chelly.net/bankperfect/wiki/Import-export-donnees.html) décrit un critère historique de doublon date/mode/montant : il ne prouve pas l'identité de deux transactions. Les limites XML/CSV locales citées en E04 rendent nécessaire un rapport de pertes même lorsque les soldes concordent. Les données `cPickle` de l'échéancier (`reference-bankperfect/Scripts/Scheduler/scheduler.py:21-56`) ne doivent **jamais** être désérialisées depuis une entrée non fiable ; aucune évaluation de règles legacy pour compléter un import. Les fichiers annexes et données d'extensions nécessitent un inventaire distinct, pas une assimilation à des opérations exportées.

## 6. Transferts et cours officiels livrés — limites de parité

| ID / priorité | Livrable et statut | Dépendances | Critère / portée |
| --- | --- | --- | --- |
| F01 / P0 | **Implémenté en mission 8 pour les flux quotidiens NBRB/CBR** : référence, nominal, date d’effet réelle et encodage contrôlés ; documentation fournisseur encore partielle. | E06 ; D01 ; observations sourcées de l’architecture. | Endpoints réellement vérifiés avec le client .NET, documentation XML CBR lue ; aide NBRB en timeout, quotas/horaires/conditions et histoire complète des codes non démontrés. Ne pas assimiler un GET à un contrat complet ni fusionner BYR/BYN. |
| F02 / P1 | **Réalisé mission 8** : acquisition manuelle/automatique optionnelle, cache/historique, conversion indicative EUR/BYN/RUB et devises du dossier, solde individuel. **Restent futurs : taux manuel et total dans l’interface.** | F01 pour le contrat complet ; D02 ; S01–S03 ; U01. | Nominal/orientation et source/date commune, dernier antérieur ≤ 31 jours affiché, futur exclu avant effet, erreurs/hors ligne explicites ; aucun taux de repli fictif ni mélange/fallback NBRB/CBR. API d’agrégat avec arrondi final unique disponible, aucune parité Total des comptes UI déclarée. |
| T01 / P1 | **Réalisé en 7C**, conservé en mission 8 : transferts intradossier même devise/multidevises, frais, rapport effectif exact et navigation ; correction/suppression d’ensemble. | Domaine exact, stockage et 7B ; indépendant du service de cours. | Deux jambes principales et frais séparés, montants réels conservés, comptes distincts/immuables, crédit/débit hors frais, source déclarée ; brouillons/Save/SaveAs, protection R globale, demi-patches refusés, digest/restauration du graphe complet. **Aucune suggestion de cours officielle appliquée**. |
| T02 / P0 | **Séparation opérationnelle en mission 8** : taux officiel de valorisation sans modification du taux effectif, des montants ou frais réels. Qualification utilisateur et matérielle toujours distincte. | F02 ; T01 ; S04. | Régressions ciblées financières/P/R/schéma 5 et octets financiers/WAL inchangés réussies ; preuves courantes dans le README. La date/source de valorisation peut varier, pas les écritures. Ne pas déduire une conversion historique BYR/BYN ni une qualification Windows non observée. |

Les [observations FX de l'architecture](ARCHITECTURE.md#5-devises-officielles-historique-et-fonctionnement-hors-ligne) distinguent les GET NBRB (échelles EUR 1/RUB 100), les [contrats XML CBR](https://www.cbr.ru/development/sxml/) et les limites documentaires restantes. Le transfert historique reste intrafichier ; ne pas étendre implicitement l'atomicité à plusieurs dossiers.

## 7. Complétude de parité : échéances, budgets, rapports, bourse, extensions

| ID / priorité | Livrable proposé | Dépendances | Critère d'acceptation futur |
| --- | --- | --- | --- |
| P01 / P1 | Réaliser échéancier, récurrences, rattrapage à l'ouverture, insertion manuelle et simulation ; transferts récurrents si confirmés. | D03 ; S01–S03 ; U02 ; T01 pour transfert récurrent ; scénarios E03. | Exemple documenté d'une récurrence le 13 rattrapée en mai/juin/juillet ; ouverture répétée sans doublon ; insertion manuelle et simulation sans avancer à tort la prochaine occurrence ; fin de mois/29 février/fin de série et modification de série validées. Interruption après génération sans duplication ni demi-transfert. |
| P02 / P1 | Réaliser budgets mensuels et annuels nommés, prévisions selon modes et périmètres confirmés, avec catégories/sous-catégories et comptes concernés. | D03 ; U03 ; P01 pour prévisions dépendant de l'échéancier. | Budget type, tableaux à douze mois Budget/Réel/Écart, mois/année, « mois inclus / mois calculé », lignes non budgétées/à zéro et sous-catégories confrontés aux exemples approuvés ; réalisé et prévisionnel distincts ; pas de double comptage des ventilations/transferts ; devises agrégées uniquement selon politique validée. |
| P03 / P1 | Réaliser rapports, totaux catégories/tiers, vues tous comptes, export/impression et analyses confirmées. | U03 ; P02 ; F02 pour valorisations multidevises. | Chaque total se réconcilie avec les mêmes lignes et filtres du tableau ; impression/export indiquent périmètre/date/devise ; montants exacts ; aucun graphique de données dupliqué ajouté sans besoin validé. Solde transféré entre comptes non compté comme revenu nouveau du dossier. |
| P04 / P2 | Réaliser besoins boursiers réellement vérifiés : titres, quantités/prix exacts, cotations datées séparées, affichages et éventuelles transactions retenues. | E03/E05 pour bourse ; D01–D02 ; S01–S03 ; U02 ; P03. | Jeu synthétique boursier et règles de quantité/prix acceptés ; actualiser une cotation ne réécrit pas le prix d'origine sans décision explicite ; pas de mélange d'une quantité et d'un montant en unités mineures ; données hors ligne datées. Toute source de cours est réelle, autorisée et caractérisée ; fournisseur historique non supposé encore disponible. |
| P05 / P2 | Couvrir chaque autre outil/extension de l'inventaire validé : saisie multiple, calcul de champs, prêts/amortissement, reçus/RIB, notes et fonctions complémentaires effectivement requises ; implémentation indépendante uniquement. | E01–E06 ; U03 ; D02 ; S01–S03 ; P01–P04 selon fonction. | Pour **chaque** entrée, scénario synthétique, sortie attendue et résultat utilisateur documentés. Aucun lancement de Python/commande historique, copie de script/asset ou chargeur de plugins hérités ; aucune fonction déclarée couverte par un simple bouton ou un export sans contenu. Un besoin non réalisé reste une non-parité bloquante, sauf retrait de périmètre explicitement accepté. |
| P06 / P0 | Clore toute la matrice de parité et les écarts de sécurité avant pilote réel. | U04 ; I03 ; T02 ; P01–P05. | Chaque ligne : réalisé et vérifié, ou différence explicitement arbitrée avec l'utilisateur ; zéro « supposé identique », commande sans effet ou perte ignorée. Les écarts FTP/mail, sauvegarde/annulation, inversion de devise et actualisation des titres ont une décision distincte. |

Sources de comportement : [échéancier](https://chelly.net/bankperfect/wiki/L-echeancier.html), [budget](https://chelly.net/bankperfect/wiki/La%20fonction%20Budget.html), [bourse](https://chelly.net/bankperfect/wiki/Les%20fonctions%20boursieres.html), [outils supplémentaires](https://chelly.net/bankperfect/wiki/Outils%20supplementaires.html). Le manuel boursier décrit une actualisation du montant ; la séparation historique/valorisation proposée ne peut donc pas être déclarée strictement identique sans arbitrage E05.

## 8. Pilote, restauration/reprise et non-régression

| ID / priorité | Livrable proposé | Dépendances | Critère d'acceptation futur |
| --- | --- | --- | --- |
| Q01 / P0 | Répéter migration native, sauvegarde/restauration et reprise sur synthétique, sur Windows indépendant du poste de développement. | P06 ; S04 ; I03 ; B06. | `.bp` fictif → analyse → rapport → nouveau `.moneta` → fermeture/réouverture → sauvegarde → restauration indépendante → comparaison complète : mêmes comptes/soldes/relations/échéances/champs ; original inchangé ; dernière sauvegarde valide préservée après incident. Une sauvegarde jamais restaurée n'est pas qualifiée. |
| Q02 / P0 | Pilote utilisateur privé avec originaux et ancienne méthode conservés, procédure de retour définie ; aucune publication de données réelles. | Q01 ; accord utilisateur et politique de confidentialité/sauvegarde. | Rapport comptes/soldes/opérations/relations accepté ; anomalies bloquantes résolues ; panne/disque plein et restauration démontrées ; ancienne référence toujours disponible. Pas de bascule définitive si une information nécessaire n'est pas reprise et acceptée explicitement. |
| Q03 / P0 | Non-régression finale, mises à jour de format et maintenance .NET/SQLite ; documentation des écarts, association et ressources originales. | Q01–Q02 ; S05 ; G02 ; version identifiée. | Rejouer scénarios et migrations depuis chaque format supporté ; calculs, raccourcis, natif BP, imports répétés, cache, transferts, échéances et restauration conformes. Moteur corrigé/licences revalidés ; aucun secret, code ou ressource propriétaire, donnée privée ou fausse déclaration de capacité. |

## Conditions transversales de passage

| Règle proposée | Dépendances | Validation exigée à chaque porte concernée |
| --- | --- | --- |
| Aucune perte silencieuse, pas de fausse réussite. | Toutes les étapes à partir de D01. | Toute entrée refusée, transformée, manquante ou ambiguë est visible et comptée ; toute action durable a un commit réussi ; tout écart financier possède une explication. |
| Clean-room, licences et référence intacte. | E01 puis chaque ajout technique. | Sources comportementales traçables ; aucune copie de code/scripts/assets propriétaires ; aucune modification de la référence ou lecture d'`identity.dat` ; dépendances futures justifiées et licences consignées ; jamais de désérialisation pickle non fiable ou d'évaluation legacy. |
| Validation utilisateur des changements de parité pour sécurité. | E05 puis chaque divergence nouvelle. | Décision explicite avant mise en œuvre d'un comportement différent, y compris raccourcis ; ne pas reproduire aveuglément FTP clair/mail non chiffré ni présenter un retrait comme une parité. |
| Preuve proportionnée, sans extrapolation. | Chaque critère d'acceptation. | Lien/source, scénario, version, résultat attendu/réel et limites documentés ; une réponse API ponctuelle ne vaut pas disponibilité garantie, une base intègre ne vaut pas migration métier complète, des soldes égaux ne prouvent pas les liens. |

**Suite proposée après mission 3 :** revue visuelle utilisateur et captures BankPerfect, qualification DPI restante et profilage mémoire ; puis spécification des invariants/arrondis et lot SQLite avec migrations, sauvegarde/restauration exercée avant toute donnée réelle. La voie native attend toujours les fichiers fictifs et B02/B03. Toute nouvelle réalisation financière doit être explicitement autorisée ; le prototype ne justifie ni import natif inventé ni persistance prématurée.

## 9. Estimation de planification demandée

**[INFERENCE — estimation d'ingénierie, pas engagement ni mesure.]** Hypothèses : un développeur senior C# à temps plein, retours réguliers de l'utilisateur, Windows local, versions BP ciblées définies, corpus fictif disponible et format natif légalement lisible sans contournement. Le calendrier commence après autorisation de programmation ; attente de fichiers, arbitrage juridique ou information propriétaire indisponible non comprise.

| Résultat visé | Fourchette indicative | Condition de sortie |
|---|---|---|
| Première version utilisable pour les parcours quotidiens, incluant **conversion native BP des versions/cas approuvés**, comptes/opérations/référentiels/pointage, transferts EUR/BYN/RUB, cours/cache et sauvegarde/restauration | **4 à 7 mois** | Compatibilité démontrée sur les données utilisées, réconciliation et reprise après incident ; aucune perte acceptée implicitement. Ce jalon n'est pas une déclaration de parité complète. |
| Parité étendue et qualification complète, budgets/échéances/rapports/bourse/outils inclus selon matrice, déploiement et documentation de maintenance | **8 à 12 mois au total** | P06 et Q01–Q03 satisfaits ; toute différence fait l'objet d'un arbitrage, pas d'une suppression du besoin. |

Le format `.bp` est le risque dominant : **aucune date fiable du lecteur natif n'est possible avant B03**. Si le corpus révèle chiffrement/protection ou champs non interprétables, réestimer après décision autorisée ; la borne haute n'est pas une garantie de compatibilité. Ne pas annoncer « première version utilisable avec migration » en livrant uniquement un import CSV ou un écran sans mécanisme réel.
