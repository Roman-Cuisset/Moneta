# Stratégie de conversion native BankPerfect vers Moneta

## État courant — mission 10A

**Étude réalisée, lecture BP8 qualifiée sur quatre fixtures, aucun importeur Moneta livré.** BankPerfect 9.13.0 MIT, révision `990f5c7f712dab07c7fefc2921cbf60beee7f6b1`, étudié séparément ; onze tests amont ciblés réussis. Réparations permissives et deux panics démontrées sur texte synthétique : aucune adoption aveugle de ce lecteur comme moteur de migration.

V5 décrit par code et **F00..F03 réellement produits dans BP8 8.0.0.373**, comparés sans écriture Rust : [recherche](BANKPERFECT_FORMAT_RESEARCH.md), [fiches](BANKPERFECT_FIXTURES.md), [matrice](BANKPERFECT_COMPATIBILITY_MATRIX.md). Après les échecs initiaux, le paquet officiel a été installé interactivement dans le seul invité, fournissant une identité de distribution propre ; identité privée et référence intactes. Le pointage/date et les liens sont observés ; **0,0001 EUR réellement conservé exige un blocage explicite dans le modèle en centimes actuel**. Compatibilité générale BP8 non présumée.

Les contrats IMP-01..IMP-09 et la matrice exhaustive COV restent applicables. **IMP-10 ci-dessous** compare les trois stratégies et précise le pipeline futur et la porte 10B. Les méthodes envisagées ne sont pas des fonctions déjà implémentées ; SQLite financier schéma 5 inchangé.

**Historique à l'issue des missions 2/9 :** stratégie documentaire, sans lecteur/conversion `.bp` Moneta ni fichier BankPerfect synthétique ; aucun binaire historique n'avait alors été exécuté. Les essais isolés et les tests amont de **10A** sont décrits ci-dessus et dans la recherche, sans modification de la référence ni consultation du `identity.dat` privé. Voir [README](../README.md) pour les fonctions Moneta existantes.

Documents de départ lus : [BANKPERFECT_AUDIT.md](BANKPERFECT_AUDIT.md), notamment sections 5–7 ; [ARCHITECTURE.md](ARCHITECTURE.md), modèle et persistance ; [FUNCTIONAL_PARITY.md](FUNCTIONAL_PARITY.md), toutes les fonctions et ajouts demandés. Il ne s'agit pas d'un nouvel audit. Le format cible indépendant est décrit dans [FILE_FORMATS.md](FILE_FORMATS.md). Identifiants de contrats `IMP-*`, de couverture `COV-*`, de corpus `BP-Fxx` et de preuves `BP-Pxx` stables.

## IMP-01 — Décision, priorité et état réel

**Cible obligatoire :** sélectionner un `.bp` dans **Fichier > Ouvrir** doit proposer sa **conversion native vers un nouveau `.moneta`**, sans modifier, renommer ni supprimer l'original. Le fichier cible est SQLite direct, pas BankPerfect renommé. Le choix du chemin et le consentement précèdent la conversion ; une fois achevée et vérifiée, Moneta ouvre le nouveau dossier, pas l'original BP en écriture.

**La lecture native `.bp` est prioritaire.** CSV/XML/QIF ne la remplacent pas sous un autre nom. Les exports sont utiles à la comparaison, à l'établissement de valeurs de référence et, si l'utilisateur choisit explicitement une reprise limitée, à un parcours de secours distinct signalant ses pertes. Ils ne satisfont pas l'objectif de conversion native complète.

**État mission 9 : lecteur Moneta non implémenté, qualification native bloquée faute de corpus fictif local.** Aucun fichier de comptes `.bp`/`.bp0` synthétique fourni ni octet de compte analysé. En revanche, le site de l'auteur renvoie désormais vers **BankPerfect 9, dépôt public MIT**, avec une description et un lecteur du conteneur historique V5 : [recherche figée et limites](BANKPERFECT_FORMAT_RESEARCH.md), [correspondance métier](BANKPERFECT_MAPPING.md). Cela ne valide pas encore notre copie 8.0.0.373 ni toutes les anciennes versions.

La recherche d'octobre ci-dessous reste historique. La piste publique ne remplace ni corpus indépendant ni contrôle de complétude. En 10A, seul V5 non protégé de nos quatre fichiers fictifs a été décodé par la façade Rust ; aucun contournement de protection, décompilation ou exécution de payload à la lecture. IMP-08 reste applicable.

## IMP-02 — Recherche publique effectuée, puis approfondissement ciblé

### Journal de consultation publique

Consultations du **28 octobre 2026**, lectures HTTP et recherches textuelles publiques seulement. Les résultats du moteur de recherche servent à découvrir des sources ; leurs conseils ou exemples générés ne sont pas des preuves du format.

| Preuve | Requête / source consultée | Résultat établi et limite |
| --- | --- | --- |
| BP-P01 | Requête `BankPerfect format fichier bp specification lecture conversion open source` ; [historique officiel](https://chelly.net/bankperfect/?page=bp_history) et [gestion des comptes](https://chelly.net/bankperfect/wiki/La%20gestion%20des%20comptes.html) | `.bp` multi-comptes ; création initiale avec un compte « courant ». L'historique annonce plusieurs changements de format. Pas de description d'enregistrements exploitable dans ces pages |
| BP-P02 | Requête `BankPerfect bp parser github import format` ; [BXP](https://github.com/zaxified/bxp), [Banksheet](https://github.com/tio-ze-rj/banksheet) | Candidats retournés mais sans rapport natif BP démontré : BXP décrit une conversion tabulaire CSV/XLSX/JSON, Banksheet des relevés PDF. Leurs descriptions ne prétendent pas lire les comptes BankPerfect. Aucun code repris, téléchargé comme paquet ni exécuté |
| BP-P03 | Requête `site:chelly.net bankperfect script API licence source format fichier` ; [API BankPerfect](https://chelly.net/bankperfect/wiki/API%20BankPerfect.html) | API d'accès aux objets de l'hôte : compte courant, lignes visibles, champs Extra sauvegardés dans les comptes. Pas un schéma de fichier sur disque |
| BP-P04 | [Scripts Python](https://chelly.net/bankperfect/wiki/Scripts%20Python%20pour%20BankPerfect.html), [Plugins](https://chelly.net/bankperfect/wiki/Plugins.html) | Noyau compilé et scripts présentés comme « open sources », possibilité annoncée de lire/améliorer ; aucune licence standard de redistribution des scripts établie dans ces pages. Catalogue public ≠ extensions présentes dans la référence |
| BP-P05 | [Import/export officiel](https://chelly.net/bankperfect/wiki/Import-export-donnees.html), page datée 21/10/2011 | Formats neutres et rapport d'import documentés ; généralisation « compte courant/lignes visibles » contredite pour XML et certains CSV par les sources locales déjà auditées |
| BP-P06 | [Historique officiel](https://chelly.net/bankperfect/?page=bp_history), entrées 3.5, 6.0, 7.0–8.0 | Déclarations historiques : format 3.5 « clé codée sur 8192 bits », format 6 « cryptage plus efficace », incompatibilité descendante, intégration de données de scripts, identifiants stables puis champs de transferts. **Ce ne sont ni l'identification d'un chiffrement ni des propriétés cryptographiques vérifiées sur un `.bp`**. Aucun ancien convertisseur n'est téléchargé ou lancé |
| BP-P07 | [Licence BXP](https://raw.githubusercontent.com/zaxified/bxp/HEAD/LICENSE.md), [licence Banksheet](https://raw.githubusercontent.com/tio-ze-rj/banksheet/main/LICENSE) | Textes publics lus : Apache-2.0 et MIT respectivement. Premier chemin BXP sur branche `main` en 404, lecture `HEAD` réussie. Licences de projets non pertinents pour le natif BP, pas autorisation sur BankPerfect ; aucun apport de code retenu |
| BP-P08 | [Directive 2009/24/CE](https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32009L0024), texte français intégral ; recherche `site:legifrance.gouv.fr L122-6-1 observer étudier tester décompilation interopérabilité` | Art. 5(3) et 6 lus directement sur EUR-Lex. [CPI L122-6-1](https://www.legifrance.gouv.fr/codes/article_lc/LEGIARTI000044365559) trouvé et extrait indexé consulté ; lecture directe refusée HTTP 403. Ne pas prétendre avoir lu le texte intégral actuel sur Légifrance |
| BP-P09 | Mission 9 : [site de l'auteur](https://chelly.net/bankperfect9/), [dépôt et licence figés](https://github.com/fabiochelly/bankperfect/tree/ea0624c408797acea6cdd58d59a0bb19560908fa), description `docs/inventaire/02-format-fichier-bp.md` et sources V5 | Source primaire pertinente vérifiée par lecture statique. V5 décrit, pas exercé sur fichiers locaux ; aucun code intégré ni binaire lancé. Les remplacements silencieux du parseur public ne sont pas des règles Moneta ; voir la recherche mission 9 |

La recherche est bornée à ces requêtes/pages, pas exhaustive des forums, archives, dépôts privés et anciennes versions. Un nouveau candidat interop doit être examiné pour sa pertinence réelle, ses versions couvertes, la provenance de sa spécification, sa licence et ses dépendances avant toute décision. Une licence libre ne prouve ni justesse du lecteur ni droit d'exécuter le logiciel propriétaire. Obtenir d'abord, si possible, une spécification ou une autorisation du titulaire ; ne pas présumer une réponse ou un accord obtenu.

### Lecture locale complémentaire : seulement les lacunes utiles

**Ordre suivi :** documents existants → documentation publique → sources lisibles ciblées → confrontation documentaire des exports. Aucun inventaire binaire recommencé.

- `reference-bankperfect/Scripts/Export XML/export_xml.py:22-40` relu : tous les comptes et toutes leurs opérations ; attributs explicites des comptes et opérations. La boucle ne transporte pas les UID/liens, dates de pointage, Extra, échéances ou budgets ; **elle n'émet pas non plus de champ explicite de solde initial**. Un éventuel encodage du solde initial parmi les opérations reste inconnu tant qu'aucun export synthétique n'est observé.
- `Scripts/Scheduler/scheduler.py:21-56` relu : espace logique `fabiobpscheduler`, sérialisation `cPickle`, ancien fichier DAT en repli. C'est un contrat entre le script et BP, **pas l'emplacement ou le format binaire de cet espace dans `.bp`**.
- Pour distinguer les domaines manquants, lectures ciblées des espaces : `Auto Fields/auto_fields.py:385-400` (`fabiobpautofields`) ; `Budget/budget.py:69-74,109-119` (`fabiobpautofilters`, `fabiobpbudget`) ; `BudgetYear/budget_year.py:72-82` (`fabiobpbudgetyear`) ; `Filters/setfilters.py:5-16` (`fabiobpautofilters`) ; `AllAccountsView/all_accounts_view.py:81-91` (`fabiobpviewall`) ; `MultipleInput/multiple_input.py:395-405` (`fabiobpabbreviations`). Ces appels portent des contenus sérialisés ; **aucun n'a été chargé ou désérialisé**.
- Les preuves déjà établies sur `#sisterAccUid` / `#sisterLineUid`, ventilation, notes et autres annexes sont reprises depuis l'audit, sans relire tout le code. Les champs préfixés `#` ne sont pas des déchets ; ils peuvent porter des relations.

**Interdit en lecture d'import :** `pickle`/`cPickle.load(s)`, évaluation d'expressions historiques, chargement d'une DLL/PYC, exécution d'un script/plugin et invocation de l'hôte BP. Si un contenu ne peut être obtenu par un décodage de données inerte, borné, légalement validé et spécifié, le domaine reste bloqué ; le désérialiser « juste pour lire » n'est pas une solution.

## IMP-03 — Ordre de faisabilité avant tout lecteur

| Étape | Travail autorisé ou futur requis | Critère de passage / état actuel |
| --- | --- | --- |
| 1. Documentation publique | Chercher spécification, API, historique, implémentations d'interop et droits ; demander au titulaire si utile | Recherche initiale IMP-02 conservée ; BP-P09 fournit maintenant description et lecteur V5 publics, à confronter à 8.0.0.373 et au corpus |
| 2. Scripts lisibles | Distinguer données logiques/API et limites utiles, sans recopier le code ; exécution seulement si rôle et isolation vérifiés | Audit statique conservé ; représentation locale désormais confrontée sur F00..F03, avec le seul transfert historique examiné pour F02 |
| 3. Exports documentés | Établir leurs champs et pertes ; demander ensuite des exports synthétiques pour comparaison | Confrontation statique IMP-04 ; aucun export produit/observé dans cette mission |
| 4. Comparaison de cas fictifs | Saisies, captures et états intermédiaires de BP8, puis lecture structurée indépendante | **Effectuée sur F00..F03**, limites et écarts dans les fiches ; pas tout F00..F22 |
| 5. Observation non destructive de fichiers synthétiques | Octets/tailles/hashes, V5, champs/dates/montants/références ; jamais les exécutables | **Effectuée sur quatre fichiers**, hashes identiques après lecture ; campagne complète d'offsets, répétitions et formats anciens non réalisée |

Le but de l'étape 5 est d'établir des faits de données, non de promettre qu'un lecteur sera réalisable sans autre méthode. Toute suspicion de protection, besoin de déchiffrement, décompilation ou autre méthode intrusive déclenche IMP-08 : **arrêt et validation préalable**, pas escalade automatique. Les futures étapes lecteur/conversion/qualification de [ROADMAP.md](ROADMAP.md) restent conditionnées par ces preuves. Les étapes documentaires ne valent pas autorisation d'exécution.

## IMP-04 — Exports : outils de preuve, non équivalents au natif

Périmètres issus de l'audit et de la lecture XML, **pas d'exécution** :

| Format | Informations transportables d'après les sources | Ce que cela ne démontre pas |
| --- | --- | --- |
| XML | Tous comptes/toutes opérations ; métadonnées bancaires, deux monnaies/taux et virtuel ; date/mode/tiers/détail/catégorie textuelle/montant/état de pointage | Solde initial explicite absent de la boucle ; UID, liens, date de pointage, champs supplémentaires, échéancier/budgets non transportés. Ordre/identité des catégories et modèles non prouvés |
| CSV | Mode compte courant/lignes visibles **ou tous comptes/toutes lignes**, un fichier par compte ; options de colonnes/séparateur/catégories/pointage | Transformations de textes, numéros de chèques retirables ; pas de preuve de reprise d'UID/liens/Extra ni des autres domaines. Correspondances et encodage à relever |
| QIF | Compte courant/lignes visibles, catégories, montants à deux décimales dans le script local | Pas de pointage/UID/ventilation structurée émis localement ; les capacités théoriques de QIF ne sont pas celles de l'export BP |
| HTML | Présentation du compte courant/lignes visibles | Pas une sauvegarde de données ; échappement de textes et précision à comparer |
| SLK sous nom « XLS » | Tableur du compte courant/lignes visibles ; quatre décimales et état de pointage dans le script | Pas BIFF/XLSX ; pas de reprise relationnelle complète. Plugin public multicomptes distinct, non établi installé |
| TXT catégories | Liste de catégories documentée | Ni opérations, ni sauvegarde complète du dossier |
| OFX | Import BP documenté, pas un export natif complet démontré | Ne constitue pas un chemin BP → Moneta ni une preuve des relations internes |
| `.bp0` et sauvegardes BP | À conserver comme originaux distincts | Ni format neutre, ni SQLite présumé, ni compatibilité native acquise |

Un export non filtré peut être utile mais n'est pas automatiquement exhaustif : périmètre, filtres, lignes cachées, comptes désactivés/virtuels et versions doivent être notés. Les comparaisons natif/export doivent signaler leurs pertes au lieu d'utiliser l'export comme vérité complète du fichier. Aucune fusion ni déduplication automatique sur date + mode + montant ; deux paiements identiques peuvent être légitimes.

## IMP-05 — Pipeline cible de Fichier > Ouvrir

**Contrat futur ; aucune étape d'import n'est implémentée.** Les interfaces publiques sont les états ci-dessous et le rapport IMP-07 ; le lecteur interne devra émettre des enregistrements inertes, leur provenance et des diagnostics bornés, jamais exécuter la source.

1. **Détection bornée.** Vérifier fichier/droits et relever un en-tête minimal sans écriture. L'extension `.bp` demande un candidat de conversion ; elle ne prouve ni SQLite, ni version BP. Ne jamais transmettre ce fichier à un moteur SQLite sur la seule extension. En l'absence actuelle de signature native connue, afficher « format BankPerfect à identifier », pas « format reconnu ».
2. **Consentement.** Expliquer original conservé, nouveau dossier `.moneta`, couverture possible/inconnue, étapes, destination et annulation. Ne pas lancer BP pour reconnaître le fichier. Si le lecteur ne prend pas en charge une version source, produire un diagnostic de refus, pas une conversion vide.
3. **Snapshot de travail immuable.** Recommander au propriétaire de sauvegarder puis fermer BP et de fournir un fichier synthétique fermé. Relever longueur et SHA-256 source avant copie ; empêcher les écritures concurrentes pendant l'acquisition par un mécanisme approprié, puis créer une copie d'analyse distincte, comparer sa longueur/hash et recontrôler la source. Une simple stabilité de taille/horodatage, ou deux hashes égaux, n'établit pas à elle seule un snapshot cohérent si un auteur peut écrire pendant la copie.
   - Si le propriétaire autorise un fichier actuellement ouvert, **ne pas copier aveuglément le fichier actif** : demander une copie de travail enregistrée par son propriétaire puis fermée, ou un snapshot cohérent dont le mécanisme est réellement documenté. Aucune API de snapshot BP n'est connue ici. Si la cohérence ne peut être assurée, bloquer et demander fermeture/copie stable ; ne pas promettre une lecture à chaud.
   - L'original et ses sauvegardes ne sont jamais cibles d'écriture. La copie de référence est conservée sans modification ; si une analyse nécessite d'autres fichiers de travail, les dériver de cette copie, pas de l'original. Aucune donnée réelle n'est acceptée pour l'étude.
4. **Lecture bornée, sans écriture.** Version/signature reconnues sur preuve ; bornes de taille, compteurs, longueurs, profondeur, mémoire, temps et décompression éventuelle à spécifier selon le format réellement établi. Vérifier limites et références avant allocation/lecture. Un enregistrement inconnu, une version ambiguë, une chaîne indécodable, un contenu protégé ou une référence cassée n'est jamais ignoré. Ne pas charger les objets sérialisés avec le moteur historique. Aucun binaire BP n'est analysé.
5. **Staging séparé.** Stocker objets candidats, unités, dates, textes source, identifiants/liens bruts, preuves de provenance (fichier/record/plage si réellement connus), diagnostics et mappings. Aucun fichier utilisateur annoncé prêt, aucune récurrence/règle appliquée, aucun téléchargement de cours, aucune fusion automatique. La base cible de staging éventuelle n'est ni un succès ni un dossier final.
6. **Rapport de couverture.** Rendre visibles détecté/importable/rejeté/ambigu/absent/inconnu, données hors `.bp`, champs non représentables et blocs inconnus, par domaine COV. Montrer les valeurs attendues séparément des valeurs observées et des hypothèses. Un compte inconnu ne devient pas un compte vide.
7. **Validation et réconciliation.** Comparer aux captures/manifeste attendus : tous comptes, nombres d'opérations, soldes initiaux et finals, débits/crédits, états et dates pointé/rapproché, liens et sens/devises/montants. Contrôler les invariants de FMT-03 et FMT-05. Seul un périmètre dont la complétude est démontrée et accepté peut passer. Un consentement à la perte ne transforme pas une reprise partielle en migration complète.
8. **Construction du nouveau `.moneta`.** Destination temporaire exclusive, format Moneta courant et transactions ; pas de renommage `.bp`, pas d'écrasement d'un existant. Fermer puis vérifier structure, références, métadonnées et invariants, réouvrir/réconcilier comme en FMT-05/FMT-06. Comparer le hash/longueur de la source avant/après l'ensemble de l'import et ceux de la copie d'analyse ; toute modification inattendue invalide la réussite et interdit publication.
9. **Publication contrôlée sans écrasement.** Publier le nouveau fichier vérifié par bascule atomique sur le même volume lorsque les garanties du système le permettent, avec primitive sans remplacement et contrôle d'erreurs ; ne pas se contenter de vérifier l'absence du chemin avant un renommage vulnérable à une course. Si le nom a été pris entre-temps, demander une nouvelle destination. L'état « complet/vérifié » et l'ouverture finale n'arrivent **qu'après** publication réussie et dernier contrôle. Préserver original et rapport ; conserver la provenance dans le dossier privé, pas dans un journal partagé.

**Annulation/échec à toute frontière :** aucune base n'est annoncée réussie. Fermer les accès ; supprimer uniquement les temporaires de cette tentative quand c'est sûr, ou les laisser explicitement marqués incomplets si la suppression échoue. Ne jamais nettoyer un fichier de l'utilisateur ou un journal de récupération inconnu. Après panne, un fichier publié sans marque d'achèvement certaine doit être revalidé avant de prendre le statut réussi. Le rapport peut être conservé sans prétendre qu'un dossier utilisable a été produit. Un mode partiel éventuel exigerait une commande distincte et une acceptation des pertes ; **il n'est pas une sortie de réussite du parcours natif complet décrit ici**.

## IMP-06 — Corpus strictement fictif à demander, pas à fabriquer hors BP

### Règles communes

**Tous les scénarios ci-dessous sont des valeurs ATTENDUES à saisir ultérieurement ; aucun fichier, export, hash, offset ou comportement BP correspondant n'a été produit ou observé.** Ne pas créer de faux binaire `.bp` avec des octets inventés. Seul le propriétaire utilisant légitimement BP peut fournir les fichiers effectivement enregistrés, ou une exécution future doit recevoir une nouvelle autorisation explicite en contexte isolé.

Namespace documentaire du corpus : `moneta.bp-study.v1`. Comptes fictifs `A`, `B`, `C`, `D` ; aucun titulaire, RIB/IBAN, adresse, numéro de carte ou identifiant réel. Les noms `A — Étude`, `B — Réserve`, tiers `Café Démo` et `Тестовый магазин` sont fictifs. Pas de mot de passe dans le corpus initial ; ne pas fournir `identity.dat`. Les numéros de chèques fictifs sont de simples libellés, pas des coordonnées bancaires.

**Extension de protocole 10A :** F00/F01/F02/F03 désignent des cas différents des anciens F01/F02/F03. Employer **`moneta.bp-study.10a.v1`**, avec [fiches, hashes et crosswalk](BANKPERFECT_FIXTURES.md). **Quatre cas 10A produits et comparés** ; l'ancien `moneta.bp-study.v1/F00..F22` reste un protocole plus large non entièrement réalisé.

Chaque variante destinée à identifier un champ repart du parent indiqué et ne change **qu'une variable**. Un cas fonctionnel comportant plusieurs actions (ajouter plusieurs opérations, créer puis ventiler, débit puis crédit) exige un snapshot intermédiaire après **chaque action** : ne pas attribuer toutes les différences du cas composé à un seul champ. À chaque état, faire trois sauvegardes répétées sans modification dans des fichiers distincts (`r1`, `r2`, `r3`), puis répéter la variante depuis le même parent pour distinguer données métier, horodatages, identifiants et autres nondéterminismes. Toute ouverture susceptible d'appliquer une échéance doit être consignée ; aucune action automatique durant l'analyse immuable.

Convention de solde attendue : **solde final = solde initial + crédits − débits**, dans la monnaie du compte. Nombre d'opérations = lignes métier hors solde initial ; si BP représente celui-ci par une ligne, relever séparément « ligne technique initiale » et ne pas la compter deux fois. Le total multidevise n'est jamais obtenu par addition des chiffres.

### Cas minimaux et relations

| ID et parent | Saisie / différence isolée ATTENDUE | Comptes, lignes et soldes ATTENDUS (non observés) |
| --- | --- | --- |
| BP-F00 — nouveau | Tenter de produire un dossier sans opération et, si l'interface le permet sans détour, zéro compte | Vrai zéro compte **non établi** : M02 indique que Nouveau crée déjà un compte. Ne pas le forcer par manipulation. Si impossible, consigner « zéro compte non représentable via ce parcours » et garder le cas minimal A avec 0,00 EUR et 0 opération |
| BP-F01 — nouveau | Un seul compte A EUR, solde initial 1 000,00, aucune opération ; date conventionnelle de contrôle 2024-01-01, à renseigner seulement si l'UI expose une date initiale, sinon inconnue consignée | 1 compte ; 0 opération ; initial = final 1 000,00. La convention de contrôle n'invente pas un champ de date BP |
| BP-F02 — F01 | Une opération débit carte de 12,34 EUR au 2024-01-15, tiers `Café Démo`, détail `Essai; "é"`, catégorie `Vie:Alimentation` | A : D=12,34 ; C=0 ; 1 ligne ; final 987,66 |
| BP-F03 — F02 | Ajouter crédit 100,00 au 2024-01-16 et débit 0,01 au 2024-01-31 ; noter ordre stable des dates égales dans une variante indépendante | 3 lignes ; D=12,35 ; C=100,00 ; final A=1 087,65 |
| BP-F04a/b — F03 | **a** : ajouter seulement B EUR initial 200,00, sans opération ; **b depuis a** : ajouter uniquement un débit B de 20,00 au 2024-01-20 | a : A 3 lignes/1 087,65 ; B 0 ligne/200,00 ; somme EUR 1 287,65. b : A inchangé ; B 1 ligne/180,00 ; 4 lignes totales et somme EUR 1 267,65. Comparer chaque sous-étape séparément |
| BP-F05 — F04b | Virement direct 50,00 EUR A → B au 2024-01-25, sans frais ; variante distincte « convertir une ligne en transfert » | Deux jambes nouvelles : A D supplémentaire 50,00 → 1 037,65 ; B C supplémentaire 50,00 → 230,00 ; 6 lignes totales ; somme EUR toujours 1 267,65. UID et liens attendus logiquement, valeurs concrètes inconnues |
| BP-F06a/b/c — F03 | Sur la ligne débit 12,34 : non pointée → pointée avec date 2024-01-31 → rapprochée ; chaque état sauvegardé séparément | 3 lignes et final 1 087,65 inchangés ; effectifs (non pointé, pointé, rapproché) : (3,0,0), (2,1,0), (2,0,1). Flux du seul sous-ensemble marqué = −12,34 ; solde initial + flux = 987,66 **si** la définition BP inclut le solde initial. Relever sa définition réelle, ne pas l'affirmer. Variante option cycle deux états, sans changer les montants |
| BP-F07 — F02 | Catégorie parent `Vie`, sous-catégorie `Alimentation`, seconde sous-catégorie `Énergie` inutilisée ; variantes une à une : nom, ordre, déplacement, promotion après suppression parent selon UI | 1 ligne et final 987,66 inchangés ; identité de la catégorie, ordre et référence de la ligne à comparer sans les déduire du seul libellé |
| BP-F08 — F01 | Débit 30,00 EUR puis ventilation en 10,00 `Vie:Alimentation` + 20,00 `Vie:Énergie` | Avant : 1 ligne/final 970,00 ; après selon script local : 2 lignes de remplacement, D=30,00/final 970,00 ; pas 3 lignes et D=60,00. Groupe/UID/champs non présumés conservés. Variante négative isolée uniquement si UI/plugin effectivement disponible : parts −5,00 et +35,00 dans sa convention visible, total net débit 30,00 ; sinon impossibilité consignée |
| BP-F09 — F01 | Définir échéance mensuelle débit 25,00, départ 2024-01-31, fin 2024-03-31, auto-insertion désactivée | Avant application : 0 opération, final 1 000,00, 1 définition. Hypothèse métier à confronter : dates 31/01, 29/02, 31/03 ; si trois occurrences de 25,00 sont produites alors final 925,00. Ce calendrier n'est pas revendiqué comportement BP. Tester séparément application, rattrapage, simulation et réouverture ; compter les insertions réelles et l'avancement de définition |
| BP-F10a/b/c/d — F01 | **a** : ajouter seulement C BYN initial 300,00 ; **b depuis a** : ajouter seulement D RUB initial 10 000,00 ; **c depuis b** : débit C 1,25 ; **d depuis c** : crédit D 2,50 | a : 2 comptes, 0 opération ; b : 3 comptes, 0 opération ; c : C 298,75 BYN, 1 opération ; d : A 1 000,00 EUR, C 298,75 BYN, D 10 002,50 RUB, 2 opérations. Aucune somme nominale entre monnaies ; chaque sous-étape comparée isolément |
| BP-F11 — F10d | Si transfert multidevise disponible : A → C, débit 10,00 EUR, crédit 32,50 BYN, taux **fictif** 3,2500 BYN/EUR, sans frais | A 990,00 EUR ; C 331,25 BYN ; D 10 002,50 RUB ; 4 lignes. Montants effectivement saisis/produits à relever. Variante frais isolée débit A 0,50 EUR : A 989,50, C inchangé ; ne pas inventer la capacité BP à saisir ce scénario |
| BP-F12 — F01 | Débit 1 000,01 puis crédit 0,02, dates 2024-02-29 et 2024-03-01 | Après débit : −0,01 EUR ; après crédit : 0,01 EUR ; D=1 000,01, C=0,02, 2 lignes ; vérifier le signe sans confondre mode et magnitude |
| BP-F13 — F01 | Une opération isolée débit 1,01 ; variantes depuis le même parent : date 1999-12-31, 2000-02-29, 2024-02-29, 2030-01-31 | Chaque variante : 1 ligne/final 998,99 ; date future, historique, bissextile et fin de mois conservées sans décalage de fuseau. Pour périodicité annuelle 29/02, comportement hors année bissextile à relever, pas présumé |
| BP-F14 — F02 | Variantes de tiers/détail uniquement : `Été à Noël €`, `Тестовый магазин`, séparateur `;`, guillemets, retour à la ligne si admis, chaîne vide | 1 ligne/final 987,66 dans chaque variante ; texte exact attendu si UI l'accepte. Documenter refus/remplacement à la saisie ; ne pas qualifier le stockage natif de Windows-1252 ou UTF-8 à partir de l'encodage des scripts |

### Données avancées, paramètres et annexes

Ces cas repartent de BP-F01 ou du parent explicitement indiqué. Ils ne supposent pas que les plugins publics soient installés. Chacun possède une variante « absence », « valeur unique » puis « changement d'un champ » ; les fonctions absentes sont notées non disponibles, pas simulées.

| ID | Namespace logique / lieu de preuve, **pas offset natif** | Valeurs ATTENDUES entièrement fictives et contrôle |
| --- | --- | --- |
| BP-F15 | Champs Extra, API publique ; BP-F02 | Champ `moneta.bp-study.v1.memo` = `Été / Тест`, puis seul le texte change ; champ numérique textuel `moneta.bp-study.v1.taux` = `1.2345`. 1 ligne, final 987,66. Champs internes `#sisterAccUid`/`#sisterLineUid` à relever dans F05, sans les fabriquer ni les modifier |
| BP-F16 | Modes et tiers modèles ; namespace natif **inconnu** | Mode débiteur `Débit Démo` et créditeur `Crédit Démo` ; tiers modèle `Modèle Démo`, débit 7,89, catégorie `Vie:Alimentation`, détail `Modèle fictif`. Créer le modèle seul : 0 ligne/final 1 000,00 ; appliquer une fois dans une variante : 1 ligne/final 992,11. Chèque fictif détail `CHQ-DEMO-0007`, progression 0008 à observer sans coordonnées réelles |
| BP-F17 | `fabiobpbudget`, `fabiobpbudgetyear` | Budget janvier `Vie:Alimentation` = 40,00 EUR ; tableau annuel `Année Démo` douze valeurs 40,00 (total attendu 480,00), compte A seul. Sur F02 : réel dépense 12,34 ; reste budget − réel = 27,66 selon cette convention. Sens/nom d'« écart » BP à observer. Définition budget ne crée aucune opération : final 987,66 sur F02 |
| BP-F18 | `fabiobpautofields`, `fabiobpautofilters`, `fabiobpviewall`, `fabiobpabbreviations` | Règle désactivée libellée `Règle Démo` dont la représentation neutre décrit « détail = Essai » ; **ne pas importer/exécuter l'expression historique**. Filtre `Janvier Démo`, bornes 01/01–31/01/2024 ; vue comptes A/B si présents ; abréviation `md` → `Modèle Démo`. Changer séparément nom, ordre et actif ; aucune variation de solde attendue des seules définitions |
| BP-F19 | Notes prévues `Scripts/Notes/notes.dat` ; profil crédit `loan.dat` ; reçu `Scripts/Receipt/receipt.dat` ; portée à établir | Note `Note fictive É / Тест` ; profil capital fictif 1 200,00 EUR, taux 0 %, 12 mensualités 100,00 si admis ; reçu fictif total 30,00, remboursé 10,00, reste 20,00. Aucun de ces outils ne doit être supposé créer des opérations. Dossier F01 reste 0 ligne/final 1 000,00 tant qu'aucune insertion explicite n'a lieu. Annexes absentes de la référence actuelle : demander uniquement celles synthétiquement créées |
| BP-F20 | Titres, représentation native inconnue ; manuel M16 | Titre `Titre Démo`, quantité 2,5, prix unitaire fictif 12,34 EUR, valeur calculée 30,85 EUR ; code non valide `DEMO-ISIN-000` pour marquer le caractère fictif (si refusé, champ vide et refus consigné). Aucun appel Yahoo/réseau. Ne pas imposer au montant stocké BP la valeur 30,85 si sa convention stocke le prix unitaire ; relever champs et solde affichés, montant comptable restant **à observer**. Cas isolé pour éviter d'inventer un solde de compte titres |
| BP-F21 | Options de fichier/application, INI ; noms de clés exacts non établis ici | Une variante par choix : solde filtré, lignes cachées, cycle de pointage, deux monnaies d'affichage, compte virtuel/désactivé, ordre comptes, affichage 1/2 lignes et 1/2/3 colonnes, saisie séparée, couleur/police et sauvegarde. Sur F03 : données et solde comptable 1 087,65 inchangés ; filtre ne voyant que débit 12,34 donne flux filtré −12,34, pas solde comptable substitué. Relever où la préférence persiste sans copier toutes les options privées |
| BP-F22 | Extensions utilisées et pièces jointes ; namespaces à inventorier | Liste noms/versions réellement utilisés, sans installer. Si fonction Documents effectivement disponible : document texte fictif `Document Démo`, taille/hash à mesurer, rattachement à une ligne F02 ; aucune image BP ni document réel. La mention publique d'une extension ne prouve ni présence ni stockage dans `.bp` |

**Tous les nombres de ce corpus sont des attentes de saisie ou calculs arithmétiques déclarés, jamais des résultats BankPerfect.** Les cas conditionnels ne justifient pas un succès inventé si l'interface refuse un caractère, une monnaie, un titre ou une ventilation. Adapter une saisie refusée exige de documenter la différence et de garder le cas original comme non représentable, pas de changer silencieusement l'attendu.

**Précaution mission 9 :** la documentation publique nouvelle décrit `MarkDate` comme date de valeur, initialisée à la date d'opération. Les variantes F06 doivent relever séparément création, pointage et modification de cette date ; ne pas la déclarer automatiquement date de vérification Moneta. L'ouverture peut aussi être une opération dédiée plutôt qu'un champ de compte : vérifier F01 sans compter son montant deux fois.


### Manifeste et protocole de comparaison à remplir seulement après réception

Pour chaque `moneta.bp-study.v1/BP-Fxx/variante/rN`, demander un manifeste neutre indiquant :

- provenance légitime et confirmation « exclusivement fictif » ; version/build BP et liste exacte d'extensions utilisées ; locale, format de dates, options utiles, date/heure de création, date métier et parent du scénario ; absence d'exécution automatique non consignée ;
- actions et **attendus** : comptes, devises/échelles, soldes initiaux, opérations avec ordre/date/sens/montant, soldes finals, pointés/rapprochés, catégories, modèles, relations et définitions ; espaces logiques ci-dessus, annexes attendues et absences prévues ;
- fichiers effectivement remis et rôle (`.bp`, sauvegarde, XML/CSV/QIF, captures, annexe synthétique), nom relatif non personnel, **longueur en octets et SHA-256 mesurés**. Une empreinte n'est ni chiffrement ni preuve de légitimité ; ne pas inscrire un hash inventé ou remplacer « non mesuré » par zéro ;
- **observations séparées** : ce que l'UI affiche, exports réels avec options/filtres/encodage, erreurs/refus de saisie et écarts aux attendus ; les champs non encore observés portent littéralement « non observé » ;
- après autorisation d'analyse non destructive : signature/en-tête réellement observé si pertinent, longueurs comparées, liste des plages différentes avec offsets début/fin et convention explicite (base zéro, fin exclusive), nombre d'octets modifiés et contexte minimal uniquement synthétique ; comparaison parent→variante puis r1→r2→r3 et répétition indépendante ;
- hypothèse et degré de preuve distincts : un offset corrélé à un changement n'est pas encore un champ identifié ; longueur changeante n'est pas preuve de compression, contenu illisible n'est pas preuve de chiffrement. Nondéterminisme, checksums et données générées restent inconnus jusqu'à expériences discriminantes ; aucune inférence publiée comme constat.

**Manifeste courant 10A : quatre fichiers BP8 fictifs, tailles et SHA-256 mesurés, comptes/lignes/soldes/relations/dates confrontés.** Voir [vérités terrain](BANKPERFECT_FIXTURES.md). L'ancien protocole étendu reste prévu ; ne pas prétendre avoir mesuré tous ses offsets, répétitions, exports ou domaines avancés.

## IMP-07 — Matrice de couverture et rapport sans faux succès

### Couverture exhaustive des domaines de parité

Chaque ligne confronte les fonctions nommées dans [FUNCTIONAL_PARITY.md](FUNCTIONAL_PARITY.md). « Copiable » signifie **information logique à préserver si sa lecture est prouvée**, pas capacité actuelle. Pour tous les domaines natifs, localisation et décodage `.bp` restent inconnus sauf preuve explicite. `N/A` signifie fonction UI/calcul, non donnée à copier ; sa configuration ou ses résultats persistés restent évalués séparément. Les données hors `.bp` ne sont jamais déclarées absentes uniquement parce que le lecteur `.bp` ne les voit pas.

| ID | Fonctions de parité confrontées | Données copiables / traitement ; lieu ou limite | Cas |
| --- | --- | --- | --- |
| COV-01 | Fichier multi-comptes, plusieurs fichiers ; Nouveau, solde initial | Dossiers distincts, tous comptes, ordre, monnaies, solde/date initiaux ; pas fusion implicite. XML sans solde initial explicite | F00–F04 |
| COV-02 | Ouvrir/enregistrer/enregistrer sous/imprimer ; gestion/changement compte | Commandes N/A ; noms/ordre/statuts comptes et préférences persistées à reprendre si prouvés, pas l'historique des gestes | F04, F21 |
| COV-03 | Informations bancaires, monnaie, RIB | Métadonnées facultatives sans demander de numéros réels ; monnaies/taux originaux sourcés. Contrôle/impression RIB N/A ; ne pas inventer coordonnées absentes | F10–F11 |
| COV-04 | Mot de passe du fichier | Protection source inconnue ; aucune clé à copier ou protection à contourner ; blocage explicite | Hors corpus initial |
| COV-05 | Agencement principal ; saisie intégrée/séparée | Écrans/commandes N/A ; préférences éventuelles à inventorier, portée fichier/application inconnue ; aucune image BP copiée | F21 |
| COV-06 | Opérations date/mode/tiers/détail/catégorie/montant | Toutes lignes, ordre, UID et valeurs exactes si lisibles ; dates et sens non reconstruits par heuristique ; solde initial distingué | F02–F04, F12–F14 |
| COV-07 | Modes natifs/personnalisés ; numéros de chèques | Noms, sens débit/crédit, numérotation et détail original ; aucune correspondance au seul nom. XML modes textuels ≠ catalogue complet | F16 |
| COV-08 | Calculatrice dans montant ; calculatrice séparée ; conversions numériques ; Luhn | Fonctions N/A ; résultats déjà inscrits dans des opérations à préserver, pas expressions exécutées ni carte réelle | F02, F16 |
| COV-09 | Auto-complétion/charger tiers ; tiers modèles | Catalogue et modèles complets (montant/mode/détail/catégorie), pas uniquement libellés de l'historique ; priorité UI N/A | F16 |
| COV-10 | Pointage/rapprochement ; date de pointage ; option deux états | États distincts, dates et références de relevé si présentes ; XML état sans date ; aucun remplacement par date d'import | F06, F21 |
| COV-11 | Annuler/refaire ; copier/coller/déplacer ; modification groupée ; remplacer | Gestes/historique volatil N/A ; écritures résultantes, identités et liens persistés à reprendre. Historique de révision seulement s'il existe et est prouvé | F03, F05 |
| COV-12 | Saisie multiple et abréviations | Opérations résultantes + espace `fabiobpabbreviations`, pas exécution de l'ancien outil | F18 |
| COV-13 | Ventilation | Lignes de remplacement, somme exacte ; groupe d'origine seulement si preuve, sinon ambiguïté visible. Ne pas fabriquer parent/liaison | F08 |
| COV-14 | Virement/navigation ; convertir ligne en transfert | Jambes, comptes/UID, montants/devises et relations Extra internes ; transfert direct vs échéancier distincts. Date+montant ne prouve pas un lien | F05, F09, F11, F15 |
| COV-15 | Filtres colonnes/date/texte/montant ; enregistrés/périodes dynamiques | Expressions et paramètres comme données inertes ; `fabiobpautofilters`. Filtres actifs ne limitent jamais l'inventaire natif | F18, F21 |
| COV-16 | Cacher lignes inutiles ; solde filtré/non filtré ; affichage lignes/colonnes | Options à relever sans appliquer la règle divergente comme suppression. Soldes dérivés à recalculer selon politique explicitée ; UI N/A | F06, F21 |
| COV-17 | Catégories/sous-catégories, suppression/promotion ; import/export TXT | Identités, noms, hiérarchie, ordre, références des lignes, valeurs non utilisées ; suppression et import/export comme commandes N/A | F07 |
| COV-18 | Champs supplémentaires ; calcul automatique | Définitions/valeurs Extra, préfixes internes inclus ; `fabiobpautofields` pour règles, jamais évaluées. Une règle non traduite n'est pas migrée « fonctionnellement » | F15, F18 |
| COV-19 | Échéancier créer/modifier/appliquer/rattraper ; ajout depuis ligne ; insertion ouverture | Définitions, prochaine/fin, exceptions, lien aux opérations produites si prouvé ; `fabiobpscheduler`, ancien DAT éventuel. **Aucune occurrence générée à l'import** | F09 |
| COV-20 | Simulation échéances/prévision solde | Fonction N/A ; opérations réellement insérées par simulation historique sont des données, à distinguer des définitions ; ne pas les recréer | F09 |
| COV-21 | Budget mensuel comparé au réel ; annuel | `fabiobpbudget` et `fabiobpbudgetyear`, périodes, méthodes, catégories/comptes, tableaux/valeurs ; réel dérivé non stocké comme nouvelle opération | F17 |
| COV-22 | Graphiques filtrés/modèles/impression ; totaux catégories/tiers | Fonctions de restitution N/A ; modèles/critères persistés à inventorier ; montants dérivés à comparer sans assimiler tiers normalisé à UID | F17–F18 |
| COV-23 | Total comptes ; relevé multicomptes | Calcul/UI N/A ; paramètres `fabiobpviewall`, comptes sélectionnés/exclus. Totaux séparés par devise avant toute valorisation | F04, F10, F18 |
| COV-24 | Comptes virtuels et désactivation | Indicateurs, ordre et périmètres de totalisation distincts ; virtuel XML établi, désactivation connue par historique ; localisation native inconnue | F21 |
| COV-25 | Import CSV/QIF/OFX ; exports CSV/QIF/HTML/XML/tableur | Fonctions N/A ; profils/mappings persistés à inventorier séparément, fichiers exports comme preuves auxiliaires. OFX2 public ne prouve pas installation | F02–F05 et exports |
| COV-26 | Sauvegardes locales/externes ; liste/restauration ; courriel/FTP | Sauvegardes BP conservées hors conversion ; réglages de rétention/destination éventuellement transposables après consentement. Aucun secret réseau demandé/transféré ; pas d'envoi ou FTP hérité | F21 |
| COV-27 | Deux monnaies/bascule ; inversion des monnaies ; convertisseur | Monnaies/taux réellement persistés et réglages INI distincts ; provenance/date inconnues restent inconnues. Pas de réécriture inverse ni téléchargement implicite | F10–F11, F21 |
| COV-28 | Crédit/amortissement et profils | Paramètres/profils `loan.dat` hors `.bp` selon sources ; tableaux dérivés N/A, annexes absentes de référence | F19 |
| COV-29 | Reçu remboursements | Données/paramètres `Scripts/Receipt/receipt.dat` si présents ; commande/impression N/A, portée à confirmer | F19 |
| COV-30 | Notes/chargement automatique | `Scripts/Notes/notes.dat` prévu hors `.bp`, absent de référence ; texte/portée à reprendre seulement si remis synthétiquement | F19 |
| COV-31 | Titres quantité/ISIN/prix/cours | Conventions de champs, quantités, prix d'acquisition et valorisations distincts ; pas de cours actuel substitué ni faux code valide | F20 |
| COV-32 | Options générales/sauvegarde/affichage/couleurs/police | Paramètres fichier/application/INI inventoriés séparément ; valeurs compatibles transposées avec preuve, sinon non importables explicites. Pas d'assets/logos BP | F21 |
| COV-33 | Installation/gestion extensions | Inventaire effectivement utilisé et données de chaque extension ; script/binaire/licence/installateur non importés/exécutés ; catalogue public seul insuffisant | F22 |
| COV-34 | Aide/manuel/mise à jour et raccourcis | Fonctions UI N/A ; pas de téléchargement BP ni copie d'aide/code/graphismes. Configuration particulière seulement si donnée persistée prouvée | F21 |
| COV-35 | Import natif complet | Enveloppe et chaque domaine présents doivent être spécifiés/testés ; blocs inconnus ou version non couverte bloquent l'étiquette complète | Tout corpus |
| COV-36 | Ajouts Moneta EUR/BYN/RUB, cours NBRB/CBR, historique/cache, soldes EUR | Exigences nouvelles, **pas données BP présumées** ; conserver unités historiques (BYR ≠ BYN), absence de cours affichée, aucune cotation officielle fabriquée à partir d'un taux BP sans date | F10–F11 |
| COV-37 | Ajouts Moneta transferts multidevises ; restauration/migration vérifiées | Montants source et liens prouvés, frais/taux si présents ; pas reconstruction spéculative. Réconciliation et restauration sur copie selon FMT-05–07 | F11 et rapport |
| COV-38 | Compléments publics éventuels Documents/KDiff/Trader/Rapprocher/archives/suites | Non établis installés ; données, pièces jointes et annexes à demander uniquement pour usages réels synthétisés. Aucun domaine inconnu écarté parce qu'il manque à la référence | F22 |
| COV-39 | Ajout Moneta format `.moneta`, versions et association Windows | N/A comme donnée BP : format cible construit indépendamment selon FMT ; jamais copier un schéma/association BP ni modifier le programme par défaut de `.bp`. La validité de la cible, ses migrations et sa restauration sont des contrôles de conversion, pas des champs source inventés | FMT-01–09, rapport |
| COV-40 | Ajout Moneta identité graphique entièrement originale | Graphismes BP **jamais importables** ; ressources créées spécifiquement Moneta. Seuls les paramètres utilisateur compatibles et prouvés relèvent de COV-32, sans charger un thème/asset BP comme identité Moneta. Pas de perte financière assimilée à un abandon de données | VISUAL_IDENTITY, F21 |

### Rapport requis pour chaque tentative future

Le rapport est privé par défaut et possède une version, un identifiant de tentative, les versions lecteur/format source prouvées, les hashes/longueurs source avant/après et snapshot, la destination proposée, les étapes réellement terminées et les décisions utilisateur. Ne pas y exposer de secrets, chemins personnels ou données financières dans les journaux techniques partageables.

Pour **chaque COV**, séparer les colonnes :

- **attendu** (manifeste utilisateur / valeur de référence sourcée) ;
- **observé** (UI, exports ou lecture native avec preuve et périmètre), qui peut rester « inconnu » ;
- **détecté**, **importé**, **rejeté**, **ambigu**, **absence prouvée**, **non déterminable**, avec unité de comptage et motif ;
- données non importables ou hors `.bp`, paramètres non repris, transformation/mapping, différence, preuve et décision.

Ne pas écrire « 0 » quand le nombre est inconnu. Un total détecté porte sur le périmètre réellement interprété, pas sur les blocs non lus. Pour une même unité, importé/rejeté/ambigu sont des issues disjointes ; l'absence prouvée et le non déterminable sont des statuts de domaines, pas des records ajoutés au dénominateur. Une conversion 1→N (ventilation/normalisation) explique ses deux comptages et conserve la provenance ; pas d'égalité artificielle des nombres de lignes.

Rapport financier obligatoire par **compte et devise** : solde initial daté, nombres d'opérations et bornes de dates, sommes débits/crédits, solde final recalculé et solde source, écart, effectifs et sommes par état non pointé/pointé/rapproché, dates de pointage/relevé connues ou manquantes. Distinguer les sous-totaux de flux du solde incluant l'initial ; fixer la définition du « solde pointé » avant comparaison. Pas de total EUR converti sans cours/date/politique explicites ; pas de zéro de secours.

Rapport relationnel obligatoire : catégories/parents/références, identifiants et type de chaque lien, deux jambes des transferts et leurs unités/montants, frais distincts si présents, liens directs ou issus d'échéancier, groupes ventilés **prouvés ou indémontrables**, calendriers/prochaines occurrences et modèles. Pour chaque lien : provenance du champ ou record, preuve directe vs hypothèse ; ne jamais transformer une hypothèse d'appariement en lien certain.

Rapport de décodage : version reconnue et méthode, enregistrements/blocs compris, **inconnus avec taille/offset seulement si mesurés**, chaînes non décodables, références orphelines, données sérialisées opaques, annexes manquantes, limites atteintes et contrôle d'intégrité natif seulement s'il est spécifié. Un fichier malformé, tronqué, protégé ou plus récent est refusé ; aucune récupération silencieuse par suppression de records.

### Statuts publics et critères

| Statut | Signification admissible |
| --- | --- |
| **Bloqué** | Version/protection/décodage/snapshot incohérent, données indispensables inconnues ou contrôles non terminés. Aucun dossier final réussi ; rapport des causes et conservation source |
| **Partiel** | Certaines données sont comprises mais des domaines restent manquants/rejetés/ambigus. Rapport de diagnostic ou de reprise limitée explicitement choisie dans un autre parcours ; **jamais « conversion native complète réussie »** |
| **Complet / vérifié** | Version explicitement couverte, chaque domaine applicable inventorié et repris sans perte non déclarée, absence démontrée des domaines non présents, aucun record inconnu susceptible de porter des données, contrôles financiers/relationnels et réouverture de la cible réussis, source inchangée et publication achevée |

Aucun de ces statuts n'est un résultat d'import exercé : la description V5 publique est maintenant identifiée, mais la qualification d'un lecteur Moneta reste bloquée faute de corpus et de correspondances validées.

## IMP-08 — Cadre légal et conditions d'arrêt

Ce document **n'est pas un avis juridique**. Juridiction applicable, contrat/licence de la copie utilisée, droits du fournisseur de fixtures et éventuelles protections techniques sont inconnus. La gratuité annoncée de BP ne vaut ni licence libre du noyau ni autorisation générale de réutilisation. Lire une page publique ou du source lisible n'autorise pas à redistribuer le programme, les scripts, le manuel, les images ou les logos. Moneta reste une implémentation originale ; aucun code source propriétaire n'est copié dans le projet.

La licence MIT du dépôt public BankPerfect 9 a été lue en mission 9 ; sa portée ne requalifie pas automatiquement l'installation historique ou ses ressources. Aucune réutilisation de code n'a lieu. Un futur choix de réutilisation devrait contrôler droits et notices, et ne dispense pas du consentement pour traiter un fichier protégé.


La [directive 2009/24/CE](https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32009L0024), **art. 5(3)**, distingue observation/étude/test par une personne habilitée dans les opérations qu'elle peut effectuer. **Historique : aucune exécution en mission 9.** La demande 10A a permis la production fictive après vérification d'une isolation réelle : BP8 et un plugin de transfert examiné, réseau désactivé, aucune donnée/identité privée exposée. Cela ne constitue ni avis juridique ni autorisation générale d'exécuter une installation/profil utilisateur.

Son **art. 6** encadre séparément la reproduction/traduction de code indispensable à l'interopérabilité d'un programme indépendant : personne habilitée ou agissant pour son compte, informations non déjà facilement et rapidement accessibles, limitation aux seules parties nécessaires. Les informations ainsi obtenues ne peuvent servir à d'autres fins, être communiquées au-delà du nécessaire ou permettre une expression substantiellement similaire/une atteinte au droit d'auteur ; l'exploitation normale et les intérêts légitimes restent protégés. Un simple objectif « interop » ne satisfait pas automatiquement ces conditions et ne confère pas un droit général au contournement. L'article 8 maintient d'autres dispositions légales pertinentes.

En France, le [CPI L122-6-1](https://www.legifrance.gouv.fr/codes/article_lc/LEGIARTI000044365559), notamment III et IV, est une référence complémentaire à revalider sur le texte officiel accessible : recherche et extrait indexé consultés, **accès direct HTTP 403** dans cette mission. Ne pas tirer une conclusion contractuelle ou juridictionnelle d'un extrait de recherche ; EUR-Lex a été lu directement.

**STOP avant** : décompilation/désassemblage d'EXE/DLL/PYC ; instrumentation intrusive ou lecture de mémoire ; tentative de déchiffrement, récupération/contournement de mot de passe, protection ou licence ; patch/altération ; exécution de données sérialisées/règles ; données personnelles réelles ; téléchargement/exécution d'un convertisseur propriétaire ; toute méthode au-delà de la lecture statique autorisée de documents/source lisible et, ultérieurement, de copies de données synthétiques. Demander alors validation préalable explicite de méthode/périmètre/droits, et avis juridique compétent si nécessaire. **Aucun bypass**, même présenté comme indispensable à l'interopérabilité. Une nouvelle autorisation d'exécution ne lèverait pas automatiquement les autres interdictions et conditions.

## IMP-09 — Demande opérationnelle pour débloquer la preuve

Option recommandée : **l'utilisateur crée lui-même**, dans son usage légitime de BP et sans donnée réelle, les cas BP-F00–BP-F22 qu'il peut effectivement saisir. Fournir progressivement :

1. À propos/version/build et liste des extensions réellement utilisées ; mention des fonctions impossibles dans l'installation disponible. Ne pas joindre `identity.dat` ou code de licence.
2. D'abord BP-F00/F01/F02/F03, originaux `.bp` fermés avec variantes de sauvegardes répétées ; manifeste neutre des attendus et copies/captures sans coordonnées réelles. Un vrai zéro compte n'est pas une exigence de contournement : noter son impossibilité si Nouveau impose un compte.
3. Puis multicomptes, virements, états et dates de pointage, catégories, ventilation, échéances et devises ; enfin modèles, Extra, budgets, notes, titres et options. Un cas absent faute de plugin est explicitement déclaré, pas inventé.
4. Exports XML/CSV/QIF correspondants sans filtre, paramètres d'export et comptes couverts, captures des valeurs pertinentes ; pour les exports filtrés expérimentaux, indiquer volontairement leur sous-périmètre. Annexes uniquement synthétiques effectivement produites et seulement pour les fonctions utilisées.
5. Confirmation de provenance légitime, de caractère intégralement fictif et d'autorisation de lecture non destructive des fichiers remis. Indiquer toute protection et **s'arrêter sans chercher à la lever**.

La production autorisée de 10A est décrite dans les fiches : quatre fichiers exclusivement fictifs, environnement isolé vérifié, original et référence intacts. Tout élargissement à des données personnelles, une protection, un autre composant non examiné ou une méthode plus intrusive exige un cadre/accord distinct ; aucune autorisation de décompilation ou de contournement déduite.

Les fichiers, snapshots, annexes, captures et binaires restent hors Git. Seuls faits abstraits et résultats synthétiques observés sont documentés, jamais secrets ou code propriétaire. **La qualification actuelle couvre quatre cas** ; elle ne prouve ni tous les BP8 ni la migration complète des domaines encore absents de Moneta.

## IMP-10 — Choix d'implémentation et passage vers 10B

### Trois voies comparées

Analyse d'ingénierie, pas benchmark ni composants ajoutés. La licence du lecteur public est MIT à la révision figée ; toute reprise substantielle de code/tables doit conserver les notices, et toute distribution Rust doit vérifier aussi les licences des dépendances.

| Critère | A — Lecteur C# indépendant | B — Bibliothèque Rust interopérable | C — Convertisseur externe contrôlé |
|---|---|---|---|
| Intégration | Même .NET/C#, modèles neutres et erreurs explicites, pas de runtime Rust livré | ABI C à concevoir, DLL Windows/architectures et versions à maintenir | Exécutable séparé, protocole de sortie versionné, distribution/validation de son identité |
| Exactitude | Parsing strict `decimal`/coefficients entiers, brut conservé avant toute normalisation | `Amount(i64)` utile, mais taux `f64`, identités réparées et replis actuels doivent être évités/corrigés | Exiger montant exact canonique et données brutes ; un JSON provenant d'un modèle déjà réparé ne suffit pas |
| Fichiers malformés | Quotas et erreurs contrôlées à concevoir/tester ; pas de récupération silencieuse | Les panics UTF-8/UID démontrées interdisent une FFI naïve ; aucun unwind au travers de l'ABI, ownership/libération de chaque buffer explicites | Processus isolable et arrêtable, limites mémoire/temps/sortie ; sortie non fiable à revalider |
| Maintenance | Une chaîne d'outils de production, convention cohérente avec les quatre projets existants | Double chaîne de compilation, FFI/mémoire/packaging et revue du graphe de dépendances | Double versionnement, gestion des erreurs/annulations et disponibilité de l'outil |
| Licence/indépendance | Code original, sources citées ; ne pas considérer une transposition substantielle comme dispensée de notice | MIT et licences transitives à auditer avant redistribution | Mêmes droits du programme redistribué ; pas de distribution de BP8 propriétaire |
| Couverture | Définie explicitement par nos fixtures et contrats, pas par le nom BankPerfect | Même périmètre du lecteur, pas de garantie automatique pour tous les BP8 ou anciens UCL/Shuffle | Même limite ; succès du processus ≠ migration complète |
| Usage recommandé | **Voie de production proposée**, après validation du format/règles | Non retenue à ce stade : coût et risques non justifiés | **Outil de qualification indépendant**, possible oracle comparatif figé, pas une dépendance produit décidée |

**Recommandation : A**, lecteur V5 C# passif, strict et borné, mapping distinct ; Rust comme contre-vérification de recherche. Ses réparations ne deviennent pas des règles Moneta. Les petits appelants Rust exécutés sont des sondes externes, **pas un importeur ou CLI distribué dans le produit**. Aucun projet, package, installateur ou décodeur Moneta ajouté.

Une future organisation possible garde les quatre projets : lecture technique et provenance dans une zone BankPerfect d'Infrastructure, structures neutres/invariants financiers dans Core, aperçu/consentement dans App, fixtures et régressions dans Tests. Les données sources ne doivent pas passer directement dans les entités financières en contournant la validation. Le besoin éventuel d'isoler la lecture dans un processus est à évaluer sur les limites/paniques et les quotas, pas à présupposer par une couche supplémentaire.

### Pipeline futur — aucune écriture dans le BP

1. **Sélection et acquisition stable en lecture seule.** Existence, taille bornée, extension non considérée comme preuve, empreinte de la copie stable ; original conservé. Pas de chargement par BP9 UI susceptible de réenregistrer en V6.
2. **Reconnaissance précise du conteneur.** V5 qualifié seulement ; refus distinct de version inconnue/format hors périmètre/protection. Aucun déchiffrement forcé ni appel à un ancien outil de récupération.
3. **Décodage et extraction neutres bornés.** Octets, encodage déclaré/qualifié, lignes et valeurs brutes conservés avec positions ; dates/montants invalides restent erreurs, pas aujourd'hui/zéro. Données de scripts, règles et annexes restent inertes ; aucun chemin fourni par le fichier n'est exécuté/ouvert automatiquement.
4. **Validation source exhaustive.** Comptes/blocs, cardinalités, doublons, UID, catégories, monnaies/échelles, dates, transferts réciproques, états et données avancées. Les identités de staging générées sont distinctes d'un UID historique absent ; pas de relation reconstruite par similitude.
5. **Aperçu et rapport avant mutation.** Par domaine : détecté/reconnu/interprété/représentable, nombre, transformation envisagée, données inconnues/pertes/ambiguïtés, soldes et relations ; original et destination explicités.
6. **Consentement explicite.** Annulation sans changement financier. Une information essentielle incomprise bloque la conversion ; accepter une perte ne transforme pas une reprise partielle en import complet. Maintenir la règle IMP-07 sur publication d'un résultat natif vérifié, sans fallback CSV silencieux.
7. **Destination neuve exclusive.** Nouveau UUID Moneta, schéma reconnu, contrat de provenance approuvé ; jamais le chemin `.bp`, jamais écrasement silencieux d'un `.moneta`. Pas de conversion « sur place ».
8. **Import atomique du graphe.** Comptes/référentiels/lignes/liens/provenance dans une transaction contrôlée ; échec ou annulation avant publication ne laisse pas de demi-virement ni de lot partiel annoncé comme valide.
9. **Vérifications métier et techniques.** Intégrité/FK, dates/devises/états, cardinalités et lignes exactes, ouvertures/crédits/débits/soldes, liens et payloads. Ne pas se contenter d'une différence de solde nulle.
10. **Fermeture/réouverture et publication.** Destination fermée/rouverte et comparée, sauvegarde/restauration future qualifiée ; publication exclusive après réussite. Un échec conserve le BP et n'efface pas de données utilisateur ; original recontrôlé inchangé.
11. **Rapport final versionné.** Complet-vérifié/partiel/bloqué, attendus/observés/importés/rejetés et raisons, conversions de cardinalité, provenance, inconnus et preuves de contrôles. Rapports privés par défaut ; pas de secret, ancien chemin/coordonnée bancaire dans les journaux partageables.

**Limites des exports inchangées :** le XML local omet notamment UID/liens/Extra, date supplémentaire et données de scripts ; QIF/CSV peuvent limiter le périmètre aux lignes visibles et perdre sémantique/références. Ils restent des contrôles/voies secondaires explicitement partielles, pas une solution de remplacement prétendument équivalente au natif.

### Portes de sortie proposées pour la mission 10B, non commencée

| Lot futur | Prérequis / résultat exigé |
|---|---|
| 10B-0 — revue du corpus 10A | Quatre cas produits/comparés, F00 avec génération d'UID signalée ; revoir les fiches et étendre aux variantes manquantes, ne pas reconstituer le corpus depuis zéro |
| 10B-1 — décisions de données | Approuver ouverture zéro/lignes conservées ou extraction prouvée, dates valeur/pointage, monnaies/précisions, libellé, UID/collisions, R héritées et limites de couverture |
| 10B-2 — lecteur diagnostic | Autorisation explicite, parsing V5 C# passif et borné, provenance/staging versionnés, aucune écriture financière ; tests négatifs de la matrice et différentiels sur corpus |
| 10B-3 — préparation du mapping | Représentation des champs absents du schéma 5, contrat d'erreur/rapport ; tout domaine présent non représentable identifié avant création d'un convertisseur |
| Conversion financière ultérieure | Seulement après acceptation des lots précédents : transaction vers nouveau `.moneta`, contrôle exhaustif, réouverture/sauvegarde/restauration et validation utilisateur ; pas de promesse de complétude avant couverture réelle |

Les [épreuves futures](BANKPERFECT_COMPATIBILITY_MATRIX.md#épreuves-futures-obligatoires--non-exécutées-dans-moneta) restent non exécutées dans Moneta. Le corpus et le geste de date de pointage sont désormais étayés **par BP8**, pas par les 516 tests Moneta. Fraction EUR non représentable, provenance et complétude restent des portes. **Pas de début de 10B ni d'import complet par cette étude.**
