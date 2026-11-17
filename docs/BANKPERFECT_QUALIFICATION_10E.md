# Qualification avancée de l’import BankPerfect

## État de référence

La mission part de `bcd7da5` sur `main`, synchronisé avec le dépôt privé. Les 594 tests de référence ont été relancés en Debug. Les huit SHA-256 de F00 à F07 concordent avant les essais. Le périmètre est celui des dossiers V5 d’essai en EUR, enregistrés par BankPerfect 8.0.0.373. [Corpus et empreintes](BANKPERFECT_FIXTURES.md)

Les builds Debug et Release ont réussi sans erreur ni avertissement. Chaque suite compte 616 tests réussis, sans échec ni test ignoré. Les trois mesures de volume sont trois exécutions du même protocole de charge. [Matrice des décisions](BANKPERFECT_COMPATIBILITY_MATRIX.md)

## Interruption et concurrence

| Frontière exercée | Vérification |
|---|---|
| Lecture, désobfuscation, parsing et validation V5 | Annulation injectée au début de chaque étape. La préparation s’arrête sans valider de rapport exploitable. Les lectures par blocs et les boucles du lecteur vérifient le jeton |
| Avant conversion, avant écriture, après fermeture du candidat, avant restauration et avant publication | Annulation contrôlée. La cible est absente et le nettoyage normal retire les fichiers de travail reconnus |
| Écriture et sauvegarde | Occupation réelle du nom du candidat ou de la sauvegarde. Refus et absence de cible |
| Restauration | Archive de contrôle modifiée après sa certification. Vérification de taille échouée et absence de cible |
| Validation finale | Ligne SQLite modifiée après restauration. La comparaison finale refuse de publier même si le solde est identique |
| Destination créée tardivement ou journal orphelin | Refus explicite. Les octets du fichier déjà présent sont préservés |
| Répertoire disparu ou déplacé | Refus. Aucun dossier cible ne paraît à l’emplacement d’origine ni à celui du répertoire déplacé. Son résidu de travail peut subsister |
| Verrou détenu | Refus pendant l’occupation. Le verrou Moneta ordinaire libéré peut être réutilisé lors d’une nouvelle tentative |
| Deux processus concurrents | Deux exécutions distinctes vers le même nom. Une publication réussit, l’autre retourne un refus |
| Source modifiée après l’aperçu | Le SHA-256 approuvé est comparé à la source relue sous verrou de lecture. Le changement est refusé |

Une sonde locale a interrompu un processus d’import fictif après la restauration, avant le contrôle final et le renommage. La cible était absente et les six fichiers de travail étaient visibles dans le répertoire de l’essai. Une nouvelle tentative au même nom a publié un dossier complet en utilisant son propre répertoire. Une deuxième sonde a arrêté un processus qui détenait le verrou de destination. La relance a réutilisé le verrou libéré. Les SHA-256 des sources sont restés identiques.

Pour l’arrêt pendant l’écriture, la sonde a attendu l’apparition du journal SQLite de 4 152 octets associé à la base temporaire sur un import de 10 000 lignes. Après l’arrêt forcé de ce processus, la cible était absente et le SHA-256 de la source concordait.

### Reprise et nettoyage

Après échec, relancer depuis le fichier BP d’origine et son nouvel aperçu. Le nom de destination peut être repris s’il est libre. Le renommage sur le même volume publie une base autonome vérifiée. Ce protocole démontre l’absence de cible partielle aux frontières testées. Il ne permet pas de conclure sur la durabilité après perte électrique ou sur tous les systèmes de fichiers.

Chaque tentative alloue un répertoire frère `.moneta-import-<UUID>`, avec les noms de travail réservés `candidate.moneta`, `backup.moneta` et `restored.moneta` et leurs compagnons. En sortie normale, la tentative nettoie ses fichiers connus et ne supprime le répertoire que s’il est vide. Un test vérifie la préservation d’un fichier tiers. Le nettoyage abandonne si le répertoire est devenu un point de réanalyse.

Un arrêt brutal peut laisser ce répertoire entier. Le préfixe aide à repérer un résidu. Il ne suffit pas à authentifier sa propriété. Pour un nettoyage manuel, fermer les imports, identifier le dossier créé par la tentative concernée et vérifier qu’il contient seulement ses fichiers de travail. L’application repart de la source et n’ouvre aucun résidu comme résultat. Une reprise automatique apporterait peu de bénéfice au regard du risque de reprendre un état incomplet.

## Charge synthétique

Le générateur de test écrit un V5 synthétique avec deux comptes EUR, quatre catégories, des tiers accentués, un catalogue de modes, un réglage du formulaire de transfert, des opérations NP/P/R et 10, 100 ou 500 virements réciproques. Chaque volume comprend les ouvertures et les jambes dans son effectif. Le test contrôle le nombre de lignes et de virements, l’empreinte de provenance et la préservation de la source. L’importateur compare exhaustivement les données tant après réouverture qu’après restauration.

Une exécution Release par volume sur Windows 11 26H2, Intel Core i5-1235U et .NET 10. Le chronométrage total couvre l’aperçu et l’import, hors génération et réouverture finale de la sonde. La mémoire indiquée est la plus haute observation en fin d’étape dans le processus de test. Elle inclut ce processus et peut sous-estimer un pic entre deux observations. Le disque et la charge de fond n’ont pas été isolés.

| Lignes | Source en octets | Cible en octets | Aperçu | Total | CPU | Privée initiale / max. observé | Résidente initiale / max. observé | Lignes/s sur le total |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 000 | 57 803 | 1 236 992 | 0,24 s | 1,95 s | 1,92 s | 23 / 38 Mio | 62 / 85 Mio | 513 |
| 10 000 | 585 160 | 10 244 096 | 0,58 s | 12,37 s | 13,78 s | 30 / 78 Mio | 69 / 128 Mio | 809 |
| 50 000 | 2 969 860 | 50 257 920 | 1,84 s | 82,91 s | 82,39 s | 44 / 249 Mio | 82 / 293 Mio | 603 |

| Étape locale, en ms arrondies | 1 000 | 10 000 | 50 000 |
|---|---:|---:|---:|
| Ouverture et lecture source | 2 | 7 | 5 |
| Désobfuscation | 5 | 12 | 22 |
| Parsing | 32 | 124 | 626 |
| Validation du V5 et des réciproques | 14 | 41 | 121 |
| Plan et prévalidation Moneta pour l’aperçu | 143 | 346 | 977 |
| Plan d’exécution | 11 | 78 | 400 |
| Sérialisation de la provenance | 8 | 57 | 283 |
| Écriture SQLite et fermeture | 775 | 5 808 | 33 819 |
| Réouverture | 125 | 941 | 8 044 |
| Comparaison exhaustive du candidat | 53 | 207 | 1 080 |
| Sauvegarde de contrôle | 179 | 1 253 | 10 841 |
| Restauration de contrôle | 402 | 2 681 | 20 916 |
| Comparaison exhaustive de la restauration | 34 | 172 | 1 075 |
| Validation finale et publication | 68 | 526 | 4 507 |

L’écriture et la restauration dominent à 50 000 lignes. La lecture et le décodage n’expliquent qu’une petite partie du temps. Les mesures ne distinguent pas chaque requête interne SQLite. Elles n’ont justifié aucune suppression de vérification ni modification du moteur financier.

Reproduire la mesure après un build Release. Chaque commande `dotnet test` démarre un processus de test pour le volume choisi. Le résultat JSON est dans la sortie du test consignée dans le TRX. Les mesures locales `ActivitySource` se limitent aux noms d’étapes et à leurs durées, sans chemins ni données source.

```powershell
foreach ($volume in 1000, 10000, 50000) {
    $env:MONETA_BP_TEST_LOAD = [string]$volume
    dotnet test tests/Moneta.Tests/Moneta.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter FullyQualifiedName~BankPerfectLoadTests --logger "trx;LogFileName=volume-$volume.trx" --results-directory private/mission10e-bp8/output/Load
    if ($LASTEXITCODE -ne 0) { break }
}
Remove-Item Env:MONETA_BP_TEST_LOAD
```

## Parcours Windows

La fenêtre WPF réelle a exécuté l’aperçu, l’import et l’annulation d’un registre de 10 000 lignes. Le résultat s’affiche après 15,6 à 19,1 s en clair et 14,4 s en sombre. La seconde conversion publie un autre nom alors que le premier dossier reste ouvert. Les deux cibles contiennent 10 000 lignes. En sombre, le bouton Annuler pendant l’écriture laisse la cible absente. Le résultat d’annulation est affiché à 6,6 à 7,0 s depuis l’ouverture de la fenêtre. La demande attend la fin du bloc d’écriture SQLite en cours. Le refus d’un nom déjà occupé par le dossier ouvert est explicite. Le blocage d’une ouverture atypique ne propose pas de créer une cible.

Pendant les conversions réussies, le plus grand intervalle mesuré entre événements d’un minuteur de l’interface était de 100 ms. Lors des annulations, il était de 68 ms. L’initialisation des fenêtres courtes a donné jusqu’à 374 ms. Cette mesure est un indicateur de disponibilité du fil WPF, avec un poste et une échelle. Le scénario automatisé fournit les chemins à la fenêtre après les sélecteurs de fichiers et vérifie les contrôles et les messages. Le parcours de registre à 50 000 lignes a été mesuré dans le moteur avec le test de charge.

## Fidélité des données

La date comptable du V5 alimente la date du registre. Le statut historique NP, P ou R reste utilisable dans Moneta. La seconde date est consultable dans le rapport d’origine, y compris après dépointage dans le fichier source. Sa sémantique est démontrée sur les gestes documentés dans BP8. Pour une évolution future, conserver la date historique avec sa provenance et son degré de qualification dans un champ dédié avant d’envisager de l’utiliser dans les règles actives du pointage. Un état R importé reste un état hérité sans relevé inventé.

La provenance correspond à la version d’origine importée. Une correction et une suppression logique d’un virement Moneta conservent ces octets, leurs UID source et l’association aux UUID cible. L’état courant, y compris la suppression logique, se lit dans le registre. Les événements formels Moneta et les instantanés de rapprochement décrivent certaines transformations. La provenance n’est pas un journal complet des modifications. Le test de réouverture d’un relevé examine la restauration de l’état P et le maintien de la provenance.

La précision de saisie et l’échelle comptable existantes restent applicables. Une éventuelle évolution devrait valider ensemble les soldes, le pointage, le rapprochement, les virements, la conversion, les sauvegardes et l’affichage sur les anciens dossiers et BP-F03. [Étude de précision](HISTORICAL_MONEY_PRECISION.md)

## Bilan et retour au développement courant

L’import EUR V5 peut servir de point d’entrée pour ce build et les signatures éprouvées. Les archives d’un autre build ou dans une autre devise nécessitent leur propre qualification. Les refus de F03, F08 et F09 couvrent des risques distincts de précision, de classification et de lien orphelin. Une prochaine validation de l’import peut cibler les autres signatures d’ouverture, les formats protégés, la durabilité aux frontières encore inconnues et les limites hautes de volume sur d’autres postes. La reprise des fonctionnalités courantes peut s’appuyer sur les invariants financiers existants. [Feuille de route](ROADMAP.md)

Les futurs rapports multidevises prendront, pour chaque dépense passée, le cours applicable à sa date d’opération. Cette règle vaut pour toutes les devises prises en charge. [Règle de valorisation](HISTORICAL_FX_VALUATION.md)
