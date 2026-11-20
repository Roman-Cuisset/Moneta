# Moneta

![Symbole Moneta](assets/branding/moneta-64.png)

**Mission 17.** Nouvelle identité bleu nuit et turquoise, thèmes clair et sombre, solde mieux repéré et commandes du registre accessibles près de la pagination. Le logo vectoriel et l’icône Windows se trouvent dans [les ressources de marque](assets/branding/). [Identité visuelle et validation Windows](docs/VISUAL_IDENTITY.md)

**Mission 16.** Le menu **Rapports** ouvre les budgets mensuels, les flux, les répartitions, les graphiques, les soldes à date, les prévisions et les rapports figés. **Ctrl+B** ouvre les budgets et **Ctrl+T** les totaux par catégorie. Les montants partiels sont identifiés et un rapport figé se rejoue hors ligne. [Budgets et rapports](docs/BUDGETS_AND_REPORTS.md)

**Mission 15.** **Outils → Convertisseur historique…** convertit à la date demandée. **Opérations → Valoriser l’opération enregistrée…** utilise la date de l’écriture. **Outils → Valoriser le solde à date…** calcule d’abord le solde natif à la date d’arrêté, jour inclus. Chaque résultat peut être exporté dans une preuve locale et reproduit hors ligne. [Valorisation historique](docs/HISTORICAL_FX_ENGINE.md)

**Mission 14.** **Comptes → Échéancier…** ou **Ctrl+E** affiche les dépenses, recettes et virements programmés. Vous pouvez traiter, reporter ou ignorer une occurrence. La confirmation prépare l’écriture, puis **Ctrl+S** l’enregistre avec sa décision. Les virements entre devises demandent un montant reçu confirmé. [Échéancier et opérations récurrentes](docs/SCHEDULED_TRANSACTIONS.md)

Successeur Windows de BankPerfect pour la gestion locale de comptes personnels. Priorité : mêmes repères de travail, avec une interface desktop moderne et originale et des garanties financières explicites.

**Mission 13.** **Outils → Options → Sauvegardes** permet d’activer les copies après Ctrl+S ou quotidiennes et de choisir un répertoire local et une rétention. **Fichier → Sauvegardes → Historique…** permet de vérifier une version, de sauvegarder immédiatement le dernier état enregistré ou de restaurer vers un nouveau fichier. [Sauvegardes automatiques et récupération](docs/AUTOMATIC_BACKUPS.md)

**Mission 12.** Recherche rapide au-dessus du registre, **F7** pour les filtres avancés et **Comptes → Registre de tous les comptes** pour la consultation multicompte. Les résultats des opérations enregistrées sont paginés par 100. Ils permettent de rejoindre l’écriture exacte et de revenir aux résultats. Les montants restent dans chaque devise native. [Recherche et filtres](docs/SEARCH_AND_FILTERS.md)

**Mission 11.** Le menu **Comptes** et le panneau gauche permettent de modifier les propriétés, réordonner, archiver ou réactiver les comptes. Les comptes archivés restent consultables avec recherche dans leurs opérations et accès aux virements et relevés. La suppression confirmée est réservée aux comptes vierges. **Ctrl+S** enregistre les modifications. Le schéma SQLite 6 ajoute les coordonnées facultatives et migre les dossiers antérieurs sur copie. [Règles et parcours de validation](docs/ACCOUNT_MANAGEMENT.md)

Les cours officiels NBRB/CBR datés et sourcés sont consultables hors ligne après mise en cache. Le convertisseur et l’estimation du solde sélectionné restent indicatifs. L’actualisation automatique est désactivée par défaut et la gestion des comptes fonctionne hors ligne.

**Consolidation 6.1 :** 97 tests Debug/Release, contrôles SQLite/sauvegardes revus et erreur de source verrouillé corrigée. Un pic CPU capturé est attribué à la compilation étagée .NET, sans boucle de layout démontrée ; aucun contournement graphique/runtime livré. [Audit, mesures et réserves avant mission 7](docs/AUDIT_MISSION_6_1.md).

## Consolidation de mission 9

- Profilage CPU OS/EventPipe : pic corrélé au **même thread de compilation étagée .NET**, sans boucle de layout permanente démontrée. Reprises tardives et limites d'attribution conservées ; **aucun contournement runtime ou gain CPU artificiellement annoncé**.
- Corrections ciblées : texte du contenu d'onglet clair, défilement du rapprochement à petite hauteur, noms accessibles des champs et ComboBox. Aucune refonte, dépendance ou modification du moteur financier.
- Builds Debug/Release : zéro erreur/avertissement ; **516 tests réussis par configuration**, aucun échec/ignoré. Huit surfaces WPF, clair/sombre, quatre DPI contrôlés : 64 combinaisons ; poste natif 125 %, autres DPI simulés, pas de qualification multi-écrans/lecteur d'écran.
- Parcours Windows synthétiques clair/sombre : comptes, recette/dépense/correction/suppression, virement avec frais, P/R, protection R, échec d'écriture et nouvelle tentative, annulation de fermeture, réouverture, sauvegarde/restauration avec comparaison intégrale. Les sondes temporaires ne sont pas comptées comme tests MSTest permanents.
- Nouvelle piste primaire : **BankPerfect 9 public MIT**, avec description/lecteur V5. Aucun fichier natif synthétique disponible dans la référence, aucun code tiers repris, aucun BankPerfect exécuté ; pas d'import implémenté.

Preuves, matrice des mesures et réserves : [PERFORMANCE_AUDIT](docs/PERFORMANCE_AUDIT.md). Préparation native : [recherche du format](docs/BANKPERFECT_FORMAT_RESEARCH.md), [mapping vers le schéma 5](docs/BANKPERFECT_MAPPING.md), [stratégie d'import](docs/BANKPERFECT_IMPORT_STRATEGY.md). `tools/Measure-MonetaIdle.ps1` permet de reproduire les mesures sur sa propre instance vide ou de démonstration sans ouvrir de données financières.


## Objectifs

- Retrouver les fonctions BankPerfect identifiées dans la matrice de parité, sans réduire le produit à un suivi bancaire générique.
- Gérer EUR, BYN et RUB, puis d'autres devises ; consulter les cours officiels, notamment NBRB, conserver leur historique et fonctionner hors ligne.
- Afficher les soldes convertis en euros et gérer des transferts multidevises sans réécrire les opérations passées.
- Ouvrir les fichiers natifs `.bp` par **Fichier → Ouvrir** et convertir les dossiers V5 EUR du périmètre qualifié dans un **nouveau `.moneta`** en préservant l’original. [Périmètre de l’import BankPerfect](docs/BANKPERFECT_QUALIFICATION_10E.md)
- Garantir des montants exacts, une persistance transactionnelle, des sauvegardes automatiques vérifiables et une restauration éprouvée.

C#, **.NET 10 LTS**, WPF et MVVM léger, avec SQLite direct via `Microsoft.Data.Sqlite` **10.0.12** et `SQLitePCLRaw.bundle_e_sqlite3` **3.0.5**. Moteur **3.53.4 observé à l'exécution**, corrigé pour WAL-reset. Interface WPF native avec palettes, pictogrammes et logo originaux Moneta. Voir [ARCHITECTURE](docs/ARCHITECTURE.md) et [FILE_FORMATS](docs/FILE_FORMATS.md).

## Organisation

```text
Moneta/
├── assets/branding/        # SVG source, PNG et ICO Moneta
├── reference-bankperfect/   # Référence locale immuable, exclue de Git
├── src/Moneta.App/          # WPF, fenêtre et interactions de présentation
├── src/Moneta.Core/         # Modèles, projections de soldes, navigation testable
├── src/Moneta.Infrastructure/ # SQLite, sauvegarde/restauration et jeu fictif séparé
├── tests/Moneta.Tests/      # Tests MSTest indépendants de WPF
├── docs/
├── tools/                  # Diagnostic CPU natif reproductible, sans entrée financière
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

Après compilation : `src\Moneta.App\bin\Release\net10.0-windows\Moneta.App.exe`. Le runtime WindowsDesktop .NET 10 est requis ; aucun installateur autonome fourni. La restauration NuGet des tests et de SQLite peut nécessiter Internet. **Par défaut, aucune requête réseau au démarrage : l’actualisation automatique des cours est désactivée.** Si elle est activée explicitement dans Options, le prochain lancement peut interroger le fournisseur choisi, sans bloquer l’ouverture des comptes. Le fonctionnement financier reste local et sans Internet obligatoire ; aucun service de mise à jour ni service réseau permanent.

## Parcours réellement disponible

1. **Fichier → Nouveau dossier / Ctrl+N** : saisir un nom ; dossier vierge en mémoire, sans compte ni opération fictive. Le démarrage n'ouvre aucun dossier automatiquement.
2. **Comptes → Ajouter un compte** permet de choisir le nom, la devise native et le solde initial. Le type, les notes, la banque, la référence et le titulaire sont facultatifs. **Propriétés du compte…** permet de modifier les champs descriptifs à tout moment. Le rang et la devise distinguent les comptes homonymes.
3. **Fichier → Enregistrer / Ctrl+S** : choisir un nouveau fichier `.moneta` au premier enregistrement ; les suivants commitent comptes, référentiels, opérations, virements/liens et rapprochements dans une transaction atomique. Ajouter/modifier/supprimer prépare un **brouillon**, sans commit implicite, y compris pour un dossier encore sans fichier. Une erreur conserve le brouillon et ne produit pas l’état « enregistré ». **Enregistrer sous / F12** copie le graphe complet existant avant d’appliquer les brouillons à la nouvelle copie ; l’ancien fichier reste inchangé.
4. **Fichier → Ouvrir / Ctrl+O** vérifie l’identité, la version, l’intégrité et les références avant d’afficher le dossier. Moneta retrouve les propriétés, soldes, états d’archivage, ordre et sélection de compte du dernier enregistrement. La fermeture ou le remplacement d’un dossier modifié propose d’enregistrer, d’abandonner ou d’annuler. L’ouverture d’un `.bp` lance l’aperçu de conversion vers un nouveau `.moneta` pour les fichiers du périmètre qualifié.
5. **Fichier → Sauvegarder le dossier** : enregistrer d'abord les modifications, choisir un nouveau nom ; copie via API SQLite, contrôle du snapshot complet, puis publication de `Sauvegarde.moneta` et `Sauvegarde.moneta.manifest.json`. **Conserver les deux ensemble.** Copier le seul fichier actif ne constitue pas une sauvegarde.
6. **Fichier → Restaurer une sauvegarde** : choisir une sauvegarde avec son manifeste et une destination neuve. Taille/hash, format, schéma, intégrité, clés et données sont vérifiés ; la copie est effectivement rouverte et comparée. Le dossier affiché et ses brouillons restent intacts. Ouvrir ensuite la copie restaurée pour une bascule volontaire ; ne pas travailler directement dans l'archive certifiée.
7. **Fichier → Mode démonstration** : quatre comptes fictifs et 3 212 opérations, dont 3 200 sur le premier compte. Ajout de comptes en mémoire possible, enregistrement financier désactivé. Aucun passage silencieux de ces données dans un dossier réel.

Les schémas connus **1 à 7** s’ouvrent après **migration consentie vers une nouvelle copie de schéma 8**. Moneta conserve l’original et crée une sauvegarde vérifiée séparée avant migration. La nouvelle copie est contrôlée sur le registre, les UUID, les rapprochements, les virements, l’échéancier et la provenance. Les manifestes des anciennes sauvegardes restent vérifiés avec leurs algorithmes d’origine. Toute version inconnue est refusée.

**Opérations → Nouvelle opération** ou bouton **Nouvelle** sous le registre : date française, sens Recette/Dépense, montant non négatif dans la devise native, libellé obligatoire, mode/tiers/catégorie/sous-catégorie/notes facultatifs. Les valeurs personnalisées alimentent les référentiels du dossier ; les variantes de casse et d’espaces ne créent pas de doublons. Une fraction sous l’unité monétaire est refusée sans arrondi ; zéro est autorisé. Sélectionner une ligne puis **Modifier**, ou double-cliquer. **Suppr / Ctrl+Suppr** dans le registre demande confirmation ; ces touches ne suppriment aucune opération depuis un champ texte. **Ctrl+Entrée / Maj+Entrée**, uniquement dans le registre, ouvrent respectivement la création/modification. **Ctrl+S** reste nécessaire pour rendre ces changements durables.

Le registre du compte reste paginé par **100 opérations** avec précédent/suivant. Le solde total et le solde progressif tiennent compte des opérations hors page. Le registre suit la date, la création UTC et l’UUID canonique. Le tri choisi dans les résultats de recherche s’applique uniquement à cette vue. La saisie reste accessible dans son dialogue depuis le registre.

Le solde actuel est **solde initial + recettes − dépenses non supprimées**, toutes dates comprises, sans conversion. **Solde pointé = solde initial + mouvements actifs P/R** ; **Non pointé = somme des mouvements actifs NP**, sans ajouter une deuxième fois le solde initial. Ces totaux restent en monnaie native même lorsque la devise d’affichage du solde actuel change. Le prévisionnel reste indisponible. Les suppressions logiques sont exclues des soldes mais restent dans les contrôles et sauvegardes ; pas encore de corbeille ni d’historique général des modifications.

**Pointage :** dans le registre, P/Espace ou le bouton de dernière colonne basculent NP/P ; **Maj+P** dépointage, **Ctrl+Maj+P** date de pointage, **F11** bascule la première colonne entre date d’opération et date de pointage. Menus Opérations et contextuel utilisent les mêmes commandes. Ctrl/clic permet plusieurs lignes de la page courante ; Pointer/Dépointer applique tout le lot ou rien. Aucun montant, libellé ou compte n’est modifié. Nouveau pointage d’une opération future refusé ; date locale de vérification proposée aujourd’hui, modifiable et distincte de la date d’opération.

**Opérations → Rapprochement bancaire / Ctrl+Maj+R dans le registre :** enregistrer d’abord le dossier ; saisir date de fin (au plus tard aujourd’hui), numéro facultatif et solde final signé. Ouverture non modifiable : solde initial du compte au premier relevé, sinon clôture du dernier relevé encore actif. Cocher les mouvements NP/P réellement portés au relevé, y compris des anciens non rapprochés. L’écart doit être nul **et** les dates, comptes, devises, références et révisions cohérents. Valider prépare un brouillon ; **Ctrl+S après fermeture** le rend durable. Annuler le dialogue ne change rien.

Les opérations R sont protégées contre édition, suppression et dépointage. Une modification financière d’une P demande confirmation et conserve son pointage. Dans **Historique et réouverture**, seul le dernier relevé actif peut être rouvert, avec motif et confirmation : anciens états/dates restaurés, instantanés et événements conservés. **Enregistrer ce brouillon avant toute autre mutation du registre**, y compris sur un autre compte. Un relevé corrigé reçoit un nouvel UUID. Les R héritées d’un schéma 3 sans relevé attesté restent identifiées comme telles ; libération explicite avec motif depuis Opérations, jamais de relevé ou date inventé.

### Virements liés — mission 7C

**Opérations → Virement…**, outil **Virement**, ou **Ctrl+Maj+T uniquement dans le registre** : choisir deux comptes actifs distincts du même dossier, une date commune et les montants principaux strictement positifs en devises natives. Même devise : le crédit recopie exactement le débit, hors frais. Devises différentes : saisir les deux montants réellement débités/crédités, sans conversion automatique ni arrondi. La date du change reprend par défaut celle du virement ; la source est **« Montants saisis par l’utilisateur »**, libre mais toujours déclarée/non vérifiée, jamais présentée comme officielle.

Les montants saisis sont des magnitudes : le principal source produit un débit (impact −), le principal reçu un crédit (impact +). Le **rapport exact = crédit / débit principal hors frais**, en unités de devise reçue pour **1 unité source** ; les deux montants exacts le conservent, pas un quotient arrondi. L’aperçu « Taux approximatif » est purement informatif ; si le quotient décimal déborde ou devient zéro, seul le rapport exact est affiché. Une devise d’affichage différente ne transforme ni ces montants ni ce rapport.

Les **frais facultatifs** sont un débit distinct sur la source, le destinataire ou un troisième compte actif du dossier, dans sa devise native. Strictement positifs au premier ajout ; un frais déjà présent, même encore en brouillon, peut être corrigé à **zéro**, en conservant son UUID, son compte et son lien. On peut ajouter les premiers frais à un virement qui n’en avait pas ; on ne retire ni ne remplace un mouvement de frais existant. Aucun total ne mélange implicitement des devises ; le débit principal n’inclut pas les frais.

Une ligne liée ouvre le **virement entier** avec Modifier/double-clic ; Supprimer demande confirmation pour l’ensemble. Les comptes source/destinataire et le compte de frais déjà ajouté sont immuables : pour corriger un compte, annuler tout le virement puis le recréer. La suppression avant tout enregistrement retire les brouillons ; après enregistrement elle conserve l’entité, tous les mouvements persistés et leurs liens comme traces supprimées. Des frais ajoutés mais non enregistrés sont écartés si le virement persistant est supprimé avant leur Save. Aucune édition/suppression financière d’une jambe isolée n’est autorisée, dans Core comme dans le stockage.

Les états **NP/P/R restent indépendants** : pointer le débit ne pointe ni crédit ni frais. Une correction d’un mouvement P conserve état/date avec confirmation si date ou montant change. **Une seule jambe R, frais compris et même hors écran, protège tout le virement** : consultation seule, édition/suppression refusées. Rouvrir le relevé concerné ou libérer explicitement une R héritée, puis **Ctrl+S avant toute correction**.

**Ouvrir le virement lié… / Autre côté du virement / Supprimer le virement entier…** sont des commandes Moneta explicites dans Opérations et le contexte ; le bouton ↔ de la ligne rejoint aussi la contrepartie. Débit → crédit ; crédit ou frais → débit. La sélection utilise l’UUID et charge sa page de **100 lignes**, même si elle n’était pas affichée. Ces accès et les garanties multidevises ne prétendent pas être une parité BankPerfect parfaite.


**Affichage → Thème → Clair / Sombre** conserve la préférence locale et la correction de sélection ; contraste élevé prioritaire. **Outils → Options** conserve ses cinq rubriques : **Devises et taux** pilote désormais le fournisseur et l’actualisation au démarrage ; **Mises à jour** reste une préférence enregistrable sans service développé. Voir [CURRENCIES_AND_SETTINGS](docs/CURRENCIES_AND_SETTINGS.md).

### Cours officiels et valorisation — mission 8

1. Ouvrir **Outils → Cours de change…**, accessible même sans dossier financier. Choisir **NBRB** (défaut, référence BYN) ou **CBR** (choix explicite, référence RUB), une date au format `jj/mm/aaaa`, puis **Consulter le cache** ou **Actualiser**. L’entrée **Outils → Actualiser les cours officiels** actualise aussi la source et la date courantes. Un changement de fournisseur est enregistré mais **consulte uniquement son cache** ; il ne déclenche pas de téléchargement, de repli vers l’autre banque ni de mélange de publications.
2. L’onglet **Cotations** affiche code, nom, nominal, valeur officielle et devise de référence. La date demandée, la date d’effet réelle, l’heure de récupération et l’origine **cache disque / réponse HTTPS / mémoire non persistée** restent visibles ; l’adresse interrogée et l’empreinte SHA-256 permettent de retrouver le contenu conservé. L’identité de la devise de référence est explicitement mathématique, pas un cours publié inventé.
3. **Historique local** montre au plus les 100 publications récentes du fournisseur ; les archives plus anciennes restent consultables par date, sans purge automatique. Pour une date de valorisation, le cache sélectionne la publication la plus récente **à cette date ou avant**, vieille d’au plus **31 jours calendaires** ; sa date réelle et son ancienneté ne sont pas masquées. Au-delà, ou si une devise n’est pas cotée, l’estimation est **Cours indisponible**, jamais zéro ou un taux de repli. Une publication anticipée est conservée mais inutilisable avant sa date d’effet.
4. Le **convertisseur de montant** prend un montant français, les devises source/destination et le jeu officiel chargé ; **Convertir** ne crée aucune opération. Le résultat est indicatif, calculé avec les nominaux officiels et arrondi **une seule fois au pair** à la précision de destination. Zéro et montants négatifs sont acceptés ; une fraction non représentable dans la devise source est refusée. Même devise = identité, sans change ni publication nécessaire. Il s’agit d’une **saisie manuelle de montant**, pas d’une saisie de taux manuel.
5. Près du **solde actuel**, le sélecteur **Devise d’affichage** conserve le montant réel en natif ; une autre devise affiche un **équivalent estimé**, sa source, date d’effet, date demandée, récupération et état du cache. Le solde natif reste indiqué séparément ; pointé/non pointé et toutes les opérations restent natifs. EUR/BYN/RUB et les devises déjà définies dans le dossier sont des choix de présentation, sans ajout au catalogue financier. Changer de cible ne modifie ni les comptes, ni les opérations, ni l’état « modifié ». L’API d’agrégat existe ; **l’interface Total des comptes / relevé multicomptes n’est pas réalisée**.
6. Dans **Outils → Options → Devises et taux**, l’option d’actualisation automatique est **désactivée par défaut** et ne s’applique qu’au prochain lancement. Si activée, un cache correspondant au jour demandé et récupéré depuis au plus six heures évite un nouveau téléchargement ; un cache historique applicable est réutilisé. **Actualiser** force une nouvelle demande. L’absence de publication peut conduire à interroger au plus les sept dates calendaires précédentes (huit au total), avec délai global de 20 s et 10 s par requête ; une erreur HTTP/réseau n’est pas retentée. **Annuler la requête**, changer de sélection ou fermer la fenêtre annule la demande concernée ; une ancienne réponse ne remplace pas une sélection plus récente.

**Hors ligne et erreurs :** les comptes restent utilisables et un cours applicable déjà validé peut servir, avec sa date et l’avertissement explicite. Sans cours applicable, aucune estimation n’est fabriquée. Un échec d’écriture du cache n’efface pas les cours valides en mémoire ; il est annoncé comme **non enregistré**, donc sans promesse de récupération au redémarrage. Le cache public indépendant `%LocalAppData%\Moneta\exchange-rates.sqlite` n’entre pas dans le `.moneta`, son WAL, ses empreintes ou ses sauvegardes financières.

**Réseau et sécurité :** seuls les endpoints HTTPS officiels fixes reçoivent une date publique, jamais comptes, montants, opérations ou identifiants. Redirections et cookies désactivés ; réponses limitées à 1 Mio et 1 000 cotations, XML sans DTD ni résolution externe, encodage déclaré respecté. L’empreinte atteste le contenu conservé, **pas une signature officielle du flux**. Aucun ajout de télémétrie, cloud ou installation de mises à jour. Les virements gardent leurs **deux montants réels figés**, frais et source déclarée : aucune suggestion officielle appliquée, aucun recalcul rétroactif et aucun changement du cycle Ctrl+S.

## Limites et divergences volontaires

L'ordre principal **Fichier | Édition | Affichage | Comptes | Opérations | Outils | Plugins | Internet | ?** et l'organisation de la principale restent fidèles à la capture privée. Elle ne prouve ni sous-menus fermés ni accélérateurs Alt. Choix Moneta explicites : Nouveau dans Fichier (également accessible sous Comptes), Enregistrer sous dans Fichier, sauvegarde/restauration manuelles et mode démonstration. F12 est documenté historiquement, pas l'emplacement exact d'Enregistrer sous. Aucun menu inconnu déclaré identique à BankPerfect.

Le total multicomptes se consulte dans **Rapports → Soldes à date**. La saisie de taux manuel, la conversion d’une opération existante en virement et l’annulation financière globale restent ouvertes. Tous les comptes d’un virement, frais compris, doivent être actifs pour le modifier. Le registre du compte conserve son solde progressif et son ordre chronologique. Les résultats de recherche disposent de leurs propres tris. Les dialogues natifs de fichier et de confirmation utilisent l’apparence Windows.

**Erreurs et sécurité :** fichier absent/incompatible/corrompu ou version future refusé, aucune base vide substituée ; destinations existantes et journaux de récupération préservés. Une session par chemin via verrou système `.moneta.lock` ; fichier vide résiduel normal après fermeture/crash. Attente SQLite bornée, checkpoint bloqué signalé en gardant la session utilisable. Stockage actif local uniquement : réseau refusé, dossiers synchronisés non pris en charge. SQLite et sauvegardes **non chiffrés/non signés** ; protéger les droits du dossier et le support. Ne jamais supprimer WAL/SHM/journal pour débloquer.

## Preuves acquises de mission 8

Les endpoints publics ont été interrogés avec le véritable client .NET `OfficialRateClient` sous Windows : **HTTP 200 pour les deux banques**, aux dates explicites du **03/04/2026** et du **03/11/2026**. [NBRB, table quotidienne](https://api.nbrb.by/exrates/rates?ondate=2026-04-03&periodicity=0) : 5 030 octets et 30 cotations ; [CBR, XML quotidien](https://www.cbr.ru/scripts/XML_daily.asp?date_req=03/04/2026) : 9 510/9 511 octets selon la réponse et 54 cotations, encodage Windows-1251 d’origine correctement lu. La [documentation XML CBR](https://www.cbr.ru/development/SXML/) a été consultée. La [page d’aide NBRB](https://www.nbrb.by/apihelp/exrates) a expiré dans l’outil de lecture : **son contenu n’est pas réputé vérifié**, contrairement aux réponses API, dont l’accès individuel EUR avec `parammode=2`.

Sur ces réponses authentiques, **1 000 BYN** donnent, au 03/11/2026, **301,67 EUR / 27 339,58 RUB avec NBRB**, contre **302,92 EUR / 27 403,10 RUB avec CBR** ; au 03/04/2026, respectivement **295,41 EUR / 27 024,84 RUB** et **294,97 EUR / 27 353,10 RUB**. Ce sont des observations datées, **pas des taux codés dans l’application ni une garantie de disponibilité**. Une lecture du dimanche 05/04 a renvoyé un effet réel au 04/04 pour CBR et au 05/04 pour NBRB : la date de réponse n’est pas réécrite pour coïncider avec la demande.

**Compilation Debug et Release : 0 erreur et 0 avertissement. Tests : 516 réussis, 0 échec et 0 ignoré dans chaque configuration**, dont les 329 cas antérieurs conservés. Résultats locaux reproductibles dans `tests/Moneta.Tests/TestResults/mission8-final-debug.trx` et `mission8-final-release.trx` (exclus de Git). Les tests permanents n’utilisent pas Internet : réponses authentiques figées, transports/cache/horloge contrôlés, SQLite temporaire. Couverture : nominaux 1/10/100/1 000 et au-delà, calculs directs/inverses/croisés, arrondi au pair final unique, dates/futur/week-end et redénominations, erreurs HTTP/timeout/annulation, cache/redémarrage/corruption, source explicite et protection des virements/P/R.

Trois défauts d’intégration ont été corrigés après revue indépendante : perte du dernier lot valide en mémoire après échec du cache ; requête tardive déclenchée par une ancienne sélection de date ; devises de présentation proposées à tort à la création d’un compte natif. Les régressions vérifient notamment la séparation du catalogue financier, les générations/annulations et la conservation des snapshots, virements, schéma 5 et octets financiers/WAL lors des conversions. Les observations des missions précédentes ci-dessous restent **historiques**, avec leurs propres conditions et limites.

Défaut historique supplémentaire reproduit : les réponses anciennes, pourtant valides, étaient étiquetées avec la monnaie de référence actuelle. Quatre régressions échouaient avant correction ; les six cas ciblés passent après garde-fou, y compris les deux bornes acceptées. **NBRB d’effet avant le 01/07/2016 et CBR avant le 01/01/1998 sont désormais refusés**, sans redénomination automatique. L’API NBRB du 30/06/2016 renvoie notamment EUR 22 210 en anciennes unités, et CBR demandé au 31/12/1997 renvoie USD 5 960 à l’effet du 30/12/1997 : les présenter comme BYN/RUB modernes aurait été faux. [Preuves, sources officielles et politique](docs/ARCHITECTURE.md#54-banque-centrale-de-russie--étude-complémentaire-mission-2).

Le vrai processus WPF Release a ensuite refusé ces deux réponses historiques, refusé leur conversion, accepté le premier jour BYN **01/07/2016**, puis rechargé les cours CBR actuels. La règle est donc exercée au-delà des tests unitaires, sans modifier un dossier financier.

### Parcours Windows et indépendance financière

Véritable processus Release : ouverture d’un `.moneta` strictement synthétique, compte à **1 000 BYN** après virement réel fictif de 500 BYN / 140 EUR et frais 3 BYN ; choix EUR/RUB/BYN, montants natifs toujours conservés, NBRB puis CBR, fenêtre des cours, source et dates visibles. Redémarrage confirmant source et préférence automatique ; essais de l’option désactivée puis activée avec cache frais.

Hors ligne simulé **uniquement dans l’environnement du processus enfant**, par proxy local inaccessible : cache disque relu après redémarrage, erreurs réseau explicites, estimations NBRB et CBR identiques à leurs résultats en ligne respectifs, sans mélange. Retour à un processus sans ce proxy : réponses officielles à nouveau validées. Le parcours CBR du **05/04/2026** affiche bien l’effet du **04/04/2026**. Aucune carte réseau Windows désactivée.

Requête réellement retenue par une sonde proxy locale finie : saisie, **Ctrl+A**, conversion avec cours déjà chargés et annulation restent utilisables pendant l’attente ; annulation explicite observée, puis fermeture normale. La sonde a effectivement reçu la connexion ; il ne s’agit pas d’une simulation de l’état visuel « téléchargement ».

Après les parcours en ligne/hors ligne, réouverture, historique et clair/sombre, **SHA-256 du fichier financier entier identique avant/après**, schéma 5, intégrité SQLite et références valides. Les nouvelles préférences ont été exercées puis les choix locaux préexistants restitués. La suite financière conserve aussi les vérifications de sauvegarde/restauration et les protections P/R ; le cache public n’est volontairement pas inclus dans une sauvegarde de comptes.

Inspection des véritables arbres visuels WPF via `RenderTargetBitmap`, clair/sombre, poste à **200 % / 192 DPI** : texte sélectionné lisible, source/date et natif visibles. Une compression des colonnes coupait les codes de devise sur la petite fenêtre ; minima de largeur corrigés, défilement horizontal conservé et texte intégral accessible en infobulle. Nouvelle inspection effectuée après correction. Captures synthétiques locales `screenshots-private/moneta-m8-{light,dark}-{rates,balance}.png`, exclues de Git. **Ce rendu d’arbre n’est pas une preuve de capture composée native** ; qualification physique multi-DPI/moniteurs, haut contraste et lecteurs d’écran à poursuivre.

### Mesures de mission 8

Windows **10.0.26220 x64**, Core **i5-1235U**, .NET **10.0.12**, 12 processeurs logiques, Release sans débogueur, 200 %. Vrai processus en démonstration (3 200 opérations), fenêtre des cours ouverte puis fermée ; 5 s d’attente avant l’intervalle visible de 30 s, puis 10 s minimisé. Aucune interrogation UIA pendant ces deux intervalles. Lancement = démarrage du processus jusqu’à fenêtre principale accessible via UIA, et non une mesure de premier pixel. **Un essai final par condition**, caches système et cours déjà chauds ; pas de garantie statistique ni comparaison stricte aux anciennes missions.

| Mesure WPF finale | Observation |
|---|---:|
| Démarrage, actualisation désactivée | 2,579 s |
| Démarrage, actualisation activée, cache frais réutilisé | 3,782 s |
| CPU visible final, intervalle 30,018 s | 33,001 % d’un cœur / 2,750 % des 12 processeurs logiques |
| CPU visible, deux essais préliminaires de 30 s | 5,934 et 9,683 % d’un cœur |
| Mémoire privée visible finale, début → fin | 72,898 → 69,430 Mio |
| Mémoire résidente visible finale, début → fin | 152,598 → 155,555 Mio |
| CPU minimisé final, intervalle 10,006 s | 0 % observé |
| Mémoire privée / résidente à la fin, minimisé | 58,988 / 145,051 Mio |

**L’objectif indicatif de démarrage inférieur à 2 s n’est pas atteint dans cette méthode ; le CPU visible reste élevé et variable sur le dernier intervalle. Ce point n’est pas déclaré résolu.** Aucune attribution causale à un service de cours ou à la compilation étagée n’est déduite de ces essais courts ; aucune garantie d’absence de fuite sur longue durée. Il n’existe ni timer permanent de cours ni service résident ajouté. Un profilage prolongé du vrai processus visible, avant le prochain pilote, est recommandé ; les anciens chiffres et les essais préliminaires ne démontrent pas à eux seuls une régression ni une amélioration.

Sonde API séparée sur réponses officielles du 03/11/2026 ; lecture et conversion : un échauffement puis médiane de 100 appels dans le même processus. Téléchargements et agrégat : **un appel observé**, sans test de débit. Le premier GET était historique (03/04) ; le suivant réutilisait le client/transport pour le jour local. Les 10 000 montants synthétiques sont répartis en BYN/EUR/RUB, avec signes différents ; le total est arrondi une fois.

| Mesure API/cache | NBRB | CBR |
|---|---:|---:|
| Premier GET historique | 845,889 ms | 461,382 ms |
| GET suivant du jour, transport chaud | 75,931 ms | 73,613 ms |
| Lecture/revalidation du cache, médiane | 3,095 ms | 3,433 ms |
| Conversion de 1 000 BYN en EUR, médiane | 0,073 ms | 0,095 ms |
| Conversion et agrégation exacte de 10 000 montants | 65,285 ms | 26,362 ms |


## Vérifications observées de mission 7C

**Campagne historique 7C : résultats et parcours ci-dessous antérieurs à la mission 8.**

**Debug et Release : 0 erreur, 0 avertissement ; 329 tests réussis, 0 échec et 0 ignoré par configuration**, dont les 261 cas précédents. Couverture supplémentaire : mêmes devises et six directions EUR/BYN/RUB, précision extensible, frais, révisions indépendantes, protection globale R, refus des demi-patches, rollback, concurrence, pagination liée, migrations et archives historiques.

Trois incompatibilités de brouillon/Save ont été reproduites puis corrigées : correction financière combinée au pointage NP/P, suppression après ajout de frais non persistés et frais ramenés à zéro avant leur premier enregistrement. **Neuf cas de régression** couvrent ces transitions, dans la suite complète. Les fixtures historiques v4 respectent les clés étrangères et reconstruisent le graphe avant certification ; les tests de copie ferment leur session propriétaire avant une ouverture indépendante.

Parcours du vrai processus Windows, UIA et souris/clavier, en clair/sombre, exclusivement synthétique : comptes EUR A (1 000), EUR B (100), BYN (8 000) ; A → B de 150 + frais 2 corrigé à **175 + 2,50 EUR** ; BYN → A de **500 BYN / 140 EUR + frais 3 BYN** ; virement brouillon de 10 créé puis supprimé en entier ; Save. Débit EUR 175 pointé P et crédit 175 rapproché R dans un relevé enregistré : consultation de l’ensemble et refus de suppression exercés. Fermeture/réouverture normale, sauvegarde à chaud puis restauration vers une copie et ouverture effectuées. Soldes observés : **A 962,50 EUR / pointé 825 ; B 275 EUR / pointé 275 ; BYN 7 497**.

Comparaison hors interface réussie : **deux virements, six mouvements dont deux frais, un relevé, un instantané et un événement** identiques entre source et restauration, avec tous les champs, UUID, révisions, liens, états et historique V1 — pas seulement les soldes. Une sonde synthétique séparée a aussi vérifié individuellement le graphe de **1 006 virements dont six supprimés, 2 352 mouvements**, après cycles de création/correction/suppression, réouverture et sauvegarde/restauration.

Les véritables fenêtres WPF ont été construites et leur arbre visuel inspecté via `RenderTargetBitmap` : registre, dialogue multidevise modifiable et consultation protégée R, clair/sombre, sélection de texte. Le libellé des frais désactivés était noir sur fond sombre : correction par style partagé de case à cocher, puis inspection confirmant sa lisibilité ; le lien du registre est compact et ses infobulles conservent les explications. Captures synthétiques locales dans `screenshots-private/moneta-m7c-*.png`, exclues de Git. **La capture composée native GDI reste limitée sur ce poste** ; validation utilisateur, lecteurs d’écran et campagne multi-DPI/moniteurs restent nécessaires.

### Mesures de mission 7C — historiques

Windows 10.0.26220 x64, Core i5-1235U, 12 processeurs logiques, .NET 10.0.12, Release sans débogueur. Sonde API : trois comptes EUR/EUR/BYN ; **1 000 virements initiaux, 2 334 mouvements, 334 frais**, moitié même devise / moitié multidevise. Un échauffement puis cinq échantillons dans le même processus ; médianes ci-dessous, sans vidage du cache disque ni garantie statistique. Création/correction/suppression désignent la préparation du brouillon, distincte du commit. Initialisation du corpus : **2,424 s**, un essai séparé.

| Mesure API | Médiane |
|---|---:|
| Créer un virement avec frais | 38,139 ms |
| Modifier le virement complet | 45,483 ms |
| Supprimer le virement complet | 28,208 ms |
| Rejoindre la contrepartie hors première page | 13,381 ms |
| Enregistrer après création / correction / suppression | 345,122 / 343,225 / 267,832 ms |
| Ouvrir, vérifier et charger la première page du dossier | 269,536 ms |
| Lire une page de 100 mouvements et leurs liens | 7,421 ms |
| Sauvegarder à chaud | 301,330 ms |
| Restaurer, vérifier et rouvrir | 642,640 ms |

L’exécutable WPF a aussi été observé sur le petit dossier du scénario Windows : trois comptes, deux virements/six mouvements et un relevé. Un lancement, disponibilité de la fenêtre via UIA : **2,512 s**, pas une mesure du premier pixel. Le parcours automatisé d’ouverture a pris **6,127 s**, dialogue natif et attentes UIA inclus : ce n’est pas le temps d’E/S mesuré par la sonde API.

Après dix secondes de stabilisation, fenêtre visible pendant trente secondes sans interrogation UIA, puis minimisée pendant dix secondes après deux secondes de transition :

| Mesure de l’exécutable | Observation |
|---|---:|
| CPU visible / minimisé, équivalent un cœur | 0 / 0 % observé |
| Mémoire privée, début → fin de fenêtre visible | 150,04 → 149,86 Mio |
| Mémoire résidente, début → fin de fenêtre visible | 252,05 → 251,93 Mio |
| Mémoire privée / résidente, minimisée | 149,73 / 251,93 Mio |

Pas de croissance sur cet intervalle court ; aucune preuve d’absence de fuite à long terme. Le raccourci **Ctrl+Maj+T depuis le registre** a ouvert le vrai dialogue, annulé sans mutation, et le processus s’est fermé normalement. Conditions et bornes de démarrage différentes de 7B, autres vérifications sur le même poste : **pas de comparaison causale avant/après** revendiquée. Les sondes et projets temporaires ne sont pas livrés.

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
| [Devises et paramètres](docs/CURRENCIES_AND_SETTINGS.md) | Comptes dynamiques, cours officiels/cache, préférences locales et séparation financière/valorisation |

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

**Mission 10 proposée :** obtenir un corpus `.bp` intégralement fictif et fermé, confronter la description publique V5 à BankPerfect 8.0.0.373, puis valider monnaies/précisions, solde initial, sémantique de date historique, identités et R héritées. Seulement après ces preuves et l'autorisation du lot : lecteur C# passif/borné, modèle intermédiaire de provenance et rapport d'analyse, avant conversion complète vers un nouveau `.moneta`. Aucun fichier personnel ni contournement de protection. Rendu composé, moniteurs physiques, lecteur d'écran, endurance CPU et frontières matérielles de durabilité restent à qualifier. Les autres fonctions absentes restent dans la [ROADMAP](docs/ROADMAP.md), sans commencer budgets ou échéanciers.
