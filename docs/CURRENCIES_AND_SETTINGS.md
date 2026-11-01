# Devises, comptes et paramètres — état mission 7B

## Capacités réelles et limites

Comptes et opérations sont persistés dans un `.moneta` SQLite après Ctrl+S. La mission 7B ajoute pointage, relevés et historique, sauvegardés avec le registre ; démonstration toujours séparée et non enregistrable. Les paramètres non financiers restent locaux. Aucun lecteur `.bp`, fournisseur de cours, cache de taux, service de mise à jour ou téléchargement n’est implémenté.

## Comptes et registre de devises

- `CurrencyRegistry` expose un référentiel observable en lecture seule, initialisé avec EUR, BYN et RUB. `Register` permet d'autres devises ; aucune énumération fermée ni plafond de comptes. La limite pratique reste la mémoire disponible, pas une promesse de capacité infinie.
- Une devise porte code à trois lettres majuscules, nom, symbole, précision et `CurrencyDisplayRules` (culture, code/symbole, position du symbole). La validation contrôle la forme du code, pas son attribution dans un annuaire ISO 4217 certifié. Les montants restent en `decimal`.
- `Account.Currency` est une propriété en lecture seule : le choix d'affichage ne peut pas changer la monnaie native. Type et notes sont facultatifs. Aucun changement de monnaie native n'est proposé ; une future migration nécessitera un parcours explicite et contrôlé.
- **Comptes → Ajouter un compte** : nom, devise native, type libre, solde initial français et notes. Plusieurs comptes peuvent partager une devise ; le registre provient du dossier ouvert, sans forcer EUR/BYN/RUB s'ils en sont absents. Le compte est sélectionné, sans opération. En mode démonstration il reste en mémoire ; dans un dossier réel il est marqué **non enregistré** jusqu'à `Enregistrer`.
- Nom 120 caractères, type 80, notes 2 000 ; aucun plafond produit du nombre de comptes. Solde signé borné par `±long.MaxValue` unités mineures, fraction sous l'unité mineure refusée sans arrondi silencieux ; stockage `INTEGER` relu exactement. `Account.Currency` et la date de création sont immuables dans un dossier enregistré.

## Devise d'affichage et contrats futurs de cours

Le sélecteur du panneau gauche choisit uniquement `MainViewModel.DisplayCurrency`. Changer de compte rétablit sa monnaie native. Dans cette monnaie, le montant affiché est son solde calculé ; dans une autre, **Cours indisponible**. Aucun calcul de conversion ni taux fictif. Les opérations, leur ordre, montants et monnaie native restent inchangés.

`ExchangeRateQuote` est un contrat de données passif : devise de base, devise de cotation, valeur décimale publiée, échelle entière, date et source. La convention est **Value unités de QuoteCurrency pour Scale unités de BaseCurrency**. Ce record n'est ni un validateur ni un service de change ; aucun consommateur ne calcule de cours dans cette mission.

Pour les futurs adaptateurs :

1. Source **NBRB** prioritaire, source **CBR** séparée et explicitement identifiable. Aucun remplacement automatique de fournisseur.
2. Conserver la valeur publiée et son nominal (`Cur_Scale` NBRB, `Nominal` CBR) ; ne pas assimiler 1, 10 ou 100 unités. Valider valeurs/échelle/devises/date/source avant tout calcul.
3. Cache daté et sourcé, réponses originales/provenance et éventuelles corrections ; charger le cache avant toute requête réseau. Une date manquante reste manquante, ou un dernier taux connu est signalé comme tel après choix explicite.
4. Commande manuelle **Outils → Actualiser les cours officiels** déjà placée mais désactivée. À terme, requêtes asynchrones bornées/annulables si la préférence le permet, erreurs visibles non bloquantes et aucun cours estimé de secours.
5. Taux réellement appliqué aux écritures/transferts futur figé avec source/date/échelle, jamais réécrit lors d'une actualisation. Voir [ARCHITECTURE](ARCHITECTURE.md) et [FILE_FORMATS](FILE_FORMATS.md).

## Préférences locales opérationnelles

`SettingsStore` sauvegarde `%LocalAppData%\Moneta\settings.json`, schéma 1 : `SchemaVersion`, `Theme`, `CheckUpdatesAtStartup`, `RefreshRatesAtStartup`. Pas de compte, montant, devise d'affichage de compte, taux, secret ou jeton dans ce document.

- Menu **Affichage → Thème → Clair / Sombre** : applique et conserve la préférence. Le sélecteur supérieur a été supprimé. Le contraste élevé Windows reste prioritaire. **Suivre Windows n'est pas proposé** : seule la sélection explicite des deux thèmes est qualifiée ici ; pas de suivi système annoncé sans essai.
- **Outils → Options** : Général, Apparence, Devises et taux, Mises à jour, Sauvegardes. Enregistrer applique les modifications ; Annuler/Échap les abandonne. Les deux options de démarrage sont enregistrables et fausses à la première utilisation, mais étiquetées **préférence uniquement — service non développé, aucune requête**.
- Premier lancement sans fichier : valeurs en mémoire, pas d'écriture implicite. Sauvegarde dans un temporaire du même dossier, `Flush(true)`, puis renommage avec remplacement. L'état `Current` ne change qu'après succès. `File.Replace` a échoué sur ce poste pendant un test ; le renommage évite sa fusion inutile des métadonnées. Les erreurs restent remontées, sans faux succès ni réessai caché.
- JSON corrompu, incomplet ou de version non supportée : avertissement visible, contenu conservé, écriture bloquée jusqu'à correction/déplacement puis redémarrage. Un fichier devenu invalide après chargement est également préservé. Aucun fichier invalide n'est silencieusement réinitialisé.
- Le renommage local des préférences ne remplace pas les garanties de transaction/sauvegarde financière futures. Pas de service permanent ni de tâche réseau au démarrage, même lorsque les deux préférences sont cochées.

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
