# Formats de fichiers Moneta

Le format financier courant utilise le **schéma SQLite 6**. Il couvre les comptes, le registre, les rapprochements, les virements liés avec frais et la provenance des imports BankPerfect. Les cours officiels et leur historique public résident dans un **cache SQLite séparé, schéma 1**. Les préférences locales utilisent le schéma JSON 3. [Architecture](ARCHITECTURE.md)

Le [diagnostic BankPerfect V5](BANKPERFECT_FORMAT_RESEARCH.md#lecteur-diagnostique-moneta--mission-10b) utilise un modèle en mémoire et une CLI indépendante du format financier.

La recherche de mission 12 lit le schéma 6 et réutilise les tables de comptes, opérations, référentiels et virements ainsi que leurs index existants. Les critères et pages restent en mémoire. Le texte consultable provient du registre actif et des virements actifs. Le BLOB de provenance BankPerfect reste conservé selon le même contrat. [Recherche et filtres](SEARCH_AND_FILTERS.md)

## Comptes et schéma courant

Le schéma **6** ajoute à `Accounts` les colonnes texte facultatives `bank_name`, `bank_reference` et `holder`. Une migration de v5 vers v6 les initialise à NULL et compare le contenu avant et après, en incluant le condensat du registre et l’empreinte de provenance. Le schéma 6 inclut ces champs et l’ordre des comptes dans le contrôle de sauvegarde. Les sauvegardes v1 à v5 restent vérifiées avec leur représentation d’origine.

La suppression d’un compte se fait dans la transaction d’enregistrement. Le contrôle porte sur le solde initial, les écritures actives et supprimées, les rapprochements et les comptes d’origine référencés dans la provenance. Les liens de virements et de frais dépendent de leurs écritures. Les relations historiques restent en place lors de l’archivage et de la réactivation. [Gestion des comptes](ACCOUNT_MANAGEMENT.md)

## Provenance des imports BankPerfect

Le rapport source reste un instantané de l’import après les corrections, les suppressions logiques et les changements de rapprochement réalisés dans Moneta. Les valeurs courantes se lisent dans le registre. Les événements et instantanés de relevés décrivent les transformations de rapprochement qu’ils couvrent. [Qualification après modification](BANKPERFECT_QUALIFICATION_10E.md#fidélité-des-données)

En 10D, le JSON v1 accepte des champs additionnels dans le rapport privé. Les avertissements portent un type `kind` et conservent `blocking`. Une correspondance peut indiquer `openingConfidence` et, pour un virement, `transfer` détaille chaque jambe jusqu’à l’UUID d’opération cible. Les positions de source et les UID écrits sont conservés, y compris leurs zéros initiaux. Les anciens rapports v1 restent lisibles via la même API.

Les dossiers créés par la mission 10C utilisent les mêmes tables financières, la même précision et `user_version=5`. Ils possèdent une table optionnelle non financière `BankPerfectProvenance`, associée au marqueur d’origine `native-accounts-v5+bankperfect-v1`. Ce marqueur exige la table lors d’une ouverture. Son schéma exact et son unique rapport JSON v1 en BLOB sont validés, avec SHA-256 et limite de 64 Mio. Les dossiers ordinaires et leurs manifestes existants gardent leur format. Les versions de Moneta antérieures à 10C rejettent cette extension.

Le rapport contient l’empreinte des octets BankPerfect, les positions décodées, les UID historiques y compris leur absence, les UUID Moneta, les règles de correspondance, les champs bruts et les avertissements. `MonetaFile.ReadBankPerfectProvenance` permet une consultation locale. Le digest des copies inclut l’empreinte de la provenance si elle existe. L’empreinte contrôle l’intégrité des données et ne signe pas leur origine. Le rapport peut contenir des données privées, comme le dossier qui l’héberge.

La prévalidation refuse aussi les registres dont le nombre de lignes multiplié par la taille des référentiels dépasse cinquante millions, pour borner le coût des contrôles des API de stockage. Les plafonds du lecteur s’appliquent à la source. Le candidat autonome est vérifié avant le renommage sans écrasement.

**Consolidation mission 9 : aucun changement de format ni migration supplémentaire.** Revue des contrôles d'identité, transactions, références, montants exacts, historique et graphes de virements ; les tests existants de corruption, concurrence, sauvegarde/restauration et migrations restent verts. Une publication de sauvegarde interrompue sans manifeste final n'est pas certifiée ; un échec d'écriture conserve les brouillons et l'ancien snapshot. La garantie SHA-256 est un contrôle d'intégrité, pas une signature d'authenticité. Preuves et limites : [audit mission 9](PERFORMANCE_AUDIT.md).


Les identifiants `FMT-*` sont stables pour les renvois de la feuille de route. « Doit » exprime une exigence Moneta ; une propriété documentée de SQLite n'est pas une garantie d'implémentation Moneta.

## FMT-01 — Format natif retenu et frontières

- **Un dossier de comptes = une base SQLite directe portant l'extension `.moneta`**, par exemple `Mes comptes.moneta`. Plusieurs comptes peuvent appartenir à cette base ; plusieurs dossiers indépendants peuvent exister.
- Ce n'est **ni une archive ZIP**, ni un conteneur à décompresser, ni un `.bp` renommé, ni une encapsulation de BankPerfect. Aucune installation, API ou exécution BankPerfect n'est nécessaire au format natif Moneta.
- Le fichier commence par les 16 octets de signature SQLite `SQLite format 3\0` (zéro terminal). Cette signature identifie SQLite, **pas Moneta**. L'extension seule n'identifie aucun format et une base SQLite quelconque renommée `.moneta` doit être refusée comme dossier Moneta.
- L'identification doit combiner signature, `PRAGMA application_id`, métadonnées Moneta, version et schéma reconnu. `application_id` est le champ applicatif 32 bits de l'en-tête SQLite ; `user_version` est une convention de l'application. Ne pas utiliser `schema_version`, que SQLite gère pour son propre schéma, comme numéro de migration Moneta [S01, S02].
- Contrat livré : marqueur `Moneta`, `application_id=0x4D4E5441` (`MNTA`), `user_version=5` et métadonnées concordantes ; UUID stable, nom, versions minimales lecteur/écrivain, créateur historique `Moneta-6`, UTC et sélection facultative. L’identifiant applicatif n’a pas de réservation officielle revendiquée.
- Treize tables `STRICT`, sans montants financiers `REAL` :

| Table | Champs / relations réalisés |
|---|---|
| `Document` | Une ligne singleton ; identité, versions, nom, création/modification UTC, sélection |
| `Currencies` | Code unique, nom, symbole, précision, culture/règles d'affichage et ordre ; EUR/BYN/RUB proposés à la création, registre extensible |
| `Accounts` | UUID unique, nom, type, devise native FK vers `Currencies`, `opening_minor INTEGER`, notes, archive, création/modification UTC, ordre, banque, référence bancaire et titulaire |
| `MigrationHistory` | Version source/cible, identifiant/définition de migration et date UTC ; seules les définitions reconnues sont acceptées |
| `Categories` | UUID, nom, clé de correspondance, parent facultatif ; racines et un niveau de sous-catégories, unicité par parent |
| `Payees` | UUID, nom et clé unique de correspondance |
| `PaymentMethods` | UUID, nom et clé unique de correspondance |
| `Transactions` | UUID, compte/devise native, date ISO, magnitude `amount_minor INTEGER` non négative et sens séparé, libellé/notes, références facultatives aux tiers/catégories/modes, état de pointage, création/modification UTC, révision et suppression logique ; `marked_on` ISO nullable, `reconciliation_id` FK nullable cohérente avec état/suppression |
| `Reconciliations` | UUID, compte/devise cohérents, date/numéro, ouverture/clôture attendue/calculée en unités mineures signées, prédécesseur actif, UTC de validation, état/révision et motif/date de réouverture |
| `ReconciliationEntries` | Clé session/opération, FK sans cascade, version d’instantané 1 et JSON canonique complet avant validation |
| `ReconciliationHistory` | UUID d’événement, session ou opération selon type, validation/réouverture/libération héritée, UTC, motif et instantané V1 pour libération |
| `Transfers` | UUID, date commune, référence/notes, date/source déclarées du change nullable, UTC, révision et suppression logique ; ni comptes ni montants dupliqués |
| `TransferLegs` | FK virement/opération sans cascade ; clé `(transfer_id, role)`, rôle 0 débit/1 crédit/2 frais ; `transaction_id` unique ; index d’unicité pour recherche dans les deux sens |

Les opérations référencent les comptes, tiers, modes et catégories par UUID. La sous-catégorie doit appartenir à la racine choisie ; pas de références orphelines ni de suppression cascade des opérations. Un index compte/date/création/UUID soutient l’ordre stable du registre ; les index et le schéma exact sont contrôlés à l’ouverture. Les virements sont des relations normalisées séparées : `Transactions` et les instantanés canoniques `OperationSnapshotV1` restent inchangés par le passage 4 → 5. Ventilations, échéanciers et provenance BP restent des extensions futures, sans colonne fictivement utilisée.
- SQLite ordinaire n'apporte pas de chiffrement implicite. Aucun mot de passe BP, clé, secret ou protection inconnue n'est transféré ou émulé. La protection future d'un dossier est un sujet explicite distinct, pas une propriété déduite de l'extension.

## FMT-02 — Reconnaître avant d'écrire ou de créer

### Ouverture d'un fichier existant

1. Résoudre le fichier choisi, contrôler son existence et ses droits sans mode « créer si absent ». Un chemin manquant, un fichier vide, un ZIP, un `.bp` ou un en-tête tronqué donne une erreur descriptive, **jamais une base neuve de remplacement**.
2. Lire un en-tête borné sans écriture. La taille, les limites de pages/texte et les ressources admissibles devront être définies et éprouvées sur données synthétiques. Une extension trompeuse ne change pas le résultat de détection. Un `.bp` passe par le parcours de conversion, non par un essai d'ouverture SQLite [IMP-01 à IMP-04].
3. Ouvrir pour diagnostic initial en lecture seule, sans création, avec moteur SQLite maintenu/corrigé, extensions chargeables désactivées, schéma non fiable (`trusted_schema=OFF`), requêtes applicatives paramétrées et limites de ressources. Ne pas exécuter du SQL, une fonction, un module, un déclencheur applicatif ou un plugin fourni par le dossier. Comparer le schéma au schéma attendu plutôt que faire confiance à des tables de noms semblables [S07].
4. Contrôler identité/version, intégrité structurelle puis références et invariants. `integrity_check` ne vérifie pas les clés étrangères : **`foreign_key_check` est distinct** [S02]. Une vérification annulée ou incomplète n'est pas un résultat valide.
5. Si des journaux de reprise sont présents, ne pas les ignorer et ne pas activer `immutable=1` pour contourner un verrou ou une récupération. Le terme « immuable » ne s'applique qu'à un snapshot réellement stable. Si la lecture seule ne permet pas la reprise, arrêter et proposer une procédure explicite conservant l'ensemble original ; pas de « réparation » automatique sur l'unique exemplaire. Une éventuelle récupération SQLite s'exerce d'abord sur une copie cohérente complète, avec résultats comparés.
6. Autoriser l'écriture uniquement après validation du format pris en charge, des contrôles et du parcours de migration si nécessaire. Vérifier les valeurs réellement prises par les PRAGMA : un PRAGMA inconnu peut être ignoré sans erreur par SQLite [S02].

### Création d'un nouveau dossier

**Fichier → Nouveau / Ctrl+N** demande un nom et prépare un dossier vide en mémoire. Le premier Enregistrer demande la destination. Création exclusive d'un temporaire voisin, schéma/identité/données vérifiés, fermeture puis publication sans écrasement et réouverture comparée. Un principal existant ou un journal compagnon préexistant bloque la création, sans supprimer ces fichiers. Annuler ne crée aucun dossier financier. Les copies restent sur le même volume pendant publication ; aucune atomicité inter-volume n'est revendiquée.

### Versions anciennes, futures ou inconnues

- Version connue courante : contrôles puis accès normal.
- Version connue plus ancienne : sauvegarde cohérente restaurable, consentement de migration et procédure FMT-07.
- Version future ou schéma non reconnu : refus d'écriture et de migration. Lecture seule seulement si un contrat de compatibilité explicite et éprouvé la permet ; sinon refus total avec conservation du fichier. **Pas de downgrade** ni de modification du numéro pour forcer l'ouverture.
- Identité ambiguë, métadonnée incohérente, table inattendue ou corruption : blocage expliqué, aucune recréation et aucune suppression des journaux.

## FMT-03 — Valeurs financières et dates exactes

Soldes initiaux, opérations et montants natifs des virements sont réalisés. Les cours officiels de mission 8 sont conservés exclusivement dans le cache public FMT-10 ; prix/quantités boursiers restent futurs. Le rapport effectif d’un virement est défini par ses deux montants exacts, pas par une colonne de taux arrondi.

| Donnée | Contrat de stockage | Validation requise |
| --- | --- | --- |
| Montant comptabilisé et solde initial | `INTEGER` 64 bits en unités mineures, devise et échelle explicites ; pour une opération, magnitude non négative avec sens débit/crédit séparé | Pas de supposition « toute devise a deux décimales ». À deux décimales, 12,34 EUR = 1234 unités ; solde négatif et sens restent distincts. Débordement ou fraction sous l'unité rejetés explicitement, jamais tronqués |
| Prix unitaire, quantité de titres — futurs | `TEXT` décimal canonique invariant, point décimal, sans séparateur de milliers ; précision, unité et provenance explicites | Ex. quantité fictive `2.5`. Préserver la précision source nécessaire ; ne pas passer par `double` ou `REAL`. Pour les cours officiels réalisés : réponse brute et métadonnées dans FMT-10, pas de colonne financière supplémentaire |
| Dates métier | Texte ISO `YYYY-MM-DD`, sans fuseau | Dates d'opération, pointage, échéance et relevé distinctes ; inconnue reste inconnue |
| Horodatages techniques | UTC avec représentation non ambiguë | Création/import/sauvegarde ne remplacent jamais les dates métier |
| Texte utilisateur | Unicode, valeur et provenance conservées | Aucune translittération ou normalisation destructive silencieuse ; ne pas exécuter les champs de règles historiques |

SQLite `REAL` est binaire flottant ; `DECIMAL(10,2)` ne garantit ni échelle ni exactitude. `Microsoft.Data.Sqlite` **10.0.12** est utilisé avec des paramètres entiers explicites pour les soldes, pas sa conversion automatique de `decimal`. `ExactMoney` rejette toute fraction sous l'unité et tout dépassement de `±long.MaxValue` unités mineures ; **aucun arrondi à la saisie**. Les valeurs officielles sont analysées exactement en `decimal`, les fractions intermédiaires utilisent des coefficients entiers privés et le seul arrondi de valorisation est `ToEven` à la précision cible ; aucun de ces calculs ne réécrit un montant comptabilisé. Pas de `TOTAL`, `AVG`, cast `REAL` ou somme implicite de valeurs `TEXT` pour une vérité financière.

Pour une opération, la magnitude doit être entre zéro et `long.MaxValue` unités mineures ; `Direction=Debit` soustrait, `Credit` ajoute. Le solde initial reste signé. Les agrégats sont calculés en `decimal` contrôlé sans être forcés dans `Int64`. Date, énumérations, devise native, précision, UUID, hiérarchie, projections des noms et horodatages/révisions sont validés. L’interface n’accepte ni signe négatif ni arrondi silencieux ; les zéros fractionnaires supplémentaires exacts n’altèrent pas le montant.

Insertion avec révision attendue 0 ; correction/suppression avec révision attendue égale à la version enregistrée. Compte, devise, UUID et création immuables. Les brouillons conservent leur base de révision jusqu’au commit. Suppression logique, sans purge/corbeille ni historique général revendiqué. Les R formelles ou héritées ne passent pas par la correction/suppression normale.

### Pointage, relevés et preuves

NP = non pointé ; P = vérifié ; R = membre d’un relevé validé, sauf état hérité explicitement non attesté. NP n’a ni date de pointage ni session. Une date P/R héritée peut rester inconnue ; aucun relevé n’est fabriqué à la migration. Une session exige une date de fin cohérente, un compte/devise, une chaîne active et un écart exactement nul, mais aussi des instantanés/révisions/references concordants.

Les soldes d’opération restent inchangés par le pointage. Pointé = ouverture + mouvements actifs P/R ; non pointé = mouvements actifs NP ; actuel = somme de ces deux totaux, toutes dates incluses. Agrégats C# `decimal` ; ouverture/clôture de relevé doivent en plus tenir dans `±long.MaxValue` unités mineures pour leur stockage `INTEGER`, sans arrondi de repli.

Les instantanés JSON V1 stockent magnitude en unités mineures entières, devise/précision/règles, références et noms historiques, date/notes/pointage, UUID/révisions et UTC. Membres inconnus, version inconnue ou représentation non canonique refusés. Réouverture du dernier actif seulement, avec conservation du relevé/instantanés et ajout d’événement ; état/date des membres rétablis. Libération d’une R héritée : événement portant le snapshot original et le motif.

Le lot est enregistré atomiquement avec ses modifications d’opérations. Toute autre mutation du registre exige d’abord ce commit ; la création vierge ne reçoit pas de lot de rapprochement. Une date nouvellement fixée lors de la validation est contrôlée contre son horodatage de validation (jour UTC ±1), jamais remplacée par le jour du Save ; les dates préexistantes doivent rester identiques.

### Virements, montants et relations — schéma 5

Chaque virement exige exactement un principal débiteur et un principal créditeur, sur deux comptes distincts du même dossier, plus un débit de frais facultatif. Les montants des opérations restent des **magnitudes non négatives** en `INTEGER` natif ; principal strictement positif, impact signé −débit/+crédit/−frais, solde initial signé distinct. Même devise : principaux exactement égaux hors frais et métadonnées de change null. Multidevise : deux montants réellement saisis, date du change et source déclarée/non vérifiée obligatoires ; défauts de saisie = date du virement et « Montants saisis par l’utilisateur ». Aucun arrondi ni conversion automatique des jambes ; les cours officiels de mission 8 ne sont ni injectés dans ce dialogue ni appliqués rétroactivement.

Le **rapport exact crédit principal / débit principal**, hors frais, cote des unités de devise reçue pour **1 unité source**. Ses opérandes exacts sont conservés avec leurs devises/échelles ; un quotient `decimal` affiché reste « Taux approximatif ». Débordement ou quotient devenu zéro : rapport seul. Ni cours d’affichage ni frais ne changent le principal ou son ratio.

Frais sur compte source, destinataire ou troisième compte actif, en devise native : positifs au premier ajout ; une jambe déjà ajoutée peut être corrigée à zéro, UUID/compte/lien conservés, y compris avant son premier Save. Le stockage accepte ce brouillon corrigé ; il ne lui impose pas un arrondi ou un montant artificiellement positif. Comptes principaux et liens existants immuables ; compte de frais immuable dès ajout, première jambe de frais ajoutable si absente. Aucun retrait/remplacement d’une jambe de frais persistée.

La validation groupée contrôle rôles, références, comptes/devises natifs, dates/notes et suppression concordantes, sans jambe partagée/orpheline. Une modification financière exige le delta de l’entité et les patches **complets** de ses jambes, chacun avec sa révision attendue ; les révisions initiales du brouillon ne sont pas rebascu­lées avant commit. Une mutation financière/suppression isolée d’une jambe est refusée dans Core et stockage. Les changements NP/P et leurs dates restent indépendants selon 7B et peuvent accompagner un patch complet dans le même Save ; ils ne propagent aucun pointage.

Toute jambe **R**, frais compris, bloque correction/suppression de l’ensemble, même si la jambe sélectionnée n’est pas R. Réouverture/libération motivée puis **Save préalable**. Les instantanés V1 et leurs reçus restent intacts ; l’appartenance au virement est vérifiée et hachée séparément, pas ajoutée au JSON historique.

Create/Save/SaveAs reçoivent les brouillons de virements en plus des opérations, y compris sur dossier jamais enregistré. Validations de tous les comptes et soldes avant publication en mémoire ; contrôles de concurrence, écritures et validation du graphe dans la même transaction disque avant commit. Une erreur conserve les brouillons, sans état partiel ni succès annoncé. Supprimer avant premier Save retire l’ensemble des brouillons ; après Save, entité et toutes jambes persistées sont supprimées logiquement ensemble, liens conservés. Si seuls de nouveaux frais sont encore en brouillon lors de la suppression d’un virement enregistré, ils sont abandonnés et ne créent pas de trace orpheline.

Le digest v5 ajoute un domaine distinct `Moneta-transfers-v5`, métadonnées, rôles, UUID des jambes, révisions et tombstones ; `TransferCount` inclut les entités supprimées. Parcours d’une jointure ordonnée avec un seul groupe à la fois ; projection des liens bornée à la page de registre. SaveAs, sauvegarde/restauration incluent aussi rapprochements, instantanés et événements : une égalité de soldes ne suffit jamais à établir l’identité des copies. Aucune provenance BankPerfect fabriquée : une future correspondance import utilisera les UUID stables et des preuves de format source.



Contraintes de domaine, unicité, références et transactions doivent maintenir les soldes, les deux jambes des transferts et les ventilations équilibrées. Un montant ou une relation indéchiffrable à l'import ne devient pas zéro ou une relation déduite par ressemblance.

## FMT-04 — Un seul fichier logique ne signifie pas un seul fichier actif

SQLite peut créer des fichiers de travail adjacents dont le nom **ajoute** un suffixe au nom complet [S01, S03, S04] :

| Mode / rôle | Exemple exact |
| --- | --- |
| Base principale | `Mes comptes.moneta` |
| Journal WAL | `Mes comptes.moneta-wal` |
| Index/mémoire partagée WAL | `Mes comptes.moneta-shm` |
| Journal de retour arrière, selon mode | `Mes comptes.moneta-journal` |

Le WAL peut contenir des transactions validées encore absentes du fichier principal. `-shm` est un index de coordination, pas une sauvegarde de remplacement. Un journal de retour arrière « chaud » peut être indispensable à la récupération. D'autres temporaires SQLite restent possibles : ne pas promettre une liste exhaustive de fichiers physiques ni une suppression systématique à la fermeture.

**Politique réalisée :** disque local, WAL, `synchronous=FULL`, `foreign_keys=ON`, `trusted_schema=OFF`, extensions désactivées, pool désactivé et attente de verrou bornée à trois secondes ; valeurs critiques vérifiées. Bundle explicite `SQLitePCLRaw.bundle_e_sqlite3` **3.0.5**, moteur **3.53.4 observé à l'exécution** ; minimum corrigé **3.51.3** exigé. Voir [package fournisseur](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12), [bundle](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/3.0.5) et défaut WAL-reset [S03].

### Checkpoint, concurrence et durabilité

- Commit réussi et checkpoint réussi ne sont pas synonymes : un commit peut être durable dans le WAL sans être déjà recopié dans le principal.
- Un lecteur long peut bloquer l'avancement d'un checkpoint. SQLite peut retourner `SQLITE_BUSY` même en WAL ; lire le résultat et les compteurs du checkpoint, ne pas supposer qu'un appel ou un seuil automatique a vidé le journal.
- Prévoir attente bornée/annulation et message visible en cas de verrouillage. Un checkpoint incomplet ne justifie jamais de supprimer le WAL. Tant que l'état autonome n'est pas établi, ne pas annoncer « prêt à copier comme fichier unique ».
- `synchronous=FULL` demande les synchronisations nécessaires aux commits WAL ; ce n'est pas une garantie contre un matériel qui ment sur les écritures, un système de fichiers défectueux, toute coupure ou toute erreur d'E/S [S03, S04]. Ne pas dégrader silencieusement vers `NORMAL`, `OFF` ou `journal_mode=OFF`.
- Disque plein, droits perdus, déconnexion du support, échec de commit/checkpoint/fermeture : échec explicite ; aucune réussite inventée, aucun nettoyage des fichiers de reprise. L'application ne supprime ni ne renomme manuellement un journal chaud.

Un fichier adjacent `.moneta.lock` porte le verrou exclusif de session Moneta. Son existence n'indique pas une session active : le handle système est libéré après fermeture/crash, le fichier vide reste volontairement présent. Ne pas le confondre avec un journal SQLite. Un échec de checkpoint conserve connexion et verrou pour permettre la reprise ; pas d'annonce de fermeture autonome prématurée. Une écriture externe détectée au prochain Save est refusée avant toute modification.

Audit 6.1 : le verrou est coopératif et nommé par chemin, pas par identité physique du fichier ; alias/liens et clients externes non coopératifs ne sont pas exclus par ce mécanisme seul. La comparaison transactionnelle protège contre les écritures SQLite ordinaires détectables, pas contre toute altération historique ou filesystem. Voir [l'audit](AUDIT_MISSION_6_1.md).

### Réseau, synchronisation et transport

**Non pris en charge pour une base active :** partage SMB/NAS, lecteur réseau, accès depuis plusieurs machines et dossier synchronisé (OneDrive, Dropbox ou analogue). Une synchronisation de fichiers n'est pas une transaction SQLite ; le WAL exige une coordination locale. L'usage local-first ne signifie pas « cloud sans conflit ».

Un `.moneta` est transportable comme **fichier unique seulement** :

1. après fermeture propre de **toutes** les connexions, arrêt des écritures et confirmation de l'état autonome cohérent, sans journal nécessaire à une reprise ; ou
2. comme destination cohérente, fermée et validée d'une API de sauvegarde SQLite (FMT-05).

Copier le seul principal en cours d'utilisation est incorrect. **Copier successivement le principal et le WAL, même tous deux présents à l'arrivée, n'est pas un snapshot sûr** : leurs états peuvent appartenir à des instants différents. Le hash de chaque copie ne corrige pas cette incohérence. Ne pas conseiller une copie manuelle « base + WAL » comme sauvegarde à chaud. Une copie de transport fermée peut ensuite être synchronisée comme fichier inactif, sans ouverture concurrente et après contrôle à destination ; ce n'est pas une fonction de synchronisation Moneta.

Après crash, conserver principal et journaux à leurs noms et emplacements cohérents ; aucune suppression de `-wal`, `-shm` ou `-journal` pour « débloquer ». Laisser le moteur SQLite gérer la reprise autorisée, sur copie préservée si l'état est suspect. Diagnostic et sauvegarde sont des opérations distinctes.

## FMT-05 — Créer une sauvegarde vérifiable

**Procédure manuelle réalisée :**

1. Enregistrer d'abord les modifications en attente. La sauvegarde représente les données committées, pas un brouillon.
2. Comparer le snapshot vivant au dernier snapshot enregistré ; utiliser `SqliteConnection.BackupDatabase` vers un temporaire exclusif voisin de la destination.
3. Vérifier identité, version, schéma exact, `integrity_check`, `foreign_key_check` et empreinte canonique des métadonnées/référentiels et de **chaque opération, virement/lien, session, instantané et événement**, frais, libérations héritées et tombstones compris. Calcul en streaming, pas les seuls soldes. Consolider le WAL de la copie puis passer la copie autonome en journal `DELETE`.
4. Fermer et calculer SHA-256 et la longueur. Le manifeste JSON version 1 certifie le schéma, l’UUID du dossier, le fichier et son contenu complet. L’empreinte v4 couvre le pointage, les sessions, les événements et les instantanés. L’empreinte v5 ajoute le graphe de virements. L’empreinte v6 ajoute les trois champs bancaires. Le manifeste stocke les empreintes et garde les champs financiers et descriptifs dans le dossier. Les archives anciennes utilisent les algorithmes 1 à 5 d’origine.
5. Publier la base sous un nom neuf, puis son `<nom>.moneta.manifest.json` **en dernier**. Le couple n'est pas une transaction multifichier : sans manifeste final ou en cas de divergence, la copie n'est pas certifiée. Une publication interrompue ne supprime aucune ancienne sauvegarde.
6. Conserver les deux fichiers ensemble, à l'écart du dossier actif, et tester une restauration. L'empreinte n'est ni chiffrement ni signature d'authenticité. Ne pas ouvrir/enregistrer directement l'archive comme dossier de travail : son hash pourrait changer ; restaurer une nouvelle copie.

La bibliothèque de mission 13 utilise le même manifeste de sauvegarde. Le descripteur `version.json` ajoute à la génération son UUID, sa date UTC, son type, le schéma et l’empreinte de l’archive. La génération est publiée par renommage du répertoire préparé. Le registre financier conserve le schéma 6. [Publication et rotation](AUTOMATIC_BACKUPS.md#publication-et-rotation)

## FMT-06 — Restaurer réellement, pas seulement vérifier un hash

1. Choisir la sauvegarde **et conserver son manifeste adjacent**. Manifeste absent/incomplet/inconnu, hash/taille divergents, identité/version/schéma invalides ou corruption : refus.
2. Restaurer par API SQLite vers un temporaire local distinct ; aucun fichier principal ou compagnon de destination existant n'est remplacé.
3. Vérifier le contenu complet contre le digest certifié, puis publier sous un nouveau nom, rouvrir avec `MonetaFile.Open` et comparer à nouveau. Un hash ou `integrity_check=ok` isolé ne suffit pas.
4. Le dialogue annonce uniquement une **restauration sur nouvelle copie rouverte et comparée**. Le dossier actuellement affiché, ses données et brouillons restent inchangés. Utiliser ensuite Fichier → Ouvrir pour une bascule volontaire.

Restauration exercée historiquement en 7B et 7C avec comptes EUR/BYN/RUB, opérations, frais, virements liés et relevés avec instantanés et événements. Le contrôle applicatif compare les relations et l’ensemble du registre. Les archives 1 à 5 sont vérifiées selon leur schéma d’origine avant migration de la copie de travail.

Audit 6.1 : un source bloqué par partage/verrou Windows (codes 32/33) renvoie maintenant `DocumentError.Locked` avec un message explicite, sans essai de contournement. Tests supplémentaires : écriture WAL non committée, lecteur WAL épinglé, sources bloqués et readonly pour restauration/migration. Lecture seule SQLite ne garantit pas l'absence de fichiers de coordination adjacents.

## FMT-07 — Migration du schéma, sans rétrogradation

Les transitions connues vont de **1 → 2 → 3 → 4 → 5 → 6**. Elles ajoutent successivement le drapeau archivé, le registre et ses référentiels, le pointage et les rapprochements, les virements, puis les descriptions bancaires. L’ordre stocké des comptes précédents est repris à la migration.

L’ouverture normale d’une version **1 à 5** demande une migration sur une nouvelle copie consentie. Moneta lit la source en lecture seule, compare la représentation d’origine, applique les transitions à la copie et la rouvre pour vérification. Le contrôle couvre les UUID, devises, soldes, opérations, états, révisions, virements, rapprochements et la provenance disponibles. Les anciennes R gardent leur statut hérité et les dates inconnues restent nulles. Le certificat d’origine reste valide pour sa version.

Version future, définition inconnue ou schéma altéré : refus, jamais de downgrade. Pour les futures versions publiées, étendre ce catalogue et les essais d'interruption à chaque frontière, avec sauvegarde cohérente/restauration préalable qualifiée. Aucun mécanisme générique de « réparation » ou migration automatique sur l'unique original.

## FMT-08 — Association Windows consentie, ultérieure

Aucune modification du registre, association ou installation n'est réalisée pendant cette mission. Contrat d'intégration future limité à **`.moneta`**, avec consentement ; **ne pas associer `.bp`, `.bp0` ou les fichiers auxiliaires BP à Moneta**, ni altérer leur programme par défaut.

- Enregistrement **par utilisateur** sous `HKEY_CURRENT_USER\Software\Classes`, pas écriture globale dans `HKEY_CLASSES_ROOT` (vue fusionnée) ou élévation administrative implicite [S10].
- ProgID proposé : `Moneta.Document.1` ; ce suffixe décrit le contrat d'association, pas une prétendue version de base implémentée. Inscrire ce ProgID comme candidat dans `.moneta\OpenWithProgids`. L'entrée doit rendre Moneta disponible dans « Ouvrir avec » ; elle ne vaut pas choix par défaut.
- Verbe `shell\open\command` : chemin absolu de l'exécutable **et argument fichier tous deux entre guillemets**, forme documentaire `"C:\Chemin choisi\Moneta.exe" "%1"` [S11]. Aucun passage par `cmd /c`, script ou concaténation interprétée. Le futur parseur traite l'argument comme chemin, y compris espaces, accents et noms ressemblant à des options.
- Le changement de programme par défaut relève de l'interface Windows et du choix utilisateur. **Ne pas écraser `UserChoice`, modifier son hash, forcer le défaut ou rétablir Moneta après un choix contraire.** La documentation « Default Programs » avertit que ses anciennes API de changement ne s'appliquent pas à Windows 10 ; ce n'est pas une recette à réutiliser aveuglément [S12].
- Désinstallation : retirer uniquement les entrées possédées par Moneta ; ne supprimer aucun document ni préférence d'un autre programme. Aucune icône/logo BP ni pack graphique réutilisé ; l'identité visuelle Moneta reste originale.

## FMT-09 — Critères de validation et limites des preuves

Les tests d'intégration et scénarios Windows de mission 6 couvrent le socle décrit ci-dessus ; résultats et limites dans le README. La matrice reste le contrat élargi : disque plein forcé, coupure électrique matérielle, toutes les frontières de migration et association Windows ne sont **pas** déclarés qualifiés.

| ID | Exercice sur données entièrement synthétiques | Résultat exigé |
| --- | --- | --- |
| FMT-V01 | Fichier absent/vide/tronqué, ZIP, faux suffixe, SQLite d'une autre application | Refus sans création ni écriture ; identification explicite |
| FMT-V02 | Versions connues anciennes, future, métadonnées incohérentes, schéma altéré | Migration uniquement connue ; refus futur/incohérent sans downgrade |
| FMT-V03 | Centimes, négatifs, échelles différentes, représentations décimales exactes, débordement | Relecture exacte et dépassement visible ; aucun flottant de repli |
| FMT-V04 | Interruption avant/durant/après commit, coupure simulée, disque plein | Reprise moteur sans effacer de journal ; aucune réussite mensongère |
| FMT-V05 | Lecteur long, checkpoint `BUSY`, deux ouvertures, fermeture interrompue | État cohérent préservé ; pas de déclaration « fichier autonome » prématurée |
| FMT-V06 | Sauvegarde pendant écritures contrôlées ; panne avant publication | Snapshot cohérent comparé ; dernière copie valide préservée |
| FMT-V07 | Restauration effective sur nouvelle copie puis réouverture | Structure, clés et données financières réconciliées |
| FMT-V08 | Migration copie avant/après et interruption à chaque frontière | Original préservé, différences attendues seules, version cible atomiquement cohérente |
| FMT-V09 | Copie fermée vers autre volume ; sauvegarde sans manifeste ; hash divergent | Aucun certificat de réussite sans contrôles ; publication non inter-volume présumée |
| FMT-V10 | Association optionnelle avec espaces/accents et défaut concurrent | Commande correctement citée, consentement respecté, `.bp` et `UserChoice` intacts |

## FMT-10 — Cache public de cours et préférences — mission 8

### Trois formats indépendants

| Fichier | Version / identité | Contenu et portée |
|---|---|---|
| Dossier `.moneta` | Schéma **5**, `MNTA` / `0x4D4E5441` | Vérité financière, inchangée par la mission 8 ; sauvegarde/manifeste et migrations FMT-01 à FMT-07 inchangés |
| `%LocalAppData%\Moneta\exchange-rates.sqlite` | Schéma **1**, `user_version=1`, `application_id=0x4D465831` (`MFX1`) | Réponses officielles publiques, partagées entre dossiers locaux ; ni donnée de compte ni migration du dossier financier |
| `%LocalAppData%\Moneta\settings.json` | Schéma **2**, lecture compatible du schéma **1** complet | Thème, booléens de démarrage et `RateSource` (`NBRB`/`CBR`) ; aucun montant/cours/secret |

Un ancien JSON 1 conserve ses choix et prend NBRB par défaut. Le prochain enregistrement explicite écrit 2 ; l’ouverture seule ne réécrit pas le fichier. Champ obligatoire manquant, source inconnue, corruption ou version inconnue : avertissement, fichier conservé, écriture bloquée selon `SettingsStore`. Une nouvelle préférence source ne constitue pas un téléchargement ; l’option automatique est désactivée par défaut et ne prend effet qu’au prochain démarrage.

### Lots bruts, immuables et vérifiables

`SqliteRateCache` emploie une seule table **`RateSets STRICT`** et l’index `RateSets_ProviderDate(provider,effective_date,retrieved_utc)` :

| Colonne | Représentation |
|---|---|
| `id` | UUID canonique `TEXT`, clé primaire |
| `provider` | `INTEGER`, 0 NBRB / 1 CBR |
| `requested_date`, `effective_date` | `TEXT` ISO `YYYY-MM-DD`, requête réelle et date retournée distinctes |
| `retrieved_utc` | `TEXT` UTC canonique au format rond-trip `O` |
| `endpoint` | `TEXT`, URL HTTPS fixe du fournisseur avec sa date de requête |
| `sha256` | `TEXT`, 64 caractères hexadécimaux, empreinte des octets originaux |
| `payload` | `BLOB`, réponse d’origine non modifiée, de 1 à 1 048 576 octets |

Les valeurs numériques ne sont pas dupliquées dans une seconde table de cotations. Le lot structuré **`OfficialRateSet`** est reconstruit en reparsant le JSON NBRB ou le XML CBR original ; valeurs et nominaux restent exacts, sans `REAL`. Les réponses conservent notamment l’encodage Windows-1251 CBR. Une correction ajoute un lot ; elle n’écrase ni le précédent ni une écriture financière. Réutiliser le même UUID avec un contenu différent est refusé. Il n’y a aucune purge automatique.

La recherche ne mélange pas les fournisseurs : date d’effet la plus récente dans **[date de valorisation −31 jours, date de valorisation]**, puis réception la plus récente. Une publication anticipée peut être stockée mais reste exclue des valorisations antérieures à sa date d’effet. L’historique est paginable par l’API (100 lots au maximum par page) ; l’interface présente les 100 plus récents, les lots antérieurs restent recherchables par date. La réception UTC ne prétend pas être l’heure de publication bancaire.

Les références actuelles ont une borne de validité : **NBRB/BYN à partir du 01/07/2016**, **CBR/RUB à partir du 01/01/1998**, sur la **date d’effet réellement retournée**. Un lot antérieur est refusé par le validateur commun au parsing, au stockage et aux conversions ; il n’est pas réétiqueté, divisé ou remplacé automatiquement. La lecture de l’historique n’est pas une certification de monnaies anciennes ; voir les constats réseau et sources de [l’architecture](ARCHITECTURE.md#54-banque-centrale-de-russie--étude-complémentaire-mission-2).

### Intégrité, erreurs et frontières de persistance

Connexions courtes, sans pool, extensions désactivées, `trusted_schema=OFF`, journal **DELETE** et `synchronous=FULL` contrôlés ; ce n’est pas le mode WAL du dossier financier. Une création publie uniquement une base temporaire complète et committée sous un nom encore absent. Chaque opération contrôle identité/version, schéma exact et intégrité ; chaque lot retourné est reparsé et ses métadonnées/empreinte comparées aux octets. Réponse limitée à 1 Mio, 1..1 000 cotations uniques, valeurs décimales strictement positives et nominaux bornés ; DTD et résolution externe XML interdites.

Cache absent = aucun cours disponible, pas création à la simple lecture. Cache corrompu, altéré, d’une autre application, de version inconnue, verrouillé ou inaccessible = diagnostic explicite ; **aucune suppression, réparation, réinitialisation ou migration financière implicite**. Le hash détecte une divergence locale, mais n’est ni une signature du fournisseur ni une authentification contre un attaquant capable de remplacer contenu et hash.

Après une réponse réseau valide, un échec d’écriture du cache n’invalide pas la valeur reçue : la session peut la conserver **en mémoire non persistante**, avec avertissement et `IsPersistent=false`. Une requête ultérieure hors ligne pour le même fournisseur/date conserve ce lot applicable ; aucun succès disque ni disponibilité au redémarrage n’est annoncé. `FromCache` décrit la réutilisation d’un lot de session ; seule la confirmation de persistance distingue disque et mémoire.

L’actualisation et son cache sont hors transaction financière : ils ne changent ni `IsDirty`, ni registre de devises financier, ni `.moneta`/WAL, ni instantanés V1, digests, rapports effectifs et protections NP/P/R des virements. Les sauvegardes financières n’embarquent pas le cache public ou les préférences locales. Calculs, limites temporelles et politiques réseau : [ARCHITECTURE §5](ARCHITECTURE.md#5-devises-officielles-historique-et-fonctionnement-hors-ligne) et [CURRENCIES_AND_SETTINGS](CURRENCIES_AND_SETTINGS.md).

## Sources officielles consultées le 28 octobre 2026

| ID | Source | Apport / limite |
| --- | --- | --- |
| S01 | [SQLite — Database File Format](https://www.sqlite.org/fileformat.html) | Signature, en-tête, journaux chauds ; pas un schéma Moneta |
| S02 | [SQLite — PRAGMA](https://www.sqlite.org/pragma.html), sections [application_id](https://www.sqlite.org/pragma.html#pragma_application_id), [user_version](https://www.sqlite.org/pragma.html#pragma_user_version), [integrity_check](https://www.sqlite.org/pragma.html#pragma_integrity_check), [foreign_key_check](https://www.sqlite.org/pragma.html#pragma_foreign_key_check), [wal_checkpoint](https://www.sqlite.org/pragma.html#pragma_wal_checkpoint) | Identité/version et contrôles distincts ; les PRAGMA inconnus peuvent être ignorés |
| S03 | [SQLite — Write-Ahead Logging](https://www.sqlite.org/wal.html) | Fichiers latéraux, checkpoints, `BUSY`, réseau, durabilité et correctif WAL-reset (section 11) |
| S04 | [SQLite — Atomic Commit](https://www.sqlite.org/atomiccommit.html) | Hypothèses matérielles et rôle de la récupération ; pas garantie absolue du poste |
| S05 | [SQLite — Online Backup API](https://www.sqlite.org/backup.html) | Copie cohérente utilisée par Moneta via `BackupDatabase` ; essais dans le README |
| S07 | [SQLite — Security](https://www.sqlite.org/security.html) | Limites et schéma non fiable ; aucune base étrangère exécutée |
| S08 | [SQLite — Datatypes](https://www.sqlite.org/datatype3.html) | Référence reliée à l'étude d'architecture : affinités et flottants, pas type financier décimal automatique |
| S09 | [Microsoft — SQLite types](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types) | Différence `decimal`/`TEXT` ; Moneta lie explicitement les soldes en `INTEGER` |
| S10 | [Microsoft — File Types](https://learn.microsoft.com/en-us/windows/win32/shell/fa-file-types) | ProgID, `OpenWithProgids`, enregistrement par utilisateur |
| S11 | [Microsoft — Launching Applications](https://learn.microsoft.com/en-us/windows/win32/shell/launch) | Commande du verbe d'ouverture, chemins cités |
| S12 | [Microsoft — Default Programs](https://learn.microsoft.com/en-us/windows/win32/shell/default-programs) et [changement Windows 10](https://blogs.windows.com/windows-insider/2015/05/20/announcing-windows-10-insider-preview-build-10122-for-pcs/) | Limite des anciennes API et contrôle utilisateur du défaut ; vérifier l'intégration sur les Windows supportés lors de sa réalisation |

Les règles du format natif et celles de lecture BankPerfect sont indépendantes : voir [BANKPERFECT_IMPORT_STRATEGY.md](BANKPERFECT_IMPORT_STRATEGY.md). Le présent document ne transforme aucune inconnue `.bp` en connaissance SQLite.
