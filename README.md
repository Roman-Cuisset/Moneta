# Moneta

Successeur Windows de BankPerfect pour la gestion locale de comptes personnels. Priorité : mêmes repères de travail, avec une interface desktop moderne et originale et des garanties financières explicites.

**Mission 6 : dossiers `.moneta` SQLite, comptes multidevises persistants et sauvegarde/restauration vérifiées. Version de développement à qualifier sur données fictives avant tout usage réel.** Les opérations restent limitées à un mode démonstration explicite en mémoire. Aucun lecteur `.bp`, taux inventé, édition financière complète ou sauvegarde automatique.

**Consolidation 6.1 :** 97 tests Debug/Release, contrôles SQLite/sauvegardes revus et erreur de source verrouillé corrigée. Un pic CPU capturé est attribué à la compilation étagée .NET, sans boucle de layout démontrée ; aucun contournement graphique/runtime livré. [Audit, mesures et réserves avant mission 7](docs/AUDIT_MISSION_6_1.md).

## Objectifs

- Retrouver les fonctions BankPerfect identifiées dans la matrice de parité, sans réduire le produit à un suivi bancaire générique.
- Gérer EUR, BYN et RUB, puis d'autres devises ; consulter les cours officiels, notamment NBRB, conserver leur historique et fonctionner hors ligne.
- Afficher les soldes convertis en euros et gérer des transferts multidevises sans réécrire les opérations passées.
- Ouvrir les fichiers natifs `.bp` par **Fichier > Ouvrir**, proposer leur conversion et créer un **nouveau `.moneta`**, sans jamais modifier ni supprimer l'original. Objectif prioritaire : le format `.bp` n'est pas encore spécifié ni sa lecture implémentée.
- Garantir des montants exacts, une persistance transactionnelle, des sauvegardes automatiques vérifiables et une restauration éprouvée.

C#, **.NET 10 LTS**, WPF et MVVM léger, avec SQLite direct via `Microsoft.Data.Sqlite` **10.0.12** et `SQLitePCLRaw.bundle_e_sqlite3` **3.0.5**. Moteur **3.53.4 observé à l'exécution**, corrigé pour WAL-reset. Interface WPF native, palettes et pictogrammes Moneta originaux ; aucun framework graphique, logo définitif ou actif BankPerfect. Quatre projets conservés, aucun ORM/serveur/service cloud. Voir [ARCHITECTURE](docs/ARCHITECTURE.md) et [FILE_FORMATS](docs/FILE_FORMATS.md).

## Organisation

```text
Moneta/
├── reference-bankperfect/   # Référence locale immuable, exclue de Git
├── src/Moneta.App/          # WPF, fenêtre et interactions de présentation
├── src/Moneta.Core/         # Modèles, projections de soldes, navigation testable
├── src/Moneta.Infrastructure/ # SQLite, sauvegarde/restauration et jeu fictif séparé
├── tests/Moneta.Tests/      # Tests MSTest indépendants de WPF
├── docs/
├── tools/                  # Aucun outil applicatif
├── .gitignore
└── README.md
```

`Moneta.slnx` regroupe les quatre projets ; `global.json` sélectionne un SDK stable .NET 10.

La référence était déjà présente lors de la **mission 1**. Sa comparaison en lecture seule avec le dossier original avait confirmé **128 fichiers identiques par SHA-256, 39 sous-dossiers, 1 649 599 octets**, sans fichier manquant ni supplémentaire. Cette preuve est conservée dans l'audit initial ; la mission 2 ne recommence pas cet audit. L'installation originale et la référence ne doivent jamais être modifiées.

## Compiler, tester et lancer

Prérequis : Windows x64 compatible .NET 10 et **SDK .NET 10** incluant les références WPF. Le poste de validation dispose du SDK 10.0.401 et du runtime WindowsDesktop 10.0.12. En leur absence, installer explicitement le SDK depuis <https://dotnet.microsoft.com/download/dotnet/10.0> ; aucune installation système n'est effectuée par Moneta. `dotnet --info` permet de vérifier l'environnement.

Depuis la racine du dépôt, dans PowerShell ou un terminal :

```powershell
dotnet restore Moneta.slnx
dotnet build Moneta.slnx -c Debug --no-restore
dotnet build Moneta.slnx -c Release --no-restore
dotnet test Moneta.slnx -c Debug --no-build
dotnet test Moneta.slnx -c Release --no-build
dotnet run --project src/Moneta.App/Moneta.App.csproj -c Release --no-build
```

Après compilation : `src\Moneta.App\bin\Release\net10.0-windows\Moneta.App.exe`. Le runtime WindowsDesktop .NET 10 est requis ; aucun installateur autonome fourni. La restauration NuGet des tests et de SQLite peut nécessiter Internet. **Aucune connexion réseau au démarrage de l'application** ; aucun service permanent de taux ou de mise à jour.

## Parcours réellement disponible

1. **Fichier → Nouveau dossier / Ctrl+N** : saisir un nom ; dossier vierge en mémoire, sans compte ni opération fictive. Le démarrage n'ouvre aucun dossier automatiquement.
2. **Comptes → Ajouter un compte** : nom, devise native, type libre, solde initial français exact et notes. Plusieurs comptes de même devise, aucun plafond produit. Le compte est sélectionné et le dossier marqué **non enregistré**.
3. **Fichier → Enregistrer / Ctrl+S** : choisir un nouveau fichier `.moneta` au premier enregistrement ; les suivants commitent les modifications atomiquement. Ajouter n'enregistre pas automatiquement. Une erreur conserve le brouillon et ne produit pas l'état « enregistré ». **Enregistrer sous / F12** crée une copie neuve, sans modifier l'ancien fichier.
4. **Fichier → Ouvrir / Ctrl+O** : identité, version, intégrité et références vérifiées avant affichage. Comptes, devises, UUID, notes, soldes exacts et dernière sélection enregistrée sont relus, sans fusion avec l'ancien dossier. Fermer/remplacer un dossier modifié propose enregistrer / abandonner / annuler. Un `.bp` reçoit une information d'importation future, jamais une écriture.
5. **Fichier → Sauvegarder le dossier** : enregistrer d'abord les modifications, choisir un nouveau nom ; copie via API SQLite, contrôle du snapshot complet, puis publication de `Sauvegarde.moneta` et `Sauvegarde.moneta.manifest.json`. **Conserver les deux ensemble.** Copier le seul fichier actif ne constitue pas une sauvegarde.
6. **Fichier → Restaurer une sauvegarde** : choisir une sauvegarde avec son manifeste et une destination neuve. Taille/hash, format, schéma, intégrité, clés et données sont vérifiés ; la copie est effectivement rouverte et comparée. Le dossier affiché et ses brouillons restent intacts. Ouvrir ensuite la copie restaurée pour une bascule volontaire ; ne pas travailler directement dans l'archive certifiée.
7. **Fichier → Mode démonstration** : quatre comptes fictifs et 3 212 opérations, dont 3 200 sur le premier compte. Ajout de comptes en mémoire possible, enregistrement financier désactivé. Aucun passage silencieux de ces données dans un dossier réel.

Navigation conservée : sélection de compte, registre/détails readonly, clavier, défilement et virtualisation, colonnes et panneau gauche redimensionnables. Un dossier réel n'a pas encore d'opérations persistantes ; son registre est vide, le solde actuel est son solde initial.

**Devise d'affichage** : native = montant réel ; autre = **Cours indisponible**, sans modifier compte/opérations. **Affichage → Thème → Clair / Sombre** conserve la préférence locale et la correction de sélection ; contraste élevé prioritaire. **Outils → Options** conserve ses cinq rubriques ; options de taux et de mises à jour enregistrables uniquement, services non développés. Voir [CURRENCIES_AND_SETTINGS](docs/CURRENCIES_AND_SETTINGS.md).

## Limites et divergences volontaires

L'ordre principal **Fichier | Édition | Affichage | Comptes | Opérations | Outils | Plugins | Internet | ?** et l'organisation de la principale restent fidèles à la capture privée. Elle ne prouve ni sous-menus fermés ni accélérateurs Alt. Choix Moneta explicites : Nouveau dans Fichier (également accessible sous Comptes), Enregistrer sous dans Fichier, sauvegarde/restauration manuelles et mode démonstration. F12 est documenté historiquement, pas l'emplacement exact d'Enregistrer sous. Aucun menu inconnu déclaré identique à BankPerfect.

Pas encore d'édition/suppression de compte, d'opérations persistantes, filtre effectif, pointage modifiable, import/export, change, transfert, échéancier, budget ou automatisation des sauvegardes. Le drapeau archivé est conservé par le format, sans action UI. Le solde progressif fictif reste à gauche ; le tri est désactivé pour conserver sa signification.

**Erreurs et sécurité :** fichier absent/incompatible/corrompu ou version future refusé, aucune base vide substituée ; destinations existantes et journaux de récupération préservés. Une session par chemin via verrou système `.moneta.lock` ; fichier vide résiduel normal après fermeture/crash. Attente SQLite bornée, checkpoint bloqué signalé en gardant la session utilisable. Stockage actif local uniquement : réseau refusé, dossiers synchronisés non pris en charge. SQLite et sauvegardes **non chiffrés/non signés** ; protéger les droits du dossier et le support. Ne jamais supprimer WAL/SHM/journal pour débloquer.

## Vérifications de mission 6

Parcours WPF réellement exercé sous Windows : annuler Nouveau et le premier Enregistrer ; créer un dossier vide ; ajouter quatre comptes synthétiques EUR/BYN/RUB dont deux EUR ; enregistrer ; sélectionner BYN et vérifier l'absence de conversion inventée ; sauvegarder à chaud ; restaurer une nouvelle copie, comparer UUID et montants exacts ; Enregistrer sous ; revenir à la démonstration 3 200 lignes puis rouvrir le dossier, retrouver la sélection et fermer proprement. Captures clair/sombre de données fictives examinées dans `screenshots-private/moneta-m6-*.png`, exclues de Git.

Reprise en processus distincts : un commit encore dans le WAL survit à une terminaison forcée sans `Dispose` ; une transaction non validée ayant écrit 254 frames WAL est annulée après terminaison. Les snapshots complets, montants au-delà de la précision entière de `double`, identifiants/devises, intégrité SQLite et clés étrangères restent exacts. Une deuxième instance est refusée pendant détention du verrou, puis peut rouvrir après la mort du propriétaire. **Ce n'est pas un essai de coupure électrique matérielle.**

Scénarios d'intégration supplémentaires : base inconnue/future/altérée, fractions et bornes, isolation, destination déjà existante, erreurs de Save, lecteur bloquant le checkpoint puis reprise, écritures externes détectées, journaux orphelins préservés, manifeste absent/altéré, restauration et migration 1 → 2 sur copie. Disque plein forcé, panne matérielle et toutes les frontières possibles d'interruption ne sont pas qualifiés.

### Performances et limites de mission 6

Windows x64 10.0.26220, 12 processeurs logiques, SDK 10.0.401/runtime 10.0.12, Release sans débogueur ni inspection UIA pendant les fenêtres CPU. Une fenêtre visible au premier plan, cinq secondes de stabilisation, puis fenêtres de 5 s. Démarrage mesuré jusqu'à `WaitForInputIdle`, pas premier pixel ; deux échantillons seulement, sans valeur statistique.

| Mesure à vide | Échantillon 1 | Échantillon 2 |
|---|---:|---:|
| Démarrage | 2,286 s | 2,303 s |
| Mémoire résidente | 185,74 Mio | 172,21 Mio |
| Mémoire privée | 118,72 Mio | 108,14 Mio |
| CPU au repos, équivalent un cœur | 0,00 % / 5,01 s | 0,00 % / 5,01 s |

Ouverture d'un dossier persistant synthétique à quatre comptes depuis l'interface réelle : **1,496 s** puis **0,862 s** du choix du fichier à l'interface prête (dialogue exclu, attente pilotée par UIA). Chargement et validation seuls, sans interface : 4 comptes **6,6–9,6 ms** (médiane 8,3 ms, 10 essais alternés) ; 1 000 comptes **24,7–66,2 ms** (médiane 29,8 ms). Ces durées incluent la validation lecture seule puis écriture, pas la fermeture/checkpoint ni un démarrage à froid.

**Observation historique de mission 6 :** 15–100 % d'un cœur et croissance mémoire sur des fenêtres visibles de démonstration, également observés sur `7248ad1` ; minimisation suivie de 0 %. **L'interprétation causale précédente (« les anciens zéros signifiaient absence de rendu actif ») n'était pas établie.** L'audit 6.1 capture un travail de compilation étagée .NET et un retour au repos sans changement WPF ; cela explique les pics mesurés, pas nécessairement toutes les occurrences prolongées antérieures. Voir le [diagnostic étayé](docs/AUDIT_MISSION_6_1.md#3-diagnostic-cpu--faits-pas-correction-cosmétique).

Les campagnes disque plein, coupure électrique matérielle, supports amovibles, plusieurs moniteurs physiques et lecteurs d'écran restent non exercées.

## Vérifications de mission 5

Debug et Release : **0 erreur, 0 avertissement ; 29 tests réussis dans chaque configuration**. Les 12 tests initiaux sont inchangés ; 10 cas de comptes/devises et 7 cas de paramètres couvrent notamment absence de mutation financière, quatrième devise, nombreux comptes, précision, bornes, rechargement et préservation des JSON invalides.

Défaut de sélection reproduit visuellement avant correction : le rendu historique WPF par adorner recouvrait les glyphes avec la sélection opaque et ignorait `SelectionTextBrush`. `TextSelectionRendering` choisit le rendu natif du texte avant création des contrôles ; styles partagés et sélection inactive cohérents, y compris ComboBox éditables. Matrice Light/Dark observée : actif, sans focus, readonly, désactivé, lignes et menus. Gestes souris/clavier et Ctrl+Z natif exercés sous Windows. Captures fictives `screenshots-private/selection-before-*`, `selection-after-*` et `m5-*`, jamais suivies.

Scénario WPF réel : ajout via dialogue, sélection du nouveau compte, plus de cent comptes, plusieurs EUR, 3 200 lignes conservées, devise d'affichage sans taux, thèmes enregistrés/rechargés et options persistées. Aucun réseau ou service de taux/mise à jour n'est présent. Scripts temporaires retirés après validation.

### Performances de mission 5

Windows x64 10.0.26220, SDK 10.0.401/runtime 10.0.12 ; Release Light, sans débogueur ni inspection UIA pendant la mesure. Lancement → `WaitForInputIdle` (pas premier pixel), cinq secondes de stabilisation puis 5,01 secondes de CPU. Le commit M4 a été reconstruit dans un dossier temporaire pour comparer les deux versions **sur le poste dans son état actuel**, sans toucher à l'historique ou au travail courant.

| Mesure | M4 historique | M4 contrôle actuel `660add7` | M5 actuel |
|---|---:|---:|---:|
| Démarrage à chaud | 1,662 s | 3,734 s | 3,326 s |
| Mémoire résidente | 173,38 Mio | 176,56 Mio | 177,56 Mio |
| Mémoire privée | 124,07 Mio | 124,62 Mio | 123,97 Mio |
| CPU, un cœur logique | 0,00 % | 0,00 % | 0,00 % |

L'ancien commit est également plus lent qu'à sa mesure historique ; ces échantillons ne permettent pas d'attribuer ce ralentissement global aux ajouts M5. Pas de conclusion statistique ni garantie de performance. Premier lancement M5 observé : 3,550 s, 195,89 Mio résidents/138,30 Mio privés, CPU 0,00 %. Objectif RAM <100 Mo toujours non atteint. Pas de processus permanent de mise à jour ou de cours.

**Portée des mesures historiques :** valeurs ponctuelles conservées, pas preuve de charge permanente ni d'absence de rendu. Le diagnostic de mission 6.1 distingue optimisations runtime de démarrage, activité UI et repos stabilisé ; les conditions des anciens zéros ne sont pas reconstituées avec certitude. Voir [l'audit 6.1](docs/AUDIT_MISSION_6_1.md).


## Vérifications de mission 4

- Debug et Release : **0 erreur, 0 avertissement ; 12 tests réussis dans chaque configuration**, sans suppression ou modification des tests existants.
- Application Release réellement ouverte : quatre comptes et relevés distincts, sélection au clavier, détails readonly, changement Light/Dark, colonne redimensionnée, panneau gauche ajusté au clavier, dernière opération accessible, menu Ouvrir désactivé, dialogue sombre thématisé fermé avec Échap, puis Quitter sans processus résiduel.
- Un scénario Windows jetable a exercé 16 combinaisons : deux thèmes × quatre échelles WPF × taille normale/minimale. Échelles 96/120/144/192 DPI appliquées par `WM_DPICHANGED`, confirmées avec `VisualTreeHelper.GetDpi` ; **ce n'est pas une modification de l'échelle du moniteur**, resté à 125 %. À 150/200 %, la taille normale est bornée par l'espace du bureau ; le minimum 900 × 500 DIPs reste parcourable grâce aux défilements et au débordement de barre d'outils. Une campagne physique multi-écran reste à faire.
- Le scénario vérifie conservation de sélection lors des thèmes, champs non éditables, date non tronquée, filtres désactivés, débordement des outils et virtualisation : seulement 6 à 12 conteneurs de lignes réalisés sur 3 200, selon la taille/échelle observée. Les scripts temporaires sont retirés après vérification.
- Contraste des paires de texte principal, secondaire, sélection et désactivation : **au moins 5,09:1** dans les palettes fournies, calcul sRGB relatif. Pas de certification d'accessibilité : lecteur d'écran et activation réelle du contraste élevé Windows non exercés.
- Captures locales de la fenêtre seule : `screenshots-private/moneta-m4-light.png`, `moneta-m4-dark.png` et dialogue sombre ; variantes DPI fictives également locales. **La capture BankPerfect privée n'est ni incorporée aux documents, ni suivie, ni copiée en ressources.**

### Performances après refonte

Même poste Windows 11 x64 10.0.26220, SDK 10.0.401, runtime 10.0.12, Release sans débogueur, thème Light et 3 200 lignes EUR. Même protocole que mission 3 : lancement → `WaitForInputIdle`, cinq secondes de stabilisation puis environ cinq secondes d'échantillon sans inspection UIA. Mesures ponctuelles, pas une comparaison statistique ni un benchmark de premier pixel.

| Mesure | Mission 3 | Mission 4, version finale |
|---|---:|---:|
| Démarrage à chaud | 1,627 s | 1,662 s |
| Working set | 171,83 MiB | 173,38 MiB |
| Mémoire privée | 123,33 MiB | 124,07 MiB |
| CPU, équivalent un cœur | 0,00 % / 5,02 s | 0,00 % / 5,01 s |

L'objectif RAM inférieur à 100 Mo n'est toujours pas atteint. Les faibles écarts de cette mesure ne démontrent pas une régression significative ni une absence de régression dans tous les usages.

Un premier échantillon avant le dernier ajustement de fonte a atteint 213,47 MiB et 86,83 % d'un cœur ; **il n'est pas effacé du bilan ni présenté comme repos stable**. Le diagnostic n'a observé aucun événement de disposition récurrent pendant les échantillons instrumentés ; un suivi natif ultérieur de 24 secondes montre un début à 10,85 %, puis cinq fenêtres de deux secondes à 0,00 %. La cause du pic initial n'est pas établie. Le dernier lancement mesuré selon le protocole de référence donne les valeurs du tableau ; surveiller les phases transitoires lors d'une qualification plus longue.


## Vérifications de mission 3

Compilations Debug et Release : **0 erreur, 0 avertissement**. **12 tests réussis dans chaque configuration** : isolation des comptes/devises, changement/effacement de sélection, ordre chronologique stable, soldes exacts incluant solde initial, devise incohérente, orphelin, montant négatif, débordement, format français et valorisation séparée.

Application Release réellement lancée sous Windows ; contrôles UI Automation : quatre comptes, détails et devises correspondants, effacement de l'ancien détail, navigation clavier, huit colonnes, défilement jusqu'à la fin des 3 200 lignes, redimensionnement d'une colonne, menu et fermeture sans processus survivant. Captures **de la fenêtre seule** examinées à 125 % (120 DPI), taille normale et taille minimale 760 × 520 DIPs. Captures locales fictives dans `screenshots-private/`, exclues de Git. Les échelles 100/150/200 %, autres écrans et lecteur d'écran n'ont pas été exercés ; le manifeste déclare PerMonitorV2, pas une conformité acquise.

Mesures ponctuelles : Windows 11 x64 10.0.26220, SDK 10.0.401, runtime 10.0.12, Release sans débogueur, 12 processeurs logiques. Démarrage mesuré du lancement du processus à `WaitForInputIdle`, pas un benchmark de premier pixel ; repos après cinq secondes de stabilisation, sans inspection UI Automation pendant l'échantillon.

| Mesure observée | Résultat | Limite / objectif |
|---|---:|---|
| Premier lancement observé | 6,201 s | Pas un démarrage à froid contrôlé |
| Redémarrage à chaud | 1,627 s | Objectif indicatif < 2 s atteint sur cet essai uniquement |
| Working set au repos | 171,83 MiB | Objectif indicatif < 100 Mo **non atteint** |
| Mémoire privée au repos | 123,33 MiB | Distincte du working set |
| CPU au repos, équivalent un cœur | 0,00 % sur 5,02 s | Observation courte, pas garantie permanente |

Après une inspection UI Automation intensive et un grand défilement, un échantillon non stabilisé a atteint 257,96 MiB et 8,52 % CPU ; il ne représente pas le repos normal. Profilage mémoire et qualification multi-DPI restent à faire, sans dégrader lisibilité ni fiabilité.


## Documents

| Document | Contenu |
|---|---|
| [Audit de BankPerfect](docs/BANKPERFECT_AUDIT.md) | Inventaire, sources locales et officielles, extensions, formats, limites et inconnus |
| [Fidélité fonctionnelle](docs/FUNCTIONAL_PARITY.md) | Correspondance des fonctions, menus partiellement prouvés, raccourcis et état de réalisation |
| [Architecture](docs/ARCHITECTURE.md) | Choix techniques, données, calculs, cours, sécurité, sauvegardes et migration |
| [Feuille de route](docs/ROADMAP.md) | Priorités, dépendances, validations et conditions de passage au développement |
| [Formats de fichiers](docs/FILE_FORMATS.md) | `.moneta` SQLite, journaux, copie, récupération, sauvegarde/restauration, versions et association Windows |
| [Stratégie d'import BankPerfect](docs/BANKPERFECT_IMPORT_STRATEGY.md) | Recherche native `.bp`, cadre d'interopérabilité, corpus fictif, couverture et rapport de migration |
| [Cartographie des commandes](docs/UI_MENU_MAP.md) | Menus prouvés/provisoires, raccourcis, comportements, fenêtres et priorités |
| [Inventaire des écrans](docs/UI_SCREEN_INVENTORY.md) | Champs, interactions, preuves manquantes et protocole de captures synthétiques |
| [Identité visuelle](docs/VISUAL_IDENTITY.md) | Ressources entièrement originales, principes Windows classiques et validation graphique séparée |
| [Devises et paramètres](docs/CURRENCIES_AND_SETTINGS.md) | Comptes dynamiques, contrat de cours, préférences locales, commandes et architecture des futurs services |

La référence statique prouve une présence ou une déclaration ; elle ne prouve pas qu'une commande fonctionne encore. Les pages du manuel sont anciennes et parfois contradictoires. Aucun exécutable ou script BankPerfect n'a été lancé ; aucune décompilation ni aucun désassemblage n'a été effectué.

## Confidentialité et Git

- Dépôt GitHub **privé** créé à la demande de l'utilisateur en mission 5 : <https://github.com/Roman-Cuisset/Moneta>. Identité et visibilité `private` contrôlées par l'API avant tout envoi. Aucune publication publique, Pages, release ou licence de diffusion publique.
- `reference-bankperfect/` est entièrement ignoré, y compris ses scripts et ressources. Ne jamais forcer son ajout avec `git add -f`.
- Les dossiers `data/`, `private/`, `imports/`, `exports/`, `backups/` et `screenshots-private/` sont réservés aux fichiers privés et ignorés. Aucun n'est requis pour cette mission.
- Les fichiers BankPerfect, **`.moneta` et leurs auxiliaires/manifestes**, SQLite, exports bancaires, archives, secrets, journaux et productions `bin/obj` sont exclus. Les règles restent conservatrices : CSV, XML, HTML, TXT, PDF et DAT sont aussi ignorés hors des dossiers privés. Aucun véritable fichier de comptes ne doit être suivi.
- Un `.gitignore` ne détecte pas une donnée personnelle dans un Markdown et ne protège pas un fichier déjà suivi. **Avant chaque ajout : liste explicite de fichiers autorisés et inspection du contenu.** Ne pas utiliser `git add .` pour des données de migration.
- Les futures données de test doivent être entièrement fictives. Toute exception d'exclusion devra être étroite et revue ; jamais une base réelle ou un export bancaire réel dans le dépôt.
- Ne pas ouvrir ni recopier les valeurs de `identity.dat`, les identifiants, numéros de comptes, chemins personnels ou secrets dans les rapports. Les rapports présents ne reproduisent pas ces contenus.

- Avant création : 40 fichiers suivis et les 4 commits/68 versions de fichiers existants examinés ; aucun artefact privé interdit ou signature de secret recherchée détecté. Les ajouts passent par une liste explicite et le contrôle est réexécuté sur l'historique final avant envoi. Ce contrôle n'est pas une garantie universelle de détection ; les captures et données privées restent hors historique.

## Référence et propriété intellectuelle

BankPerfect reste un logiciel tiers. Son caractère gratuit ne donne pas une autorisation de réutiliser son code, ses scripts, logos ou ressources. La référence locale sert uniquement à l'analyse autorisée. Le futur code Moneta devra être original ; aucune licence de redistribution de BankPerfect n'est présumée, et aucune licence de publication de Moneta n'est décidée ici.

Manuel officiel : <https://chelly.net/bankperfect/wiki/manuel.html>.

## Prochaine mission proposée

Valider ce cycle de dossier et la restauration sur données synthétiques ; ajouter ensuite les opérations persistantes (saisie, règles de correction, catégories/tiers/modes et pointage) par migration, avec soldes et historique vérifiés. Qualifier les sauvegardes avant cadence/rétention automatiques. Le corpus `.bp` fictif demeure nécessaire pour le lecteur indépendant prioritaire ; aucun export n'est assimilé à une migration complète. Change daté, transferts, accessibilité multi-écran et automatisation restent des lots distincts. Voir [ROADMAP](docs/ROADMAP.md).
