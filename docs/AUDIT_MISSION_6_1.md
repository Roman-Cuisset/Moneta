# Mission 6.1 — audit indépendant de consolidation

## Périmètre et méthode

Base examinée : `c5ffa2e`, après `c44d18e`, dépôt initialement propre. Relecture des différences de mission 6, du contrat de format, des quatre projets et des chemins de coordination WPF. Deux examens séparés : session/schéma/montants et sauvegarde/restauration/migration. Aucun fichier BankPerfect, compte réel ou ressource propriétaire utilisé. Aucun changement de schéma, de modèle financier, de bibliothèque de production ou de présentation WPF.

Référence exécutée : **91 tests Debug réussis** avant modification. Après correction : **97 tests Debug et Release**, 0 échec et 0 avertissement aux compilations. Les 91 tests précédents sont conservés ; six cas ajoutés, sans réépingler de tests de formulation.

## 1. Format, montants et transactions

| Contrôle | Conclusion et preuve dans le code |
|---|---|
| Identification | Signature SQLite + application ID `0x4D4E5441`, version supportée et métadonnées concordantes ; SQLite étranger refusé. `MonetaFile.Open`, `ReadWithinTransaction` |
| Schéma | Tables `STRICT`, définitions et index attendus, refus des tables/vues/triggers/index non reconnus ; `integrity_check` et `foreign_key_check` distincts. `ValidateSchema` |
| Identité/données | UUID et UTC canoniques, ordres contigus, compte sélectionné présent, devises cohérentes et échelles enregistrées ; historique de migrations limité aux définitions connues |
| Montants | `decimal` → entier signé exact en unités mineures ; fractions excessives et dépassements refusés, `long.MinValue` exclu. Aucun passage financier par `REAL`, `float` ou `double`. `ExactMoney`, `WriteSnapshot` |
| Enregistrement | Transaction non différée avant relecture du snapshot enregistré ; comparaison du contenu avant écritures, commit avant mise à jour du snapshot mémoire. `MonetaFile.Save` |
| Coordination | Brouillons uniquement en mémoire avant Save ; Nouveau vide, annulation conservatrice, Save As vers nouveau chemin, remplacement après ouverture vérifiée. `DocumentWorkspace`, `MainWindow.RunIo` |
| Versionnement | Schéma 2 ; transition 1 → 2 sur copie du brouillon interne, pas une compatibilité avec une ancienne version distribuée. Source conservée, comparaison et réouverture |

### Les trois protections signalées

- **A — Close et checkpoint occupé : confirmée.** `Close` ne dispose connexion/verrou et ne marque la session fermée qu'après checkpoint réussi. Le test existant exerce `Locked`, propriété du verrou maintenue, nouveau Save après libération du lecteur, puis Close et réouverture réussis.
- **B — compagnons orphelins : confirmée.** La création refuse principal, `-wal`, `-shm` ou `-journal` existants avant d'ouvrir SQLite à la destination, puis recontrôle avant publication. Le test existant utilise un véritable WAL committé orphelin et vérifie sa préservation ; pas de suppression pour réussir le test.
- **C — modifications externes : confirmée dans son contrat.** La transaction d'écriture verrouille SQLite avant relecture/validation/comparaison. Identité, version/schéma, devise native, échelle, notes et autres champs du snapshot sont contrôlés avant écriture. Une modification concurrente ordinaire est refusée plutôt que perdue.

**Limites de C et du verrou :** le `.lock` est coopératif et nommé par chemin, pas par identité physique du fichier. Liens physiques/symboliques, copies divergentes et clients qui ne respectent pas ce verrou ne sont pas exclus par lui. Les verrous SQLite et la comparaison transactionnelle restent les protections contre les écrivains SQLite ordinaires. Pas de surveillance continue ni preuve cryptographique d'absence de modification ; un changement puis retour exact à l'état initial n'est pas détecté. Les dates de l'historique de migration ne font pas partie du snapshot financier comparé ; elles sont validées et laissées intactes. Appels parallèles sur une même session et mutation concurrente des listes fournies ne sont pas un usage pris en charge.

## 2. Sauvegarde, restauration et défaut confirmé

`MonetaBackup` utilise `BackupDatabase`, pas une copie du principal actif. Snapshot vivant comparé avant copie et contenu copié comparé après ; destination consolidée/autonome en journal DELETE. Manifeste borné et versionné : hash/longueur du fichier, identité/version, nombres et digest du contenu du snapshot. Publication du manifeste en dernier. Restauration sur nouvelle destination, réouverture et comparaison ; le dossier courant n'est pas remplacé.

**Défaut reproduit sur les DLL de référence :** un source dont l'ouverture est refusée par le partage Windows renvoyait `WriteFailed`, alors que l'erreur native était 32. Restauration et migration affectées ; aucun fichier de destination publié pendant la reproduction.

**Correction ciblée :** `MonetaBackup.Execute` classe les erreurs de partage/verrouillage Windows 32/33 en `DocumentError.Locked`, conserve l'exception interne et fournit un message de verrou explicite. Aucun nouvel essai automatique, aucun contournement et aucun changement de format.

Six cas de régression ajoutés dans `BackupRestoreTests` :

- écrivain WAL non committé : la sauvegarde certifie seulement l'état validé ;
- lecteur WAL conservant un ancien snapshot : sauvegarde du dernier commit malgré le checkpoint bloqué ;
- sources bloqués par partage Windows : Restore et Migrate renvoient `Locked`, octets conservés, aucune destination/temporaire publié ;
- sources en lecture seule : Restore et Migrate préservent octets, contenu complet et attribut readonly.

Restent couverts par les tests précédents : erreurs de formats/versions, corruptions, manifestes absents/altérés, destinations existantes, publication sans manifeste, différences de notes malgré hash de fichier recalculé, rollback, exactitude des montants et migration sur copie.

## 3. Diagnostic CPU : faits, pas correction cosmétique

### Instrumentation et attribution

- Sonde WPF chargée avec les assemblages livrés : 3 200 opérations, **12 conteneurs de lignes**, aucun `LayoutUpdated`, `ScrollChanged` ou `PropertyChanged` pendant les deux fenêtres stables de 6 s. Allocation managée négligeable. Aucune minuterie/animation applicative trouvée dans le code et les styles inspectés ; pas de prétention à couvrir toutes les animations système.
- Isolations temporaires : registre masqué et rendu logiciel. Pas de preuve d'une boucle de gabarit/layout ; ces réglages ne sont **pas livrés**.
- Processus Moneta réel : pic observé à **35,87 % d'un cœur**, puis retour à 0–0,94 % pendant les quatre fenêtres suivantes de 5 s ; minimisé : 0 %. Ce run ne reproduit pas une charge soutenue indéfiniment.
- Attribution native sur un autre processus réel avec hook diagnostique de sélection de démonstration : **2 625 ms CPU** sur le thread nommé **`.NET Tiered Compilation Worker`**, dans une fenêtre d'environ 5 s où le processus atteint **56,87 % d'un cœur** ; ensuite 0–0,31 %. Le hook reste privé et temporaire, pas un comportement du produit.
- Collecte EventPipe séparée : 335 événements `Method/JittingStarted`, événements `TieredCompilation/BackgroundJitStart/Stop` et `Resume` ; bibliothèques WPF, modèles et classes dynamiques concernés. Le pic nommé et cette collecte sont deux exécutions distinctes, pas une corrélation temporelle inventée.
- Contrôle diagnostique du processus avec `DOTNET_TieredCompilation=0` : 0–0,31 % sur cinq fenêtres de 5 s. Désactivation **non conservée** : supprimer toutes les optimisations runtime ne serait pas une correction WPF étayée.

**Conclusion : la compilation étagée .NET est une cause démontrée du pic attribué au thread runtime ; aucune boucle WPF n'est démontrée.** La durée variable des optimisations de démarrage doit être distinguée d'un repos stabilisé. Cela ne prouve pas que chaque épisode prolongé 15–100 % de la mission 6 possède exactement cette cause. Une fuite mémoire managée ou un défaut permanent de rendu n'est pas établi par ces mesures ; ni son impossibilité garantie. Pas de changement graphique ou de commutateur runtime livré sur cette seule piste.

WPR est installé mais sa collecte CPU est refusée par la politique du poste (`0xc5585011`), sans élévation demandée. `dotnet-trace` 10.0.750501 a été utilisé dans un dossier privé jetable, sans installation système/PATH ni dépendance ajoutée à Moneta. Sa collecte `dotnet-sampled-thread-time` mesure du temps de thread, **pas exclusivement du CPU** ; l'attribution CPU ci-dessus utilise les compteurs OS des threads. Traces non publiées.

### Mesures et comparabilité

Windows x64 10.0.26220, SDK 10.0.401/runtime 10.0.12, 12 processeurs logiques, Release sans débogueur ; interface à 125 % (120 DPI), rendu matériel niveau 2, session non distante. Échantillons ponctuels, cache disque chaud, pas de garantie statistique. Les comparaisons contrôle runtime ne constituent pas un gain avant/après d'un correctif WPF : aucun correctif WPF n'a été appliqué.

| Mesure native, sans dossier | Essai 1 | Essai 2 |
|---|---:|---:|
| Lancement → WaitForInputIdle | 1,739 s | 1,557 s |
| CPU après 5 s de stabilisation, sur 5 s | 0 % | 0 % |
| Mémoire résidente | 150,99 Mio | 150,68 Mio |
| Mémoire privée | 100,44 Mio | 100,32 Mio |

Démonstration native, compilation étagée active, cinq fenêtres successives de 5 s : CPU **35,87 / 0 / 0,62 / 0 / 0,94 %** ; mémoire résidente **209,48 → 209,48 Mio**, privée **144,23 → 144,09 Mio** sur cette observation. Avec désactivation diagnostique : résidente **201,08 → 201,03 Mio**, privée **145,21 → 145,09 Mio** ; aucune baisse constante de la mémoire démontrée par cette option. Préférences locales non financières contrôlées inchangées après mesure.

Deux passages de la sonde de dossier synthétique : coordination ouverture + projection/liaison **14,92–21,69 ms** (ne mesure pas le rendu final ni le sélecteur natif) ; service Open/lecture/Close de 4 comptes **8,47–11,06 ms**, 1 000 comptes **30,79–31,06 ms**. Échantillons ponctuels, non comparables aux anciennes mesures « choix du fichier → interface prête » comprenant l'attente UIA.

## 4. Scénarios Windows et résistance effectivement exercés

- Compte EUR, BYN, RUB ajoutés par le véritable dialogue WPF, puis second EUR ; Save commun, quatre comptes persistés, registre réel vide sans fausses opérations.
- Thèmes Light/Dark, sélection readonly et captures fictives privées examinées ; sélection de compte, registre démo 3 200 lignes, sélection/détails et enregistrement désactivé en démo.
- Sauvegarde ouverte, restauration vers nouveau fichier, comparaison des UUID, dates, devises, règles d'affichage, comptes, types, notes, soldes et sélection ; dossier courant conservé. Fermeture/réouverture des connexions et fenêtres puis nouvelle comparaison. La sonde WPF héberge la vraie fenêtre ; elle n'est pas présentée comme deux lancements indépendants de l'exécutable.
- Processus enfants fictifs tués sans Close : commit encore dans un WAL de **20 632 octets** retrouvé ; transaction non committée ayant produit un WAL de **745 752 octets** rejetée à la reprise, snapshot complet identique au dernier commit. Deuxième ouverture refusée pendant détention du verrou, autorisée après mort du propriétaire. Répertoires temporaires supprimés après réouverture/fermeture vérifiées.

## 5. Risques résiduels et mission 7

- Durabilité = contrat SQLite FULL + garanties OS/support, pas preuve de résistance à toute panne électrique ou matériel mensonger. Disque plein forcé, chaque frontière de publication/migration, toutes les ACL et supports amovibles non qualifiés.
- Lecture seule SQLite ne signifie pas répertoire immuable : des fichiers de coordination/reprise peuvent être créés ; `immutable=1` ne doit pas masquer le WAL.
- Une erreur après publication peut laisser une destination existante ou un temporaire orphelin ; pas de promesse de rollback de tous les effets filesystem. Sans manifeste final, aucune sauvegarde certifiée. Une empreinte n'est ni chiffrement ni authentification.
- Bases actives locales seulement ; chemins synchronisés non pris en charge. Pas de garantie contre remplacement malveillant des fichiers/journaux par un client filesystem non coopératif.
- Les champs d'historique technique non inclus dans le digest ne constituent pas une journalisation financière authentifiée. Extension du digest et des contrôles obligatoire avant de persister de nouveaux domaines.

**Préparation à mission 7 : oui pour un lot contrôlé sur données synthétiques, pas pour annoncer un logiciel financier complet.** Avant recettes/dépenses/corrections/suppression/pointage/transferts :

1. Schéma 3 et migration sur copie ; identifiants stables de tiers/catégories/modes, dates métier, ordre déterministe et FK. Les records actuels de démonstration ne sont pas un contrat de stockage complet.
2. Garder le cycle manuel : brouillon/modifications → transaction atomique → état enregistré ; ne pas introduire un auto-save incompatible en parallèle.
3. Soldes par devise = initial + crédits − débits en exact/checked ; règles de correction, annulation/suppression contrôlée et historique explicites, sans réécriture rétroactive par taux.
4. Deux jambes de virement et leurs références dans une transaction ; devises natives/montants réellement débités et crédités, frais/taux explicites. Pas de jambe orpheline ni total multidevise sans politique.
5. Inclure toutes les nouvelles tables et relations dans baseline/concurrence, digest de sauvegarde, restauration et comparaison financière. Un test de restauration des seuls comptes ne suffira plus.
6. Prévoir requêtes/projections bornées du registre plutôt que charger/sérialiser toutes les opérations au démarrage ; conserver virtualisation, clavier et thèmes.

Aucune de ces fonctionnalités nouvelles n'est implémentée dans cet audit.
