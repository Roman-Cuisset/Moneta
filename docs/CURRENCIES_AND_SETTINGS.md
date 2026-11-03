# Devises, comptes et paramètres — état mission 8

## Capacités réelles et limites

Comptes, opérations, pointages, rapprochements et virements liés sont persistés dans un `.moneta` SQLite **schéma 5**, après Ctrl+S. La mission 8 ajoute les cours officiels NBRB/CBR, leur cache local indépendant et la valorisation indicative, sans migration financière ni modification des montants natifs. Démonstration toujours séparée et non enregistrable. Aucun lecteur `.bp`, service de mise à jour, téléchargement ou installation de logiciel n’est implémenté.

## Comptes et registre de devises

- `CurrencyRegistry` expose un référentiel observable en lecture seule, initialisé avec EUR, BYN et RUB. `Register` permet d'autres devises ; aucune énumération fermée ni plafond de comptes. La limite pratique reste la mémoire disponible, pas une promesse de capacité infinie.
- Une devise porte code à trois lettres majuscules, nom, symbole, précision et `CurrencyDisplayRules` (culture, code/symbole, position du symbole). La validation contrôle la forme du code, pas son attribution dans un annuaire ISO 4217 certifié. Les montants restent en `decimal`.
- `Account.Currency` est une propriété en lecture seule : le choix d'affichage ne peut pas changer la monnaie native. Type et notes sont facultatifs. Aucun changement de monnaie native n'est proposé ; une future migration nécessitera un parcours explicite et contrôlé.
- **Comptes → Ajouter un compte** : nom, devise native, type libre, solde initial français et notes. Plusieurs comptes peuvent partager une devise ; le registre provient du dossier ouvert, sans forcer EUR/BYN/RUB s'ils en sont absents. Le compte est sélectionné, sans opération. En mode démonstration il reste en mémoire ; dans un dossier réel il est marqué **non enregistré** jusqu'à `Enregistrer`.
- Nom 120 caractères, type 80, notes 2 000 ; aucun plafond produit du nombre de comptes. Solde signé borné par `±long.MaxValue` unités mineures, fraction sous l'unité mineure refusée sans arrondi silencieux ; stockage `INTEGER` relu exactement. `Account.Currency` et la date de création sont immuables dans un dossier enregistré.

## Devise d'affichage et cours officiels — mission 8

Le sélecteur du panneau gauche choisit uniquement `MainViewModel.DisplayCurrency`. Changer de compte rétablit sa monnaie native. Dans cette monnaie, le solde réel reste affiché, sans cours nécessaire ni faux taux publié égal à 1. Dans une autre, l’application affiche un **équivalent estimé**, le fournisseur, la date d’effet réelle, l’ancienneté, la réception et l’état réseau/cache ; sans publication applicable, **Cours indisponible**. Le solde natif reste visible séparément. Aucun changement d’opération, de précision native ou de `IsDirty`.

Les devises de présentation forment une collection observable distincte : référentiel financier du dossier complété par EUR/BYN/RUB. Cette union n’ajoute aucune devise à `Currencies` ; **Ajouter un compte** ne propose toujours que le référentiel financier réel.

Le contrat actif est le **lot structuré `OfficialRateSet`** : UUID, fournisseur, devise de référence, dates demandée/effective, réception UTC, endpoint, SHA-256 et liste de `OfficialRateQuote`. Chaque cotation conserve valeur officielle exacte, nominal, code de devise et métadonnées source. Une publication entière possède un seul fournisseur et une seule date d’effet. `RateRules` contrôle codes ASCII de trois majuscules (forme, pas certification ISO), unicité, valeur strictement positive exactement représentable en `decimal`, nominal de 1 à 1 000 000 000 et 1 à 1 000 cotations. Aucun ancien contrat passif ni alias n’est conservé.

### Sources, calculs et dates

- **NBRB par défaut**, référence BYN ; **CBR sur choix explicite**, référence RUB. Aucun repli vers l’autre banque, aucun mélange de fournisseurs/dates pour compléter une conversion.
- GET du tableau quotidien complet, à date explicite : `https://api.nbrb.by/exrates/rates?ondate=yyyy-MM-dd&periodicity=0` ou `https://www.cbr.ru/scripts/XML_daily.asp?date_req=dd/MM/yyyy`. Aucun compte, solde, mouvement, montant à convertir ou identifiant personnel n’est transmis.
- Avec `q(C) = valeur officielle(C) / nominal(C)` en monnaie de référence et `q(référence)=1`, convertir A vers B par **montant A × q(A) / q(B)**. L’identité est mathématique, pas une cotation bancaire inventée. Ni marge ni frais bancaires inclus.
- API financière publique en `decimal` ; coefficients décimaux et fractions en `BigInteger` privés pour éviter arrondi ou débordement intermédiaire. `RateConverter.ConvertAmount` n’arrondit qu’au résultat, à la précision cible 0..28, avec `MidpointRounding.ToEven`. Résultat non représentable : erreur explicite, aucun flottant de secours.
- `ConvertTotal` additionne les valeurs natives converties **exactement avant tout arrondi**, puis arrondit le total une seule fois. Une seule cotation manquante fait échouer le total entier, jamais un sous-total présenté comme complet. Cette API n’annonce pas de total multidevise supplémentaire dans l’interface.
- La sélection retient la date d’effet la plus récente **≤ date de valorisation**, âgée d’au plus **31 jours calendaires inclus**, puis la réception la plus récente pour une correction à date identique. Les dates demandée au service, effective retournée et de valorisation restent distinctes. Une publication future peut être conservée mais jamais utilisée avant sa date d’effet ; pas d’interpolation ni de date réécrite.
- Les publications d’effet antérieur au **01/07/2016 pour NBRB/BYN** ou au **01/01/1998 pour CBR/RUB** sont refusées, même si leur réponse est numériquement valide. Les anciens roubles BYR et russes ne sont jamais étiquetés comme unités actuelles ni redénominés automatiquement. Ces frontières concernent les cours, sans réécrire les dates/montants des opérations ; preuves officielles dans [ARCHITECTURE §5.4](ARCHITECTURE.md#54-banque-centrale-de-russie--étude-complémentaire-mission-2).
- Les deux montants natifs, frais et rapport effectif des virements restent figés selon leurs saisies réelles. `TransferWindow` ne propose ni suggestion officielle automatiquement appliquée ni réévaluation d’écriture. Les protections NP/P/R et le schéma financier 5 restent inchangés.

### Cache, actualisation et interface

`%LocalAppData%\Moneta\exchange-rates.sqlite` est une base indépendante de données publiques, schéma **1**, `application_id=0x4D465831` (`MFX1`). Elle conserve les octets originaux et leur SHA-256 avec les métadonnées de chaque lot immuable, sans duplication des cotations numériques ni `REAL` monétaire. Historique append-only, sans purge automatique : les 100 publications les plus récentes du fournisseur sont affichées ; les plus anciennes restent recherchables par date. Schéma, intégrité, empreinte et reparsage de la réponse sont contrôlés ; cache corrompu/inconnu/inaccessible = erreur visible, jamais effacement ou recréation silencieuse. Voir [FILE_FORMATS](FILE_FORMATS.md#fmt-10--cache-public-de-cours-et-préférences--mission-8).

Le cache est lu hors dispatcher avant le réseau. Une réponse validée reste utilisable en mémoire si son enregistrement échoue ; l’interface précise alors **mémoire uniquement / persistance non confirmée**, sans annoncer un cache disque réussi. Une actualisation ultérieure hors ligne conserve ce lot applicable pour le même fournisseur et la même date de valorisation ; sa disponibilité après redémarrage n’est pas garantie.

**Outils → Cours de change…** ouvre `RatesWindow` : source, date française, tableau des valeurs/nominaux, provenance, historique récent, convertisseur manuel indicatif, chargement du cache par date, actualisation et annulation. L’ancienne commande **Actualiser les cours officiels** est également active. Changer source ou date consulte uniquement le cache ; seul **Actualiser** force un téléchargement. La source est persistée ; une erreur d’enregistrement des préférences ne valide pas un nouveau choix.

L’actualisation au démarrage est **désactivée par défaut** et ne s’applique qu’au prochain lancement lorsqu’on la coche. Si elle est activée, un cache demandé pour aujourd’hui et reçu depuis au plus six heures évite le réseau ; une date passée déjà disponible est réutilisée hors actualisation manuelle forcée. Il n’y a pas de boucle de rafraîchissement périodique.

Chaque requête HTTP est limitée à **10 secondes**, la chaîne réseau et l’enregistrement à **20 secondes** au total. Seule une publication absente permet d’interroger jusqu’aux **sept dates calendaires précédentes** (huit dates au total) ; erreur HTTP/réseau ou réponse invalide = arrêt sans réessai caché. L’annulation et les générations de requêtes protègent les changements de source/date et la fermeture : aucun résultat dépassé ne remplace le choix courant. HTTPS et endpoints fixes, redirections/cookies désactivés, réponse ≤1 Mio et ≤1 000 cotations, XML sans DTD/résolveur externe et encodage déclaré respecté. HTTPS et SHA-256 ne constituent pas une signature bancaire du flux.

## Préférences locales opérationnelles

`SettingsStore` sauvegarde `%LocalAppData%\Moneta\settings.json`, schéma **2** : `SchemaVersion`, `Theme`, `CheckUpdatesAtStartup`, `RefreshRatesAtStartup`, `RateSource` (`NBRB` ou `CBR`). Une préférence complète de schéma **1** est acceptée en conservant thème et booléens, avec NBRB par défaut ; le prochain enregistrement explicite écrit le schéma 2, sans migration du `.moneta`. Pas de compte, montant, devise d'affichage de compte, cours, secret ou jeton dans ce JSON ; les réponses publiques sont dans le cache SQLite séparé.

- Menu **Affichage → Thème → Clair / Sombre** : applique et conserve la préférence. Le sélecteur supérieur a été supprimé. Le contraste élevé Windows reste prioritaire. **Suivre Windows n'est pas proposé** : seule la sélection explicite des deux thèmes est qualifiée ici ; pas de suivi système annoncé sans essai.
- **Outils → Options** : Général, Apparence, Devises et taux, Mises à jour, Sauvegardes. Enregistrer applique les modifications ; Annuler/Échap les abandonne. **Devises et taux** propose source officielle, actualisation au prochain démarrage et information sur les derniers cours chargés. L’option de recherche de mises à jour demeure une **préférence uniquement — service non développé**. Les deux booléens restent faux à la première utilisation.
- Premier lancement sans fichier : valeurs en mémoire, pas d'écriture implicite. Sauvegarde dans un temporaire du même dossier, `Flush(true)`, puis renommage avec remplacement. L'état `Current` ne change qu'après succès. `File.Replace` a échoué sur ce poste pendant un test ; le renommage évite sa fusion inutile des métadonnées. Les erreurs restent remontées, sans faux succès ni réessai caché.
- JSON corrompu, incomplet, source inconnue ou version non supportée : avertissement visible, contenu conservé, écriture bloquée jusqu'à correction/déplacement puis redémarrage. Un fichier devenu invalide après chargement est également préservé. Aucun fichier invalide n'est silencieusement réinitialisé.
- Le renommage local des préférences ne remplace pas les garanties de transaction/sauvegarde financière. Aucun service permanent, télémétrie, compte cloud ou code de mise à jour n’est ajouté ; le réseau de cours ne part au démarrage que si sa préférence est activée et le cache insuffisamment frais.

## Mises à jour : architecture future, non active

La préférence prépare une vérification non bloquante et désactivable. Un futur client pourra lire les métadonnées de GitHub Releases, comparer des versions structurées (y compris politique explicite de préversions), borner délais/tailles et afficher les erreurs sans empêcher l'ouverture. Aucun téléchargement, installation ou redémarrage sans consentement.

Avant activation : définir la distribution d'un dépôt privé, l'autorisation d'accès sans embarquer de jeton GitHub, le format du manifeste et une racine de confiance. HTTPS et un hash reçu du même serveur ne suffisent pas à authentifier un binaire : vérifier signature de code/publisher attendu, manifeste signé et empreinte avant toute exécution, avec rotation/révocation des clés prévues. Aucun mécanisme d'installation n'est réalisé dans cette mission ; ni release ni Pages ne sont publiées.

## Commandes et raccourcis

`WindowCommands` centralise les intentions ; menus et outils partagent les mêmes commandes. Les commandes financières sans implémentation répondent `CanExecute=false`.

| Intention | Raccourci préparé | Décision et état |
|---|---|---|
| Ouvrir / Enregistrer | Ctrl+O / Ctrl+S | **Actifs** : ouverture/commit d'un dossier `.moneta` ; Enregistrer sous = F12. Aucune réécriture d'un `.bp` |
| Annuler / Refaire une écriture | Ctrl+Z / Ctrl+Y envisagés | Désactivés et aucune liaison globale de ces touches ; l'éditeur de texte garde ses propres commandes natives |
| Rechercher | Ctrl+F | Souhait mission 5, recherche désactivée. Conflit avec **Champs automatiques** BP : le Ctrl+F affiché sur cette ancienne commande a été retiré, pas conservé simultanément ; affectation historique future à arbitrer avant son développement |
| Supprimer une opération | Suppr / Ctrl+Suppr dans le registre seulement | Mission 7A : confirmation puis suppression logique en brouillon ; Ctrl+S committe. Aucune suppression financière depuis un champ texte |
| Annuler / fermer un dialogue | Échap | Boutons WPF IsCancel, sans interception globale des champs |
| Nouvelle / modifier opération | Ctrl+Entrée / Maj+Entrée dans le registre | Mission 7A : même commande que menu/bouton, dialogue validant ; pas de raccourci global au détriment des champs |
| Pointer / basculer NP/P | P / Espace dans le registre | Colonne/contextes/menus partagent les commandes ; sélection multiple atomique, R protégées |
| Dépointer | Maj+P dans le registre | Choix Moneta explicite ; ne change aucun montant |
| Date / affichage de pointage | Ctrl+Maj+P / F11 dans le registre | Gestes historiques documentés ; date distincte de la date d’opération |
| Rapprochement bancaire | Ctrl+Maj+R dans le registre | Choix Moneta, pas raccourci BP attesté ; dossier enregistré/propre requis |

Le catalogue historique [UI_MENU_MAP](UI_MENU_MAP.md) reste une preuve BankPerfect, pas la liste des raccourcis financiers actifs dans ce prototype.

## Vérifications réalisées

Mission 5 : 29 tests Debug/Release — les 12 historiques inchangés, 10 tests de comptes/devises et 7 cas de préférences (dont corruption/version/écriture impossible). Tests WPF jetables : ajout par le véritable dialogue, 105 comptes et plusieurs comptes EUR, registre 3 200 lignes conservé, devise d'affichage sans conversion, thème enregistré/rechargé et deux options sauvegardées puis restaurées. Sélection TextBox/ComboBox active/inactive/readonly/désactivée examinée dans les deux thèmes ; gestes souris/clavier et Ctrl+Z natif vérifiés sous Windows.

Mission 6 : **91 tests Debug et Release**, les 29 précédents conservés, avec format/erreurs/verrous/checkpoint/résistance, sauvegarde-restauration, migration sur copie et transitions de dossier. Parcours WPF réel sur quatre comptes synthétiques EUR/BYN/RUB, sauvegarde, restauration comparée et réouverture. Mesures et limites, y compris le CPU non résolu d'une fenêtre de démonstration visible, dans le [README](../README.md). Pas de qualification de lecteur d'écran ou de plusieurs moniteurs physiques nouvelle dans cette mission.

Mission 8 : les contrats opérationnels ci-dessus ne constituent pas un décompte de qualification finale. Le client .NET réel a reçu les tableaux officiels NBRB et CBR en HTTP 200 pour le 3 avril 2026 et le jour local du parcours ; le XML CBR original Windows-1251 a été décodé sans réécriture des octets conservés. Les preuves historiques des missions 5/6 restent celles de leurs parcours, pas des essais des nouvelles fonctionnalités. Résultats finaux, scénarios Windows et limites de mesure de mission 8 sont centralisés dans le [README](../README.md).
