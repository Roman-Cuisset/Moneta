# Moneta

Successeur Windows de BankPerfect pour la gestion locale de comptes personnels. Priorité : mêmes repères de travail, avec une interface desktop moderne et originale et des garanties financières explicites.

**Mission 4 : refonte visuelle WPF, thèmes Light/Dark, prototype en lecture seule et exclusivement fictif. Ne pas l'utiliser pour de vrais comptes.** Les quatre comptes, le registre et les détails restent en mémoire ; aucun fichier `.moneta` n'est créé, aucun `.bp` n'est lu et aucune saisie financière n'est enregistrée.

## Objectifs

- Retrouver les fonctions BankPerfect identifiées dans la matrice de parité, sans réduire le produit à un suivi bancaire générique.
- Gérer EUR, BYN et RUB, puis d'autres devises ; consulter les cours officiels, notamment NBRB, conserver leur historique et fonctionner hors ligne.
- Afficher les soldes convertis en euros et gérer des transferts multidevises sans réécrire les opérations passées.
- Ouvrir les fichiers natifs `.bp` par **Fichier > Ouvrir**, proposer leur conversion et créer un **nouveau `.moneta`**, sans jamais modifier ni supprimer l'original. Objectif prioritaire : le format `.bp` n'est pas encore spécifié ni sa lecture implémentée.
- Garantir des montants exacts, une persistance transactionnelle, des sauvegardes automatiques vérifiables et une restauration éprouvée.

C#, **.NET 10 LTS**, WPF et MVVM léger sont utilisés. SQLite reste la persistance future, **sans fournisseur ni base dans ce prototype**. La présentation utilise des contrôles WPF natifs avec styles et palettes Moneta, fontes système et douze pictogrammes vectoriels originaux. Aucun framework graphique, pack d'icônes, logo définitif ou actif BankPerfect. MSTest reste réservé aux tests. Le choix et les alternatives sont documentés dans [VISUAL_IDENTITY.md](docs/VISUAL_IDENTITY.md).

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

- Choisir le compte dans la barre d'outils, sous les menus. Le dossier de démonstration est uniquement en mémoire ; ce contrôle ne sélectionne pas un fichier sur disque.
- Le panneau gauche affiche nom, devise et solde actuel. Le pointé, les échéances et la prévision sont explicitement non calculés/indisponibles ; aucune valeur zéro n'en tient lieu.
- Le registre affiche Date, Mode, Tiers/libellé sur deux lignes, Catégorie/sous-catégorie, Débit, Crédit et Pointage. Les 3 200 lignes EUR et les quatre lignes de chacun des autres comptes restent virtualisées.
- Sélectionner une ligne actualise ses détails consultables/copiables à gauche, le solde après opération et la ligne compacte inférieure. Cette dernière est **en lecture seule**, avec action Modifier désactivée.
- Flèches et pages pour le registre ; colonnes et panneau gauche redimensionnables, ce dernier également avec les flèches quand le séparateur reçoit le focus. Les textes de cellule tronqués ont une infobulle et un détail complet ; les champs inférieurs permettent le défilement du texte.
- Le sélecteur **Thème : Light / Dark** change réellement toutes les ressources de présentation sans réinitialiser le compte ou l'opération. Préférence limitée à la session ; le contraste élevé Windows reste prioritaire.
- **Fichier > Quitter**, fermeture Windows et **? > À propos de Moneta** utilisent des commandes WPF. Le dialogue est thématisé. Les outils non développés et leurs raccourcis, filtres d'en-tête et édition sont désactivés, pas simulés.
- Montants `decimal`, dates/nombres français et soldes progressifs inchangés ; aucune addition/conversion entre devises. Seuls des modes et sous-catégories fictifs ont été ajoutés comme métadonnées d'affichage.

## Limites et divergences volontaires

L'ordre principal **Fichier | Édition | Affichage | Comptes | Opérations | Outils | Plugins | Internet | ?** et la structure de la principale sont confirmés par la capture privée fournie en mission 4. Cela ne confirme pas les sous-menus fermés, raccourcis Alt ni tous les comportements. Les parents documentés sont conservés ; Quitter/À propos restent des choix Moneta. Remplacer, Enregistrer sous, Imprimer et l'aide non développée ne sont pas arbitrairement placés.

La barre d'outils reprend l'ordre des fonctions observables mais reste désactivée hors sélection du compte. Pas de dossier persistant, filtre effectif, édition, pointage modifiable, import/export, ouverture/enregistrement, change, transfert, échéancier ou budget. Le solde progressif, auparavant en colonne, est consultable à gauche pour l'opération sélectionnée, afin de privilégier la disposition du registre de référence. Le tri demeure désactivé pour conserver sa signification chronologique.

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

Valider visuellement cette nouvelle interface, compléter les menus déroulés BankPerfect sur des captures sans données privées et qualifier plusieurs moniteurs réels/technologies d'assistance. Puis autoriser séparément les invariants monétaires et la persistance SQLite avec migrations, sauvegarde/restauration exercée et tests avant toute donnée réelle. Le corpus `.bp` fictif demeure nécessaire ; aucune conversion, API de change ou édition financière n'est ajoutée ici. Voir [ROADMAP.md](docs/ROADMAP.md).
