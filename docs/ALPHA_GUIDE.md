# Premiers pas avec Moneta Alpha 0.1.0

Moneta s’installe sur Windows 11 x64. Téléchargez le candidat depuis la release officielle et comparez son SHA-256 avec celui communiqué avant de l’ouvrir. L’installateur Alpha ne porte pas de signature Authenticode. Selon les paramètres de Windows, SmartScreen peut signaler un éditeur inconnu ou une réputation insuffisante. En cas de blocage, arrêtez l’installation et communiquez le message affiché. Votre profil Windows accueille l’application et ses raccourcis. Vous choisissez où stocker chaque dossier financier.

1. Lancez **Moneta-0.1.0-win-x64.exe**, puis ouvrez **Moneta** depuis le menu Démarrer.
2. **Fichier → Nouveau dossier**, puis **Comptes → Ajouter un compte**. Choisissez la devise native du compte. **Ctrl+S** choisit l’emplacement au premier enregistrement.
3. **Nouvelle** sous le registre ouvre une recette ou une dépense. **Virement** relie les écritures et conserve les montants natifs de chaque compte. **Ctrl+S** enregistre les brouillons.
4. **Comptes → Échéancier** programme les opérations. Confirmez une occurrence avant de l’enregistrer. **Rapports → Budgets** compare le prévu et le réalisé et indique les conversions indisponibles.
5. **Fichier → Sauvegarder le dossier** produit un fichier et son manifeste. Conservez-les ensemble sur un autre support. **Outils → Options → Sauvegardes** règle les sauvegardes versionnées.
6. **Fichier → Restaurer une sauvegarde** vérifie l’archive et crée une copie contrôlée à un nouvel emplacement. Ouvrez ensuite cette copie pour consulter les comptes et le registre.
7. **Aide → Rechercher les mises à jour** peut être utilisé hors de la recherche automatique du démarrage. Les notes signées s’affichent dans Moneta. **Télécharger et installer** vérifie le fichier et vous laisse enregistrer ou annuler la fermeture avant l’installation.

## Parcours en préparation pour la 0.2.0

- Au premier accès à un fichier `.moneta` dans l’Explorateur, choisissez **Ouvrir avec → Moneta**. Si vous en faites l’application par défaut dans Windows, les prochains doubles-clics ouvriront le dossier. Si Moneta est déjà lancé, la demande rejoint sa fenêtre et reprend la confirmation de changement de dossier.
- Sous le registre, saisissez la date, le libellé, le sens et le montant. Les autres champs sont facultatifs. Les suggestions viennent de vos comptes et de vos opérations. Validez avec **Entrée** ou **Ajouter au brouillon**, puis enregistrez avec **Ctrl+S**. Une liste ouverte conserve son comportement de sélection. Une saisie non validée reste associée à son compte pendant la session. Avant de quitter le dossier, Moneta en demande la confirmation séparément des brouillons validés.
- Les champs acceptent `1234,56`, `1234.56`, `1 234,56` et `1,234.56`. Les espaces ordinaires et insécables peuvent grouper les milliers. Une écriture ambiguë telle que `1,234` est refusée. Pour une valeur à trois décimales, ajoutez un zéro final (`1,2340`) si la précision de la devise le permet. Pour les soldes signés et les filtres, placez le signe avant les chiffres. Les recettes et dépenses utilisent le champ **Sens**.
- **Affichage → Densité du registre** mémorise **Standard** ou **Compact**. Sélectionnez une écriture pour consulter ses détails, y compris le tiers et la sous-catégorie.
- L’actualisation au démarrage dépend du réglage **Outils → Options**. L’actualisation manuelle demande le cours du jour pour le registre. La fenêtre des cours permet de choisir une autre date. En cas d’échec réseau, le registre indique la date effective et le cache utilisé, ou l’indisponibilité du cours. Les montants natifs restent consultables et les valorisations historiques gardent leurs dates.

[Notes de préparation de la 0.2.0](releases/0.2.0.md)

## Si Moneta ne démarre plus

Gardez les originaux, les sauvegardes et leurs manifestes. Réinstallez un installateur Moneta vérifié. Ouvrez votre dossier en place ou restaurez une sauvegarde vers un nouveau dossier. Si le fichier actif est encore ouvert dans Moneta, utilisez **Sauvegarder le dossier** pour en faire une copie cohérente.

## Limites connues

L’Alpha couvre Windows 11 x64 et le périmètre qualifié des fichiers BankPerfect V5. Certains montants historiques inférieurs à l’unité minimale de leur devise sont refusés. Une conversion requiert un cours officiel applicable ou une copie valable en cache. Les sauvegardes contiennent des données financières en clair. Les mises à jour nécessitent un catalogue signé disponible, et Windows conserve ses protections d’exécution. [Import BankPerfect](BANKPERFECT_QUALIFICATION_10E.md)

GitHub fournit les préversions sur leur page de release. Le canal intégré `Latest` attend une release stable avec un catalogue signé. Pour connaître les préversions disponibles, consultez la [liste des releases](https://github.com/Roman-Cuisset/Moneta/releases).

Effectuez des sauvegardes régulières et vérifiez une restauration avant d’utiliser l’Alpha avec des données importantes.

Le code source original de Moneta est sous [licence MIT](../LICENSE). Le fichier `LICENSE` est installé à la racine de l’application et reste accessible depuis **À propos**. Les composants tiers conservent leurs propres notices dans `ThirdPartyNotices`.
