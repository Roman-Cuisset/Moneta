# Audit juridique et des licences — Moneta

> Audit documentaire et technique au **23 novembre 2026**, portant sur l’état du dépôt et du candidat Alpha 0.1.0.  
> **Ce document n’est pas un avis juridique professionnel.** Il distingue les faits observés des interprétations et signale les points qui exigent un juriste, notamment le contrat de licence de BankPerfect 8 et l’étendue exacte de l’analyse statique réalisée avec REA.

## 1. Conclusion générale

Aucun code source propriétaire de BankPerfect 8, aucun script, logo ou autre ressource graphique BankPerfect n’a été identifié dans l’arbre public de Moneta. L’architecture, les modèles et l’interface sont ceux d’une application C#/.NET/WPF distincte. La réimplémentation du format `.bp` et des fonctions d’import relève, en principe, de l’interopérabilité et de la reproduction de fonctionnalités, que la CJUE n’assimile pas à elles seules à une contrefaçon du programme.

Une reprise de code existe toutefois : le codec V5 C# reprend les tables et la logique de décodage du projet public BankPerfect 9 sous licence MIT. La notice MIT correspondante est présente dans le dépôt et dans le dossier `ThirdPartyNotices` du candidat. Les documents qui affirmaient qu’« aucun code tiers n’est incorporé » ont été corrigés pendant cet audit.

Le principal manquement documentaire **avant la prochaine distribution** est l’absence du texte de la licence MIT de Moneta dans le dossier applicatif installé par l’installateur Windows. Le guide de première utilisation, distribué séparément dans la release, contient bien ce texte, mais un utilisateur qui télécharge uniquement l’installateur n’obtient pas la notice MIT de Moneta. Il faut l’ajouter au paquet et, de préférence, la rendre accessible depuis **À propos**.

Deux autres sujets demandent une validation humaine : le contrat de licence de la copie BankPerfect 8 utilisée pour l’analyse, et la portée exacte de l’extraction statique de l’installateur au regard de ce contrat. Aucun binaire BankPerfect, fichier `.bp` privé ni rapport d’analyse privé n’est suivi par Git ou inclus dans la distribution.

**Niveau global proposé : B/C, avec aucune violation de code propriétaire constatée dans le périmètre examiné.** Cette appréciation n’est pas une certification de conformité.

## 2. Périmètre, sources et limites

### Sources examinées

- Dépôt Moneta : `LICENSE`, README, fichiers projet, dépendances NuGet, code V5, assets, packaging, scripts de construction, notices et documentation.
- Candidat Alpha 0.1.0 : dossier applicatif, notices incluses et script Inno Setup. L’installateur scellé n’a pas été réexécuté.
- Rapports privés de la Mission 19B : lecture seule, sans exécution de BankPerfect et sans ouverture de fichier financier.
- Sources publiques : licence du dépôt BankPerfect 9 à la révision épinglée, codec V5 et documentation d’inventaire du format.
- Textes et décisions cités en section 3.

### Limites

- Pas de recherche d’antériorité de marque exhaustive INPI/EUIPO/OMPI.
- Pas de comparaison pixel par pixel de toutes les interfaces BankPerfect et Moneta.
- Pas de lecture du contrat de licence utilisateur de BankPerfect 8 : aucun EULA complet n’a été trouvé dans la référence locale ; l’historique mentionne cependant une licence et un code de licence.
- Pas de nouvelle décompilation, désassemblage profond ou exécution de BankPerfect pendant cet audit.
- Pas de revue juridique externe ni de conseil contentieux.
- La page Légifrance de l’article L122-6-1 a été référencée, mais son accès automatisé a renvoyé une protection anti-robot (HTTP 403). L’analyse s’appuie sur la directive 2009/24/CE, les arrêts CJUE cités et la présentation officielle de cet article. Le texte français exact doit être vérifié avant tout contentieux.
- L’inventaire des dépendances repose sur les fichiers projet, `Moneta.App.deps.json` et les métadonnées NuGet ; il ne remplace pas un SBOM exhaustif de tous les fichiers du runtime.

## 3. Références juridiques principales

- [Directive 2009/24/CE](https://eur-lex.europa.eu/legal-content/FR/TXT/?uri=CELEX:32009L0024), notamment articles 1(2), 4, 5(3), 6 et 8. L’article 5(3) permet à la personne habilitée à utiliser une copie d’observer, étudier ou tester le fonctionnement pour déterminer les idées et principes, lorsqu’elle effectue des actes qu’elle est en droit d’effectuer. L’article 6 encadre strictement la décompilation aux fins d’interopérabilité. Les clauses contraires à l’article 6 ou aux exceptions des paragraphes 5(2) et 5(3) sont nulles.
- [Article L122-6-1 du Code de la propriété intellectuelle](https://www.legifrance.gouv.fr/codes/article_lc/LEGIARTI000044365559/2026-06-26) : transposition française des exceptions pour l’utilisation normale, la copie de sauvegarde, l’observation/étude/test et l’interopérabilité, avec nullité de certaines clauses contraires.
- [CJUE, 2 mai 2012, SAS Institute Inc. c. World Programming Ltd, C-406/10](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:62010CJ0406) : la fonctionnalité, le langage de programmation et le format de fichiers ne sont pas, en tant que tels, une forme d’expression du programme. La reproduction du code ou du manuel peut en revanche être protégée. L’observation, l’étude et le test du fonctionnement par un licencié sont admis dans les conditions de l’article 5(3).
- [CJUE, 22 décembre 2010, Bezpečnostní softwarová asociace, C-393/09](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:62009CJ0393) : l’interface graphique n’est pas protégée comme forme d’expression du programme, mais peut l’être comme œuvre autonome si elle est la création intellectuelle propre de son auteur ; les composants dictés par leur fonction technique sont exclus de l’originalité.
- [Licence MIT de BankPerfect 9 à la révision épinglée](https://raw.githubusercontent.com/fabiochelly/bankperfect/990f5c7f712dab07c7fefc2921cbf60beee7f6b1/LICENSE), copyright 2026 Fabio Chelly.
- [Licence MIT](https://opensource.org/license/mit), [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0), [licence SQLite](https://sqlite.org/copyright.html), [licence Inno Setup](https://jrsoftware.org/files/is/license.txt).

## 4. Indépendance de Moneta vis-à-vis de BankPerfect

### Faits observés

- Moneta est une application C#/.NET 10/WPF avec SQLite, un modèle de domaine propre, une architecture MVVM légère et son propre schéma de document `.moneta`.
- Le code d’import V5 est un lecteur C# distinct ; il ne contient pas de code source Delphi de BankPerfect 8.
- Le logo, les icônes XAML, les palettes et les fenêtres sont documentés comme des créations Moneta. L’arbre public ne contient aucun logo, icône ou capture BankPerfect.
- Les menus et les grands parcours reprennent une organisation de logiciel de comptes classique (Fichier, Comptes, Opérations, Outils, etc.). Cette familiarité fonctionnelle ne suffit pas, à elle seule, à caractériser une contrefaçon.
- Le format `.bp` et les fonctionnalités correspondantes ne sont pas protégés comme forme d’expression du programme selon C-406/10. Une interface graphique peut toutefois être protégée comme œuvre autonome si elle est originale (C-393/09).

### Interprétation

L’indépendance de l’implémentation est crédible et aucun élément propriétaire BankPerfect 8 n’a été identifié dans le code public. Moneta n’est cependant pas un « clean-room » formel : le développement a eu accès au codec public MIT de BankPerfect 9, à sa documentation d’inventaire et à une analyse statique du binaire historique. Le clean-room n’est pas une condition légale de non-contrefaçon ; il constitue une preuve d’indépendance. En l’absence de reprise d’expression propriétaire, cette situation n’est pas en soi une atteinte.

## 5. Reprises de code et de documentation

### Reprise MIT identifiée

`src/Moneta.BankPerfect/V5Codec.cs` reprend l’alphabet, les deltas et la logique de décodage de `crates/bp-core/src/codec_v5.rs` du dépôt BankPerfect 9, sous MIT. Le fichier cite la révision et renvoie à `THIRD-PARTY-NOTICE.md`, qui contient le texte MIT complet et le copyright de Fabio Chelly.

`src/Moneta.BankPerfect/V5Parser.cs` utilise aussi le séparateur `TNSLPPBNTSOGPTQBC280673` et le préfixe `bp_script_data_`, présents dans la source publique `v5_text.rs`. Ces éléments sont des constantes de format courtes et fonctionnelles ; la notice actuelle mentionne les tables du codec mais pas explicitement ces constantes. Une notice plus précise est recommandée, même si la licence MIT est déjà incluse.

### Absence de code propriétaire BankPerfect 8

Les rapports privés 19B décrivent une identification statique de l’installateur NSIS, des ressources, des imports PE et du membre applicatif, sans désassemblage profond, sans pseudocode et sans exécution de BankPerfect. Aucun code source Delphi, script Python ou extrait de code propriétaire n’a été identifié dans le dépôt public. Le lecteur C# est une implémentation distincte, avec ses propres diagnostics, limites, modèle et validation.

### Documentation

Les documents Moneta citent le manuel public, les inventaires publiés et les sources Rust, sans reproduire le manuel ni de longs extraits de code. Les anciennes formulations « aucun code tiers incorporé » et « aucune réutilisation de code » étaient inexactes et ont été corrigées dans :

- `docs/BANKPERFECT_FORMAT_RESEARCH.md` ;
- `docs/BANKPERFECT_IMPORT_STRATEGY.md` ;
- `docs/ROADMAP.md`.

**Niveau : A pour l’absence de code propriétaire dans le périmètre examiné ; B pour la notice du codec à préciser.**

## 6. Licences de BankPerfect

### BankPerfect 9 — dépôt public

La licence du dépôt public à la révision `990f5c7f712dab07c7fefc2921cbf60beee7f6b1` est MIT, copyright 2026 Fabio Chelly. Elle autorise la reproduction, la modification et la distribution, sous réserve de conserver la notice de copyright et le texte de permission. La reprise du codec est donc compatible avec la distribution MIT de Moneta, et la notice est présente dans le dépôt et dans le dossier `ThirdPartyNotices` du candidat.

Cette licence ne couvre pas automatiquement :

- les anciens binaires Delphi de BankPerfect 8 ;
- les scripts, plugins, images, logos ou captures de l’installation historique ;
- des composants tiers intégrés à BankPerfect 8 ;
- le manuel ou d’autres documents non couverts par la licence du dépôt public.

### BankPerfect 8 — installation historique

L’historique local de la référence mentionne une **licence BankPerfect** et un **code de licence**, ainsi qu’une mention « NON ENREGISTRE ». Aucun contrat de licence complet n’a été trouvé dans les fichiers texte examinés. La simple disponibilité publique de l’ancien installateur ne prouve ni une licence libre ni un droit de redistribution. La copie locale doit donc être considérée comme un logiciel propriétaire, sauf preuve contraire du contrat applicable.

Le binaire BankPerfect 8 et l’installateur analysé restent dans le dossier privé, exclu de Git. Ils ne doivent jamais être publiés, joints à une release, copiés dans `docs/` ou inclus dans l’installateur Moneta.

**Niveau : B — vérifier le contrat de licence et conserver la preuve du droit d’utilisation de la copie analysée. Si la copie n’est pas licenciée, le niveau devient D pour l’analyse et toute distribution.**

## 7. Importateur V5 et interopérabilité

### Analyse

L’importateur lit des fichiers `.bp` fournis par l’utilisateur, dans un format de données historique. Selon C-406/10, le format de fichier et la fonctionnalité ne sont pas protégés en tant que forme d’expression du programme. Une réimplémentation indépendante pour lire et convertir un fichier est donc, en principe, admissible.

Moneta ne contourne pas de mot de passe, ne décompile pas le programme BankPerfect et ne distribue pas son code. Le codec V5 réutilise une petite partie du codec public MIT, avec notice. Les données `.bp` restent locales ; aucun fichier financier n’est envoyé à un service externe.

### Conditions et incertitudes

- L’utilisateur doit détenir une copie licite des fichiers qu’il importe et respecter le contrat applicable à BankPerfect 8.
- L’exception d’observation de l’article 5(3) n’est pas une autorisation générale de reproduction ou de distribution du programme.
- L’article 6 (décompilation aux fins d’interopérabilité) n’a pas été utilisé et ne serait pas nécessairement disponible si les informations étaient déjà publiques.
- Une clause contractuelle restrictive de BankPerfect 8 doit être examinée : l’article 8 de la directive et l’article L122-6-1 IV du CPI limitent la validité de certaines clauses contraires, mais le périmètre exact de l’analyse statique doit être qualifié par un juriste.
- Le nom « BankPerfect » est utilisé de manière descriptive dans l’interface d’import ; il ne doit pas être présenté comme une marque de Moneta ni suggérer une affiliation.

**Niveau : A/B — l’implémentation indépendante est cohérente avec C-406/10 ; la licence de la copie BankPerfect 8 et le contrat applicable restent à confirmer.**

## 8. REA et rétro-ingénierie

### Ce qui a été fait

Les rapports privés 19B indiquent :

- identification statique de l’installateur NSIS BankPerfect 8 ;
- lecture des ressources et imports PE ;
- décompression du flux NSIS et comparaison du membre applicatif avec la référence locale ;
- aucune exécution de BankPerfect ;
- aucun désassemblage profond, graphe d’appels ou pseudocode ;
- aucune ouverture de fichier financier personnel ;
- conservation des résultats dans le dossier privé ignoré par Git.

### Analyse juridique

L’article 5(3) de la directive permet à la personne habilitée à utiliser une copie d’observer, étudier ou tester le fonctionnement du programme pour déterminer les idées et principes sous-jacents, dans le cadre des actes qu’elle est en droit d’effectuer. Cette exception n’autorise pas nécessairement l’extraction complète d’un exécutable intégré à un installateur, qui peut constituer une reproduction au sens de l’article 4. L’article 6 encadre séparément la décompilation pour l’interopérabilité et subordonne celle-ci à des conditions strictes, dont l’absence d’informations déjà facilement accessibles.

En l’espèce :

- aucune décompilation de code objet n’est revendiquée ;
- aucune information propriétaire n’a été publiée ;
- les informations de format proviennent aussi de sources publiques MIT ;
- le codec public MIT réduit fortement la nécessité d’une décompilation ;
- le binaire et les rapports restent privés.

Le risque pratique est donc limité, mais la qualification exacte de l’extraction statique au regard du contrat BankPerfect 8 n’est pas certaine. Il faut conserver la preuve du droit d’utilisation de la copie et ne pas publier les résultats bruts.

**Niveau : B — revue juridique recommandée ; D si la copie n’est pas licite ou si des éléments propriétaires sont publiés.**

## 9. Logos, icônes et interface

### Constats

- Le logo M, le SVG, les PNG, l’ICO et les douze géométries XAML sont documentés comme créés pour Moneta.
- Aucun logo, icône, police ou capture BankPerfect n’est suivi dans l’arbre public.
- Les captures publiques du README montrent uniquement Moneta avec des données fictives.
- Aucune police n’est embarquée : l’interface utilise les polices système.
- Les logos de banques présents dans la référence BankPerfect ne sont pas réutilisés dans les assets Moneta.
- `MainWindow.xaml` porte un commentaire explicite d’absence de ressource ou de forme extraite de BankPerfect, et `VISUAL_IDENTITY.md` documente la provenance.

### Appréciation

Les ressources graphiques propres ne présentent pas de problème identifié. L’interface reprend des conventions de logiciels financiers (menus, registre, barre d’outils, comptes à gauche). C-393/09 indique qu’une interface graphique n’est pas protégée comme programme mais peut l’être comme œuvre autonome si elle est originale ; les composants dictés par leur fonction technique ne sont pas originaux. Une comparaison visuelle complète n’a pas été réalisée, mais aucun élément graphique copié n’a été identifié.

**Niveau : A pour les assets identifiés ; B pour une éventuelle appréciation globale du « look and feel » et pour la marque.**

## 10. Marques et présentation publique

Une recherche publique rapide n’a pas permis d’identifier une inscription INPI certaine pour « BankPerfect ». Cette absence de résultat n’est pas une preuve d’absence de marque : une recherche exhaustive INPI, EUIPO et OMPI reste à faire.

Le README et le README français contiennent la mention appropriée :

> Moneta est un projet indépendant inspiré de BankPerfect. Il ne s’agit pas d’une version officielle et le projet n’est ni affilié ni approuvé par BankPerfect ou son auteur.

Cette formulation est suffisante pour la page GitHub. Elle devrait aussi apparaître dans **À propos** et dans les notes de la prochaine release. L’usage de « BankPerfect » dans l’importateur est descriptif et ne doit pas être accompagné d’un logo ou d’une présentation suggérant une affiliation.

La disponibilité du nom « Moneta » n’a pas été vérifiée de manière exhaustive. Une recherche de marque avant une communication commerciale importante est recommandée.

**Niveau : B — recherche de marque et ajout de la mention d’indépendance dans À propos/release.**

## 11. Inventaire des dépendances

Versions constatées dans `Moneta.App.deps.json`, les fichiers projet et les métadonnées NuGet.

| Composant | Version | Licence | Notice / constat |
|---|---:|---|---|
| .NET Runtime / WindowsDesktop / WPF | 10.0.12 | MIT | `DotNet.LICENSE`, `DotNet.NOTICES`, `WindowsDesktop.LICENSE` inclus |
| Microsoft.Data.Sqlite | 10.0.12 | MIT | Licence MIT du package ; la notice `EFCore.LICENSE` est mal nommée et ne cite pas Microsoft |
| Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | Même remarque |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 | Apache-2.0 | Licence Apache et NOTICE inclus |
| SQLitePCLRaw.core / provider / config | 3.0.5 | Apache-2.0 | Couverts par la notice du bundle |
| SQLite | 3.53.4 | Domaine public / bénédiction SQLite | `SQLite.LICENSE` inclus |
| Codec BankPerfect V5 | révision MIT épinglée | MIT | `BankPerfect-V5.NOTICE.md` inclus ; description à préciser |
| Inno Setup | 6.7.4 | Licence Inno Setup | Compilateur utilisé ; aucune notice Inno dans `ThirdPartyNotices` |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | Tests uniquement, non distribués |
| MSTest.TestAdapter / TestFramework | 3.6.4 | MIT | Tests uniquement, non distribués |

**EF Core n’est pas une dépendance du produit** : aucun assembly EF Core n’a été identifié dans le dossier publié. `EFCore.LICENSE` est donc une notice supplémentaire mal nommée, pas une preuve d’utilisation d’EF Core.

### Compatibilité avec la distribution MIT

- MIT, Apache-2.0 et le domaine public SQLite sont compatibles avec une distribution MIT, sous réserve des notices.
- Apache-2.0 impose de fournir la licence, le NOTICE et d’indiquer les modifications éventuelles. Le NOTICE SQLitePCLRaw est présent.
- Inno Setup autorise l’usage et la redistribution binaire sous conditions, notamment de conserver les mentions de copyright et les adresses du projet dans les redistributions binaires. Le compilateur n’est pas intégré comme bibliothèque de Moneta, mais l’installateur généré contient son amorce. Une notice explicite est recommandée.
- Les packages de test ne sont pas distribués avec Moneta ; leurs licences restent dans le dépôt et les outils de développement.

**Niveau : A/B — licences permissives compatibles ; notices Microsoft.Data.Sqlite et Inno Setup à corriger/préciser.**

## 12. Licence MIT de Moneta

### Ce qui est correct

- `LICENSE` contient le texte MIT complet et le copyright 2026 Roman Cuisset.
- README et README français déclarent la licence MIT et renvoient vers `LICENSE`.
- Le dépôt GitHub est public et reconnaît la licence MIT.
- Les fichiers source n’ont pas besoin d’un en-tête individuel : la licence du dépôt couvre l’œuvre, et la notice doit accompagner les copies.
- La licence MIT autorise la redistribution et la commercialisation ; les dépendances permissives sont compatibles.

### Lacune identifiée

Le dossier applicatif publié et l’installateur ne contiennent pas `LICENSE`. Le script de construction copie le dossier `app` et ajoute `ThirdPartyNotices`, mais ne copie pas la licence Moneta. Le guide de première utilisation, distribué séparément dans la release, reprend le texte complet de la licence ; ce n’est pas équivalent pour un utilisateur qui télécharge uniquement l’installateur.

L’article MIT exige que la notice de copyright et le texte de permission accompagnent les copies ou parties substantielles du logiciel. **Correction recommandée avant la prochaine publication :** copier `LICENSE` dans le dossier applicatif, l’ajouter à `ThirdPartyNotices` ou à la racine installée, et afficher un lien depuis **À propos**.

**Niveau : C — correction recommandée avant 0.2.0 ; pas de suppression de la licence ni de changement du texte MIT.**

## 13. Notices et attribution

### Présentes dans le candidat

`ThirdPartyNotices` contient :

- `DotNet.LICENSE` et `DotNet.NOTICES` ;
- `WindowsDesktop.LICENSE` ;
- `SQLitePCLRaw.LICENSE` et `SQLitePCLRaw.NOTICE` ;
- `SQLite.LICENSE` ;
- `BankPerfect-V5.NOTICE.md` ;
- `EFCore.LICENSE`.

### Points à corriger

1. **Licence Moneta absente** de l’installateur (niveau C).
2. **Microsoft.Data.Sqlite** : ajouter une notice MIT avec le copyright Microsoft, ou renommer/corriger la notice existante ; `EFCore.LICENSE` ne correspond pas au composant effectivement livré (niveau B).
3. **Inno Setup** : vérifier si l’installateur généré doit inclure la notice de copyright/URL d’Inno Setup ; ajouter une notice dans `ThirdPartyNotices` par prudence (niveau B).
4. **Codec BankPerfect** : préciser que la notice couvre les tables et la logique de décodage, pas seulement les constantes (niveau B).
5. **À propos** : aucun lien vers la licence ou les notices, ni mention d’indépendance. Ce n’est pas nécessairement une obligation légale si les fichiers sont installés, mais c’est une amélioration de transparence (niveau B).

## 14. Risques et classifications

| Niveau | Élément | Preuve / source | Conséquence possible | Correction |
|---|---|---|---|---|
| **A** | Absence de code source propriétaire BankPerfect 8 | Dépôt public, rapports 19B, code C# distinct | Aucune contrefaçon de code constatée dans ce périmètre | Maintenir la séparation et la traçabilité |
| **A** | Codec V5 MIT et notice | `V5Codec.cs`, `THIRD-PARTY-NOTICE.md`, dossier `ThirdPartyNotices` | Réutilisation autorisée si notice conservée | Conserver la notice ; préciser son périmètre |
| **B** | Périmètre de la notice du codec V5 | La notice cite les tables ; `V5Parser` reprend aussi séparateur et préfixe | Attribution perfectible, sans omission du texte MIT | Étendre la notice aux constantes et à la logique dérivée |
| **A** | Logo, icônes, polices, captures | `assets/`, `VISUAL_IDENTITY.md`, arbre Git | Aucun asset BankPerfect identifié | Aucune modification du logo nécessaire |
| **A** | Binaires et données privés | `.gitignore`, `git check-ignore`, absence de fichiers suivis | Pas de distribution de BP8/.bp privé constatée | Maintenir `private/` hors Git |
| **B** | Contrat BankPerfect 8 et droit d’utilisation de la copie | Historique local, absence d’EULA complet | Analyse/usage contractuellement incertain | Faire vérifier le contrat et conserver la preuve de licence |
| **B** | Extraction statique REA | Rapports 19B : PE/NSIS, pas de décompilation | Qualification incertaine au regard de l’article 5(3) | Ne pas publier les artefacts ; avis juridique si distribution dérivée |
| **B** | Interface et « look and feel » | Menus et parcours proches, assets propres | Protection possible de l’interface originale (C-393/09) | Revue visuelle et de marque avant 0.2.0 |
| **B** | Marque BankPerfect / Moneta | Recherche publique non concluante | Risque de confusion si usage suggestif | Recherche INPI/EUIPO ; conserver la mention d’indépendance |
| **B** | Microsoft.Data.Sqlite notice | `EFCore.LICENSE` mal nommée, package MIT Microsoft | Attribution imprécise | Ajouter la notice MIT Microsoft exacte |
| **B** | Inno Setup notice | Licence Inno, absence de notice dans `ThirdPartyNotices` | Condition de redistribution binaire à vérifier | Ajouter la notice/URL ou confirmer l’exemption |
| **B** | **À propos** sans licence, notices ni mention d’indépendance | `AboutWindow.xaml` | Transparence insuffisante ; pas d’obligation autonome si les fichiers sont installés | Ajouter les liens et la mention |
| **C** | Licence MIT Moneta absente de l’installateur | Dossier `app`, script de build, `Moneta.iss` | Copie binaire sans notice MIT complète | Ajouter `LICENSE` au paquet et à **À propos** |
| **C** | Documents affirmant l’absence de code tiers | Anciens textes | Attribution inexacte | **Corrigé pendant cet audit** |
| **D** | Aucun risque significatif établi | — | — | Aucun |

Les niveaux A à D sont des outils de priorisation, pas une qualification juridique définitive. Un niveau A signifie « aucun problème identifié dans le périmètre examiné », pas « conformité certifiée ».

## 15. Corrections documentaires effectuées

Sans modifier le code, le moteur financier, les installateurs, les binaires, les logos ou la licence MIT :

- `docs/BANKPERFECT_FORMAT_RESEARCH.md` : la phrase « aucun code tiers n’est incorporé » est remplacée par la description exacte du codec V5 MIT et de l’absence de code propriétaire BankPerfect 8.
- `docs/BANKPERFECT_IMPORT_STRATEGY.md` : « aucune réutilisation de code » est remplacé par la mention de la seule reprise identifiée, le codec MIT attribué.
- `docs/ROADMAP.md` : « aucun code tiers intégré » est remplacé par « codec V5 MIT attribué, aucun code propriétaire BankPerfect 8 intégré ».
- Création du présent rapport.

Aucune release, aucun installateur, aucun code source, aucune ressource graphique et aucun historique Git n’a été modifié.

## 16. Points nécessitant une validation humaine ou juridique

1. **Contrat de licence BankPerfect 8** : le droit d’utiliser la copie analysée, les restrictions de rétro-ingénierie et les droits sur les fichiers `.bp`.
2. **Portée de l’extraction statique REA** : qualification au regard de l’article 5(3) et du contrat, même en l’absence de décompilation.
3. **Notice Inno Setup** : conditions exactes applicables à l’installateur généré.
4. **Notice Microsoft.Data.Sqlite** : texte et copyright exacts à inclure.
5. **Interface et marque** : recherche INPI/EUIPO et comparaison visuelle si Moneta commercialise sa compatibilité.
6. **Licence Moneta dans l’installateur** : ajout et vérification du paquet 0.2.0.
7. **Juridiction et distribution** : le présent audit vise principalement le droit français/européen ; une distribution hors UE peut soulever d’autres règles.

## 17. Recommandations avant Moneta 0.2.0

1. **Ajouter `LICENSE`** au dossier applicatif et à `ThirdPartyNotices`, avec un lien dans **À propos**.
2. **Corriger les notices** : Microsoft.Data.Sqlite avec le copyright Microsoft ; Inno Setup ; BankPerfect V5 avec la logique du codec et les constantes de format.
3. **Supprimer ou renommer** `EFCore.LICENSE` puisqu’aucun EF Core n’est distribué.
4. **Ajouter la mention d’indépendance** dans **À propos** et dans les notes de release : « Moneta est un logiciel indépendant, non affilié à BankPerfect ni à son auteur. »
5. **Conserver les binaires et rapports BankPerfect 8** dans le dossier privé ignoré ; ne jamais les joindre à une release, un ticket ou un document public.
6. **Ne pas publier** de fichier `.bp`, `.moneta`, sauvegarde, capture privée ou rapport 19B.
7. **Documenter la méthode REA** et limiter les analyses futures à ce qui est nécessaire à l’interopérabilité ; ne pas décompiler sans validation.
8. **Vérifier le contrat BankPerfect 8** et, si nécessaire, consulter un juriste avant toute nouvelle analyse ou distribution dérivée.
9. **Rechercher les marques** BankPerfect et Moneta dans les bases INPI/EUIPO/OMPI avant une communication commerciale.
10. **Ajouter un contrôle de licences/SBOM** au pipeline de release : vérifier la présence de `LICENSE`, `ThirdPartyNotices`, des versions NuGet et des notices Apache/MIT.
11. **Ne pas réutiliser** les logos, icônes, captures, scripts ou manuels BankPerfect ; les ressources Moneta restent originales.
12. **Ne pas modifier** la licence MIT de Moneta ni les licences tierces sans revue.

## 18. Avertissement final

Cet audit est une analyse technique et documentaire proportionnée, fondée sur les éléments accessibles au 23 novembre 2026. Il ne constitue ni une opinion juridique, ni une garantie d’absence de risque, ni une validation de la licence BankPerfect 8. Les questions contractuelles, de marque et d’interopérabilité doivent être confirmées par un conseil qualifié si Moneta poursuit une distribution commerciale ou si le titulaire de BankPerfect conteste l’analyse.
