# Moneta

Successeur Windows de BankPerfect pour la gestion locale de comptes personnels. Priorité : fidélité des fonctions, menus, raccourcis et habitudes de navigation, avec une interface légèrement modernisée et des garanties financières explicites.

**État : initialisation et étude uniquement. Aucune application, solution Visual Studio, dépendance installée ou commande de lancement à ce stade. La programmation attend la validation de l'utilisateur.**

## Objectifs

- Retrouver les fonctions BankPerfect identifiées dans la matrice de parité, sans réduire le produit à un suivi bancaire générique.
- Gérer EUR, BYN et RUB, puis d'autres devises ; consulter les cours officiels, notamment NBRB, conserver leur historique et fonctionner hors ligne.
- Afficher les soldes convertis en euros et gérer des transferts multidevises sans réécrire les opérations passées.
- Migrer les données BankPerfect de façon contrôlée, avec rapport de rapprochement et traitement explicite des éléments non transportables.
- Garantir des montants exacts, une persistance transactionnelle, des sauvegardes automatiques vérifiables et une restauration éprouvée.

Cible proposée : C#, **.NET 10 LTS**, WPF, SQLite et MVVM sobre. Il s'agit d'un choix d'architecture, **pas d'un logiciel déjà créé**. Les compromis et sources officielles figurent dans l'étude.

## Organisation

```text
Moneta/
├── reference-bankperfect/   # Référence locale immuable, exclue de Git
├── src/                    # Vide hors fichier de conservation .gitkeep
├── tests/                  # Idem ; futurs tests des invariants financiers
├── docs/
├── tools/                  # Idem ; aucun outil applicatif créé
├── .gitignore
└── README.md
```

La référence était déjà présente lors de la mission. Sa comparaison en lecture seule avec le dossier original a confirmé **128 fichiers identiques par SHA-256, 39 sous-dossiers, 1 649 599 octets**, sans fichier manquant ni supplémentaire. Aucune réécriture inutile n'a été faite. L'installation originale et la référence ne doivent jamais être modifiées par le développement.

## Documents

| Document | Contenu |
|---|---|
| [Audit de BankPerfect](docs/BANKPERFECT_AUDIT.md) | Inventaire, sources locales et officielles, extensions, formats, limites et inconnus |
| [Fidélité fonctionnelle](docs/FUNCTIONAL_PARITY.md) | Correspondance des fonctions, menus partiellement prouvés, raccourcis et état de réalisation |
| [Architecture](docs/ARCHITECTURE.md) | Choix techniques, données, calculs, cours, sécurité, sauvegardes et migration |
| [Feuille de route](docs/ROADMAP.md) | Priorités, dépendances, validations et conditions de passage au développement |

La référence statique prouve une présence ou une déclaration ; elle ne prouve pas qu'une commande fonctionne encore. Les pages du manuel sont anciennes et parfois contradictoires. Aucun exécutable ou script BankPerfect n'a été lancé ; aucune décompilation ni aucun désassemblage n'a été effectué.

## Confidentialité et Git

- Dépôt **local uniquement** ; aucun distant ni dépôt GitHub créé. Ne pas publier sans décision explicite.
- `reference-bankperfect/` est entièrement ignoré, y compris ses scripts et ressources. Ne jamais forcer son ajout avec `git add -f`.
- Les dossiers `data/`, `private/`, `imports/`, `exports/`, `backups/` et `screenshots-private/` sont réservés aux fichiers privés et ignorés. Aucun n'est requis pour cette mission.
- Les fichiers BankPerfect, SQLite et leurs journaux auxiliaires, exports bancaires courants, archives, secrets, journaux et productions `bin/obj` sont exclus. Les règles sont volontairement conservatrices : CSV, XML, HTML, TXT, PDF et DAT sont aussi ignorés hors des dossiers privés.
- Un `.gitignore` ne détecte pas une donnée personnelle dans un Markdown et ne protège pas un fichier déjà suivi. **Avant chaque ajout : liste explicite de fichiers autorisés et inspection du contenu.** Ne pas utiliser `git add .` pour des données de migration.
- Les futures données de test doivent être entièrement fictives. Toute exception d'exclusion devra être étroite et revue ; jamais une base réelle ou un export bancaire réel dans le dépôt.
- Ne pas ouvrir ni recopier les valeurs de `identity.dat`, les identifiants, numéros de comptes, chemins personnels ou secrets dans les rapports. Les rapports présents ne reproduisent pas ces contenus.

## Référence et propriété intellectuelle

BankPerfect reste un logiciel tiers. Son caractère gratuit ne donne pas une autorisation de réutiliser son code, ses scripts, logos ou ressources. La référence locale sert uniquement à l'analyse autorisée. Le futur code Moneta devra être original ; aucune licence de redistribution de BankPerfect n'est présumée, et aucune licence de publication de Moneta n'est décidée ici.

Manuel officiel : <https://chelly.net/bankperfect/wiki/manuel.html>.

## Prochaine mission proposée

Compléter la spécification de fidélité **avant toute programmation** : captures anonymisées de l'installation réellement utilisée, menus/sous-menus/contextes, fenêtres clés et exports entièrement fictifs produits par l'utilisateur. Résoudre les contradictions et établir les contrôles de migration ; valider ensuite l'architecture et le premier lot à développer. Voir les critères détaillés de la feuille de route.
