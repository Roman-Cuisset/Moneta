# Identité visuelle Moneta — décisions et principes

## Statut et périmètre

Document d'analyse au 28 octobre 2026, à lire avec [UI_MENU_MAP.md](UI_MENU_MAP.md), [UI_SCREEN_INVENTORY.md](UI_SCREEN_INVENTORY.md), [FUNCTIONAL_PARITY.md](FUNCTIONAL_PARITY.md) et les limites de [BANKPERFECT_AUDIT.md](BANKPERFECT_AUDIT.md). **Aucun logo définitif, aucune charte couleur définitive, aucune maquette ni ressource graphique n'est créé ou livré pendant cette mission.** Aucun aspect visuel de Moneta n'est présenté comme implémenté ou testé.

Le format `.moneta` et la stack C# / .NET 10 LTS / WPF / SQLite / MVVM léger / tests sont des décisions de projet acquises, pas une invitation à lancer le développement dans cette mission. La programmation ultérieure et la présentation de concepts graphiques nécessitent leurs validations propres ; ni ce document ni la décision de stack ne valent accord sur un logo.

Les identifiants `VI-01` à `VI-12` stabilisent les principes pour l'architecture et les futurs travaux. Les contraintes absolues ci-dessous sont des décisions utilisateur ; les objectifs d'ergonomie et d'accessibilité sont des exigences de conception future, sans résultat d'essai revendiqué.

## VI-01 — Origine absolument indépendante des ressources

**Toutes les ressources graphiques spécifiques à l'application seront spécialement créées, originales et propres à Moneta : logo, icônes, pictogrammes, illustrations, images de marque et leurs variantes.** Cette décision ne se limite pas à changer le nom ou l'extension d'un fichier.

Sont exclus :

- tout logo, icône, image ou autre ressource extrait de BankPerfect, de ses exécutables, DLL, scripts, archives, écrans ou répertoires ; aucune extraction, décompilation ou désassemblage n'est autorisé ;
- toute copie, réencodage, vectorisation, recolorisation, redessin fidèle, décalque ou adaptation d'une ressource BP, **même sous un autre nom** ou dans un autre format ;
- tout logo bancaire ou de carte présent dans la référence, y compris lorsqu'il représente une marque tierce et non BankPerfect ;
- tout pack d'icônes ou d'illustrations tiers réutilisé « pour démarrer » ou « temporairement ». Aucun pack tiers n'est admis sans réouverture explicite de cette décision par l'utilisateur ;
- tout asset BP intégré à `src`, ressources, paquets, `build`, installateurs, exemples, documents ou livrables. Les captures de référence ne deviennent pas des assets Moneta ni une bibliothèque d'images pour la documentation.

Les contrôles Windows standard et les fontes système, rendus comme éléments de la plate-forme plutôt qu'extraits/copiés en fichiers, ne constituent pas un pack graphique de marque repris. Leur emploi n'autorise pas la copie de ressources embarquées de BP ni l'introduction d'un pack d'icônes tiers.

## VI-02 — Compatibilité ergonomique, indépendance graphique

L'objectif de fidélité porte sur les **fonctions, repères, gestes et parcours**, non sur la reproduction des formes graphiques historiques. Il est possible de conserver une commande « Transfert », sa place fonctionnelle et Ctrl+Maj+T sans copier son dessin.

| À préserver ou vérifier | À créer indépendamment pour Moneta |
|---|---|
| Hiérarchie menus / comptes / outils / informations / filtres / relevé / saisie / état, documentée par M01 | Identité, détails de composition, dessins et traitement des surfaces |
| Commandes, contextes et raccourcis recensés dans MENU_MAP | Icônes et illustrations originales de ces mêmes concepts |
| Densité du tableau, distinction tiers/détail et catégorie/sous-catégorie | Tracé, proportions, géométrie, style et variantes des pictogrammes |
| Liste des comptes, saisie intégrée/séparée, filtres de colonnes | Palette propre, avec rôles sémantiques à valider et réglages accessibles |
| Emplacement/fonction d'un repère de compte ou de banque, **seulement comme repère ergonomique** | Aucun logo bancaire repris ; privilégier le libellé textuel et, si utile et validé, un symbole abstrait original Moneta |

La référence à un logo existant ne sert qu'à comprendre **où il se trouve et quelle fonction il remplit**. Elle n'est ni un modèle de dessin ni une instruction pour recréer sa silhouette. La présence d'un logo de banque dans BP ne justifie pas de l'afficher dans Moneta.

L'ordre des menus, les séparateurs, les accélérateurs Alt et certains chemins ne sont pas connus. Les stabiliser demande les preuves utilisateur prévues dans SCREEN_INVENTORY ; ils ne seront pas déduits d'une impression visuelle ou de l'ordre des tables documentaires.

## VI-03 — Windows classique, sobre et stable

Principes proposés pour les futurs écrans `SCREEN-001` et `SCREEN-002` :

- une fenêtre Windows classique avec barre de menus, commandes compréhensibles et dialogues cohérents ; pas de remplacement par un ruban, un écran d'accueil marketing, une navigation par grandes cartes ou une réécriture de l'agencement sans arbitrage utilisateur ;
- des tableaux denses mais lisibles, tailles de lignes raisonnables et réglables si nécessaire, colonnes alignées, montants et monnaies clairement associés ; éviter les grands espaces décoratifs qui font perdre des lignes utiles ;
- une hiérarchie visuelle stable : l'ouverture d'un outil, un filtre ou un changement de compte ne déplace pas arbitrairement la saisie ou les principaux repères ;
- une présentation explicite des états, commandes dangereuses et erreurs ; un traitement sobre plutôt qu'une décoration forte, des animations imposées ou des effets transparents gênant la lecture ;
- les nouveaux outils BP→Moneta et les sous-fonctions de taux suivent les points d'entrée établis : Fichier > Ouvrir, Outils > Conversions de devises. Ils ne créent pas une nouvelle navigation principale pour des motifs graphiques.

Ces propositions ne sont pas une maquette et ne fixent ni pixels, ni dimensions, ni grille complète. Tout écart fonctionnel connu, notamment simulation sans écriture ou suppression d'une réécriture monétaire silencieuse, doit rester un arbitrage explicite, pas une « modernisation graphique » qui le masquerait.

## VI-04 — Lisibilité des données financières

- Utiliser les fontes système et une taille lisible ; ne pas embarquer une police décorative ou un fichier de fonte provenant de BP. Choix de graisse, taille exacte et éventuels réglages restent à valider.
- Aligner les montants pour faciliter la comparaison ; expliciter devise et précision d'affichage. Distinguer montant natif, montant converti, total filtré, solde du compte et solde pointé par des libellés, pas seulement une couleur ou une position.
- Maintenir des en-têtes, états de sélection et informations de filtre lisibles. Le détail en seconde ligne et les sous-catégories doivent rester identifiables sans dépendre d'une infobulle.
- Prévoir le texte long, les libellés français, les signes, séparateurs décimaux et dates, sans tronquer silencieusement l'information essentielle. Une infobulle peut compléter, pas remplacer l'unique moyen de lecture.
- Pour `SCREEN-050/051`, distinguer aperçu, confirmation, résultat **complet/partiel/bloqué** et original BP conservé. Pour `SCREEN-046/052/053/055`, afficher source, date d'effet, unité/nominal, origine manuelle ou officielle, statut de cache et absence de cours ; aucune mise en couleur ne doit transformer une donnée absente en taux réputé disponible.

## VI-05 — Palette sémantique indicative, non définitive

Aucun code couleur, logo, thème exportable ou fichier de palette n'est défini ici. Les rôles ci-dessous guident les futures propositions ; ils devront être adaptés aux réglages système et au contraste élevé.

| Rôle | Intention indicative | Redondance obligatoire |
|---|---|---|
| Fond et texte principal | Neutres, sobres, fort contraste utile | Hiérarchie par texte et structure, pas par teinte seule |
| Sélection et focus | Accent limité, repérable au clavier | Cadre/focus distinct et état de sélection discernable |
| Action principale | Accent cohérent et parcimonieux | Verbe d'action visible, état activé/désactivé explicite |
| Validation / pointé / rapproché | Statuts différenciés sans reprendre les coches colorées BP | Libellé, symbole original et nom accessible pour chaque état |
| Avertissement / couverture partielle / cache ancien | Attention sans suggérer un échec total ni une réussite complète | Message explicatif et action possible |
| Erreur / conversion bloquée / taux manquant | Problème clairement identifiable | Texte d'erreur, contexte et conséquence ; pas simple rouge |
| Débit / crédit / solde | Comparaison lisible et non ambiguë | Signe, intitulé ou colonne ; ne pas coder le sens par rouge/vert seulement |
| Valeur officielle / manuelle / cache | Provenance indépendante de la valeur | Mention de source et date, origine en texte ; le cache n'est pas un fournisseur |

Une paire rouge/vert ne doit pas être le seul moyen de distinguer des états. Les couleurs historiques `globalcolors.ini` sont une preuve de personnalisation BP, **pas une palette à importer**. La présence de réglages couleur/police dans `SCREEN-040` ne permet pas de charger un thème BP comme asset Moneta.

## VI-06 — Icônes originales, cohérentes et légendées

La future famille d'icônes devra avoir des règles propres et cohérentes de tracé, épaisseur, angles, marges, taille optique et variantes d'état. Elle devra rester lisible dans une barre d'outils dense et à petite taille, sans recopier la géométrie de BP.

Le cahier fonctionnel pourra citer des **concepts**, par exemple ouvrir, enregistrer, ajouter, modifier, supprimer, filtrer, transférer, ventiler, échéancier, graphique, sauvegarde, devise et aide. Citer un concept courant n'autorise pas à reprendre le dessin de sa représentation historique. Les références graphiques, silhouettes et fichiers BP ne sont pas des matériaux de création.

Les actions importantes auront un libellé textuel lorsque l'espace le permet ; les boutons à icône seule auront un nom accessible et une infobulle explicite. Les états désactivés, sélectionnés, pointés et rapprochés devront être compréhensibles sans couleur seule. L'icône ne remplace ni la commande de menu ni le raccourci documenté.

Aucun choix de pack, aucune série d'icônes « provisoires » et aucun logo applicatif ne sont fournis dans cette mission.

## VI-07 — HiDPI et adaptation, objectifs futurs

Les futures fenêtres, tableaux, dialogues et icônes devront être examinés à **100 %, 125 %, 150 % et 200 %** sous Windows, avec textes réels de longueur synthétique représentative. Sont visés :

- absence de texte coupé, de chevauchement, de bouton hors fenêtre et de zone de saisie inaccessible ;
- tailles de clic et de focus cohérentes ; mise à l'échelle des pictogrammes originaux sans flou ou détail essentiel perdu ;
- visibilité des colonnes, barres de défilement, totaux et erreurs, sans exiger une résolution unique ;
- cohérence de la saisie séparée, calendriers et fenêtres modales ; adaptation entre écrans de facteurs différents à prévoir lors de la conception WPF.

**Aucun essai HiDPI ni test multi-écran n'a été effectué.** Les échelles sont des objectifs de validation et des variantes de capture futures, pas une compatibilité acquise.

## VI-08 — Clavier, focus, accélérateurs et accessibilité

Les raccourcis de [la parité](FUNCTIONAL_PARITY.md#raccourcis-à-préserver) restent ceux de la cartographie ; particulièrement **Ctrl+F = Champs automatiques**, **Ctrl+D = Conversion(s) de devises**, **Ctrl+Maj+T = Transfert**. Une nouvelle fonction ne les détourne pas vers une recherche ou un panneau générique.

Objectifs futurs :

- toutes les tâches essentielles accessibles sans souris ; focus visible et non masqué ; ordre Tab/Shift+Tab logique, aucun piège clavier ; retour de focus prévisible après dialogue ;
- distinction des gestes tableau/Date/Montant, Entrée d'ajout versus Maj+Entrée de modification, et touches de navigation de date ; pas d'interception globale qui empêche la saisie normale ;
- accélérateurs Alt réellement relevés chez BP avant toute proposition de correspondance ; aucun Alt ni ordre de tabulation inventé dans cette mission ;
- prise en compte du **contraste élevé Windows**, des réglages de fonte système et des états de sélection/désactivation, sans images qui portent seules l'information ;
- noms, rôles, valeurs, états et relations de labels exposés par **Windows UI Automation (UIA)**, notamment pour grille, saisie, pointage, messages d'erreur, provenance des taux et rapport de conversion ;
- validation ultérieure au clavier et avec technologies d'assistance, en distinguant navigation visuelle, arborescence UIA et lecture effective. Une simple capture ne prouve pas l'accessibilité.

Il s'agit d'objectifs de conception et de vérification futurs : **aucun résultat de conformité, test UIA, lecteur d'écran ou contraste n'est revendiqué**.

## VI-09 — Concept graphique uniquement après accord utilisateur

Avant tout travail graphique, demander l'accord utilisateur sur la réalisation de **planches de concepts originales** et leur périmètre. Cette étape future doit être distincte de la programmation, du gel d'ergonomie et de la décision de stack.

Les planches pourront comparer, si autorisé : sobriété du traitement, densité typographique, principes d'icônes et rôles de palette. Chaque proposition devra indiquer ce qui est exploratoire, préserver les repères fonctionnels, et montrer les états nécessaires sans reproduire une capture BP sous un nouveau nom.

Un concept retenu ne sera pas appelé « logo définitif » sans validation explicite. Tout changement de logo, palette ou recours à un pack tiers devra repasser par la décision concernée. **Aucune planche, image, variante de logo ni palette codée n'est produite ici.**

## VI-10 — Provenance et licence de chaque ressource future

Toute ressource spécialement créée devra disposer d'une provenance explicite avant intégration. Le registre futur associé aux ressources approuvées devra consigner au minimum :

| Information | Utilité |
|---|---|
| Identifiant stable, nom et chemin de la ressource | Lier version source, exports et usages UI |
| Auteur/créateur, date et contexte de création | Traçabilité de l'origine spécifiquement Moneta |
| Méthode/outils de création et éléments de référence autorisés | Montrer qu'aucun fichier ni forme BP n'a servi de matériau copié |
| Source de création originale et variantes exportées | Éviter d'intégrer un export sans origine connue |
| Licence/cession/autorisation applicable et éventuelles obligations | Rendre les conditions de livraison explicites, sans présumer les droits |
| Décision utilisateur, version approuvée et usages prévus | Distinguer exploration et ressource distribuable |
| Contrôle d'absence de contenu BP et de pack tiers non autorisé | Appliquer VI-01 à toute nouvelle variante |

L'origine originale ne dispense pas de documenter la licence ou l'autorisation applicable. Une mention « gratuit », « trouvé en ligne », « libre » sans preuve ou un simple renommage ne suffit pas. Aucun registre d'assets rempli artificiellement n'est créé puisqu'aucun asset n'existe dans cette mission.

## VI-11 — Contrôle futur de la chaîne de livraison

L'interdiction de VI-01 s'applique à **tous** les chemins de livraison, pas seulement à l'icône principale : sources, ressources WPF embarquées, paquets, copies de build, installateurs, archives, raccourcis, associations de fichiers, documentation et exemples distribués.

Les contrôles futurs devront :

1. n'autoriser comme ressources graphiques spécifiques que les créations Moneta approuvées et tracées ; maintenir la référence BP hors Git et hors entrées de construction ;
2. vérifier les références et inclusions de fichiers, y compris copies indirectes, dépendances/paquets et ressources embarquées ; une exclusion Git seule ne prouve pas l'absence dans un build ;
3. contrôler la provenance, pas uniquement nom ou extension. Un asset renommé, réencodé ou incorporé en données reste interdit ; une comparaison d'empreintes peut aider pour copies exactes mais ne démontre pas à elle seule l'indépendance ;
4. examiner les sorties de build, installateurs et documents pour déceler logos/images BP ou packs non autorisés ; ne pas utiliser ces contrôles comme prétexte pour extraire/décompiler les binaires de référence ;
5. refuser la livraison de toute ressource sans origine/conditions claires ou contrevenant à VI-01, même si l'application fonctionne.

Ce sont des critères de travail futur. **Aucun script, pipeline, build, installateur ou scan d'assets n'est créé ou exécuté dans cette mission**, et aucune chaîne de build n'est déclarée conforme.

## VI-12 — Limites et sources

- L'inventaire de 29 logos PNG, les fonctions des repères historiques et `globalcolors.ini` sont établis dans [l'audit](BANKPERFECT_AUDIT.md). Leur existence ne confère ni droit ni autorisation de réutilisation ; la décision utilisateur est de ne rien reprendre.
- La hiérarchie de fenêtre et les modes d'affichage proviennent du [manuel officiel, fenêtre principale](https://chelly.net/bankperfect/wiki/La%20fenetre%20principale%20de%20BankPerfect.html). Les options de police/couleurs et styles sont décrites dans [Options](https://chelly.net/bankperfect/wiki/Options.html). Ces textes ne fixent pas l'identité Moneta.
- Les objectifs Windows s'appuient sur les documentations primaires Microsoft : [principes d'accessibilité Windows](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview), [présentation de UI Automation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview), [WPF et haute résolution](https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware). Le premier guide traite notamment Windows App SDK/XAML ; le second est une introduction historiquement .NET Framework ; la page HiDPI signale elle-même son contexte historique Windows 8.1. Ces sources orientent les principes, pas une recette d'implémentation .NET 10 ; elles ne prouvent aucun test ni prise en charge par un code Moneta inexistant.
- Ce document fixe une politique de projet et des principes ergonomiques, **pas un avis juridique** sur BankPerfect, les marques bancaires, les licences ou la similarité d'interfaces. Une question de droits non résolue ne sera pas tranchée par une supposition de l'assistant.

### Résumé de décision

**Ergonomie compatible à vérifier, graphisme indépendant à créer spécifiquement Moneta. Aucun asset BP ou pack tiers repris, aucun logo ni palette définitifs, aucun asset créé ici.** Les captures attendues servent à documenter fonctions et emplacements, non à fournir les matériaux graphiques de l'application. Toute future planche de concepts et toute ressource distribuée exigent leur accord et leur provenance propres.
