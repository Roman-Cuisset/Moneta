# Langues et formats régionaux

## Choisir la langue

Le code de développement de la 0.2.0 propose le français (`fr-FR`) et l’anglais américain (`en-US`). Le français est utilisé à la première ouverture et lorsqu’un réglage antérieur est absent ou inconnu.

Dans **Outils → Options → Langue** ou **Tools → Options → Language**, le choix s’applique immédiatement. **Enregistrer / Save** conserve la langue dans les préférences locales pour le prochain démarrage. Fermer ou annuler les options rétablit la langue précédemment enregistrée. Le changement ne recharge pas le dossier. Les brouillons, la sélection du registre et les résultats de recherche restent en mémoire. Une fenêtre modale ouverte ensuite utilise la langue choisie.

## Saisir et lire des données

| Valeur | Français (France) | Anglais (États-Unis) |
| --- | --- | --- |
| Date complète, par exemple le 5 avril 2026 | `05/04/2026` | `04/05/2026` |
| Mois, par exemple avril 2026 | `04/2026` | `04/2026` |
| Saisie d’un montant | `1234,56` ou `1 234,56` | `1234.56` |
| Filtre de montant signé | `-12,34` | `-12.34` |

La saisie anglaise et les filtres de montant excluent les séparateurs de milliers. Les entrées ambiguës ou plus précises que ne le permet la devise sont refusées. L’affichage respecte la précision et la devise d’origine. Sélectionner une langue ne déclenche ni conversion ni nouvelle recherche de cours. Les libellés saisis par l’utilisateur et les codes de rapprochement `NP`, `P` et `R` gardent leurs valeurs.

## Ressources et persistance

`Moneta.Localization` regroupe les ressources neutres françaises dans `Resources/Strings.resx` et le catalogue anglais dans `Resources/Strings.en-US.resx`. Le satellite `en-US/Moneta.Localization.resources.dll` doit accompagner l’application. `L.Get` et `L.Format` servent aux textes et messages. Les extensions WPF `LocalizedText` et `LocalizedLanguage` observent la préférence. Les présentations déjà calculées, notamment les lignes du registre et les résultats de recherche, actualisent leur affichage à partir des valeurs typées existantes.

Le champ facultatif `Language` des préférences locales, au schéma 3, n’ajoute aucune colonne au dossier financier. Dates, unités monétaires, écritures, échanges SQLite, identifiants et formats de preuve restent indépendants de la langue. Les diagnostics inclus dans des données de provenance ou dans une preuve gardent leur représentation canonique. Quand une correspondance existe, l’interface affiche la traduction du diagnostic. Les boîtes natives de Windows, les détails techniques de composants externes et le texte signé des notes de version peuvent garder leur langue d’origine.

Pour ajouter un libellé, choisir une clé qui décrit la commande ou le message et conserver les mêmes indices de paramètres dans les deux ressources. Pour ajouter une langue, compléter la liste des cultures supportées, définir ses règles de date et de saisie, fournir un catalogue complet puis étendre les tests de ressources, de lecture, d’affichage et de basculement WPF. Toute modification d’un diagnostic canonique doit tenir compte de son usage dans les traces persistantes. [Ressources .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/localization)

Pour une future publication bilingue, le champ de notes déjà signé peut contenir deux sections rédigées par l’équipe, `Français` et `English`. Le texte entier reste couvert par la signature. L’interface actuelle indique qu’elle affiche les notes d’origine. Le processus de vérification conserve le format du catalogue.

## Qualification

`LocalizationTests` vérifie les clés et paramètres des deux catalogues, les notifications de changement de langue, les dates et montants exacts, la rétrocompatibilité des préférences, la sélection du registre et l’empreinte du fichier financier après un aller-retour entre les deux langues. Les tests couvrent aussi la restauration d’un instantané, la preuve historique et la récurrence, ainsi que les codes de diagnostic d’import et le refus d’une mise à jour incompatible.

Les builds Debug et Release passent 684 tests chacun. Un parcours automatisé a ouvert les vraies fenêtres WPF en Release sous Windows 11 x64, à 125 %, dans les thèmes clair et sombre. Il a exercé les menus, les noms accessibles, le registre, la recherche, les options, la saisie d’un brouillon dans chaque langue, un rapport calculé hors ligne, les onglets Graphiques et Budgets, les fenêtres de compte, de virement, d’échéancier et d’import ainsi que l’offre de mise à jour. Le redémarrage dans un second processus a chargé l’anglais enregistré, rouvert le dossier fictif et permis d’enregistrer le retour au français. L’empreinte du fichier SQLite et les soldes ont été vérifiés avant et après.

Le satellite anglais a été contrôlé dans la sortie Release WPF et dans l’exécutable autonome de l’assistant de mise à jour compilé localement. Le parcours n’a lancé aucune installation de mise à jour.

Le temps entre le choix du réglage et la fin du traitement de l’interface WPF a été de 368 ms en thème clair et de 346 ms en thème sombre lors d’un passage sur le dossier fictif. Les fenêtres du registre, des filtres et des options étaient ouvertes. Sur un autre dossier de 20 000 écritures, six basculements du registre ont pris de 219 à 425 ms, avec une médiane de 284 ms. Les 100 lignes affichées ont conservé leur identité en mémoire et l’empreinte du dossier est restée identique. Ce sont des mesures ponctuelles sur Windows 11 x64 avec un Core i5-1235U. La qualification visuelle et l’accessibilité restent à élargir à d’autres facteurs d’échelle et à un lecteur d’écran avant la publication de la 0.2.0.
