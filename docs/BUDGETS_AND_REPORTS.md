# Budgets, rapports et graphiques

## Consulter le dossier

Ouvrir **Rapports** depuis un dossier enregistré. Les onglets donnent accès à la synthèse, aux flux et cours, à la répartition, aux graphiques, aux soldes à date, aux prévisions, aux budgets et aux rapports figés. Le bouton **Retour au registre** ferme la fenêtre. Moneta reprend les accès BankPerfect **Outils → Budget mensuel** avec **Ctrl+B** et **Totaux par catégories** avec **Ctrl+T**. Les filtres et les graphiques sont propres à Moneta. [Budget BankPerfect](https://chelly.net/bankperfect/wiki/La%20fonction%20Budget.html) · [Historique des rapports et graphiques BankPerfect](https://www.chelly.net/bankperfect/?page=bp_history)

Choisir un mois, un trimestre, une année ou des dates personnalisées inclusives. Sélectionner un ou plusieurs comptes, la devise cible et la banque centrale. L’option d’archivage rend les comptes archivés sélectionnables. Le filtre de catégorie porte sur les flux. Le relevé de soldes couvre chaque compte sélectionné à la date de fin. **Générer** analyse les écritures actives déjà enregistrées. Les écritures futures comptent dans le réalisé à partir de leur date métier. La présentation distingue le net des flux du solde consolidé des comptes.

## Flux, soldes et arrondis

Les flux utilisent la date de l’opération, et chaque solde est d’abord recalculé en devise native, arrêté inclus. Un cours officiel NBRB ou CBR applicable à cette date suffit selon les règles du fournisseur sélectionné. Les conversions utilisent les fractions exactes et l’arrondi au pair de la mission 15. Les recettes et les dépenses sont additionnées séparément après l’arrondi de chaque flux. Les deux jambes d’un virement interne sont exclues du réalisé de recettes et dépenses, même lorsqu’un seul compte est sélectionné. Les frais restent des dépenses dans leur catégorie enregistrée. Les frais sans catégorie sont présentés sous **Sans catégorie**. [Contrat historique](HISTORICAL_FX_ENGINE.md)

- La couverture compte les recettes, dépenses et frais éligibles de la période et du périmètre. Les virements internes et opérations supprimées sont hors dénominateur. **Flux et cours** présente la date, le montant natif, la source et le motif d’absence. Sélectionner une ligne pour voir le trajet, les nominaux et les valeurs. Les montants connus d’un rapport incomplet restent visibles. Le flux net complet exige toutes les conversions.
- Les soldes incluent le solde initial, les dépenses, les recettes, les jambes et les frais. Un compte archivé peut être inclus à la demande. Un total consolidé exige toutes les valorisations au même arrêté et dans la même devise cible. Une date d’arrêté future affiche des conversions indisponibles. Les soldes natifs correspondants reposent sur les écritures déjà enregistrées.
- Les regroupements utilisent les UUID et les identités natives. Les sous-catégories homonymes sous des parents différents restent distinctes. Les lignes sans catégorie sont visibles. La comparaison mensuelle conserve les mois sans flux et le détail des cours employés pour chaque date. Les montants des colonnes financières restent dans la devise cible, même lorsque les lignes sont regroupées par devise d’origine.
- Les graphiques WPF présentent un anneau des dépenses, des colonnes de dépenses par mois et des colonnes recettes/dépenses sur la même échelle. Les catégories au-delà des sept principales deviennent **Autres catégories** quand la liste excède huit entrées. Les graphiques d’un rapport incomplet représentent les seuls montants connus. Un astérisque signale chaque mois incomplet. Les tableaux restent disponibles pour le détail, y compris les catégories petites à l’échelle de l’anneau.

## Budgets mensuels

Dans **Budgets**, choisir le mois puis créer une enveloppe nommée avec sa devise, son montant prévu et ses comptes et catégories. **Modifier**, **Dupliquer** et **Supprimer** agissent sur l’enveloppe. La duplication propose le mois suivant, modifiable dans la boîte de dialogue. La suppression est confirmée. **Ctrl+S** valide les changements dans le dossier.

Une catégorie parente couvre ses écritures directes et toutes ses sous-catégories. Sélectionner une catégorie et un enfant compte chaque écriture une fois dans l’enveloppe. Deux enveloppes indépendantes peuvent se recouvrir. Elles restent présentées séparément, y compris en devises différentes. **Calculer le réalisé** valorise les charges enregistrées jusqu’à aujourd’hui avec leurs cours historiques. Les frais d’un virement ne rejoignent une enveloppe que si leur catégorie enregistrée correspond. Le reste est `prévu − réalisé` et l’utilisation est arrondie au dixième. Lorsqu’il manque des cours, le réalisé connu et le nombre de lignes manquantes restent visibles et le reste complet est indisponible. Pour un prévu nul, le pourcentage est indisponible. Un mois futur affiche son enveloppe et le statut **Mois futur**. La prévision issue de l’échéancier a sa propre vue.

## Prévisions

La vue **Prévisions** lit les programmations enregistrées pour les comptes sélectionnés, de demain à 365 jours au maximum dans la période demandée. Elle affiche les dates d’échéance, les montants natifs et les totaux mensuels par devise. Les occurrences déjà traitées sortent de la prévision. Une écriture préparée mais en attente de Ctrl+S est signalée et exclue des totaux. Les virements prévus restent visibles hors totaux, avec les deux devises et le montant reçu connu ou à confirmer. Leurs frais prévus restent des charges séparées. Les programmations suspendues et celles qui dépendent d’un compte archivé sortent de cette projection.

Cocher **Estimer les montants en devise du rapport** pour utiliser le cours officiellement applicable à la date du calcul, avec sa source et sa date d’effet. Chaque conversion porte la mention **Estimation**. La ligne consolidée prévisionnelle précise s’il manque des conversions. Le mode initial se limite au cache. Autoriser les demandes en ligne dans le périmètre du rapport permet d’interroger la banque sélectionnée sur la date d’ancrage. Cette estimation reste extérieure aux écritures et aux rapports figés.

## Figer, reproduire et recalculer

Dans **Rapports figés**, **Figer les résultats** prépare un rapport et **Ctrl+S** l’enregistre. Une preuve conserve la version de calcul, le périmètre, la date d’observation, les comptes, les catégories, les résultats et les motifs d’absence. Chaque ligne garde le montant natif et l’UUID/révision, ou le périmètre et l’empreinte d’arrêté pour un solde. Les cotations référencées partagent les valeurs, nominaux, dates, sources et empreintes de la mission 15. La convention d’agrégation est liée à la version de la preuve. Les dimensions et le contenu sont contrôlés avant enregistrement.

**Rejouer hors ligne** vérifie l’empreinte SHA-256, recalcule les montants puis produit les mêmes tableaux et graphiques, indépendamment du cache et du registre actuel. Un rapport incomplet peut être figé avec ses absences explicites. **Recalculer depuis le dossier** réapplique le périmètre aux écritures enregistrées actuelles. Avec les requêtes en ligne autorisées, ce recalcul demande de nouveaux lots. L’action **Figer les résultats** peut ensuite enregistrer une nouvelle version liée à la précédente. L’empreinte contrôle l’intégrité et le rejeu, sans signature électronique.

Le schéma 8 ajoute quatre tables. La migration d’un dossier plus ancien produit une copie et une sauvegarde vérifiée de l’original. Les sauvegardes automatiques et manuelles vérifient les enveloppes, leurs liens, la filiation et le rejeu des rapports figés. La restauration récupère le dossier avec ces preuves. [Format](FILE_FORMATS.md) · [Sauvegardes](AUTOMATIC_BACKUPS.md)

## Limites et périmètre de qualification

Un rapport accepte au plus 120 mois calendaires, 50 000 flux et 1 000 comptes. Une preuve est bornée à 32 Mio. Les analyses sont locales et les fournisseurs ne reçoivent que les paramètres publics des requêtes de cours. Les rapports et preuves contiennent des données privées dans le dossier financier. Le budget annuel dédié, l’impression et l’export, le graphique temporel des soldes, la gestion des catégories archivées et le regroupement par tiers restent hors de cette livraison. Le modèle de catégories existant conserve leurs identifiants. Une version figée garde les libellés vus au moment de sa génération.

## Validation Windows et performances

Les builds Debug et Release terminent sans avertissement. Les deux suites complètes comptent **666 tests réussis**. Les six nouveaux tests couvrent le périmètre multicomptes, les virements et frais, les catégories homonymes, la couverture, le cycle budgétaire, les prévisions, la migration et les versions figées. Ils exercent aussi le refus d’une sauvegarde si une preuve a été altérée.

Le parcours WPF Release a utilisé des comptes fictifs EUR/BYN/RUB et des cotations simulées, en clair et en sombre. Il a exercé le routage des menus, le focus initial, les changements de devise, la comparaison de mois, les graphiques, la création d’un budget, la couverture incomplète, la prévision mensuelle, le rejeu hors ligne après correction du cache et le recalcul d’une nouvelle version. Le service de sauvegarde et restauration a été exercé sur des chemins fictifs. La réouverture a retrouvé les versions et le budget, avec le même condensat du registre et des liens de virement. Les essais ne couvrent pas un lecteur d’écran, un second moniteur physique ou la latence réelle des banques centrales.

Sur Windows 11 x64 avec Intel Core i5-1235U et SSD NTFS, médianes de trois répétitions sur trois comptes et vingt dates fictives en cache. La préparation des dossiers est exclue. L’extraction comprend l’arrêté des comptes. Le rendu du graphique est mesuré en mode logiciel. Les allocations sont cumulées sur l’essai.

| Étape | 1 000 flux | 10 000 flux | 50 000 flux |
| --- | ---: | ---: | ---: |
| Lecture SQLite et arrêtés | 20,99 ms | 243,34 ms | 1 286,84 ms |
| Valorisation | 40,13 ms | 73,92 ms | 102,33 ms |
| Calcul d’une enveloppe depuis le lot | 0,22 ms | 1,95 ms | 4,48 ms |
| Agrégation et préparation des graphiques | 2,33 ms | 24,33 ms | 72,57 ms |
| Rendu WPF d’une comparaison sur deux mois | 1,67 ms | 1,82 ms | 2,10 ms |
| CPU du processus | 78 ms | 453 ms | 1 750 ms |
| Allocations cumulées | 2,6 Mio | 20,0 Mio | 100,7 Mio |

Un passage avec les 50 000 lignes dans la fenêtre WPF réelle a pris environ 2,10 s depuis **Générer** jusqu’au retour au repos du dispatcher. Une sonde à 16 ms a continué à recevoir des impulsions pendant le calcul, avec un intervalle maximum observé d’environ 130 ms. Les résultats dépendent de la machine et du périmètre. Un rapport qui demande de nombreux cours absents du cache dépend en plus de la latence réseau.
