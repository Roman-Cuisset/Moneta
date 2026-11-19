# Sauvegardes automatiques et récupération

La mission 14 étend le contrôle du contenu au schéma 7. La sauvegarde certifie les programmations, les suspensions, les reports, les décisions finales et leurs liens avec le registre. Elle prend l’état enregistré lors du même commit que l’écriture ou le virement. La restauration vérifie l’ensemble dans une nouvelle copie et préserve les identifiants qui empêchent un deuxième traitement. Le manifeste garde sa version 1 et les certificats des anciens schémas gardent leur algorithme d’origine. [Échéancier](SCHEDULED_TRANSACTIONS.md)

## Choisir la politique

Dans **Outils → Options → Sauvegardes**, activer les sauvegardes et choisir **Après un enregistrement réussi**, **Chaque jour où le dossier est ouvert**, ou les deux. Les sauvegardes automatiques sont désactivées au départ. Les deux déclenchements sont proposés, avec 30 minutes entre deux publications après Ctrl+S et 10 versions par dossier. L’intervalle est réglable de 1 à 1 440 minutes et la rétention de 1 à 100 versions. Les préférences utilisent le format JSON 3. Les anciens fichiers d’options conservent leurs réglages et reviennent aux valeurs de sauvegarde par défaut.

- Après Ctrl+S, Moneta confirme d’abord le commit financier. La sauvegarde s’exécute ensuite sur un thread de travail, avec sa propre connexion SQLite en lecture seule. Elle compare l’état lu et l’archive à l’instantané enregistré qui a déclenché la demande. Un état plus récent ou modifié par un autre client peut faire refuser la copie en cours. Les demandes de sauvegarde après enregistrement pendant un travail en cours sont regroupées vers la dernière demande en attente.
- Le contrôle quotidien s’effectue à l’ouverture du dossier et chaque minute pendant que l’application est ouverte. Une version publiée puis contrôlée par son empreinte suffit pour le jour civil local, quel que soit son déclenchement. Le délai après Ctrl+S est mesuré en UTC. Une version endommagée ne retarde pas une nouvelle tentative. Les horodatages futurs sont ignorés pour le calcul de cadence après un recul de l’horloge.
- **Sauvegarder maintenant** dans l’historique ignore la cadence et prend le dernier état enregistré, même en présence d’un brouillon. **Fichier → Sauvegarder le dossier…** conserve l’export manuel vers un chemin choisi. Il demande d’abord l’enregistrement des brouillons s’il y en a. L’export indépendant ne fait pas partie de la rotation.

Un échec après Ctrl+S affiche **Dossier enregistré, mais sauvegarde automatique échouée**. Il reste visible dans la session jusqu’à consultation de l’historique. Une fermeture normale signale les échecs encore en attente. Le bouton d’annulation arrête le travail à une frontière sûre. Pendant un appel SQLite natif, l’annulation attend son retour. Dès la publication, l’archive reste au catalogue et la rotation peut être reportée. Après la décision de fermer, Moneta désactive la saisie, attend le travail déjà engagé, puis ferme proprement le dossier.

## Emplacement et confidentialité

La destination par défaut est `%LOCALAPPDATA%\Moneta\Sauvegardes`. Toute destination contient la sous-arborescence `versions-moneta/<UUID du dossier>/v-<UUID de la version>/`. Deux dossiers homonymes d’identités différentes ont des historiques distincts. Les copies de travail qui partagent intentionnellement le même UUID consultent la même bibliothèque. Un emplacement différent affiche son propre historique. Les anciennes bibliothèques restent sur leur support.

Choisir un autre disque pour être protégé d’une panne physique du disque actif. Les archives contiennent des données financières en clair. Utiliser les protections du compte Windows ou d’un support chiffré, et un emplacement non synchronisé si les données doivent rester sur la machine. Moneta n’envoie aucune donnée de sauvegarde. La bibliothèque refuse les chemins réseau reconnus, les chemins UNC et les redirections Windows dans l’arborescence gérée. La copie active et la restauration doivent être à l’extérieur de cette bibliothèque.

## Publication et rotation

Une génération réunit `dossier.moneta`, `dossier.moneta.manifest.json` et `version.json`. Le manifeste 1 contient le SHA-256 du fichier autonome et l’empreinte canonique de toutes les données. Le descripteur de génération 1 lie l’UUID du dossier, l’UUID de version, l’horodatage UTC, le type, le schéma, la taille et le SHA-256. L’ouverture avec **Vérifier** compare ces informations, la structure SQLite, les références et le contenu complet. Le descripteur ne contient pas le chemin du dossier source.

Moneta réutilise la copie à chaud de SQLite pour les commits présents dans le WAL, valide la copie autonome et demande le vidage du fichier et des métadonnées écrites. Il renomme ensuite le répertoire préparé sur le volume de destination. L’historique reconnaît les répertoires publiés. Un répertoire `.pending-…` contient un travail interrompu ou incomplet. Il est hors de l’historique normal. Les fichiers actifs et les journaux SQLite sont pris en charge par SQLite. [API de sauvegarde en ligne de SQLite](https://www.sqlite.org/backup.html)

Le verrou par dossier et destination sérialise création, vérification d’une version et restauration dans la bibliothèque. Après publication, la dernière version est tenue ouverte pendant la rotation. Moneta vérifie chaque version candidate et laisse en place les archives corrompues, illisibles, ouvertes ou accompagnées d’un élément inconnu. La rétention vise les versions vérifiables. L’espace occupé peut donc dépasser le nombre configuré. Un candidat vérifié est renommé en `.retired-…` avant nettoyage de ses seuls fichiers reconnus. Une interruption peut laisser ce répertoire sur le disque. La rotation ne parcourt pas le dossier de travail.

Le renommage sur le même volume définit le point de publication logique sur le système de fichiers compatible. Le vidage demandé et le renommage ne garantissent pas le résultat d’une coupure électrique ou d’une panne matérielle. En cas d’incertitude au redémarrage, vérifier les versions avant restauration. Les supports amovibles, leurs options d’écriture et leurs systèmes de fichiers peuvent modifier le comportement observé sur SSD local.

## Restaurer vers un nouveau fichier

1. Ouvrir **Fichier → Sauvegardes → Historique…**. La date, le type, la taille et le chemin décrivent une publication. **Vérifier** confirme le contenu à cet instant. La vérification est refaite avant la restauration.
2. Choisir **Restaurer vers un nouveau fichier…**, lire l’identité et le résumé de la version, choisir un chemin neuf et confirmer.
3. Moneta valide la copie source, restaure via SQLite, migre la copie si le schéma certifié l’exige, puis rouvre le nouveau fichier et compare toutes ses données. Cette comparaison comprend comptes, propriétés, opérations, virements, frais, pointages, rapprochements, événements et provenance BankPerfect.
4. Ouvrir volontairement le fichier restauré avec **Fichier → Ouvrir**. Le dossier en cours et ses brouillons restent en place jusqu’à cette action.

Si l’original est introuvable, parcourir `versions-moneta` sur le support. Conserver chaque `dossier.moneta` avec son manifeste adjacent. Utiliser **Fichier → Restaurer une sauvegarde…** avec le fichier de la version voulue. Une copie incomplète ou incohérente sera refusée. Si le disque d’origine est en panne, garder le support de sauvegarde intact et restaurer sur un autre disque. Pour conserver une version hors rotation, la restaurer vers un fichier de travail neuf, puis en faire une sauvegarde manuelle à part.

Après une interruption, laisser les répertoires de travail en place jusqu’à contrôle des versions publiées. À l’arrêt de Moneta, isoler d’abord les éléments résiduels dans un emplacement de quarantaine local. Une paire base et manifeste complète peut y être vérifiée via la restauration manuelle. Un élément d’origine incertaine reste à conserver pour diagnostic. L’application ne nettoie automatiquement que le travail qu’elle a réservé elle-même et les anciennes versions qu’elle a vérifiées pour la rotation.

## Qualification et mesures

Debug et Release compilent sans erreur ni avertissement. Les 641 tests passent dans chaque configuration, dont 10 nouveaux. Les essais couvrent la persistance des options, la cadence après redémarrage, les homonymes et horodatages égaux, l’intégrité avec des brouillons, les versions corrompues et verrouillées, les erreurs d’écriture simulées, l’annulation, les sauvegardes concurrentes et les commits survenant avant ou après l’instantané SQLite. La réouverture exclusive des copies après les opérations vérifie que les accès de sauvegarde et de contrôle ont été libérés.

Les fenêtres Release ont été exercées en clair et en sombre sous Windows, avec les paramètres, les noms accessibles, Ctrl+S, la rotation, l’échec de destination, l’historique, le choix natif du nouveau fichier et la confirmation. L’interruption de l’accès à la destination a été simulée sur le disque local. Le dossier fictif importé de BankPerfect comprenait des comptes EUR/BYN/RUB, un compte archivé, des pointages, un rapprochement, un virement et des frais. La comparaison de la restauration a porté sur les propriétés des comptes, le condensat complet du registre, les soldes natifs, le rapprochement, les frais et la provenance octet pour octet.

Des processus Windows dédiés ont été arrêtés au début de la vérification après copie SQLite, avant le renommage de publication et au début de la rotation. Avant publication, seule la précédente version était visible et vérifiable. Après publication, les deux versions étaient vérifiables. L’empreinte du dossier actif est restée identique et une nouvelle sauvegarde a repris la rotation. Le manque d’espace et le refus d’écriture ont été injectés pendant la préparation dans des tests. Ces essais ne reproduisent pas une panne du support, une coupure électrique ou les ACL d’un autre environnement.

Mesures Release sur le SSD local de ce poste Windows 11, avec un Core i5-1235U et 32 Gio de RAM. Médianes de trois enregistrements et trois cycles de sauvegarde et restauration après une version de chauffe, sous charge du poste variable. Les dossiers fictifs de départ comportent 1 000, 10 000 ou 50 000 opérations. La rotation conserve une version dans ce scénario. La capture inclut la validation de la source et la copie SQLite. La vérification couvre la copie, les empreintes et les métadonnées. Le temps total comprend aussi la préparation. Les médianes des étapes ne forment pas nécessairement la médiane du total.

| Opérations | Commit ms | Capture ms | Vérification ms | Publication ms | Rotation ms | Sauvegarde totale ms | Restauration ms |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 000 | 151,5 | 73,4 | 79,7 | 3,5 | 69,6 | 257,9 | 316,3 |
| 10 000 | 786,6 | 441,9 | 494,1 | 3,4 | 421,1 | 1 403,1 | 1 941,1 |
| 50 000 | 9 380,6 | 3 421,8 | 4 577,6 | 3,3 | 3 527,3 | 11 546,1 | 16 843,9 |

| Opérations | CPU sauvegarde ms | Allocations du thread Mio | Mémoire résidente du processus Mio |
| ---: | ---: | ---: | ---: |
| 1 000 | 343,8 | 6,85 | 137,7 |
| 10 000 | 1 406,2 | 61,61 | 137,9 |
| 50 000 | 11 250,0 | 305,83 | 135,7 |

À 50 000 opérations, un parcours WPF a observé 9,62 s pour revenir de Ctrl+S et 10,55 s de sauvegarde déjà engagée après ce retour. Le minuteur WPF a continué de traiter les événements pendant l’attente, avec 573 déclenchements et un intervalle maximal de 34,3 ms. La fermeture a attendu la publication et l’interface a bloqué les nouvelles saisies après décision de fermer. Les commandes financières restent indisponibles pendant le commit lui-même. Le temps de sauvegarde en arrière-plan et le temps de commit constituent deux mesures différentes.

Avant Alpha, qualifier les systèmes de fichiers et volumes externes prévus, les vrais incidents d’espace et de permissions et le parcours de récupération sans dossier actif. À 50 000 écritures, les contrôles complets du registre restent perceptibles, surtout lors du commit et de la restauration. Les validations automatiques n’incluent pas le lecteur d’écran ni le comportement d’un support physiquement déconnecté pendant une écriture.
