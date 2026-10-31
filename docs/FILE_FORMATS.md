# Formats de fichiers Moneta

**État mission 7A : schéma courant 3, dossiers, devises, comptes, référentiels et opérations persistants, sauvegarde/restauration complètes.** Recettes/dépenses, modification et suppression logique utilisent le cycle manuel Enregistrer ; pas encore de virements, ventilations, pointage bancaire, automatisation ou lecture `.bp`. Voir [README](../README.md) et [ARCHITECTURE](ARCHITECTURE.md).

Les identifiants `FMT-*` sont stables pour les renvois de la feuille de route. « Doit » exprime une exigence Moneta ; une propriété documentée de SQLite n'est pas une garantie d'implémentation Moneta.

## FMT-01 — Format natif retenu et frontières

- **Un dossier de comptes = une base SQLite directe portant l'extension `.moneta`**, par exemple `Mes comptes.moneta`. Plusieurs comptes peuvent appartenir à cette base ; plusieurs dossiers indépendants peuvent exister.
- Ce n'est **ni une archive ZIP**, ni un conteneur à décompresser, ni un `.bp` renommé, ni une encapsulation de BankPerfect. Aucune installation, API ou exécution BankPerfect n'est nécessaire au format natif Moneta.
- Le fichier commence par les 16 octets de signature SQLite `SQLite format 3\0` (zéro terminal). Cette signature identifie SQLite, **pas Moneta**. L'extension seule n'identifie aucun format et une base SQLite quelconque renommée `.moneta` doit être refusée comme dossier Moneta.
- L'identification doit combiner signature, `PRAGMA application_id`, métadonnées Moneta, version et schéma reconnu. `application_id` est le champ applicatif 32 bits de l'en-tête SQLite ; `user_version` est une convention de l'application. Ne pas utiliser `schema_version`, que SQLite gère pour son propre schéma, comme numéro de migration Moneta [S01, S02].
- Contrat livré : marqueur `Moneta`, `application_id=0x4D4E5441` (`MNTA`), `user_version=3` et métadonnées concordantes ; UUID stable du dossier, nom, versions minimales lecteur/écrivain, créateur historique `Moneta-6`, horodatages UTC et compte sélectionné facultatif. L’identifiant applicatif n’a pas de réservation officielle revendiquée.
- Huit tables `STRICT`, sans montants financiers `REAL` :

| Table | Champs / relations réalisés |
|---|---|
| `Document` | Une ligne singleton ; identité, versions, nom, création/modification UTC, sélection |
| `Currencies` | Code unique, nom, symbole, précision, culture/règles d'affichage et ordre ; EUR/BYN/RUB proposés à la création, registre extensible |
| `Accounts` | UUID unique, nom, type, devise native FK vers `Currencies`, `opening_minor INTEGER`, notes, archive, création/modification UTC et ordre |
| `MigrationHistory` | Version source/cible, identifiant/définition de migration et date UTC ; seules les définitions reconnues sont acceptées |
| `Categories` | UUID, nom, clé de correspondance, parent facultatif ; racines et un niveau de sous-catégories, unicité par parent |
| `Payees` | UUID, nom et clé unique de correspondance |
| `PaymentMethods` | UUID, nom et clé unique de correspondance |
| `Transactions` | UUID, compte/devise native cohérents, date ISO, magnitude `amount_minor INTEGER` non négative, sens, libellé/notes, références facultatives, état de pointage, création/modification UTC, révision et suppression logique |

Les opérations référencent les comptes, tiers, modes et catégories par UUID. La sous-catégorie doit appartenir à la racine choisie ; pas de références orphelines ni de suppression cascade des opérations. Un index compte/date/création/UUID soutient l’ordre stable du registre ; les index et le schéma exact sont contrôlés à l’ouverture. Virements, ventilations, échéanciers et provenance BP seront des extensions normalisées versionnées, sans colonnes fictivement utilisées aujourd’hui.
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

Le solde initial et les opérations sont réalisés ; les taux/prix/quantités ci-dessous restent des contrats futurs.

| Donnée | Contrat de stockage | Validation requise |
| --- | --- | --- |
| Montant comptabilisé et solde initial | `INTEGER` 64 bits en unités mineures, devise et échelle explicites ; pour une opération, magnitude non négative avec sens débit/crédit séparé | Pas de supposition « toute devise a deux décimales ». À deux décimales, 12,34 EUR = 1234 unités ; solde négatif et sens restent distincts. Débordement ou fraction sous l'unité rejetés explicitement, jamais tronqués |
| Taux, quantité de titres, prix unitaire | `TEXT` décimal canonique invariant, point décimal, sans séparateur de milliers ; précision, unité et provenance explicites | Ex. taux fictif `3.2500`, quantité `2.5`. Préserver la précision source nécessaire ; ne pas passer par `double` ou `REAL` |
| Dates métier | Texte ISO `YYYY-MM-DD`, sans fuseau | Dates d'opération, pointage, échéance et relevé distinctes ; inconnue reste inconnue |
| Horodatages techniques | UTC avec représentation non ambiguë | Création/import/sauvegarde ne remplacent jamais les dates métier |
| Texte utilisateur | Unicode, valeur et provenance conservées | Aucune translittération ou normalisation destructive silencieuse ; ne pas exécuter les champs de règles historiques |

SQLite `REAL` est binaire flottant ; `DECIMAL(10,2)` ne garantit ni échelle ni exactitude. `Microsoft.Data.Sqlite` **10.0.12** est utilisé avec des paramètres entiers explicites pour les soldes, pas sa conversion automatique de `decimal`. `ExactMoney` rejette toute fraction sous l'unité et tout dépassement de `±long.MaxValue` unités mineures ; **aucun arrondi à la saisie**. Les taux/prix futurs resteront distincts, exacts et sourcés. Pas de `TOTAL`, `AVG`, cast `REAL` ou somme implicite de valeurs `TEXT` pour une vérité financière.

Pour une opération, la magnitude doit être entre zéro et `long.MaxValue` unités mineures ; `Direction=Debit` soustrait, `Credit` ajoute. Le solde initial reste signé. Les agrégats sont calculés en `decimal` contrôlé sans être forcés dans `Int64`. Date, énumérations, devise native, précision, UUID, hiérarchie, projections des noms et horodatages/révisions sont validés. L’interface n’accepte ni signe négatif ni arrondi silencieux ; les zéros fractionnaires supplémentaires exacts n’altèrent pas le montant.

Insertion avec révision attendue 0 ; modification/suppression avec révision attendue égale à la version enregistrée. Compte, devise, UUID et création immuables. Plusieurs modifications en brouillon conservent la base de révision jusqu’au commit. La suppression marque `is_deleted` et incrémente la révision ; aucune purge physique, corbeille UI ou version précédente complète n’est revendiquée. Les trois états de pointage sont conservables, mais la saisie crée seulement non pointé et le rapprochement bancaire reste hors mission 7A.


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
3. Vérifier identité, version, schéma exact, `integrity_check`, `foreign_key_check` et empreinte canonique de tous les champs dossier/devises/comptes, des référentiels et de **chaque opération**, y compris références, dates, notes, révisions et lignes supprimées. L’empreinte du registre est calculée en streaming, pas à partir des seuls soldes finaux. Consolider le WAL de la copie puis passer la copie autonome en journal `DELETE`.
4. Fermer et calculer SHA-256/longueur. Le manifeste JSON version 1 contient version de manifeste/schéma, UUID du dossier, empreinte du fichier et du snapshot complet, nombres de comptes/devises. Le digest de schéma 3 intègre aussi empreinte du registre et nombre d’opérations, tombstones compris. Aucun nom, note ou solde en clair dans le manifeste. L’algorithme ancien 1/2 reste inchangé pour vérifier les archives antérieures.
5. Publier la base sous un nom neuf, puis son `<nom>.moneta.manifest.json` **en dernier**. Le couple n'est pas une transaction multifichier : sans manifeste final ou en cas de divergence, la copie n'est pas certifiée. Une publication interrompue ne supprime aucune ancienne sauvegarde.
6. Conserver les deux fichiers ensemble, à l'écart du dossier actif, et tester une restauration. L'empreinte n'est ni chiffrement ni signature d'authenticité. Ne pas ouvrir/enregistrer directement l'archive comme dossier de travail : son hash pourrait changer ; restaurer une nouvelle copie.

La date UTC propre à la sauvegarde, un identifiant de génération distinct du hash, la planification, la rétention et les copies externes automatisées restent à réaliser avant automatisation. Aucun déclenchement automatique ni rotation n’est annoncé.

## FMT-06 — Restaurer réellement, pas seulement vérifier un hash

1. Choisir la sauvegarde **et conserver son manifeste adjacent**. Manifeste absent/incomplet/inconnu, hash/taille divergents, identité/version/schéma invalides ou corruption : refus.
2. Restaurer par API SQLite vers un temporaire local distinct ; aucun fichier principal ou compagnon de destination existant n'est remplacé.
3. Vérifier le contenu complet contre le digest certifié, puis publier sous un nouveau nom, rouvrir avec `MonetaFile.Open` et comparer à nouveau. Un hash ou `integrity_check=ok` isolé ne suffit pas.
4. Le dialogue annonce uniquement une **restauration sur nouvelle copie rouverte et comparée**. Le dossier actuellement affiché, ses données et brouillons restent inchangés. Utiliser ensuite Fichier → Ouvrir pour une bascule volontaire.

Restauration exercée avec comptes EUR/BYN/RUB, recettes/dépenses, catégories/sous-catégories, tiers, modes, notes, révisions et suppressions logiques. Une différence d’opérations conservant le même solde final est détectée. Les anciennes archives certifiées sont vérifiées selon leur schéma original, puis seule la copie de travail est migrée avant publication et réouverture.

Audit 6.1 : un source bloqué par partage/verrou Windows (codes 32/33) renvoie maintenant `DocumentError.Locked` avec un message explicite, sans essai de contournement. Tests supplémentaires : écriture WAL non committée, lecteur WAL épinglé, sources bloqués et readonly pour restauration/migration. Lecture seule SQLite ne garantit pas l'absence de fichiers de coordination adjacents.

## FMT-07 — Migration du schéma, sans rétrogradation

Schéma courant **3**. Transitions connues : **1 → 2**, brouillon interne synthétique ajoutant le drapeau archivé ; puis **2 → 3**, ajout transactionnel des référentiels, opérations et index, sans recréer les tables de comptes/devises existantes.

L’ouverture normale d’une version 1 ou 2 demande une migration. L’utilisateur consent à créer une copie à un nouveau chemin. Copie SQLite du source en lecture seule, comparaison de son contenu original, transition(s) connue(s), contrôle des comptes/devises/soldes initiaux/notes/UUID/dates/sélection, historique/version transactionnels puis réouverture comparée. Le hash du principal source reste inchangé ; aucun original ni journal n’est supprimé. Un ancien dossier ne reçoit aucune opération ou référence inventée.

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
| FMT-V03 | Centimes, négatifs, échelles différentes, taux `TEXT`, débordement | Relecture exacte et dépassement visible ; aucun flottant de repli |
| FMT-V04 | Interruption avant/durant/après commit, coupure simulée, disque plein | Reprise moteur sans effacer de journal ; aucune réussite mensongère |
| FMT-V05 | Lecteur long, checkpoint `BUSY`, deux ouvertures, fermeture interrompue | État cohérent préservé ; pas de déclaration « fichier autonome » prématurée |
| FMT-V06 | Sauvegarde pendant écritures contrôlées ; panne avant publication | Snapshot cohérent comparé ; dernière copie valide préservée |
| FMT-V07 | Restauration effective sur nouvelle copie puis réouverture | Structure, clés et données financières réconciliées |
| FMT-V08 | Migration copie avant/après et interruption à chaque frontière | Original préservé, différences attendues seules, version cible atomiquement cohérente |
| FMT-V09 | Copie fermée vers autre volume ; sauvegarde sans manifeste ; hash divergent | Aucun certificat de réussite sans contrôles ; publication non inter-volume présumée |
| FMT-V10 | Association optionnelle avec espaces/accents et défaut concurrent | Commande correctement citée, consentement respecté, `.bp` et `UserChoice` intacts |

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
