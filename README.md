# Moneta

Successeur Windows de BankPerfect pour la gestion locale de comptes personnels. Priorité : fidélité des fonctions, menus, raccourcis et habitudes de navigation, avec une interface légèrement modernisée et des garanties financières explicites.

**Mission 3 : prototype Windows WPF compilable et exécutable, en lecture seule, exclusivement en mémoire et avec données fictives. Ne pas l'utiliser pour de vrais comptes.** Sélection des quatre comptes, registre et détails fonctionnent ; aucun fichier `.moneta` n'est créé, aucun `.bp` n'est lu et aucune saisie financière n'est enregistrée.

## Objectifs

- Retrouver les fonctions BankPerfect identifiées dans la matrice de parité, sans réduire le produit à un suivi bancaire générique.
- Gérer EUR, BYN et RUB, puis d'autres devises ; consulter les cours officiels, notamment NBRB, conserver leur historique et fonctionner hors ligne.
- Afficher les soldes convertis en euros et gérer des transferts multidevises sans réécrire les opérations passées.
- Ouvrir les fichiers natifs `.bp` par **Fichier > Ouvrir**, proposer leur conversion et créer un **nouveau `.moneta`**, sans jamais modifier ni supprimer l'original. Objectif prioritaire : le format `.bp` n'est pas encore spécifié ni sa lecture implémentée.
- Garantir des montants exacts, une persistance transactionnelle, des sauvegardes automatiques vérifiables et une restauration éprouvée.

C#, **.NET 10 LTS**, WPF et MVVM léger sont utilisés. SQLite reste le choix de persistance future, **sans bibliothèque SQLite ni base installée/créée dans ce prototype**. Les composants Windows natifs et fontes système remplacent tout pack graphique ; aucun logo définitif ni actif BankPerfect n'est intégré. MSTest est utilisé uniquement pour les tests.

## Organisation

```text
Moneta/
├── reference-bankperfect/   # Référence locale immuable, exclue de Git
├── src/Moneta.App/          # WPF, fenêtre et interactions de présentation
├── src/Moneta.Core/         # Modèles, projections de soldes, navigation testable
├── src/Moneta.Infrastructure/ # Jeu fictif déterministe, sans stockage ni réseau
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

Après compilation, on peut aussi lancer directement `src\Moneta.App\bin\Release\net10.0-windows\Moneta.App.exe`. Le runtime WindowsDesktop .NET 10 est requis sur le poste d'exécution ; aucun installateur autonome n'est fourni. La restauration des trois packages de test peut nécessiter Internet ; **l'application n'a aucune connexion réseau au démarrage** et fonctionne sans NuGet ni SDK une fois compilée, avec le runtime présent.

## Parcours réellement disponible

- Ouvrir la liste **Compte** sous les menus ; choisir Courant EUR, BYN, RUB ou Épargne EUR. Chaque entrée affiche nom, devise et solde fictif ; le registre est remplacé et l'ancienne sélection/détail est effacée.
- Parcourir le registre en lecture seule : 3 200 lignes EUR et quatre lignes dans chacun des trois autres comptes. Sélectionner une ligne pour lire ses date, libellé, tiers, catégorie, montant d'origine, pointage et détail.
- Flèches et Page précédente/suivante naviguent dans le tableau ; barres de défilement et redimensionnement des colonnes sont natifs WPF. Le panneau de détail revient en haut à chaque sélection et sa hauteur peut être ajustée avec le séparateur.
- **Fichier > Quitter**, la fermeture Windows et **? > À propos de Moneta** sont opérationnels. Les autres commandes sont grisées, avec infobulle d'indisponibilité ; leurs raccourcis affichés ne sont pas actifs.
- Montants en `decimal`, dates et nombres français, soldes progressifs par compte ; aucune addition/conversion entre devises. Une éventuelle valorisation est un objet distinct du montant d'origine, non calculé ici.

## Limites et divergences volontaires

L'ordre complet des menus BankPerfect n'est pas confirmé : le prototype signale **« Disposition des menus provisoire »**. Il conserve les parents documentés, dont Nouveau sous Comptes et les échanges sous Fichier ; ordre, séparateurs, accès Alt et positions de Quitter/À propos sont des choix provisoires Moneta. Remplacer, Enregistrer sous, Imprimer, Annuler/Refaire et l'aide native sont omis plutôt que placés dans un emplacement non prouvé. Le prototype n'est pas une reproduction certifiée de l'interface BankPerfect.

La zone basse est un **détail en lecture seule**, pas la zone de saisie BankPerfect. Pas de barre d'outils décorative, filtre, édition, pointage modifiable, import/export, ouverture/enregistrement, fichier natif, change, transfert effectif, échéancier ou budget. Aucun bouton ne prétend sauvegarder. Le tri est désactivé pour maintenir l'ordre chronologique des soldes progressifs ; largeur des colonnes et défilement horizontal restent disponibles.

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

La référence statique prouve une présence ou une déclaration ; elle ne prouve pas qu'une commande fonctionne encore. Les pages du manuel sont anciennes et parfois contradictoires. Aucun exécutable ou script BankPerfect n'a été lancé ; aucune décompilation ni aucun désassemblage n'a été effectué.

## Confidentialité et Git

- Dépôt **local uniquement** ; aucun distant ni dépôt GitHub créé. Ne pas publier sans décision explicite.
- `reference-bankperfect/` est entièrement ignoré, y compris ses scripts et ressources. Ne jamais forcer son ajout avec `git add -f`.
- Les dossiers `data/`, `private/`, `imports/`, `exports/`, `backups/` et `screenshots-private/` sont réservés aux fichiers privés et ignorés. Aucun n'est requis pour cette mission.
- Les fichiers BankPerfect, **`.moneta` et leurs auxiliaires/manifestes**, SQLite, exports bancaires, archives, secrets, journaux et productions `bin/obj` sont exclus. Les règles restent conservatrices : CSV, XML, HTML, TXT, PDF et DAT sont aussi ignorés hors des dossiers privés. Aucun véritable fichier de comptes ne doit être suivi.
- Un `.gitignore` ne détecte pas une donnée personnelle dans un Markdown et ne protège pas un fichier déjà suivi. **Avant chaque ajout : liste explicite de fichiers autorisés et inspection du contenu.** Ne pas utiliser `git add .` pour des données de migration.
- Les futures données de test doivent être entièrement fictives. Toute exception d'exclusion devra être étroite et revue ; jamais une base réelle ou un export bancaire réel dans le dépôt.
- Ne pas ouvrir ni recopier les valeurs de `identity.dat`, les identifiants, numéros de comptes, chemins personnels ou secrets dans les rapports. Les rapports présents ne reproduisent pas ces contenus.

## Référence et propriété intellectuelle

BankPerfect reste un logiciel tiers. Son caractère gratuit ne donne pas une autorisation de réutiliser son code, ses scripts, logos ou ressources. La référence locale sert uniquement à l'analyse autorisée. Le futur code Moneta devra être original ; aucune licence de redistribution de BankPerfect n'est présumée, et aucune licence de publication de Moneta n'est décidée ici.

Manuel officiel : <https://chelly.net/bankperfect/wiki/manuel.html>.

## Prochaine mission proposée

Faire valider visuellement ce prototype, compléter les captures BankPerfect manquantes et qualifier les autres facteurs DPI. Puis définir les invariants monétaires et le premier lot de persistance SQLite avec migrations, sauvegarde/restauration exercée et tests avant toute donnée réelle. Le protocole de fichiers `.bp` fictifs demeure nécessaire pour la voie native ; aucune conversion ou API de change n'est ajoutée dans cette mission. Voir [ROADMAP.md](docs/ROADMAP.md) pour les étapes non réalisées.
