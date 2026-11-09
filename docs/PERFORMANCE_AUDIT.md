# Audit de performances et qualification WPF — mission 9

## Périmètre et état des preuves

Point de départ : `06184df`, arbre propre et synchronisé. Builds Debug/Release sans erreur ni avertissement ; **516 tests réussis, aucun échec/ignoré dans chaque configuration**, avant toute modification. Les chiffres des anciennes missions restent historiques et ne constituent pas une comparaison causale.

Cet audit sépare : disponibilité du HWND visible, réponse UI Automation, chargement explicite du dossier, échauffement/JIT, repos stabilisé, minimisation et instrumentation. `IsWindowVisible` ne prouve pas le premier pixel composé ; `WaitForInputIdle` n'est pas utilisé comme équivalent du premier rendu. Les fenêtres et traces ne contiennent que des données synthétiques ; les préférences non financières sont restituées après chaque exécution. Aucune instance préexistante n'a été fermée.

Environnement observé : Windows 10.0.26220 x64, Core i5-1235U, 12 processeurs logiques, SDK 10.0.401, runtime 10.0.12 ; fenêtre native à **120 DPI / 125 %**. Release sans débogueur. Bibliothèques de diagnostic **déjà en cache**, utilisées uniquement par un projet temporaire : Microsoft.Diagnostics.NETCore.Client 0.2.510501 et TraceEvent 3.2.6 ; aucun package ni paramètre runtime ajouté au produit.

## Méthode native

Chaque essai lance son propre `Moneta.App.exe`, vérifie son PID et l'état réellement affiché, charge explicitement le corpus ou la démonstration, puis n'interroge plus UI Automation pendant les fenêtres CPU de 5 secondes. Mesure OS de `TotalProcessorTime` pour le processus et ses threads, descriptions natives via `GetThreadDescription`, mémoire privée/résidente, puis 10 secondes minimisé après 2 secondes de transition. Le pourcentage est celui d'**un cœur** ; diviser par 12 pour la normalisation totale du poste. Les threads disparus entre deux relevés peuvent empêcher une attribution exhaustive, sans perdre le total CPU processus.

Corpus distincts : sans dossier ; fichier avec un compte vide ; fichier de 12 opérations ; fichier de 10 000 opérations ; démonstration explicite de 3 200 lignes sur le premier compte. Tous créés avec les API de production, soldes initiaux et mouvements exacts. Les fichiers réels restent paginés à 100 lignes, la démonstration conserve ses 3 200 objets ; le nombre de lignes visuelles est observé séparément. Aucun mélange de cas déduit du simple lancement : **le produit démarre sans dossier**, pas en démonstration.

## Cause démontrée du pic capturé

Exécution native profilée, PID 30572, démonstration sombre. La mesure OS et les événements EventPipe concernent le **même processus et les mêmes identifiants de threads** ; ce n'est pas un rapprochement de deux essais différents.

| Observation | Résultat |
|---|---:|
| CPU processus sur le premier intervalle de 5,012 s après chargement | 82,606 % d'un cœur |
| CPU du thread 11156, `.NET Tiered Compilation Worker` | 4 046,875 ms |
| Événements JittingStarted sur ce même thread | 3 114 |
| Événements JittingStarted, trace entière | 3 570 |
| TieredCompilation BackgroundJitStart / Stop | 446 / 446 |
| TieredCompilation Resume | 10 |
| GC Start / Stop, trace entière | 9 / 9 |

Les méthodes compilées incluent WPF, runtime et méthodes dynamiques. Une répétition native **sans collecteur** retrouve le même thread : 2 671,875 ms dans la première fenêtre à 53,539 % d'un cœur. Les petites reprises tardives sont distinguées des intervalles stables : on ne transforme pas une moyenne incluant l'échauffement en « CPU au repos ».

La fin de la collecte/rundown génère elle-même une activité du thread `.NET EventPipe` (281,250 ms dans une fenêtre) ; cette fenêtre instrumentée est **exclue des références non profilées**. Aucun temps échantillonné de thread n'est présenté comme du CPU. Une absence d'événement réseau dans un essai désactivé ne constitue pas une garantie globale sur tous les scénarios.

## Contre-vérification WPF séparée

Sonde utilisant les vrais assemblages et ressources, compteur `LayoutUpdated`, `PropertyChanged`, `ScrollChanged`, changements de session de cours, GC et allocations. **Aucun abonnement `CompositionTarget.Rendering`**, qui pourrait changer l'activité observée. Cette sonde n'est pas le benchmark de démarrage de l'exécutable natif.

Sur la démonstration : **11 conteneurs DataGridRow pour 3 200 lignes** ; à partir du troisième intervalle jusqu'à 60 s, zéro layout, notification, défilement et changement de cours, zéro GC et aucune allocation détectée par le compteur sur ces intervalles. Le rendu managé `ContentRendered` est observé à 1 478,213 ms dans cette sonde ; ce n'est pas une mesure du premier pixel composé.

Cela ne démontre pas une boucle d'invalidation applicative, une relecture SQLite permanente ou une fuite. Cela ne prouve pas non plus leur impossibilité dans tous les parcours. Le pic attribué est une **compilation étagée transitoire**, pas un motif suffisant pour désactiver globalement Tiered Compilation ou livrer un contournement graphique. La comparaison visible puis minimisé peut confondre le passage du temps et la disparition du pic : elle ne prouve pas seule une cause graphique.

## Matrice native répétée, sans collecteur

Deux exécutions par variante, 45 s visibles (60 s pour 10 000 lignes), puis 10 s minimisées. « Après 15 s » est une **fenêtre d'observation**, pas une garantie que la compilation a fini : toutes les reprises tardives restent incluses. Actualisation désactivée sauf indication ; cache Windows non vidé, poste partagé avec son activité normale. Le mode hors ligne est isolé au processus par un proxy local inaccessible, sans couper le réseau du poste.

| Cas et thème | Fenêtre interactive UIA, s | CPU moyen après 15 s, % d'un cœur | Maximum après 15 s, % | Mémoire privée finale, Mio | Mémoire résidente finale, Mio |
|---|---:|---:|---:|---:|---:|
| Sans dossier, clair | 1,328–1,548 | 0–0 | 0 | 104,4–104,7 | 161,6–162,1 |
| Sans dossier, sombre | 1,604–3,866 | 0–1,868 | 10,592 | 104,3–118,3 | 161,3–183,3 |
| Démonstration, clair | 1,403–4,277 | 0–0,761 | 3,112 | 144,8–165,2 | 209,2–234,2 |
| Démonstration, sombre | 1,482–1,527 | 0,485–0,830 | 7,468 | 153,9–161,1 | 222,9–233,7 |
| Compte vide, clair | 1,485–2,927 | 0–0,052 | 0,311 | 133,9–134,5 | 229,6–230,3 |
| 12 opérations, sombre | 1,220–1,582 | 0–0,052 | 0,312 | 154,2–157,3 | 254,2–256,9 |
| 10 000 opérations, sombre | 1,393–1,880 | 0,035–2,319 | 20,504 | 164,0–168,7 | 264,1–269,1 |
| 10 000 opérations, sombre, réseau isolé | 1,482–1,735 | 0–0,069 | 0,623 | 161,5–163,9 | 264,7–265,0 |
| 12 opérations, sombre, actualisation activée | 2,393–2,506 | 0,052–0,259 | 0,622 | 160,0–161,0 | 260,4–262,3 |
| Même cas, réseau isolé | 1,717–3,511 | 0,104–2,804 | 16,842 | 157,4–161,2 | 256,1–261,1 |

Le maximum d'échauffement atteint 94,560 % d'un cœur dans cette matrice. Le pic tardif à 16,842 % est encore porté principalement par le worker de compilation (734,375 ms). Celui à 20,504 % n'est **pas attribué complètement** : des threads ont disparu entre les relevés ; aucune cause n'est inventée pour le CPU manquant. Tous les relevés minimisés sont à zéro sauf un à 0,156 % d'un cœur. Aucune croissance mémoire continue n'est démontrée sur ces durées courtes ; elles ne remplacent pas un essai d'endurance.

### Contrôles et comparaison après correction WPF

- Minimisation précoce après chargement : premier intervalle à 7,157 %, suivants à zéro. Un intervalle visible suivi d'un minimisé ne suffit donc toujours pas à incriminer le rendu.
- Deux processus de diagnostic avec `DOTNET_TieredCompilation=0` : intervalles mesurés à zéro. **Ce paramètre reste limité aux contrôles**, absent de la configuration du produit ; ce n'est pas une optimisation livrée.
- Après les correctifs d'onglets/accessibilité/layout : deux répétitions natives de 10 000 lignes, 60 s visibles ; CPU après 15 s approximativement 0,104 % / 0,138 %, maxima 0,312 % / 0,623 %, CPU minimisé nul ; privée 163,3 Mio, résidente 264,1–264,6 Mio. Les fenêtres interactives sont à 2,336 / 2,352 s. **Aucun gain CPU causal n'est revendiqué** : les pics restent variables, les changements portent sur des défauts d'interface, pas sur le moteur runtime.
- Sonde reproductible `tools/Measure-MonetaIdle.ps1` réellement exécutée : instance sans dossier, 120 DPI, 30 s visibles ; premier intervalle à 58,557 %, trois derniers à zéro, minimisé à zéro, réponse UIA à 4,166 s. Ce résultat défavorable reste visible, pas éliminé au profit du meilleur essai.

Le délai « dossier prêt » des essais natifs comprend volontairement l'automatisation du dialogue de fichier et ses attentes ; il ne doit **pas** être vendu comme temps d'E/S SQLite seul. Le premier pixel composé n'est pas mesuré. L'objectif interactif inférieur à 2 s est atteint dans plusieurs essais, **pas de façon garantie**. Aucun travail à risque sur les ressources WPF, le JIT ou la durabilité n'a été ajouté pour forcer cet objectif.

Reproduction sans modifier préférences ni paramètres runtime :

```powershell
dotnet build Moneta.slnx -c Release
powershell -NoProfile -File tools/Measure-MonetaIdle.ps1 -Demo -Seconds 60
```

La sonde ne ferme que son propre PID, par fermeture normale. Elle n'accepte pas de fichier financier en entrée ; son rapport privé temporaire distingue HWND visible, réponse UIA, démonstration, DPI, mémoire et threads. Le marqueur d'échauffement est indicatif : examiner les threads tardifs avant de qualifier un intervalle de « repos stabilisé ».

## Interprétation et documentation .NET

Les événements observés correspondent au mécanisme documenté par Microsoft : [paramètres de compilation](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation) et [événements de compilation étagée](https://learn.microsoft.com/dotnet/fundamentals/diagnostics/runtime-tiered-compilation-events). Les reprises `Resume` après une période d'appels initiaux expliquent pourquoi un petit sursaut peut apparaître après plusieurs fenêtres calmes. Les compteurs OS ont une granularité d'environ 15,625 ms sur ces essais : une fenêtre de 5 s peut donc passer de 0 à environ 0,312 % sans constituer une boucle permanente.

## Qualification WPF : défauts observés avant correction

Inspection de 64 combinaisons : huit surfaces, deux thèmes, quatre DPI WPF contrôlés. Le poste est physiquement observé à 120 DPI ; une injection **WM_DPICHANGED de diagnostic** modifie le DPI WPF à 96/120/144/192, tandis que `GetDpiForWindow` reste à 120. C'est une **qualification contrôlée du layout et du texte**, pas une campagne sur quatre moniteurs physiques. Les captures `RenderTargetBitmap` ne prouvent toujours pas le premier pixel composé natif.

Défauts reproduits :

- **Texte invisible en contenu d'onglet clair** : le style `TabItem` appliquait `SelectionTextBrush` au contrôle entier, donc au contenu autant qu'à son en-tête. Les explications de Général dans Options étaient blanches sur blanc. Le correctif réserve cette couleur au présentateur de l'en-tête.
- **Rapprochement comprimé à petite hauteur** : les champs et paginations consommaient la hauteur disponible, laissant le registre de candidats sans ligne visible. La zone d'onglet devient défilante, avec un contenu de hauteur finie minimale ; le DataGrid reste contraint et virtualisé.
- **Noms accessibles absents** : champs du compte, nom du dossier, date/devises du convertisseur, champs du relevé et TextBox internes des ComboBox éditables. Noms sémantiques ajoutés aux champs et propagés par le template partagé.

Après correction : builds Debug/Release sans erreur ni avertissement, **516 tests réussis dans chaque configuration**, 64 configurations WPF réexercées avec assertions et captures de l'arbre visuel. Aucun champ activé/focalisable sans nom accessible détecté dans ce périmètre ; contraste du corps Général **15,085:1 en clair et 14,441:1 en sombre** ; hauteur finie du registre de rapprochement **166 à 465 DIP**. Le défilement permet de consulter les contrôles hors viewport ; les autres onglets sont aussi rendus et l'ordre de focus est parcouru. Les captures avant/après restent privées.

Limites matérielles : un seul écran à 125 % natif ; simulations contrôlées de DPI WPF à 100/150/200 %, pas un déplacement entre quatre moniteurs. Aucun lecteur d'écran ni campagne de contraste élevé réellement exécuté. Les noms UI Automation ne valent pas une qualification complète avec lecteur d'écran ; les captures de l'arbre visuel ne remplacent pas une preuve du compositeur Windows.


## Audit financier ciblé

Le moteur, le schéma 5 et le contrat manuel ne changent pas. Revue de `MonetaFile.Save`, de la validation du graphe, des règles de rapprochement, du calcul de conversion et de `MonetaBackup` :

| Invariant / défaillance | Preuve exécutée dans les suites Debug et Release |
|---|---|
| Montants natifs exacts, références et pagination | `MonetaFileTests`, `LedgerStorageTests` : unités mineures, limites, révisions, références invalides, balances hors page et isolation |
| Pas de demi-virement ni de frais cachés | `TransferStorageTests` : lot complet, liens immuables, échec du writer puis nouvelle tentative, opérations orphelines/corrompues refusées |
| R protégées, réouverture traçable | `ReconciliationStorageTests`, `TransferStorageTests` : correction/suppression refusées, états indépendants des jambes, instantanés et événements conservés |
| Changements externes même à solde identique | `LedgerStorageTests`, `TransferStorageTests` : empreinte du contenu et des relations, pas comparaison du seul solde |
| Sauvegarde à chaud cohérente | `BackupRestoreTests`, `TransferBackupTests` : writer WAL non validé exclu, lecteur épinglé, graphe complet restauré, doublons/liens et tombstones conservés |
| Publication interrompue / source altérée | `BackupRestoreTests`, `LedgerBackupTests` : manifeste absent ou invalide refusé, hash concordant insuffisant pour masquer une différence sémantique |
| Migrations et sources protégées | `BackupRestoreTests`, `ReconciliationBackupTests`, `TransferStorageTests` : anciennes représentations comparées, source readonly inchangée, versions futures/corruption refusées |
| Cache indisponible / concurrence / change | `ExchangeRateSessionTests`, `OfficialRateCacheTests`, `OfficialRateDomainTests`, `RateDisplayTests` : lots immuables, erreur visible, conservation des cours applicables, absence de repli inter-banque et de mutation financière |

Les validations de lecture sont épinglées dans une transaction SQLite ; l'enregistrement relit l'état complet dans sa transaction avant les écritures et ne publie le snapshot mémoire qu'après commit. La sauvegarde utilise `BackupDatabase`, consolide une copie autonome puis vérifie contenu et manifeste ; la restauration rouvre réellement la nouvelle copie. Les cours passent par un calcul rationnel exact construit à partir de `decimal`, avec un seul arrondi monétaire final.

Aucun nouveau défaut financier n'est démontré dans ce périmètre. Les corrections étant visuelles, leur régression est exercée sur le vrai runtime WPF, pas par des tests permanents de chaînes XAML. Les **516 tests permanents sont conservés** ; les sondes temporaires WPF/performance ne gonflent pas ce décompte.

Limites : rollback transactionnel et interruptions de publication simulés, pas panne électrique matérielle ; toutes les ACL, disque plein, supports amovibles et alias filesystem non qualifiés. Le verrou de session reste coopératif/par chemin ; une session n'est pas une API d'écritures parallèles. Le hash de sauvegarde n'authentifie pas son auteur et ne chiffre aucune donnée.


## Parcours Windows et E/S isolées

Deux parcours synthétiques complets **clair et sombre**, avec les assemblages/ressources WPF de production dans une sonde temporaire : vrais dialogues, commandes routées, boîtes Windows et stockage SQLite, pas de services factices. L'exécutable produit est, séparément, celui des mesures natives précédentes et des ouvertures de dossiers. Les parcours vérifient :

- Nouveau dossier vide, ajout de trois comptes (deux EUR et un BYN), validation refusée d'une précision excessive avec champs conservés ;
- recette/dépense, correction puis suppression confirmée ; virement de deux montants natifs et frais séparés, P sur une jambe et relevé R sur l'autre, consultation protégée du virement ;
- writer SQLite concurrent : erreur affichée, brouillon conservé, transaction adverse abandonnée, annulation de fermeture puis enregistrement réussi ;
- démonstration isolée, réouverture réelle sans fusion, sauvegarde à chaud via dialogue, restauration vers destination neuve et comparaison d'identité/empreinte du graphe complet ;
- fermeture du runtime WPF puis nouvelle ouverture des deux copies pour comparer encore leur empreinte.

Dans chaque parcours final : **3 comptes, 6 opérations stockées dont une supprimée logiquement, 1 virement et 1 relevé** ; solde fictif du courant EUR **1 147,00**, du BYN **1 175,00**. Les comparaisons portent aussi sur les liens, états et l'historique, pas uniquement ces soldes.

Les premières sondes ont échoué sur leurs propres hypothèses de synchronisation/interop (le bouton natif « OK » était exposé avec l'ID 2, pas 1 ; appel d'automatisation synchrone bloquant). Leur correction n'a modifié aucun comportement du produit. Seuls les deux parcours finaux complets ci-dessus sont annoncés réussis ; les sources de sondes ne sont pas livrées comme code applicatif.

Mesure SQLite distincte, même corpus synthétique, **5 ouvertures fermées proprement par volume**, Release sans UI Automation dans l'intervalle. Cache OS non vidé et JIT non neutralisé. Ouverture inclut la validation complète ; lecture de page inclut les totaux exacts du compte.

| Opérations | Objets de première page | Première ouverture, ms | Médiane des 5 ouvertures, ms | Médiane première page, ms |
|---|---:|---:|---:|---:|
| 0 | 0 | 164,772 | 13,909 | 0,150 |
| 12 | 12 | 51,316 | 17,154 | 1,180 |
| 10 000 | 100 | 1 555,162 | 988,581 | 49,557 |

Ce sont des observations locales, pas des garanties de latence. La validation intégrale reste volontairement O(N) ; aucune optimisation risquant de masquer une incohérence n'est faite.


## Conservation des preuves et confidentialité

Rapports numériques, trace synthétique et résultats des parcours finaux conservés localement dans `screenshots-private/mission9-measurements/`, avec les captures privées de qualification ; aucun de ces éléments n'est versionné. Les TRX restent dans `tests/Moneta.Tests/TestResults/`, également ignorés. Le projet de sonde temporaire et ses bases synthétiques sont retirés après fermeture des accès ; les préférences initiales restent inchangées.

Le contrôle d'exclusions a aussi révélé que `*.bp[0-9]` n'excluait pas les sauvegardes à plusieurs chiffres. Le motif devient `*.bp[0-9]*` ; contrôles `bp0`, `bp10` et `bp200`, `.moneta`/journaux/manifeste, cache SQLite, captures et référence concluants. Cette protection ne prétend pas effacer l'historique existant.
