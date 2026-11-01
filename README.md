# Moneta

Successeur Windows de BankPerfect pour la gestion locale de comptes personnels. Priorité : mêmes repères de travail, avec une interface desktop moderne et originale et des garanties financières explicites.

**Mission 7B : pointage individuel/multiple, soldes pointés et rapprochements bancaires persistants avec réouverture motivée et historique conservé. SQLite schéma 4 ; cycle manuel Ctrl+S maintenu. Version de développement à qualifier sur données synthétiques avant usage réel.** Pas de lecteur `.bp`, de change effectif, de virements, d’échéancier ou de sauvegarde automatique.

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
3. **Fichier → Enregistrer / Ctrl+S** : choisir un nouveau fichier `.moneta` au premier enregistrement ; les suivants commitent comptes, référentiels et opérations dans une transaction atomique. Ajouter/modifier/supprimer prépare un **brouillon**, sans commit implicite. Une erreur conserve le brouillon et ne produit pas l’état « enregistré ». **Enregistrer sous / F12** copie aussi toutes les opérations existantes avant d’appliquer les brouillons à la nouvelle copie ; l’ancien fichier reste inchangé.
4. **Fichier → Ouvrir / Ctrl+O** : identité, version, intégrité et références vérifiées avant affichage. Comptes, devises, UUID, notes, soldes exacts et dernière sélection enregistrée sont relus, sans fusion avec l'ancien dossier. Fermer/remplacer un dossier modifié propose enregistrer / abandonner / annuler. Un `.bp` reçoit une information d'importation future, jamais une écriture.
5. **Fichier → Sauvegarder le dossier** : enregistrer d'abord les modifications, choisir un nouveau nom ; copie via API SQLite, contrôle du snapshot complet, puis publication de `Sauvegarde.moneta` et `Sauvegarde.moneta.manifest.json`. **Conserver les deux ensemble.** Copier le seul fichier actif ne constitue pas une sauvegarde.
6. **Fichier → Restaurer une sauvegarde** : choisir une sauvegarde avec son manifeste et une destination neuve. Taille/hash, format, schéma, intégrité, clés et données sont vérifiés ; la copie est effectivement rouverte et comparée. Le dossier affiché et ses brouillons restent intacts. Ouvrir ensuite la copie restaurée pour une bascule volontaire ; ne pas travailler directement dans l'archive certifiée.
7. **Fichier → Mode démonstration** : quatre comptes fictifs et 3 212 opérations, dont 3 200 sur le premier compte. Ajout de comptes en mémoire possible, enregistrement financier désactivé. Aucun passage silencieux de ces données dans un dossier réel.

**Opérations → Nouvelle opération** ou bouton **Nouvelle** sous le registre : date française, sens Recette/Dépense, montant non négatif dans la devise native, libellé obligatoire, mode/tiers/catégorie/sous-catégorie/notes facultatifs. Les valeurs personnalisées alimentent les référentiels du dossier ; les variantes de casse et d’espaces ne créent pas de doublons. Une fraction sous l’unité monétaire est refusée sans arrondi ; zéro est autorisé. Sélectionner une ligne puis **Modifier**, ou double-cliquer. **Suppr / Ctrl+Suppr** dans le registre demande confirmation ; ces touches ne suppriment aucune opération depuis un champ texte. **Ctrl+Entrée / Maj+Entrée**, uniquement dans le registre, ouvrent respectivement la création/modification. **Ctrl+S** reste nécessaire pour rendre ces changements durables.

Navigation conservée : sélection de compte, détails, clavier, défilement et virtualisation, colonnes et panneau gauche redimensionnables. Registre réel paginé par **100 opérations** avec précédent/suivant ; solde total et solde progressif tiennent compte des opérations hors page. Ordre stable : date, création UTC, UUID canonique. La ligne compacte reste une prévisualisation avec boutons d’action ; la saisie s’effectue dans un dialogue validant, pas directement dans les cellules.

Le solde actuel est **solde initial + recettes − dépenses non supprimées**, toutes dates comprises, sans conversion. **Solde pointé = solde initial + mouvements actifs P/R** ; **Non pointé = somme des mouvements actifs NP**, sans ajouter une deuxième fois le solde initial. Ces totaux restent en monnaie native même lorsque la devise d’affichage du solde actuel change. Le prévisionnel reste indisponible. Les suppressions logiques sont exclues des soldes mais restent dans les contrôles et sauvegardes ; pas encore de corbeille ni d’historique général des modifications.

**Pointage :** dans le registre, P/Espace ou le bouton de dernière colonne basculent NP/P ; **Maj+P** dépointage, **Ctrl+Maj+P** date de pointage, **F11** bascule la première colonne entre date d’opération et date de pointage. Menus Opérations et contextuel utilisent les mêmes commandes. Ctrl/clic permet plusieurs lignes de la page courante ; Pointer/Dépointer applique tout le lot ou rien. Aucun montant, libellé ou compte n’est modifié. Nouveau pointage d’une opération future refusé ; date locale de vérification proposée aujourd’hui, modifiable et distincte de la date d’opération.

**Opérations → Rapprochement bancaire / Ctrl+Maj+R dans le registre :** enregistrer d’abord le dossier ; saisir date de fin (au plus tard aujourd’hui), numéro facultatif et solde final signé. Ouverture non modifiable : solde initial du compte au premier relevé, sinon clôture du dernier relevé encore actif. Cocher les mouvements NP/P réellement portés au relevé, y compris des anciens non rapprochés. L’écart doit être nul **et** les dates, comptes, devises, références et révisions cohérents. Valider prépare un brouillon ; **Ctrl+S après fermeture** le rend durable. Annuler le dialogue ne change rien.

Les opérations R sont protégées contre édition, suppression et dépointage. Une modification financière d’une P demande confirmation et conserve son pointage. Dans **Historique et réouverture**, seul le dernier relevé actif peut être rouvert, avec motif et confirmation : anciens états/dates restaurés, instantanés et événements conservés. **Enregistrer ce brouillon avant toute autre mutation du registre**, y compris sur un autre compte. Un relevé corrigé reçoit un nouvel UUID. Les R héritées d’un schéma 3 sans relevé attesté restent identifiées comme telles ; libération explicite avec motif depuis Opérations, jamais de relevé ou date inventé.

**Devise d'affichage** : native = montant réel ; autre = **Cours indisponible**, sans modifier compte/opérations. **Affichage → Thème → Clair / Sombre** conserve la préférence locale et la correction de sélection ; contraste élevé prioritaire. **Outils → Options** conserve ses cinq rubriques ; options de taux et de mises à jour enregistrables uniquement, services non développés. Voir [CURRENCIES_AND_SETTINGS](docs/CURRENCIES_AND_SETTINGS.md).

## Limites et divergences volontaires

L'ordre principal **Fichier | Édition | Affichage | Comptes | Opérations | Outils | Plugins | Internet | ?** et l'organisation de la principale restent fidèles à la capture privée. Elle ne prouve ni sous-menus fermés ni accélérateurs Alt. Choix Moneta explicites : Nouveau dans Fichier (également accessible sous Comptes), Enregistrer sous dans Fichier, sauvegarde/restauration manuelles et mode démonstration. F12 est documenté historiquement, pas l'emplacement exact d'Enregistrer sous. Aucun menu inconnu déclaré identique à BankPerfect.

Pas encore d’édition/suppression de compte, de filtre effectif, d’import/export, de change, de transfert, d’échéancier, de budget, d’annulation financière globale ou d’automatisation des sauvegardes. Le drapeau archivé est conservé ; les comptes archivés ne reçoivent pas de modifications d’opérations. Le solde progressif reste à gauche ; le tri libre est désactivé pour conserver sa signification. Les dialogues natifs de fichier et de confirmation gardent l’apparence fournie par Windows.

**Erreurs et sécurité :** fichier absent/incompatible/corrompu ou version future refusé, aucune base vide substituée ; destinations existantes et journaux de récupération préservés. Une session par chemin via verrou système `.moneta.lock` ; fichier vide résiduel normal après fermeture/crash. Attente SQLite bornée, checkpoint bloqué signalé en gardant la session utilisable. Stockage actif local uniquement : réseau refusé, dossiers synchronisés non pris en charge. SQLite et sauvegardes **non chiffrés/non signés** ; protéger les droits du dossier et le support. Ne jamais supprimer WAL/SHM/journal pour débloquer.

## Vérifications de mission 7B

Debug et Release : **0 erreur, 0 avertissement ; 261 tests réussis, 0 échec, 0 ignoré par configuration**, dont les 168 cas précédents. Couverture ajoutée : transitions/dates/futurs, lots atomiques, totaux exacts et devises, protections des R, corrections des P, sessions et réouverture, R héritées, concurrence, refus conservant les brouillons, sauvegarde/restauration de l’historique complet et migrations 1/2/3 → 4.

Parcours réel du binaire Release sous Windows : comptes EUR/BYN synthétiques, recette/dépenses, Ctrl+clic multiple, P/Espace/clic/Maj+P, édition de date et F11, sélection de texte au clavier sans pointage parasite, écart non nul refusé, validation puis Ctrl+S, protections R, fermeture du processus et réouverture, sauvegarde/restauration, réouverture motivée, nouvel enregistrement et nouvelle restauration. Comparaison hors interface : trois opérations, un relevé, deux instantanés, un événement avant correction puis deux après correction, référentiels et empreintes complets identiques entre copies correspondantes. Sonde séparée : 10 000 opérations restaurées et comparées individuellement, avec session/instantanés/événement.

Défauts confirmés et corrigés : sélection multiple réduite par le binding SelectedItem ; brouillon de réouverture/libération modifiable puis impossible à enregistrer ; date de pointage recalculée au jour du Save ; cases de rapprochement ignorant le TogglePattern d’accessibilité ; onglets non thémés. L’analyse exacte du solde initial réutilise maintenant `MoneyInput`, pour refuser une longue fraction qu’un parseur `decimal` général arrondissait silencieusement.

**Limite visuelle du poste :** les captures écran GDI/PrintWindow produisent un client blanc ou périmé, y compris au démarrage sans dossier. Elles ne prouvent donc pas l’affichage composé natif. Les contrôles réels ont été exercés par UIA et souris/clavier ; l’arbre visuel WPF a été inspecté via `RenderTargetBitmap`, en rendu matériel puis logiciel de diagnostic, dans les deux thèmes. Le rendu logiciel et l’instrumentation ne sont pas livrés. Captures synthétiques `screenshots-private/moneta-m7b-*-visual.png` locales seulement. Validation utilisateur de la fenêtre composée, lecteurs d’écran et campagne multi-DPI/moniteurs restent nécessaires.

Les conventions Moneta ne prétendent pas reproduire intégralement le cycle BP NP → P → R → NP : **R exige ici un relevé validé**, et sa correction est traçable. Les commandes « Magic », pause de rapprochement, relevé initial différent du solde d’ouverture du compte et restauration individuelle d’une opération restent absentes. La liste des candidats est paginée en mémoire, mais sa validation parcourt encore le compte à chaque sélection ; pas de cache financier ajouté.

### Mesures de mission 7B

Windows 10.0.26220 x64, Core i5-1235U (12 processeurs logiques), Intel Iris Xe pilote 32.0.101.7092, SDK 10.0.401, Release sans débogueur. Deux lancements à vide, cinq secondes de stabilisation puis un échantillon de cinq secondes. Démarrage jusqu’à `WaitForInputIdle`, **pas au premier pixel**. Démonstration : dix secondes de stabilisation puis six fenêtres de cinq secondes sans interrogation UIA pendant l’échantillon.

| Mesure du véritable exécutable | Résultat observé |
|---|---:|
| Démarrage à vide, deux essais | 1,376–1,491 s |
| Résidente / privée à vide | 150,34–150,82 / 95,13–95,72 Mio |
| CPU à vide, équivalent un cœur | 0,62–2,50 % |
| Démonstration visible, 3 200 lignes dans le premier compte | 207,56–207,63 Mio résidente ; 142,06–142,18 Mio privée |
| CPU démonstration, six fenêtres | 0 / 0,94 / 0 / 0 / 0 / 0,31 % d’un cœur |
| Première fenêtre après minimisation, transition incluse | 3,43 % d’un cœur |
| Après restauration et trois secondes de stabilisation | 0 % d’un cœur ; 208,18 / 142,05 Mio |

Pas de croissance progressive sur ces trente secondes, ni de charge prolongée comparable aux pics historiques ; observation courte seulement. La minimisation n’a pas de deuxième fenêtre stabilisée, donc **pas de CPU nul minimisé revendiqué**. Les mesures 7A restent ci-dessous : conditions proches, pas un benchmark statistique ni une preuve causale d’amélioration. La limite de capture composée native s’applique aussi à cette campagne.

Sonde API séparée, **10 000 opérations dans un seul compte EUR**, métadonnées synthétiques, page de 100 ; pas directement comparable aux quatre comptes de la sonde 7A. Pointage individuel : dix échauffements puis cent bascules de brouillon ; autres valeurs = un essai, sans dialogue.

| Mesure API | Durée |
|---|---:|
| Création des 10 000 opérations | 7,623 s |
| Ouverture et validation | 1,546 s |
| Pointage d’une ligne, médiane / P95 | 0,018 / 0,124 ms |
| Pointage d’une page de 100 | 1,878 ms |
| Commit après pointage | 1,620 s |
| Page de candidats au rapprochement | 122,463 ms |
| Préparation d’un relevé de deux opérations | 187,947 ms |
| Commit du relevé | 1,859 s |
| Sauvegarde / restauration | 1,711 / 4,035 s |

Le coût de validation/commit reste linéaire et plus important que le clic de pointage. Les candidats formels parcourent encore le compte ; une optimisation SQL mesurée reste à envisager si les gros relevés deviennent lents, sans cache financier opaque. Chronométrage de l’ouverture **par dialogue WPF** de ce gros fichier non obtenu : l’automatisation native s’est bloquée ; aucune durée API présentée comme durée UI. Les restaurations et leurs 10 000 opérations/historique ont néanmoins été comparées intégralement. Sondes et scripts temporaires retirés après vérification ; aucune dépendance supplémentaire.


## Vérifications de mission 7A

Debug et Release : **0 erreur, 0 avertissement ; 168 tests réussis, 0 échec et 0 ignoré dans chaque configuration**. Les 97 cas antérieurs sont conservés ; les anciennes hypothèses de numéro de schéma sont adaptées à de véritables fixtures historiques. Nouveaux cas : recettes/dépenses, correction/suppression, UUID/révisions, montants et échelles 0/2/3/28, agrégat dépassant Int64, références invalides, doublons légitimes, isolation, pages/ancrage, rollback, conflits externes, migration 1/2 → 3, SaveAs et restauration complète, altération conservant le même solde final. Répertoires de tests synthétiques supprimés après fermeture.

Parcours Windows réel : créer un dossier vierge ; ajouter deux comptes EUR et un BYN ; refuser une fraction sous l’unité sans perdre les champs ; saisir recette/dépense ; enregistrer avec Ctrl+S ; modifier la dépense ; annuler puis confirmer sa suppression ; vérifier les soldes ; fermer le processus ; relancer et rouvrir le fichier ; sauvegarder, restaurer vers une nouvelle copie puis l’ouvrir. Les métadonnées, référentiels et empreinte du registre source/restauré sont aussi comparés hors interface, y compris la ligne supprimée. Sélection de texte et de ligne examinée en clair/sombre ; Suppr dans un champ texte ne supprime pas d’opération. Démonstration toujours non éditable et 3 200 lignes conservées.

Captures synthétiques locales examinées : `screenshots-private/moneta-m7a-light.png`, `moneta-m7a-dark.png` et variantes `*-selection.png`, non suivies. Corrections issues du parcours : sélection de ligne maintenue lors du remplacement des pages et après commit, retour au compte précédent après erreur de lecture, texte réel de validation exposé à UIA. La création rend désormais son temporaire SQLite autonome avant vérification en lecture seule, pour ne pas laisser de WAL/SHM sous le nom temporaire ; aucun journal n’est supprimé manuellement.

Relecture statique indépendante des frontières stockage/validation et présentation : aucun défaut supplémentaire confirmé. Ce contrôle ne remplace pas les tests ni une qualification sur tous les matériels.

### Mesures de mission 7A

Windows x64 10.0.26220, 12 processeurs logiques, SDK 10.0.401/runtime 10.0.12, Release sans débogueur. Deux lancements à vide, cinq secondes de stabilisation puis environ cinq secondes d’échantillon. Démarrage jusqu’à `WaitForInputIdle`, **pas au premier pixel** ; pas de comparaison statistique avec les missions précédentes.

| Mesure à vide | Échantillon 1 | Échantillon 2 |
|---|---:|---:|
| Démarrage | 2,183 s | 2,094 s |
| Mémoire résidente | 154,35 Mio | 154,05 Mio |
| Mémoire privée | 101,72 Mio | 101,24 Mio |
| CPU, équivalent un cœur | 0,00 % / 5,02 s | 0,00 % / 5,01 s |

Démonstration visible : dix secondes de stabilisation puis six fenêtres de cinq secondes sans inspection UIA. Premier échantillon à **0,31 % d’un cœur**, cinq suivants à **0 %** ; mémoire résidente 211,75–211,81 Mio, privée 146,66–146,78 Mio, sans croissance progressive sur ces trente secondes. Minimisation et ses premières cinq secondes : 0,31 % ; après restauration et trois secondes de stabilisation : 0 %. Ces observations courtes ne prouvent pas l’absence de tout pic de compilation/rendu futur. L’objectif mémoire inférieur à 100 Mio n’est pas atteint.

Sonde séparée, trois essais séquentiels sur **10 000 opérations synthétiques / quatre comptes**, 2 500 opérations dans le compte chargé. Seules 100 opérations complètes sont renvoyées par page. Les sauvegardes finales contiennent aussi trois suppressions logiques.

| Mesure API observée | Durée |
|---|---:|
| Création initiale des 10 000 opérations | 5,570 s |
| Ouverture et validation complète | 1,049–1,232 s |
| Page de 100 parmi 2 500 | 28,58–54,73 ms |
| Préparation d’un ajout | 36,70–54,25 ms |
| Préparation d’une modification | 30,93–317,15 ms |
| Préparation d’une suppression | 34,90–37,95 ms |
| Commit et rafraîchissement après mutation | 1,230–1,337 s |
| Sauvegarde de 10 003 lignes, tombstones compris | 1,530 s |
| Restauration, réouverture et comparaison | 3,094 s |

Le premier coût de modification est conservé, pas écarté comme « anormal ». Mesures API, hors temps de saisie/dialogues ; E/S et validation complète dominent le commit. Pas de garantie de latence sur un dossier arbitraire, de qualification disque plein/coupure matérielle ou de nouvelle campagne physique multi-DPI. Scripts de validation et sonde retirés après preuve ; aucune nouvelle dépendance.

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

Mission 7B : valider les règles de pointage et rapprochement avant de développer dates, relevés, protections des opérations rapprochées et soldes pointés. Définir séparément la récupération individuelle et l’historique de corrections ; qualifier sauvegardes avant cadence/rétention automatiques. Le corpus `.bp` fictif reste nécessaire pour le lecteur indépendant prioritaire. Change daté, virements liés, filtres et accessibilité multi-écran restent des lots distincts. Voir [ROADMAP](docs/ROADMAP.md).
