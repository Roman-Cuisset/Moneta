# Échéancier et opérations récurrentes

## Programmer et consulter

Ouvrir **Comptes → Échéancier…**, le bouton de la barre d’outils ou **Ctrl+E**. Le bouton indique, à l’ouverture du dossier, combien d’échéances restent à traiter aujourd’hui ou sont en retard. Moneta fonctionne hors ligne. L’ouverture du dossier déclenche le rappel et laisse les écritures en attente de confirmation. BankPerfect documentait ce menu, ce raccourci et le rattrapage à l’ouverture. Moneta demande une décision explicite pour chaque occurrence. [Manuel BankPerfect](https://chelly.net/bankperfect/wiki/L-echeancier.html) · [Raccourcis BankPerfect](https://chelly.net/bankperfect/wiki/Recapitulatif%20des%20raccourcis%20clavier.html)

- Une **dépense** prépare un débit. Une **recette** prépare un crédit. Le compte fournit la devise native. Tiers, catégorie, sous-catégorie, mode de paiement et notes sont facultatifs. Le montant est exprimé dans les unités mineures du compte et peut être nul, comme dans le registre.
- Un **virement** indique le débit, le crédit et, si nécessaire, des frais sur leur propre compte. Les montants sont affichés dans leurs devises natives.
- **Occurrences** propose toutes les échéances de la période affichée, les échéances à venir, celles en retard, celles du jour et celles du mois. Les pages contiennent 100 lignes. L’horizon de consultation va jusqu’à 365 jours dans le futur. Les dates plus lointaines restent visibles comme prochaine date dans **Programmations**. Tous les retards sont comptés, même après une longue absence.
- **Programmations** permet de modifier les données futures ou de choisir la vue des programmations suspendues et supprimées. **Historique** montre les reports, les ignorances et les écritures confirmées. Son filtre **Comptabilisées** affiche les écritures dont le commit a réussi.

### Règles de date

Choisir **Une seule fois**, ou un intervalle de 1 à 366 jours, semaines, mois ou années. Deux semaines, deux mois, trois mois et six mois correspondent respectivement à un rythme toutes les deux semaines, tous les deux mois, trimestriel et semestriel. La fin optionnelle est inclusive. Les dates utilisent le calendrier civil local, les jours calendaires et la date d’ancrage de la programmation.

Une récurrence ancrée au 31 janvier survient le 28 ou le 29 février, puis le 31 mars. Un anniversaire du 29 février tombe le 28 février les années ordinaires et retrouve le 29 pendant les années bissextiles. L’option **Dernier jour du mois** applique le dernier jour aux fréquences mensuelles ou annuelles, y compris à la première occurrence. Les samedis, dimanches et jours fériés gardent leur date. Une modification de libellé ou de montant conserve l’ancrage. Modifier la fréquence ou la prochaine date réancre la suite.

## Confirmer, reporter ou ignorer

Traiter les occurrences ordinaires d’une même programmation dans leur ordre. **Confirmer l’occurrence…** ouvre les valeurs de cette occurrence et la date à comptabiliser. Les types et comptes de débit et crédit restent ceux de l’occurrence. Le libellé, les montants et les autres conditions modifiables s’appliquent à cette seule écriture. **Préparer l’écriture** place l’écriture dans les brouillons du dossier. **Ctrl+S** ou **Enregistrer le dossier** rend l’écriture et sa décision durables ensemble. Le statut **Écriture en brouillon** reste affiché avant ce commit. Pour modifier une écriture programmée depuis le registre, l’enregistrer au préalable. Annuler le formulaire laisse l’occurrence en attente. Abandonner les modifications du dossier abandonne la décision et son écriture.

**Reporter…** choisit une date postérieure, conserve l’identité et les conditions de l’occurrence, puis rend la suivante traitable. L’occurrence reportée reste accessible à sa propre date. **Ignorer…** prend une décision finale pour cette occurrence sans créer d’écriture. Ces décisions prennent effet durablement après **Ctrl+S**. La suspension et la reprise n’effacent aucun retard ni report. Supprimer une programmation la retire pour l’avenir et conserve le journal et toutes les écritures déjà comptabilisées. Pour reprendre une occurrence qui utilise un compte archivé, réactiver le compte. Ce contrôle vaut pour le débit, le crédit et les frais. Un compte référencé dans l’échéancier ne peut être supprimé physiquement.

## Devises et cours

Dans la même devise, le crédit reprend le débit et les frais restent séparés. Entre devises, un montant reçu peut être proposé par la programmation. L’utilisateur confirme le débit et le crédit réels dans leurs devises natives avant de préparer le virement. Sans crédit renseigné, le formulaire l’exige. Le moteur de virements existant enregistre les deux montants, calcule le taux effectif et conserve la provenance déclarée si elle est renseignée. Les montants utilisent les unités mineures entières et les calculs monétaires existants. L’échéancier ne consulte pas de cours à terme et ne présente aucun montant proposé comme un cours officiel.

Pour les rapports à venir, valoriser les flux au cours applicable à la date de l’opération et les soldes au cours applicable à la date d’arrêté. Les prévisions doivent annoncer la méthode utilisée et conserver les références et unités de cotation. [Feuille de route](ROADMAP.md)

## Identité, enregistrement et récupération

Le schéma SQLite 7 ajoute `SchedulePlans` et `ScheduleDecisions`. Chaque occurrence associe l’UUID de sa programmation à son numéro monotone. Chaque report ajoute un événement avec son rang et sa date. Un index SQLite unique autorise une seule décision finale par occurrence. Les clés étrangères conservent le lien unique vers une opération ordinaire ou vers le virement complet. L’historique des décisions est immuable. SQLite enregistre la décision, la progression de la programmation, l’écriture et, le cas échéant, toutes les jambes et les frais dans la même transaction. Si un autre client a enregistré une version différente, le second commit échoue avec le contrôle de concurrence existant. Il doit recharger le dossier avant de poursuivre.

L’ouverture d’un dossier de schéma 1 à 6 propose une nouvelle copie de schéma 7. Moneta produit une sauvegarde vérifiée séparée avant migration et conserve l’original. La migration 6 → 7 ajoute les deux tables et contrôle la conservation du contenu historique. Les imports BankPerfect existants restent lisibles. Les programmations propres à l’ancien plugin BankPerfect ne sont pas importées.

Les sauvegardes manuelles et versionnées contrôlent les programmations, les conditions historiques, les dates, les reports et les liens dans l’empreinte de contenu v7. Restaurer crée une nouvelle copie qui garde les mêmes identifiants. L’utilisateur peut continuer à traiter les occurrences restées en attente au moment de la sauvegarde. [Sauvegardes](AUTOMATIC_BACKUPS.md)

## Qualification Windows

Les compilations Debug et Release terminent sans erreur ni avertissement. Les **652 tests** réussissent dans chaque configuration, dont 11 nouveaux tests d’échéancier. Lancer `dotnet test Moneta.slnx -c Debug` ou `dotnet test Moneta.slnx -c Release` pour rejouer la suite.

Les fenêtres WPF Release ont été exécutées en clair et sombre sur Windows 11, avec EUR, BYN et RUB fictifs. Le scénario a exercé les commandes de création, la réouverture, le rappel, l’annulation, la modification ponctuelle et future, le refus d’un doublon, le report, l’ignorance, la suspension, la reprise et le blocage d’un compte archivé. Le virement EUR → BYN exigeait la saisie du montant reçu et conservait des frais en RUB. Une copie restaurée a été rouverte et comparée sur les programmations, les décisions, le registre, les comptes, les soldes et les liens du virement. Le parcours a piloté les contrôles WPF et vérifié des rendus des deux thèmes. La sauvegarde et la restauration de ce parcours ont utilisé le service du dossier avec des chemins fictifs. La qualification ne couvre ni lecteur d’écran ni second écran physique.

Les tests couvrent aussi les propriétés calendaires et monétaires, dont zéro et le montant maximal en unités mineures, la migration v6, la reprise après restauration et le rejet SQLite d’un lien invalide en fin de transaction. Un second processus Moneta a reçu le refus d’accès au dossier déjà ouvert. Un autre essai a arrêté un processus enfant pendant l’insertion de la décision, avant `COMMIT`. Au redémarrage, aucune écriture ni décision partielle n’était visible. Une nouvelle confirmation a créé exactement une écriture.

### Mesures sur dossiers synthétiques

Médianes de trois essais Release par volume, sur Windows 11 x64, Intel Core i5-1235U, 32 Gio de RAM et SSD NTFS. Une règle quotidienne bornée à dix jours par programmation donne 1 000 ou 10 000 occurrences, dont 40 % en retard. Le chronométrage WPF inclut la création et le rendu jusqu’au retour de la boucle d’interface en mode logiciel. Les temps des services sont mesurés séparément, sans boîte de dialogue.

| Mesure | 100 programmations | 1 000 programmations |
| --- | ---: | ---: |
| Ouverture du dossier et validation | 25 ms | 86 ms |
| Détection des retards | 0,30 ms | 2,19 ms |
| Calcul des 100 premières lignes | 0,75 ms | 2,27 ms |
| Parcours des occurrences du lot | 3,36 ms | 40,11 ms |
| Ouverture de l’échéancier WPF | 562 ms | 495 ms |
| Changement de filtre et rendu WPF | 362 ms | 329 ms |
| Préparation d’une occurrence | 0,59 ms | 0,67 ms |
| Commit du dossier | 24 ms | 91 ms |
| Sauvegarde vérifiée | 74 ms | 181 ms |
| Restauration vérifiée | 117 ms | 363 ms |
| CPU du processus sur l’ensemble de l’essai | 1,70 s | 2,61 s |
| Allocations sur le thread de mesure | 21,1 Mio | 69,6 Mio |
| Mémoire résidente de fin, processus WPF inclus | 219 Mio | 257 Mio |

Les mesures WPF intègrent le rendu logiciel et l’échauffement du même processus. Elles varient avec le thème, la virtualisation, la charge machine et la configuration graphique. Les allocations rapportées sont cumulées pendant chaque parcours. Le calendrier compte les récurrences à partir de leurs bornes et ne matérialise que les dates parcourues par la page. Les performances d’un historique déjà très volumineux ou d’un support distant restent à mesurer.
