# Valorisation historique multidevise

La mission 16 réutilise ce moteur pour les flux, budgets et soldes. Un lot de rapport contient au plus 50 000 flux et peut inclure les arrêtés de 1 000 comptes, avec une résolution commune par date et fournisseur. Les rapports figés conservent les preuves dans le dossier financier. [Budgets et rapports](BUDGETS_AND_REPORTS.md)

## Consulter une valorisation

| Commande | Montant et date retenus |
| --- | --- |
| **Outils → Convertisseur historique…** | Montant signé et date saisis, dans la précision de la devise native |
| **Opérations → Valoriser l’opération enregistrée…** | Écriture enregistrée sélectionnée, montant natif signé et date de l’opération |
| **Outils → Valoriser le solde à date…** | Solde natif du compte sélectionné, calculé à la date d’arrêté inclusive |

Les commandes sur le registre utilisent l’état enregistré. Les modifications en brouillon attendent Ctrl+S. La date de pointage, la date de valeur importée et le jour de consultation gardent leur propre rôle. `HistoricalUse` distingue conversion, flux, solde à date et prévision. Les prévisions et les dates futures retournent un état dédié. Le moteur de conversion est extérieur au stockage des opérations. [Format financier](FILE_FORMATS.md)

## Sources et dates

- **NBRB** fournit des montants en BYN pour `Cur_Scale` unités de `Cur_Abbreviation`. La réponse JSON quotidienne `ondate` doit porter exactement la date demandée dans ses champs `Date`. Un lot absent ou daté autrement ne suffit pas pour établir une couverture, y compris le week-end. Le service n’avance ni ne recule la date de sa propre initiative. [Aide API NBRB](https://www.nbrb.by/apihelp/exrates)
- **CBR** fournit des montants en RUB pour `Nominal` unités de `CharCode`. La réponse XML à `date_req` identifie par `ValCurs@Date` la date d’effet du tableau applicable. Un cours entre en vigueur le jour civil qui suit sa fixation et continue jusqu’au prochain cours. Une réponse à la date demandée peut donc porter une date d’effet antérieure de plusieurs jours, sans limite locale arbitraire. Lorsqu’elle porte une date antérieure, Moneta exige une récupération au plus tôt le jour demandé en UTC pour écarter une consultation faite d’avance depuis l’ancien écran. [Documentation XML](https://www.cbr.ru/development/sxml/) · [Règle d’effet CBR](https://www.cbr.ru/currency_base/)
- Le lot garde séparément la date demandée, la date d’effet et l’horodatage de récupération UTC. La date de publication reste inconnue dans les réponses quotidiennes consommées. Les dates antérieures à l’introduction des références BYN et RUB ne font l’objet d’aucune redénomination automatique.

Le fournisseur initial reprend la préférence Moneta et se choisit dans la fenêtre. Une paire utilise les cotations de ce fournisseur dans le même lot. Si une devise manque, la valorisation échoue avec un motif. Choisir l’autre banque est une action explicite et peut donner un autre résultat. Le convertisseur de Mission 8 reste une indication distincte, avec sa recherche de cours récent. Ses résultats approchés ne constituent pas des preuves de couverture historique.

## Chemin et calcul exact

Une cotation donne `valeur` unités de la devise de référence pour `nominal` unités de la devise cotée. Un trajet direct part d’une devise cotée vers la référence. Le trajet inverse part de la référence. Un trajet croisé passe entre deux devises cotées par la référence du lot. La preuve conserve les deux cotations du trajet croisé. Une conversion dans la même devise n’exige aucune cotation.

Le moteur calcule `montant × valeur(source) × nominal(cible) / (nominal(source) × valeur(cible))` en fractions d’entiers. Le nominal implicite dans la référence vaut 1, comme sa valeur. Les intermédiaires restent exacts jusqu’à l’arrondi à la précision cible, au pair en cas de milieu (`ToEven`). Une valeur hors de la capacité du résultat décimal retourne un débordement explicite. Le zéro réel et le résultat arrondi à zéro restent distincts du cours indisponible.

**Exemple fictif.** Avec `1 EUR = 3 BYN` et `100 RUB = 4 BYN` dans un même lot NBRB, `1 000 RUB → BYN → EUR` donne `13,33 EUR`. Avec `1 EUR = 2 BYN`, `0,01 BYN` donne `0,00 EUR` au pair. Deux opérations de cette valeur totalisent `0,00 EUR` avec l’arrondi par ligne, alors qu’un arrondi unique de leur somme donnerait `0,01 EUR`.

## Flux, virements et arrêtés

- Les flux conservent l’UUID et la révision enregistrée, la devise native, le signe et la date métier. Une opération importée de BankPerfect suit la même règle. La valorisation ne change pas la précision du registre. L’import de BP-F03 reste refusé car `0,0001 EUR` est sous l’unité mineure actuelle.
- Les deux jambes portent l’identité du virement et restent consultables individuellement. Les agrégats de recettes et dépenses ignorent ces jambes et comptent les frais comme une charge. Le moteur conserve le virement et son taux effectif historique tels qu’enregistrés.
- `HistoricalFlowTotals` agrège les lignes déjà arrondies en devise cible, distingue recettes et dépenses et dénombre les lignes non valorisées. Le contrat rejette les UUID répétés, les devises cibles incompatibles et le mélange de sources à l’intérieur d’un total.
- Un arrêté additionne le solde initial natif et les opérations actives du compte dont la date est au plus celle de l’arrêté, frais et jambes compris. Le solde initial est une base sans date d’effet dans le format actuel. Le calcul lit l’état enregistré actuellement et exclut les suppressions logiques, y compris lorsque leur date d’opération précède l’arrêté. Les comptes archivés restent consultables. Une empreinte d’entrée couvre la devise, le solde initial, le périmètre et les identités, dates, montants et révisions retenus.

## Preuves, cache et accès hors ligne

Le cache réutilise les lots SQLite publics de Mission 8. Pour une valorisation historique, une ancienne réponse simplement proche ne suffit pas. L’application exige une réponse mise en cache pour le couple source et date demandée, avec la date d’effet admissible. Une demande de lot effectue au plus une résolution par date distincte pour le fournisseur choisi et garde l’ordre des entrées. Les lots sont limités à 50 000 valorisations. Sur un échec réseau, l’utilisateur peut réessayer. Les requêtes contiennent la date et les paramètres publics de cotation, sans compte ni montant du registre.

**Utiliser uniquement le cache local** coupe les téléchargements. **Recalculer avec un nouveau cours** force une demande et peut produire un résultat révisé. Une preuve déjà exportée conserve son résultat. Une erreur de cache pendant l’écriture peut laisser une cotation reçue utilisable pour la preuve en cours. Le message distingue ce cas d’un enregistrement dans le cache.

**Enregistrer la preuve…** publie un nouveau fichier `*.fxproof.json`. **Reproduire depuis une preuve…** vérifie et recalcule avec les données enregistrées, sans réseau ni cache. La preuve contient le montant natif, le type d’entrée, la date, les identifiants ou l’empreinte d’arrêté, le chemin, le fournisseur, les nominaux, les valeurs exactes, les références, l’empreinte du payload, la récupération, la convention d’arrondi, la version des règles et le résultat. Le cache garde aussi la réponse brute du fournisseur. Le fichier de preuve vérifie la cohérence du calcul et reste non signé. Conserver ce fichier séparément avec les pièces privées. Il est hors de la sauvegarde `.moneta` et peut contenir un montant financier.

## Qualification des fournisseurs

La documentation et le tableau officiel CBR ont été consultés pendant la mission. La consultation du 11 janvier 2026 montre une date d’effet au 31 décembre 2025, un nominal de 1 pour EUR et BYN, et des nominaux de 10 et 100 pour d’autres devises. Les requêtes HTTPS directes du poste vers les deux API sur les 1er, 11 et 12 janvier 2026 ont échoué sur le DNS ou TLS. La page d’aide NBRB a également été indisponible lors de la lecture. Les tests vérifient toujours les deux captures authentiques du 3 avril 2026 conservées dans le dépôt depuis Mission 8. Les nouveaux essais automatisés utilisent des cotations fictives pour les révisions, périodes sans publication, erreurs réseau et cas limites. [Tableau officiel CBR au 11 janvier 2026](https://www.cbr.ru/eng/currency_base/daily/?UniDbQuery.Posted=True&UniDbQuery.To=11.01.2026)

## Qualification Windows et mesures

La solution compile sans avertissement en Debug et Release. Les deux suites complètes totalisent **660 tests réussis** chacune, dont huit nouveaux. Le corpus d’import EUR vérifie également que le flux historique reprend la date et la révision importées.

Sur Windows 11 x64, les trois commandes de la fenêtre principale et les fenêtres WPF Release ont été pilotées en clair et sombre sur des dossiers fictifs EUR/BYN/RUB. Le parcours couvre la date d’effet d’un week-end, la lecture du cache, l’absence de couverture, l’annulation, la réponse à une action du dispatcher pendant la requête, l’arrêté, les flux, les jambes et les frais. La réouverture et la restauration conservent le digest du registre et les liens de virement. Le rejeu utilise le fichier de preuve local. Les services de sauvegarde, restauration et preuve ont reçu des chemins de test. Le contrôle ne couvre ni le lecteur d’écran ni un second écran.

Sur un Intel Core i5-1235U et un SSD NTFS, médianes de cinq essais isolés. Résolution en cache **2,74 ms**, récupération avec transport simulé et écriture en cache **12,34 ms**, conversion avec cache **2,26 ms** et rejeu calculatoire d’une preuve déjà lue **0,0132 ms**. La récupération réelle dépend du réseau et du fournisseur.

| Médiane de trois essais, 20 dates en cache | 1 000 flux | 10 000 flux | 50 000 flux |
| --- | ---: | ---: | ---: |
| Valorisation du lot | 53,69 ms | 74,77 ms | 103,33 ms |
| Agrégation des montants arrondis | 0,86 ms | 5,21 ms | 14,60 ms |
| Arrêté SQLite et empreinte d’entrée | 11,31 ms | 46,23 ms | 517,29 ms |
| CPU du processus sur les trois étapes | 47 ms | 188 ms | 641 ms |
| Allocations cumulées | 1,1 Mio | 6,1 Mio | 28,1 Mio |

Les mesures utilisent des données synthétiques, la même devise native par lot et un cache peu profond. L’arrêté lit des données enregistrées. Le lot reçoit des écritures déjà extraites. La mémoire de travail relevée atteint 151 Mio avec le processus WPF. Les mesures excluent la préparation des dossiers, le réseau réel et le rendu d’un rapport. Un cache historique de grande taille ou des lots couvrant de nombreuses dates distinctes demandent une mesure séparée.
