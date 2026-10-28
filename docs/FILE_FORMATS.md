# Formats de fichiers Moneta

Contrat de format issu de la mission documentaire 2. La mission 3 réalise un prototype WPF **sans persistance**, aucun fournisseur SQLite, schéma, fichier `.moneta` ou essai de sauvegarde/restauration. Les exigences suivantes restent futures ; elles ne décrivent pas une capacité du prototype. Voir [ARCHITECTURE.md](ARCHITECTURE.md), [BANKPERFECT_IMPORT_STRATEGY.md](BANKPERFECT_IMPORT_STRATEGY.md) et [README](../README.md). Les preuves de l'audit initial sont conservées.

Les identifiants `FMT-*` sont stables pour les renvois de la feuille de route. « Doit » exprime une exigence Moneta ; une propriété documentée de SQLite n'est pas une garantie d'implémentation Moneta.

## FMT-01 — Format natif retenu et frontières

- **Un dossier de comptes = une base SQLite directe portant l'extension `.moneta`**, par exemple `Mes comptes.moneta`. Plusieurs comptes peuvent appartenir à cette base ; plusieurs dossiers indépendants peuvent exister.
- Ce n'est **ni une archive ZIP**, ni un conteneur à décompresser, ni un `.bp` renommé, ni une encapsulation de BankPerfect. Aucune installation, API ou exécution BankPerfect n'est nécessaire au format natif Moneta.
- Le fichier commence par les 16 octets de signature SQLite `SQLite format 3\0` (zéro terminal). Cette signature identifie SQLite, **pas Moneta**. L'extension seule n'identifie aucun format et une base SQLite quelconque renommée `.moneta` doit être refusée comme dossier Moneta.
- L'identification doit combiner signature, `PRAGMA application_id`, métadonnées Moneta, version et schéma reconnu. `application_id` est le champ applicatif 32 bits de l'en-tête SQLite ; `user_version` est une convention de l'application. Ne pas utiliser `schema_version`, que SQLite gère pour son propre schéma, comme numéro de migration Moneta [S01, S02].
- Proposition de contrat de métadonnées : marqueur littéral `Moneta`, identifiant stable du dossier, version entière du format/schéma, version minimale de lecteur et d'écrivain compatible, version de l'application créatrice, dates UTC de création/migration et historique des migrations. `application_id` doit être non nul, constant pour cette famille et publié avec le premier schéma ; sa valeur devra être choisie sans collision connue dans la liste SQLite. **Aucun identifiant numérique n'est réservé ou prétendu enregistré dans cette mission.** La version native Moneta est indépendante des versions BankPerfect et du numéro de version SQLite. L'accord des deux représentations de version (`user_version` et métadonnées) est contrôlé ; une discordance n'est pas réparée silencieusement.
- La forme des tables, contraintes et métadonnées sera figée lors de la réalisation autorisée. Il n'existe aujourd'hui **aucun schéma Moneta livré**. Les exigences ci-dessus évitent précisément de présenter n'importe quelle base comme une version 1 existante.
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

Commande distincte « Nouveau », nom et destination explicites, chemin local admissible, espace/droits contrôlés, création exclusive d'un temporaire dans le dossier de destination. Valider ce temporaire, le fermer et le publier sans écraser un fichier concurrent. **Ouvrir** n'est pas **Créer**. Ni une erreur de lecture ni une annulation ne doivent déclencher cette procédure. La publication et sa résistance aux pannes seront éprouvées sur les systèmes de fichiers Windows retenus ; le mot « atomique » ne vaut pas garantie universelle entre volumes ou sur support réseau.

### Versions anciennes, futures ou inconnues

- Version connue courante : contrôles puis accès normal.
- Version connue plus ancienne : sauvegarde cohérente restaurable, consentement de migration et procédure FMT-07.
- Version future ou schéma non reconnu : refus d'écriture et de migration. Lecture seule seulement si un contrat de compatibilité explicite et éprouvé la permet ; sinon refus total avec conservation du fichier. **Pas de downgrade** ni de modification du numéro pour forcer l'ouverture.
- Identité ambiguë, métadonnée incohérente, table inattendue ou corruption : blocage expliqué, aucune recréation et aucune suppression des journaux.

## FMT-03 — Valeurs financières et dates exactes

Le modèle logique suit l'architecture ; il ne constitue pas ici un DDL.

| Donnée | Contrat de stockage futur | Validation requise |
| --- | --- | --- |
| Montant comptabilisé et solde initial | `INTEGER` 64 bits en unités mineures, devise et échelle explicites ; pour une opération, magnitude non négative avec sens débit/crédit séparé | Pas de supposition « toute devise a deux décimales ». À deux décimales, 12,34 EUR = 1234 unités ; solde négatif et sens restent distincts. Débordement ou fraction sous l'unité rejetés explicitement, jamais tronqués |
| Taux, quantité de titres, prix unitaire | `TEXT` décimal canonique invariant, point décimal, sans séparateur de milliers ; précision, unité et provenance explicites | Ex. taux fictif `3.2500`, quantité `2.5`. Préserver la précision source nécessaire ; ne pas passer par `double` ou `REAL` |
| Dates métier | Texte ISO `YYYY-MM-DD`, sans fuseau | Dates d'opération, pointage, échéance et relevé distinctes ; inconnue reste inconnue |
| Horodatages techniques | UTC avec représentation non ambiguë | Création/import/sauvegarde ne remplacent jamais les dates métier |
| Texte utilisateur | Unicode, valeur et provenance conservées | Aucune translittération ou normalisation destructive silencieuse ; ne pas exécuter les champs de règles historiques |

SQLite `REAL` est binaire flottant ; `DECIMAL(10,2)` ne garantit ni échelle ni exactitude. Le fournisseur Microsoft documente le stockage de `decimal` en `TEXT` ; cela ne signifie pas que ce fournisseur est installé ou définitivement sélectionné [S08, S09]. Les calculs futurs sont en C# `decimal`/entiers contrôlés, sans repli flottant après dépassement ; les sommes sont vérifiées par devise. **Pas de calcul financier par `TOTAL`, `AVG`, conversion `REAL` ou somme SQL implicite de nombres `TEXT`.** Sens des taux, échelles, étapes et politique d'arrondi sont versionnés et visibles dans les calculs [ARCHITECTURE.md](ARCHITECTURE.md).

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

**Politique Moneta proposée :** base active sur disque local, WAL avec `synchronous=FULL`, chemin d'écriture unique et contrôle des ouvertures incompatibles. La version exacte du moteur est à sélectionner avant développement : la documentation SQLite signale le défaut de corruption « WAL-reset », corrigé en **3.51.3 et suivantes**, ainsi que par certains rétroportages 3.44.6/3.50.7 [S03, section 11]. Exiger une version maintenue qui inclut ce correctif et les corrections pertinentes ultérieures ; ne pas figer une ancienne version minimale comme garantie suffisante. Cette lecture documentaire n'est pas un diagnostic du poste.

### Checkpoint, concurrence et durabilité

- Commit réussi et checkpoint réussi ne sont pas synonymes : un commit peut être durable dans le WAL sans être déjà recopié dans le principal.
- Un lecteur long peut bloquer l'avancement d'un checkpoint. SQLite peut retourner `SQLITE_BUSY` même en WAL ; lire le résultat et les compteurs du checkpoint, ne pas supposer qu'un appel ou un seuil automatique a vidé le journal.
- Prévoir attente bornée/annulation et message visible en cas de verrouillage. Un checkpoint incomplet ne justifie jamais de supprimer le WAL. Tant que l'état autonome n'est pas établi, ne pas annoncer « prêt à copier comme fichier unique ».
- `synchronous=FULL` demande les synchronisations nécessaires aux commits WAL ; ce n'est pas une garantie contre un matériel qui ment sur les écritures, un système de fichiers défectueux, toute coupure ou toute erreur d'E/S [S03, S04]. Ne pas dégrader silencieusement vers `NORMAL`, `OFF` ou `journal_mode=OFF`.
- Disque plein, droits perdus, déconnexion du support, échec de commit/checkpoint/fermeture : échec explicite ; aucune réussite inventée, aucun nettoyage des fichiers de reprise. L'application ne supprime ni ne renomme manuellement un journal chaud.

### Réseau, synchronisation et transport

**Non pris en charge pour une base active :** partage SMB/NAS, lecteur réseau, accès depuis plusieurs machines et dossier synchronisé (OneDrive, Dropbox ou analogue). Une synchronisation de fichiers n'est pas une transaction SQLite ; le WAL exige une coordination locale. L'usage local-first ne signifie pas « cloud sans conflit ».

Un `.moneta` est transportable comme **fichier unique seulement** :

1. après fermeture propre de **toutes** les connexions, arrêt des écritures et confirmation de l'état autonome cohérent, sans journal nécessaire à une reprise ; ou
2. comme destination cohérente, fermée et validée d'une API de sauvegarde SQLite (FMT-05).

Copier le seul principal en cours d'utilisation est incorrect. **Copier successivement le principal et le WAL, même tous deux présents à l'arrivée, n'est pas un snapshot sûr** : leurs états peuvent appartenir à des instants différents. Le hash de chaque copie ne corrige pas cette incohérence. Ne pas conseiller une copie manuelle « base + WAL » comme sauvegarde à chaud. Une copie de transport fermée peut ensuite être synchronisée comme fichier inactif, sans ouverture concurrente et après contrôle à destination ; ce n'est pas une fonction de synchronisation Moneta.

Après crash, conserver principal et journaux à leurs noms et emplacements cohérents ; aucune suppression de `-wal`, `-shm` ou `-journal` pour « débloquer ». Laisser le moteur SQLite gérer la reprise autorisée, sur copie préservée si l'état est suspect. Diagnostic et sauvegarde sont des opérations distinctes.

## FMT-05 — Créer une sauvegarde vérifiable

**Procédure future requise, non exécutée :**

1. Identifier le dossier/version et le point cohérent visé ; protéger la source contre les écritures incompatibles avec les comparaisons. Utiliser l'**Online Backup API** ou un mécanisme SQLite cohérent explicitement validé via le fournisseur retenu, pas une copie de fichiers actifs [S05].
2. Créer une destination temporaire exclusive sur le volume où la sauvegarde sera publiée ; ne jamais réutiliser l'unique sauvegarde valide comme destination de travail. Contrôler tous les codes de retour de l'API, y compris finalisation et fermeture ; annulation/`BUSY`/disque plein ne vaut pas succès.
3. Fermer proprement la destination et la rendre autonome ; la rouvrir pour vérification bornée en lecture seule, contrôler signature/identité/version, `integrity_check`, `foreign_key_check` et invariants métier. Comparer comptes, devises, nombres de lignes, soldes initiaux/finals, totaux débit/crédit, pointage/rapprochement, transferts et ventilations au **même snapshot** source, pas à une source qui aurait évolué entre deux mesures.
4. Fermer tous les accès de validation, calculer **SHA-256 et longueur** du fichier final stable. Établir le manifeste technique : identifiant de sauvegarde/dossier, versions, instants UTC établis, taille, empreinte et résultats des contrôles, sans noms de tiers/comptes, numéros bancaires ou soldes dans la version partageable. Conserver **séparément une référence de rapprochement financière privée** du même snapshot : comptes identifiés techniquement, nombres et totaux par devise/état, soldes et relations attendus ; la lier à cette sauvegarde par identifiant et empreinte. Elle ne va ni dans Git ni dans les journaux partageables. Une empreinte ne chiffre rien et ne prouve pas seule l'authenticité.
5. Publier sous un nom neuf par bascule sur le même volume sans écrasement concurrent, avec gestion vérifiée des erreurs et de la durabilité. Ne déclarer « sauvegarde créée et contrôlée » qu'après la publication et ses contrôles ; un temporaire restant après incident est identifié comme incomplet, pas découvert comme sauvegarde réussie.
6. Conserver l'ancienne copie valide ; rétention configurable et consentie, support externe indépendant recommandé. L'indisponibilité du support ou l'échec de rotation reste visible, jamais compensé par l'effacement de la dernière copie valide. La copie de référence BP n'est pas une cible de sauvegarde Moneta.

La base et ses pièces de contrôle ne forment pas magiquement une transaction multifichier : publier le manifeste d'achèvement après la base et les références privées requises validées, puis traiter toute publication interrompue comme non certifiée jusqu'à recontrôle. En l'absence de référence financière, ne pas revendiquer une comparaison au snapshot source non disponible. Les garanties de renommage/flush Windows devront être éprouvées ; aucune promesse d'atomicité inter-volume.

## FMT-06 — Restaurer réellement, pas seulement vérifier un hash

1. Choisir une sauvegarde et vérifier longueur/hash, identité et compatibilité ; si le manifeste manque, ne pas la déclarer préalablement certifiée.
2. **Restaurer effectivement vers un nouveau fichier local**, distinct du dossier courant et de l'archive de sauvegarde. Aucun écrasement direct de l'unique exemplaire.
3. Ouvrir la copie restaurée, refaire les contrôles structurels, clés étrangères et invariants de FMT-05, puis réconcilier comptes, opérations, devises, soldes et liens avec le manifeste financier privé du snapshot. Un `integrity_check=ok` seul ne prouve pas une reprise financière correcte.
4. Fermer/réouvrir cette copie avec le futur logiciel et exercer le parcours de lecture utile ; comparer à nouveau les valeurs attendues. **Une restauration doit être réellement exercée**, non remplacée par l'ouverture du fichier de sauvegarde ou par une promesse.
5. Présenter la comparaison. Pour une bascule consentie, préserver auparavant l'état courant dans une sauvegarde cohérente, fermer toutes les connexions et publier la destination validée. Ne jamais supprimer l'archive ni le dossier précédent après un échec.

Statuts distincts : « sauvegarde créée/contrôlée », « restauration sur copie exercée et réconciliée », « bascule effectuée ». Une seule de ces réussites ne permet pas de revendiquer les trois. **Aucune de ces opérations n'a été exercée dans cette mission.**

## FMT-07 — Migration du schéma, sans rétrogradation

Chaque migration a une version source/cible connue, un identifiant, une empreinte de définition et des pré/postconditions. Avant de modifier : contrôler l'identité source, disposer d'une sauvegarde cohérente **avec restauration sur copie exercée**, d'espace/droits suffisants et du consentement requis.

Contrat privilégié : migrer **une copie de travail distincte**, jamais le seul original. Relever avant transformation les identifiants, comptes/devises, nombres, soldes, états/dates de pointage, liens, ventilation, échéances, budgets et autres domaines persistés ; appliquer les migrations ordonnées et transactionnelles quand SQLite le permet ; écrire la version cible seulement avec la transformation réussie. Pour une opération non transactionnelle, produire une nouvelle copie cohérente, pas une demi-migration publiée.

Après transformation : contrôles FMT-05 et **comparaison avant/après des données et invariants**. Les différences attendues de représentation sont décrites par la migration ; toute différence financière inattendue bloque la publication. Réouverture de la copie cible, conservation des preuves et publication contrôlée sans destruction de la source. Une erreur laisse le dossier d'origine utilisable avec sa version compatible ; ni version artificiellement abaissée, ni downgrade automatique. Le retour à l'état antérieur passe par la copie conservée et l'application compatible, pas par une migration inverse improvisée.

## FMT-08 — Association Windows consentie, ultérieure

Aucune modification du registre, association ou installation n'est réalisée pendant cette mission. Contrat d'intégration future limité à **`.moneta`**, avec consentement ; **ne pas associer `.bp`, `.bp0` ou les fichiers auxiliaires BP à Moneta**, ni altérer leur programme par défaut.

- Enregistrement **par utilisateur** sous `HKEY_CURRENT_USER\Software\Classes`, pas écriture globale dans `HKEY_CLASSES_ROOT` (vue fusionnée) ou élévation administrative implicite [S10].
- ProgID proposé : `Moneta.Document.1` ; ce suffixe décrit le contrat d'association, pas une prétendue version de base implémentée. Inscrire ce ProgID comme candidat dans `.moneta\OpenWithProgids`. L'entrée doit rendre Moneta disponible dans « Ouvrir avec » ; elle ne vaut pas choix par défaut.
- Verbe `shell\open\command` : chemin absolu de l'exécutable **et argument fichier tous deux entre guillemets**, forme documentaire `"C:\Chemin choisi\Moneta.exe" "%1"` [S11]. Aucun passage par `cmd /c`, script ou concaténation interprétée. Le futur parseur traite l'argument comme chemin, y compris espaces, accents et noms ressemblant à des options.
- Le changement de programme par défaut relève de l'interface Windows et du choix utilisateur. **Ne pas écraser `UserChoice`, modifier son hash, forcer le défaut ou rétablir Moneta après un choix contraire.** La documentation « Default Programs » avertit que ses anciennes API de changement ne s'appliquent pas à Windows 10 ; ce n'est pas une recette à réutiliser aveuglément [S12].
- Désinstallation : retirer uniquement les entrées possédées par Moneta ; ne supprimer aucun document ni préférence d'un autre programme. Aucune icône/logo BP ni pack graphique réutilisé ; l'identité visuelle Moneta reste originale.

## FMT-09 — Validations futures obligatoires

Cette liste est une spécification d'acceptation, **pas un compte rendu de tests**. Aucun test SQLite applicatif, fichier `.moneta`, projet ou générateur n'est créé ici.

| ID | Exercice futur sur données entièrement synthétiques | Résultat exigé |
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
| S05 | [SQLite — Online Backup API](https://www.sqlite.org/backup.html) | Snapshot cohérent via l'API ; aucun essai Moneta réalisé |
| S07 | [SQLite — Security](https://www.sqlite.org/security.html) | Limites et schéma non fiable ; aucune base étrangère exécutée |
| S08 | [SQLite — Datatypes](https://www.sqlite.org/datatype3.html) | Référence reliée à l'étude d'architecture : affinités et flottants, pas type financier décimal automatique |
| S09 | [Microsoft — SQLite types](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types) | Référence existante d'architecture sur `decimal`/`TEXT` ; fournisseur candidat non installé |
| S10 | [Microsoft — File Types](https://learn.microsoft.com/en-us/windows/win32/shell/fa-file-types) | ProgID, `OpenWithProgids`, enregistrement par utilisateur |
| S11 | [Microsoft — Launching Applications](https://learn.microsoft.com/en-us/windows/win32/shell/launch) | Commande du verbe d'ouverture, chemins cités |
| S12 | [Microsoft — Default Programs](https://learn.microsoft.com/en-us/windows/win32/shell/default-programs) et [changement Windows 10](https://blogs.windows.com/windows-insider/2015/05/20/announcing-windows-10-insider-preview-build-10122-for-pcs/) | Limite des anciennes API et contrôle utilisateur du défaut ; vérifier l'intégration sur les Windows supportés lors de sa réalisation |

Les règles du format natif et celles de lecture BankPerfect sont indépendantes : voir [BANKPERFECT_IMPORT_STRATEGY.md](BANKPERFECT_IMPORT_STRATEGY.md). Le présent document ne transforme aucune inconnue `.bp` en connaissance SQLite.
