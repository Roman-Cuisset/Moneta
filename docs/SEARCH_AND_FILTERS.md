# Recherche dans le registre

## Depuis Moneta

- Saisir du texte au-dessus du registre et appuyer sur Entrée ou **Rechercher**. **Édition → Rechercher** place le focus dans ce champ. La recherche rapide part du compte sélectionné.
- Utiliser **Filtres** dans la barre d’outils, **Affichage → Filtres avancés** ou **F7** pour ouvrir les filtres. **Comptes → Registre de tous les comptes** ouvre la même fenêtre en mode multicompte. Dans ce mode, **Inclure les archives** est facultatif. Un compte archivé explicitement sélectionné peut aussi être interrogé seul.
- Lancer la recherche après modification des critères. Un tri s’applique immédiatement aux derniers critères validés. La fenêtre identifie le périmètre, le nombre de résultats, la page et la présence de filtres. « Aucune opération enregistrée » décrit un registre vide dans le périmètre. « Aucun résultat pour les filtres appliqués » invite à élargir la recherche.
- Utiliser **Voir dans le registre**, Entrée sur une ligne ou un double-clic. Moneta rejoint le compte et la page de l’UUID de l’opération. **Retour aux résultats** retrouve les critères, le tri, la page et, si disponible, la sélection. Échap retourne au registre depuis la fenêtre de recherche. **Effacer les filtres** ou **Maj+F7** remet les critères à zéro, conserve le périmètre et revient à la première page. Les critères appartiennent au dossier ouvert et restent en mémoire pour la session.

Le manuel BankPerfect confirme les filtres près des colonnes, l’icône Filtres, les comparaisons signées et les raccourcis F7 et Maj+F7. Moneta place les filtres détaillés dans une fenêtre de consultation. Ctrl+F reste disponible pour le calcul automatique des champs documenté chez BankPerfect. Les filtres nommés et la syntaxe historique de recherche avancée restent à implémenter. [Filtrage BankPerfect](https://chelly.net/bankperfect/wiki/Le%20filtrage.html) · [Raccourcis BankPerfect](https://chelly.net/bankperfect/wiki/Recapitulatif%20des%20raccourcis%20clavier.html)

## Règles de correspondance

Le texte libre couvre le libellé, les notes, le tiers, la catégorie, la sous-catégorie et le mode. Il couvre aussi la référence et les notes du virement actif pour chaque jambe, frais compris. La référence est un champ du virement. La recherche porte sur les données actives du registre et des référentiels.

Le texte libre est découpé sur les espaces Unicode. Chaque mot doit apparaître comme sous-chaîne dans au moins un champ. Les filtres Tiers, Catégorie, Sous-catégorie et Mode cherchent une sous-chaîne littérale dans leur champ. L’application retire les espaces de bord et ignore les champs vides. La normalisation Unicode NFC fait correspondre les accents composés et décomposés. La comparaison ordinale .NET ignore la casse qui y est définie et reste sensible aux accents. Les signes de ponctuation, apostrophes, accents, `%`, `_`, `|` et `/` sont littéraux.

Les familles de critères se combinent avec **ET**. Pour les états, NP + P et P + R combinent leurs valeurs avec **OU**. Les cinq choix de type comprennent tous les types, recettes seules, dépenses seules, virements et frais, et frais de virement seuls. Les jambes liées portent leur type et leur UUID de virement.

### Dates et devises

Les deux bornes sont inclusives et portent sur la date de l’opération. Répéter la même date dans les deux champs filtre un jour précis. Une borne seule suffit pour un intervalle ouvert. Les raccourcis de période calculent le mois en cours, le mois précédent ou l’année en cours à partir de la date locale. Un mois ou une année quelconque se saisit avec ses dates de début et de fin. Les dates futures restent admissibles.

Le montant signé est positif pour un crédit et négatif pour un débit. Zéro est admissible pour les deux sens. L’exact, le minimum et le maximum sont simultanés, avec bornes inclusives. Les saisies acceptent un signe et une virgule décimale française, jusqu’à 28 chiffres significatifs et 28 décimales, sans séparateur de milliers. Pour « dépense supérieure à 50 », le montant signé doit être inférieur à −50. Avec une devise à deux décimales, saisir un maximum de −50,01.

**Chaque devise** compare le nombre dans chaque devise native. Une recherche de 100 peut donc afficher séparément 100 EUR et 100 BYN. Sélectionner EUR limite le critère aux opérations EUR. Le tri montant groupe les codes ISO par ordre ascendant, puis trie les montants signés dans chaque groupe. Aucun cours ni total multidevise ne participe à la recherche.

## Pagination, navigation et intégrité

Le comptage et la page lisent la même transaction SQLite sur une connexion indépendante en lecture seule. Les critères s’appliquent au registre entier avant la limite de **100 résultats**. Les clés de tri sont la date, le montant, le libellé, le compte, la catégorie, le tiers et le pointage. L’UUID de l’opération départage chaque égalité dans les deux sens. Les montants sont reconstruits sans arrondi à partir des unités mineures entières, avec le nombre de décimales de la devise native.

La recherche voit les opérations enregistrées et exclut les suppressions logiques. Après Ctrl+S, les résultats sont rafraîchis. Un résultat ouvre la page originale, où les brouillons et protections usuels s’appliquent. Si une suppression en brouillon rend l’opération indisponible, le saut est refusé. Recherche, tri et navigation ne marquent pas le dossier comme modifié et n’écrivent aucune donnée financière. La vue de résultats ne propose aucune commande d’édition.

La saisie déclenche une recherche sur demande. Une nouvelle demande annule la précédente, SQLite reçoit un signal via sa fonction de progression et l’interface rejette toute réponse devenue obsolète. Une erreur de saisie ou de lecture est affichée dans la fenêtre. Le schéma 6, les index existants et le contenu des sauvegardes restent applicables. Pas de cache financier ni d’index de texte persistant. Le comptage et le classement textuel peuvent parcourir toutes les lignes candidates, selon le filtre.

Sur deux requêtes simplifiées dans le dossier de 50 000 écritures, le diagnostic `EXPLAIN QUERY PLAN` de SQLite 3.49.1 utilise `Transactions_AccountOrder` pour restreindre un compte par dates. En multicompte, le filtre de sous-chaîne parcourt `Transactions_Candidates` et le tri par libellé utilise une structure temporaire. Les mesures Release ci-dessous portent sur le moteur SQLite embarqué par Moneta et sur les requêtes complètes.

## Validation sur Windows

Les compilations Debug et Release se terminent sans erreur ni avertissement. Chaque configuration réussit 629 tests, dont 5 nouveaux pour la recherche.

Les essais utilisent des opérations fictives EUR, BYN et RUB sur Windows 11, i5-1235U, 32 Gio, SSD local, affichage natif à 125 %. Le parcours WPF Release utilise les commandes réelles du registre et de recherche, les contrôles dans les deux thèmes et une fenêtre de 530 × 460 unités WPF. Le montage du dossier utilise le modèle financier. Les essais couvrent les filtres combinés, NP/P/R, les archives, la devise native, le tri, le saut hors première page, le retour, l’effacement, la réouverture et le SHA-256 du dossier fermé. La vérification d’accessibilité porte sur les noms des contrôles et le focus, à compléter avec un lecteur d’écran.

### Mesures Release

Médianes de cinq recherches par scénario après une exécution de chauffe. « Ciblée » combine un texte Unicode, un montant exact et EUR pour retrouver une écriture proche de la fin du registre. « Large » cherche un mot présent dans presque toutes les écritures et charge la neuvième page triée par libellé. Le temps total inclut l’ouverture de la connexion. Le temps CPU cumule les threads du processus. Les allocations représentent les octets alloués sur le thread de la requête. La mémoire résidente du processus inclut WPF et la préparation préalable des fichiers. Les durées et la consommation dépendent des textes, de la sélectivité et de la machine.

| Écritures | Scénario | Total ms | Comptage ms | Page ms | CPU ms | Allocations Mio | Résident Mio |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 000 | Ciblée | 6,7 | 1,1 | 1,3 | 15,6 | 0,41 | 158,3 |
| 1 000 | Large | 20,8 | 3,3 | 11,7 | 15,6 | 1,56 | 157,9 |
| 10 000 | Ciblée | 40,7 | 16,8 | 17,9 | 46,9 | 4,26 | 158,9 |
| 10 000 | Large | 100,6 | 27,0 | 68,5 | 140,6 | 9,31 | 159,0 |
| 50 000 | Ciblée | 181,6 | 72,2 | 104,6 | 218,8 | 21,35 | 159,7 |
| 50 000 | Large | 292,1 | 110,6 | 169,9 | 281,2 | 35,95 | 160,2 |

Pour le scénario large à 50 000 écritures, le passage à un seul appel .NET par ligne pour le texte a ramené les allocations mesurées de 90,85 à 35,95 Mio. Les durées des deux exécutions ont varié. Une requête large annulée en cours d’exécution a été suivie d’une recherche réussie. Lors de deux demandes successives depuis WPF à 50 000 écritures, seule la dernière a publié ses résultats. Le minuteur de priorité saisie s’est déclenché 34 fois, avec un écart maximal de 32,1 ms pendant ce parcours.

## Rapport de reprise pour revue indépendante Sol XHigh

1. Exécuter l’intégralité des suites Debug et Release, puis les tests `LedgerSearchTests`. Examiner `MonetaFile.SearchOperations` et les limites exactes de `LedgerSearchCriteria.ParseAmount`. Contrôler les unités mineures extrêmes, l’échelle 28, la borne stricte et le regroupement par devise.
2. Générer des dossiers fictifs de 1 000, 10 000 et 50 000 lignes avec dates, textes, montants et UUID à égalité. Comparer le comptage et les UUID des pages aux résultats d’une référence indépendante, dont les pages suivantes et la dernière page.
3. Rejouer une annulation au comptage puis au tri, une recherche pendant un Ctrl+S et un changement de dossier à proximité du retour aux résultats. Contrôler l’équivalence du compte et de la ligne après navigation et la survie des critères au retour. Rejouer avec une jambe de frais dans un compte archivé et une suppression en brouillon.
4. Comparer les SHA-256 et la provenance avant et après la consultation, et contrôler l’état `IsDirty`. Vérifier les plans SQLite pour un compte et une plage de dates, puis toutes les devises avec texte et tri. Mesurer également des notes longues et des dossiers plus grands avant d’envisager un index.
5. Refaire le parcours au clavier et avec un lecteur d’écran sous Windows, sur plusieurs densités d’affichage si disponibles. Vérifier les raccourcis, les annonces de résultats, la sélection du texte en sombre et le focus après retour.

Ce rapport prépare une session de revue distincte. Les mesures de cette mission ne constituent pas une revue indépendante Sol XHigh.
