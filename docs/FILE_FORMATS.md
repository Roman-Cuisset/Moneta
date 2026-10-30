# Formats de fichiers Moneta

**État mission 6 : format natif SQLite et sauvegarde/restauration manuelles implémentés pour les dossiers, devises et comptes.** Schéma courant 2 ; opérations uniquement dans la démonstration en mémoire. Les identifiants FMT restent stables. Les exigences d'automatisation, d'association Windows et de qualification non exercées restent explicitement futures ; voir [README](../README.md), [ARCHITECTURE](ARCHITECTURE.md) et [stratégie BP](BANKPERFECT_IMPORT_STRATEGY.md).

Les identifiants `FMT-*` sont stables pour les renvois de la feuille de route. « Doit » exprime une exigence Moneta ; une propriété documentée de SQLite n'est pas une garantie d'implémentation Moneta.

## FMT-01 — Format natif retenu et frontières

- **Un dossier de comptes = une base SQLite directe portant l'extension `.moneta`**, par exemple `Mes comptes.moneta`. Plusieurs comptes peuvent appartenir à cette base ; plusieurs dossiers indépendants peuvent exister.
- Ce n'est **ni une archive ZIP**, ni un conteneur à décompresser, ni un `.bp` renommé, ni une encapsulation de BankPerfect. Aucune installation, API ou exécution BankPerfect n'est nécessaire au format natif Moneta.
- Le fichier commence par les 16 octets de signature SQLite `SQLite format 3\0` (zéro terminal). Cette signature identifie SQLite, **pas Moneta**. L'extension seule n'identifie aucun format et une base SQLite quelconque renommée `.moneta` doit être refusée comme dossier Moneta.
- L'identification doit combiner signature, `PRAGMA application_id`, métadonnées Moneta, version et schéma reconnu. `application_id` est le champ applicatif 32 bits de l'en-tête SQLite ; `user_version` est une convention de l'application. Ne pas utiliser `schema_version`, que SQLite gère pour son propre schéma, comme numéro de migration Moneta [S01, S02].
- Contrat livré : marqueur `Moneta`, `application_id=0x4D4E5441` (`MNTA`), `user_version=2` et métadonnées concordantes ; UUID stable du dossier, nom, versions minimales lecteur/écrivain, application créatrice, horodatages UTC et compte sélectionné facultatif. Cet identifiant n'avait pas de collision dans la liste SQLite consultée ; aucune réservation officielle n'est revendiquée.
- Quatre tables `STRICT`, sans colonnes monétaires `REAL` :

| Table | Champs / relations réalisés |
|---|---|
| `Document` | Une ligne singleton ; identité, versions, nom, création/modification UTC, sélection |
| `Currencies` | Code unique, nom, symbole, précision, culture/règles d'affichage et ordre ; EUR/BYN/RUB proposés à la création, registre extensible |
| `Accounts` | UUID unique, nom, type, devise native FK vers `Currencies`, `opening_minor INTEGER`, notes, archive, création/modification UTC et ordre |
| `MigrationHistory` | Version source/cible, identifiant/définition de migration et date UTC ; seules les définitions reconnues sont acceptées |

Les opérations, tiers, catégories, relations de virements et ventilations viendront par migrations normalisées, pas par champs inutilisés. Leur absence du fichier actuel est intentionnelle et annoncée ; les données fictives ne sont jamais injectées dans un nouveau dossier.
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

Le solde initial et les horodatages techniques sont réalisés. Les autres représentations ci-dessous sont des contrats pour les futurs domaines persistants, pas des tables existantes.

| Donnée | Contrat de stockage | Validation requise |
| --- | --- | --- |
| Montant comptabilisé et solde initial | `INTEGER` 64 bits en unités mineures, devise et échelle explicites ; pour une opération, magnitude non négative avec sens débit/crédit séparé | Pas de supposition « toute devise a deux décimales ». À deux décimales, 12,34 EUR = 1234 unités ; solde négatif et sens restent distincts. Débordement ou fraction sous l'unité rejetés explicitement, jamais tronqués |
| Taux, quantité de titres, prix unitaire | `TEXT` décimal canonique invariant, point décimal, sans séparateur de milliers ; précision, unité et provenance explicites | Ex. taux fictif `3.2500`, quantité `2.5`. Préserver la précision source nécessaire ; ne pas passer par `double` ou `REAL` |
| Dates métier | Texte ISO `YYYY-MM-DD`, sans fuseau | Dates d'opération, pointage, échéance et relevé distinctes ; inconnue reste inconnue |
| Horodatages techniques | UTC avec représentation non ambiguë | Création/import/sauvegarde ne remplacent jamais les dates métier |
| Texte utilisateur | Unicode, valeur et provenance conservées | Aucune translittération ou normalisation destructive silencieuse ; ne pas exécuter les champs de règles historiques |

SQLite `REAL` est binaire flottant ; `DECIMAL(10,2)` ne garantit ni échelle ni exactitude. `Microsoft.Data.Sqlite` **10.0.12** est utilisé avec des paramètres entiers explicites pour les soldes, pas sa conversion automatique de `decimal`. `ExactMoney` rejette toute fraction sous l'unité et tout dépassement de `±long.MaxValue` unités mineures ; **aucun arrondi à la saisie**. Les taux/prix futurs resteront distincts, exacts et sourcés. Pas de `TOTAL`, `AVG`, cast `REAL` ou somme implicite de valeurs `TEXT` pour une vérité financière.

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
3. Vérifier identité, version, schéma exact, `integrity_check`, `foreign_key_check` et empreinte canonique de **tous** les champs du dossier, devises et comptes, ordre compris. Consolider le WAL de la copie puis passer cette copie autonome en journal `DELETE`.
4. Fermer et calculer SHA-256/longueur. Le manifeste JSON version 1 contient version de manifeste/schéma, UUID du dossier, empreinte du fichier et du snapshot complet, nombres de comptes/devises. Aucun nom, note ou solde en clair ; pas de référence financière séparée requise puisque le rapprochement intégral utilise ce digest du même snapshot.
5. Publier la base sous un nom neuf, puis son `<nom>.moneta.manifest.json` **en dernier**. Le couple n'est pas une transaction multifichier : sans manifeste final ou en cas de divergence, la copie n'est pas certifiée. Une publication interrompue ne supprime aucune ancienne sauvegarde.
6. Conserver les deux fichiers ensemble, à l'écart du dossier actif, et tester une restauration. L'empreinte n'est ni chiffrement ni signature d'authenticité. Ne pas ouvrir/enregistrer directement l'archive comme dossier de travail : son hash pourrait changer ; restaurer une nouvelle copie.

La date UTC propre à la sauvegarde, un identifiant de génération distinct du hash, la planification, la rétention et les copies externes automatisées restent à réaliser avant automatisation. Aucun déclenchement automatique ni rotation n'est annoncé. Les invariants d'opérations/liens feront partie du digest et du rapprochement lorsqu'ils seront persistés.

## FMT-06 — Restaurer réellement, pas seulement vérifier un hash

1. Choisir la sauvegarde **et conserver son manifeste adjacent**. Manifeste absent/incomplet/inconnu, hash/taille divergents, identité/version/schéma invalides ou corruption : refus.
2. Restaurer par API SQLite vers un temporaire local distinct ; aucun fichier principal ou compagnon de destination existant n'est remplacé.
3. Vérifier le contenu complet contre le digest certifié, puis publier sous un nouveau nom, rouvrir avec `MonetaFile.Open` et comparer à nouveau. Un hash ou `integrity_check=ok` isolé ne suffit pas.
4. Le dialogue annonce uniquement une **restauration sur nouvelle copie rouverte et comparée**. Le dossier actuellement affiché, ses données et brouillons restent inchangés. Utiliser ensuite Fichier → Ouvrir pour une bascule volontaire.

La restauration est effectivement exercée par les tests et le scénario WPF avec plusieurs comptes EUR/BYN/RUB, montants signés, UUID, notes et sélection retrouvés. Elle ne certifie pas des opérations absentes du schéma.

Audit 6.1 : un source bloqué par partage/verrou Windows (codes 32/33) renvoie maintenant `DocumentError.Locked` avec un message explicite, sans essai de contournement. Tests supplémentaires : écriture WAL non committée, lecteur WAL épinglé, sources bloqués et readonly pour restauration/migration. Lecture seule SQLite ne garantit pas l'absence de fichiers de coordination adjacents.

## FMT-07 — Migration du schéma, sans rétrogradation

Schéma courant **2**. Seule transition connue **1 → 2** : brouillon interne synthétique identique hormis l'absence de `Accounts.is_archived`, ajouté avec valeur fausse. La version 1 n'est pas présentée comme une ancienne version distribuée de Moneta.

L'ouverture normale d'une version 1 signale une migration nécessaire. L'utilisateur choisit explicitement de créer une copie et sa nouvelle destination. L'API SQLite copie d'abord le source en lecture seule ; la copie avant transformation est contrôlée et comparée intégralement. Reconstruction transactionnelle de la table, historique/version mis à jour ensemble, comparaison après transformation puis réouverture de la copie publiée. Le hash du principal source est contrôlé inchangé ; aucun fichier original ni journal n'est supprimé.

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
