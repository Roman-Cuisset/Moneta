# Identité visuelle Moneta — décisions et principes

## Statut et périmètre

**Mission 4 autorise la refonte graphique fonctionnelle du prototype existant**, pas un logo définitif ni l'extension du moteur financier. La structure est guidée par la capture BankPerfect privée fournie par l'utilisateur ; aucun nom, montant, chemin personnel, logo ou dessin de cette image n'est reproduit dans les livrables.

Thèmes Light/Dark, palettes, contrôles et pictogrammes originaux sont maintenant implémentés. Les principes `VI-01` à `VI-12` restent les règles de conception ; leur application partielle ne vaut ni conformité d'accessibilité complète ni réalisation des écrans financiers futurs. Voir [README](../README.md) pour les vérifications et mesures.

### Ajustements opérationnels de mission 5

La sélection de texte était recouverte par l'adorner historique WPF lorsque son opacité valait 1 ; il ignorait le premier plan sélectionné. `TextSelectionRendering` choisit le rendu du texte avant création des contrôles, les styles partagés activent la sélection inactive et couvrent le TextBox éditable des ComboBox. La sélection de ligne reste un mécanisme distinct. Captures avant/après et matrice de contrôles Light/Dark examinées ; gestes souris/clavier et Ctrl+Z natif vérifiés, sans recopier les données de la référence.

Le thème quitte la barre supérieure pour **Affichage → Thème → Clair / Sombre** et est conservé dans le profil local. Pas de suivi Light/Dark Windows non qualifié ; le contraste élevé système reste prioritaire. Un sélecteur de monnaie compact accompagne le solde : montant natif ou état lisible **Cours indisponible**, jamais une valeur convertie supposée.

Fenêtres d'ajout de comptes fictifs et de paramètres dans les mêmes ressources, avec avertissements explicites sur le stockage en mémoire et les options sans service. Aucun framework, logo ou ressource BP ajouté. Les détails et validations figurent dans [CURRENCIES_AND_SETTINGS.md](CURRENCIES_AND_SETTINGS.md) et le README ; les principes historiques ci-dessous sont maintenus.


### Comparaison structurelle et direction retenue

| Observation de disposition, sans données privées | Prototype mission 3 | Refonte mission 4 |
|---|---|---|
| Menus puis outils horizontaux | Ordre provisoire, outils absents | Ordre observé rétabli ; outils originaux à leur emplacement, fonctions futures désactivées |
| Informations de compte à gauche | Seulement sélection/solde en haut | Panneau gauche ajustable et défilant : compte, devise, solde actuel, sélection, emplacements pointé/à venir/prévision explicitement indisponibles |
| Registre principal, modes et cellules à deux niveaux, filtres d'en-tête | Libellé en colonne séparée, aucun mode/sous-catégorie/filtre | Mode, tiers/libellé et catégorie/sous-catégorie sur deux niveaux, débit/crédit alignés, filtres désactivés |
| Saisie compacte sous le registre | Grand détail inférieur réduisant le tableau | Détail déplacé à gauche ; champs readonly compacts et action Modifier désactivée |

Direction : desktop de productivité, pas application mobile ni grandes cartes. Surfaces neutres, accent indigo original, séparateurs décoratifs fins, frontières et focus distincts, nombres tabulaires, lignes de 42 DIPs. Fenêtre minimale 900 × 500 DIPs avec défilements et débordement des outils plutôt que chevauchements. `Segoe UI Variable Text` existe sur le poste et est prioritaire ; repli `Segoe UI`, aucune fonte embarquée. Aucune animation décorative, image bancaire ou logo définitif.

### Choix de présentation et alternatives

| Solution examinée | Compatibilité, maintenance et licence constatées | Intégration et coût pour Moneta |
|---|---|---|
| WPF natif, styles originaux — **retenu** | Contrôles du runtime .NET 10, aucune licence de pack tiers ajoutée | Commandes, DataGrid, clavier et virtualisation conservés ; styles compacts centralisés à maintenir dans le projet. Pas de dépendance graphique supplémentaire |
| [WPF UI](https://github.com/lepoco/wpfui) | [NuGet 4.3.0](https://www.nuget.org/packages/WPF-UI) : cible net10.0-windows7.0, dépendance WPF-UI.Abstractions ; [licence MIT](https://github.com/lepoco/wpfui/blob/main/LICENSE), notice à conserver si distribué | Fluent, thèmes et nombreux contrôles utiles à d'autres interfaces ; davantage de surface que les menus/tableaux nécessaires ici. Aucun package installé, aucune mesure comparative de performance |
| [ModernWpf](https://github.com/Kinnara/ModernWpf) | README consulté : branche 1.x active en préversion avec cible .NET 10 ; branche 0.9.x déclarée gelée/non supportée ; [MIT](https://github.com/Kinnara/ModernWpf/blob/master/LICENSE) et notices amont à conserver en cas d'intégration | Styles et contrôles supplémentaires, mais migration/préversion et couche superflue pour ce périmètre. Non installé, compatibilité déclarée et non testée dans Moneta |

Le [Fluent natif .NET 9](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90) et ses [améliorations .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) ont été examinés. Dans la [source correspondant au runtime 10.0.12](https://github.com/dotnet/wpf/blob/v10.0.12/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Application.cs), `ThemeMode` reste marqué expérimental `WPF0001`. Moneta utilise des dictionnaires propres et ne masque pas cet avertissement. Les autres bibliothèques n'ont pas été benchmarkées : aucun gain mémoire/performance ne leur est attribué.

### Ressources et provenance de mission 4

- `Themes/Light.xaml`, `Dark.xaml`, `HighContrast.xaml` : palettes sémantiques ; aucun code couleur de présentation dispersé dans la fenêtre.
- `Themes/Controls.xaml` : contrôles WPF natifs avec gabarits Moneta, états survol/focus/sélection/désactivation, menus et débordements, sans pack tiers.
- `ThemeController` : palettes Light/Dark et priorité du contraste élevé ; depuis mission 5, choix de thème conservé par `SettingsStore`, sans stockage de données financières. Notifications libérées à la fermeture, légendes natives via DWM lorsque disponible.
- `MainWindow.Resources` : douze `Geometry` originales Accounts/Open/Save/Undo/Redo/Lock/Budget/Totals/Chart/Filter/Schedule/Transfer, dessinées par primitives XAML pour cette mission par l'assistant. Concepts usuels, pas extraction, décalque ou vectorisation d'icônes BP. Traits cohérents, sans bitmap. Affectation demandée par l'utilisateur à Moneta ; aucune licence générale de publication du projet n'est présumée.
- Captures Moneta fictives et capture BP restent uniquement dans `screenshots-private/`, hors Git et hors ressources de compilation. Les captures BP ne sont pas incorporées à ce document.

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

L'ordre principal est désormais confirmé par la capture mission 4. Séparateurs, sous-commandes et accélérateurs Alt restent inconnus ; ils ne sont pas déduits de l'ordre des tables.

## VI-03 — Desktop Windows moderne, sobre et stable

Principes proposés pour les futurs écrans `SCREEN-001` et `SCREEN-002` :

- une fenêtre Windows classique avec barre de menus, commandes compréhensibles et dialogues cohérents ; pas de remplacement par un ruban, un écran d'accueil marketing, une navigation par grandes cartes ou une réécriture de l'agencement sans arbitrage utilisateur ;
- des tableaux denses mais lisibles, tailles de lignes raisonnables et réglables si nécessaire, colonnes alignées, montants et monnaies clairement associés ; éviter les grands espaces décoratifs qui font perdre des lignes utiles ;
- une hiérarchie visuelle stable : l'ouverture d'un outil, un filtre ou un changement de compte ne déplace pas arbitrairement la saisie ou les principaux repères ;
- une présentation explicite des états, commandes dangereuses et erreurs ; un traitement sobre plutôt qu'une décoration forte, des animations imposées ou des effets transparents gênant la lecture ;
- les nouveaux outils BP→Moneta et les sous-fonctions de taux suivent les points d'entrée établis : Fichier > Ouvrir, Outils > Conversions de devises. Ils ne créent pas une nouvelle navigation principale pour des motifs graphiques.

Ces propositions ne sont pas une maquette et ne fixent ni pixels, ni dimensions, ni grille complète. Tout écart fonctionnel connu, notamment simulation sans écriture ou suppression d'une réécriture monétaire silencieuse, doit rester un arbitrage explicite, pas une « modernisation graphique » qui le masquerait.

## VI-04 — Lisibilité des données financières

- Fonte implémentée : `Segoe UI Variable Text` si disponible, puis `Segoe UI` ; tailles et poids restent affinables. Aucune fonte embarquée ou extraite de BP.
- Aligner les montants pour faciliter la comparaison ; expliciter devise et précision d'affichage. Distinguer montant natif, montant converti, total filtré, solde du compte et solde pointé par des libellés, pas seulement une couleur ou une position.
- Maintenir des en-têtes, états de sélection et informations de filtre lisibles. Le détail en seconde ligne et les sous-catégories doivent rester identifiables sans dépendre d'une infobulle.
- Prévoir le texte long, les libellés français, les signes, séparateurs décimaux et dates, sans tronquer silencieusement l'information essentielle. Une infobulle peut compléter, pas remplacer l'unique moyen de lecture.
- Pour `SCREEN-050/051`, distinguer aperçu, confirmation, résultat **complet/partiel/bloqué** et original BP conservé. Pour `SCREEN-046/052/053/055`, afficher source, date d'effet, unité/nominal, origine manuelle ou officielle, statut de cache et absence de cours ; aucune mise en couleur ne doit transformer une donnée absente en taux réputé disponible.

## VI-05 — Palette sémantique indicative, non définitive

La palette de travail est maintenant centralisée dans les trois dictionnaires `Themes/`. Elle n'est pas une identité de marque définitive. Les rôles ci-dessous restent applicables aux écrans futurs ; le contraste élevé substitue les couleurs système.

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

La mission 4 réalise uniquement les douze pictogrammes d'outils originaux consignés ci-dessus. Aucun pack ni logo définitif.

## VI-07 — HiDPI et adaptation, objectifs futurs

Les futures fenêtres, tableaux, dialogues et icônes devront être examinés à **100 %, 125 %, 150 % et 200 %** sous Windows, avec textes réels de longueur synthétique représentative. Sont visés :

- absence de texte coupé, de chevauchement, de bouton hors fenêtre et de zone de saisie inaccessible ;
- tailles de clic et de focus cohérentes ; mise à l'échelle des pictogrammes originaux sans flou ou détail essentiel perdu ;
- visibilité des colonnes, barres de défilement, totaux et erreurs, sans exiger une résolution unique ;
- cohérence de la saisie séparée, calendriers et fenêtres modales ; adaptation entre écrans de facteurs différents à prévoir lors de la conception WPF.

Mission 4 : inspection native Windows à 125 % et scénarios de rendu WPF 100/125/150/200 % par messages `WM_DPICHANGED`, avec confirmation de `VisualTreeHelper.GetDpi`, deux thèmes et tailles normale/minimale. Le moniteur reste à 125 %, sans modification des réglages Windows ; les limites de taille du bureau sont respectées. Ces essais synthétiques ne qualifient pas les déplacements entre moniteurs physiques.

## VI-08 — Clavier, focus, accélérateurs et accessibilité

Les raccourcis de [la parité](FUNCTIONAL_PARITY.md#raccourcis-à-préserver) restent ceux de la cartographie ; particulièrement **Ctrl+F = Champs automatiques**, **Ctrl+D = Conversion(s) de devises**, **Ctrl+Maj+T = Transfert**. Une nouvelle fonction ne les détourne pas vers une recherche ou un panneau générique.

Objectifs futurs :

- toutes les tâches essentielles accessibles sans souris ; focus visible et non masqué ; ordre Tab/Shift+Tab logique, aucun piège clavier ; retour de focus prévisible après dialogue ;
- distinction des gestes tableau/Date/Montant, Entrée d'ajout versus Maj+Entrée de modification, et touches de navigation de date ; pas d'interception globale qui empêche la saisie normale ;
- accélérateurs Alt réellement relevés chez BP avant toute proposition de correspondance ; aucun Alt ni ordre de tabulation inventé dans cette mission ;
- prise en compte du **contraste élevé Windows**, des réglages de fonte système et des états de sélection/désactivation, sans images qui portent seules l'information ;
- noms, rôles, valeurs, états et relations de labels exposés par **Windows UI Automation (UIA)**, notamment pour grille, saisie, pointage, messages d'erreur, provenance des taux et rapport de conversion ;
- validation ultérieure au clavier et avec technologies d'assistance, en distinguant navigation visuelle, arborescence UIA et lecture effective. Une simple capture ne prouve pas l'accessibilité.

Mission 4 : interactions UIA/clavier exercées, champs financiers readonly et focus visibles ; contrastes des paires de texte calculés, résultats dans le README. Aucun essai de lecteur d'écran ni activation réelle d'un thème de contraste élevé Windows ; aucune conformité globale revendiquée.

## VI-09 — Concept graphique uniquement après accord utilisateur

La mission 4 constitue l'autorisation utilisateur de cette direction et de sa réalisation WPF. Elle ne valide pas un logo définitif ni les écrans financiers futurs. Toute nouvelle recherche de marque ou pack tiers demande son périmètre propre.

Les planches pourront comparer, si autorisé : sobriété du traitement, densité typographique, principes d'icônes et rôles de palette. Chaque proposition devra indiquer ce qui est exploratoire, préserver les repères fonctionnels, et montrer les états nécessaires sans reproduire une capture BP sous un nouveau nom.

La palette et les pictogrammes de mission 4 sont une direction applicative à valider visuellement par l'utilisateur, pas un logo définitif. Aucun asset de la référence n'est utilisé comme matériau graphique.

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

La provenance des ressources créées en mission 4 est consignée ci-dessus. Elle n'établit ni licence de publication générale ni droit sur des marques tierces. Toute future ressource ou dépendance doit documenter ses propres conditions.

## VI-11 — Contrôle futur de la chaîne de livraison

L'interdiction de VI-01 s'applique à **tous** les chemins de livraison, pas seulement à l'icône principale : sources, ressources WPF embarquées, paquets, copies de build, installateurs, archives, raccourcis, associations de fichiers, documentation et exemples distribués.

Les contrôles futurs devront :

1. n'autoriser comme ressources graphiques spécifiques que les créations Moneta approuvées et tracées ; maintenir la référence BP hors Git et hors entrées de construction ;
2. vérifier les références et inclusions de fichiers, y compris copies indirectes, dépendances/paquets et ressources embarquées ; une exclusion Git seule ne prouve pas l'absence dans un build ;
3. contrôler la provenance, pas uniquement nom ou extension. Un asset renommé, réencodé ou incorporé en données reste interdit ; une comparaison d'empreintes peut aider pour copies exactes mais ne démontre pas à elle seule l'indépendance ;
4. examiner les sorties de build, installateurs et documents pour déceler logos/images BP ou packs non autorisés ; ne pas utiliser ces contrôles comme prétexte pour extraire/décompiler les binaires de référence ;
5. refuser la livraison de toute ressource sans origine/conditions claires ou contrevenant à VI-01, même si l'application fonctionne.

Le prototype compile ses seules ressources XAML Moneta ; aucun installateur n'est réalisé. Les vérifications de fichiers suivis et exclusions Git n'équivalent pas à une certification d'une future chaîne d'installation.

## VI-12 — Limites et sources

- L'inventaire de 29 logos PNG, les fonctions des repères historiques et `globalcolors.ini` sont établis dans [l'audit](BANKPERFECT_AUDIT.md). Leur existence ne confère ni droit ni autorisation de réutilisation ; la décision utilisateur est de ne rien reprendre.
- La hiérarchie de fenêtre et les modes d'affichage proviennent du [manuel officiel, fenêtre principale](https://chelly.net/bankperfect/wiki/La%20fenetre%20principale%20de%20BankPerfect.html). Les options de police/couleurs et styles sont décrites dans [Options](https://chelly.net/bankperfect/wiki/Options.html). Ces textes ne fixent pas l'identité Moneta.
- Les objectifs Windows s'appuient sur les documentations primaires Microsoft : [principes d'accessibilité Windows](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview), [présentation de UI Automation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview), [WPF et haute résolution](https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware). Le premier guide traite notamment Windows App SDK/XAML ; le second est une introduction historiquement .NET Framework ; la page HiDPI signale elle-même son contexte historique Windows 8.1. Ces sources orientent les principes, pas une recette d'implémentation .NET 10 ; elles ne prouvent aucun test ni prise en charge par un code Moneta inexistant.
- Ce document fixe une politique de projet et des principes ergonomiques, **pas un avis juridique** sur BankPerfect, les marques bancaires, les licences ou la similarité d'interfaces. Une question de droits non résolue ne sera pas tranchée par une supposition de l'assistant.

### Résumé de décision

**Ergonomie structurelle rétablie, graphisme indépendant implémenté dans le périmètre mission 4. Aucun asset BP ou pack tiers repris, aucun logo définitif.** Les captures privées documentent les emplacements, jamais les données ou matériaux graphiques à réutiliser. Les validations restantes et mesures effectives sont dans le README.
