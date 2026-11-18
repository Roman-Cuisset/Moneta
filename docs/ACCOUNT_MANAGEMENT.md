# Gestion des comptes

Le manuel BankPerfect place l’ajout, la modification et la suppression dans **Comptes → Gestion des comptes**, accessible par Ctrl+G. Il documente la banque et le numéro facultatifs ainsi que le sélecteur en haut à gauche. Moneta conserve le menu et le sélecteur et ajoute les commandes du compte sélectionné au panneau gauche. [Manuel BankPerfect](https://chelly.net/bankperfect/wiki/La%20gestion%20des%20comptes.html)

## Propriétés et ordre

Le nom est obligatoire. Le type, les notes, la banque, la référence bancaire et le titulaire sont facultatifs. Les saisies modifiées sont élaguées en bordure, Unicode compris. Le nom, la banque, la référence et le titulaire sont limités à 120 caractères, le type à 80 et les notes à 2 000. Un nom existant est repris tel quel tant que l’utilisateur ne le modifie pas. Le rang et la devise distinguent visuellement les homonymes. Les mouvements et les relevés continuent de référencer l’UUID.

**Monter** et **Descendre** déplacent le compte par rapport à son voisin affiché. Les comptes masqués gardent leurs positions. L’ordre enregistré est celui des comptes dans le dossier. Le dialogue Appliquer prépare un brouillon, Annuler le laisse intact, et Ctrl+S enregistre la modification. Un conflit avec une écriture SQLite extérieure fait échouer l’enregistrement et laisse le brouillon en mémoire.

La devise native et le solde d’ouverture sont fixés à la création, même sur un compte vierge. L’interface les présente en lecture seule et l’enregistrement vérifie leur intégrité. Le sélecteur d’affichage reste indépendant. Une correction comptable se saisit comme opération explicite dans le registre, avec les protections de pointage et de rapprochement usuelles. Une ouverture importée reste représentée par ses écritures et sa provenance.

## Archives et suppression

Un compte archivé reste visible avec **Afficher les comptes archivés**. Son registre, ses soldes, ses relevés et ses liens sont consultables. Le champ de recherche parcourt les libellés, les notes et les tiers des opérations enregistrées dans ce compte. Un virement vers une contrepartie archivée réaffiche et sélectionne le compte correspondant.

La saisie et le pointage sont réservés aux comptes actifs. La correction ou la suppression d’un virement exige que les comptes des deux jambes et des frais soient actifs. Un compte avec des écritures en brouillon doit les enregistrer avant archivage. La réactivation réutilise l’identité et l’ordre existants. Un rapprochement est à nouveau modifiable une fois la réactivation enregistrée, selon les protections habituelles.

**Supprimer le compte vierge…** exige un solde d’ouverture nul et l’absence d’écritures, y compris supprimées logiquement, de rapprochement, de brouillon et de référence d’origine BankPerfect. La confirmation vise le compte et son rang. Le contrôle est relu dans la transaction SQLite de Ctrl+S. Les comptes qui ont un historique restent accessibles par l’archivage.

## Format et sauvegardes

Les fichiers schéma 5 sont ouverts sur une copie migrée avec consentement. La transition vers le schéma 6 ajoute trois colonnes facultatives et préserve l’ordre, le registre, les liens, la précision monétaire et le rapport BankPerfect. L’empreinte des nouvelles sauvegardes couvre les trois champs, ainsi que les UUID, l’ordre et l’état d’archivage. Les archives anciennes sont contrôlées avec leur empreinte d’origine avant migration de la copie. Les informations bancaires restent dans le dossier local et ses copies. [Format SQLite](FILE_FORMATS.md)

## Validation Windows

Debug et Release compilent sans erreur ni avertissement. Chacune des deux configurations réussit 624 tests, dont 8 nouveaux pour les comptes. Le parcours WPF en Release utilise des dossiers fictifs EUR, BYN et RUB sur Windows 11 à 125 %, en thèmes clair et sombre. Il exerce les commandes de compte, l’annulation du dialogue et de la fermeture, la sauvegarde durable, la consultation des relevés archivés, le suivi du virement et la réouverture. Le dossier restauré a été comparé par les propriétés complètes des comptes, le condensat du registre, les jambes du virement et son compte de frais ainsi que le rapprochement. Les chemins des fichiers de sauvegarde, de restauration et de réouverture sont fournis au composant de gestion de dossier.

Le dossier fictif `BP-F06` issu de BankPerfect, déjà converti vers Moneta v5 en mission 10E, a été migré vers v6 sur copie, renommé, archivé, réactivé, sauvegardé puis restauré. Les trois virements, les UID historiques et le rapport d’origine ont été vérifiés. Le rapport reste identique octet pour octet et l’empreinte du fichier v5 source est intacte. Une sauvegarde certifiée v5 de 1 000 écritures et 10 virements a aussi été restaurée en v6.

### Mesures

Un relevé Release sur le même PC (Core i5-1235U), avec 1 000 comptes fictifs et un registre vide. Les temps de stockage incluent les vérifications SQLite et la comparaison des copies. Les changements de propriétés et d’ordre sont chronométrés dans le modèle d’interface. La recherche archivée parcourt le compte en lecture séquentielle et charge seulement la page du résultat. Les mesures varient avec le disque et la taille du registre.

| Action | Durée |
| --- | ---: |
| Ouverture et chargement des 1 000 comptes | 102,4 ms |
| Renommage | 0,5 ms |
| Archivage | 0,2 ms |
| Réactivation | 0,1 ms |
| Réorganisation | 0,2 ms |
| Enregistrement | 169,4 ms |
| Sauvegarde vérifiée | 253,2 ms |
| Restauration vérifiée | 319,8 ms |

Après stabilisation de la fenêtre, un minuteur d’interface à priorité de saisie a continué de se déclencher pendant les quatre opérations sur fichier. Les écarts maximaux observés étaient de 58,8 ms à l’ouverture, 76,1 ms à l’enregistrement, 37,7 ms à la sauvegarde et 38,0 ms à la restauration. Les commandes financières restent désactivées pendant chacune de ces opérations.
