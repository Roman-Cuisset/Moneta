# Recherche du format BankPerfect — mission 9

## Résultat et niveau de preuve

**Une source publique pertinente est désormais identifiée ; aucun lecteur Moneta ni fichier `.bp` de test n'est produit.** La conclusion documentaire d'octobre « aucune spécification suffisante identifiée dans les sources consultées » est historique, pas une absence universelle.

Le [site de l'auteur, BankPerfect 9](https://chelly.net/bankperfect9/) renvoie directement au [dépôt fabiochelly/bankperfect](https://github.com/fabiochelly/bankperfect). La [licence MIT](https://github.com/fabiochelly/bankperfect/blob/ea0624c408797acea6cdd58d59a0bb19560908fa/LICENSE), copyright 2026 Fabio Chelly, a été lue. Révision consultée : **`ea0624c408797acea6cdd58d59a0bb19560908fa`**, datée du 9 novembre 2026 dans l'API GitHub. Lecture HTTP/statique seulement : aucun clonage, package, compilation, lancement, décodage de fichier ni code/ressource incorporé à Moneta.

Cette licence concerne le dépôt public consulté ; elle ne démontre pas que tous les binaires/scripts de l'installation historique soient sous cette licence. Toute réutilisation future de code doit vérifier la portée des droits et conserver les notices requises. Moneta reste indépendant ; aucune ressource graphique BankPerfect n'est réutilisable comme identité Moneta par cette décision.

## Sources primaires figées

Les chemins suivants sont relatifs à la révision ci-dessus :

| Source | Apport vérifié par lecture statique |
|---|---|
| [Inventaire du format](https://github.com/fabiochelly/bankperfect/blob/ea0624c408797acea6cdd58d59a0bb19560908fa/docs/inventaire/02-format-fichier-bp.md) | Description de conteneur, champs, encodages, limites et formats anciens ; indique s'appuyer sur des sources Delphi de mars 2019 et un exemple. Ce n'est pas une observation de notre copie 8.0.0.373 |
| [lib.rs](https://github.com/fabiochelly/bankperfect/blob/ea0624c408797acea6cdd58d59a0bb19560908fa/crates/bp-core/src/lib.rs) | Détection de deux familles, V5 historique et V6 nouveau format ; version de conteneur distincte de la version commerciale et du schéma Moneta |
| [codec_v5.rs](https://github.com/fabiochelly/bankperfect/blob/ea0624c408797acea6cdd58d59a0bb19560908fa/crates/bp-core/src/codec_v5.rs) | Codec historique décrit comme substitution dépendant de la longueur et position ; traitement des fins de ligne et décodage Windows-1252 |
| [v5_text.rs](https://github.com/fabiochelly/bankperfect/blob/ea0624c408797acea6cdd58d59a0bb19560908fa/crates/bp-core/src/v5_text.rs) | En-tête, comptes, catégories, tiers modèles, opérations, Extra et données de scripts ; plusieurs politiques de réparation/perte non adoptables silencieusement par Moneta |
| [file.rs](https://github.com/fabiochelly/bankperfect/blob/ea0624c408797acea6cdd58d59a0bb19560908fa/crates/bp-core/src/file.rs) | Ouverture, contrôle de mot de passe et séparation de l'ancien format et du conteneur chiffré moderne ; aucune de ces fonctions n'est appelée ici |

La documentation publique cite des loaders plus anciens UCL/« BPXENCODE » et « Shuffle ». Le `detect_format` Rust consulté sélectionne V5/V6 seulement : **ne pas promettre la couverture de tous les formats anciens** en citant ce lecteur. Les nouvelles protections V6 de BankPerfect 9 ne sont ni le format Moneta ni automatiquement le format des fichiers BankPerfect 8.

## Faits établis dans ces sources, non encore confirmés par fixtures locales

| Domaine | Description publiée / code lu | Validation locale restante |
|---|---|---|
| Conteneur V5 | Texte décodé Windows-1252 ; marqueur logique `BP-FILE-5` après codec. Le code précise que l'en-tête n'est **pas en clair sur le fichier physique** | Signature physique, version effectivement écrite et comportement des fins de ligne sur fichiers synthétiques issus de 8.0.0.373 |
| Structure | En-tête clé/valeur, blocs par compte et sentinelle textuelle ; opérations séparées par lignes et champs `;` | Comptes vides, dernier bloc vide, compteurs et données malformées |
| Comptes | Champs positionnels : nom, monnaies/taux, banque/agence, identifiant, virtuel et désactivé ; catalogue `compteN` | Nombre de champs/version, monnaies réelles, sens des options ; aucun identifiant bancaire d'exemple recopié ici |
| Opérations | Date, état, mode, tiers, détail, catégorie, montant signé, date historique et Extra ; identifiant de ligne dans `UId` | Références, dates limites, duplications légitimes, précision et champs optionnels |
| Montants | Documentation : type Delphi Currency à quatre décimales ; sortie configurée de 0 à 4 décimales, séparateur régional | Les chiffres absents du fichier ne sont pas récupérables par une conversion. Ne pas arrondir à deux décimales pour faire accepter une donnée |
| Solde initial | La documentation décrit une opération « Solde initial » créée avec le compte, supprimable ; pas de champ séparé de solde initial dans les champs de compte lus | Identification fiable, distinction d'une ligne ordinaire et absence de double comptage ; ne pas se fier au seul libellé |
| Dates | Champs numériques historiques ; la documentation désigne `MarkDate` comme date de valeur, initialisée à la date d'opération | Confirmer la sémantique utilisateur. Ne pas importer automatiquement cette valeur comme date de vérification P de Moneta |
| États | Parseur public : P/1 pointé, R rapproché, autres interprétés NP | Conserver la valeur brute ; valeur inconnue à signaler/refuser, pas normalisation silencieuse. Un R ne prouve pas l'existence d'une session bancaire formelle |
| Relations | Scripts locaux et source publique exposent `#sisterAccUid` / `#sisterLineUid` et `UId` | Relations réciproques, identifiants manquants/dupliqués, dates et montants de chaque jambe, frais |
| Données de scripts | Préfixe logique `bp_script_data_` et valeurs sérialisées ; les scripts locaux emploient `cPickle` | Pas de désérialisation exécutable. Formats métier, quotas et décodage inerte à spécifier séparément |
| Annexes | L'audit existant distingue notes/profils externes, scripts et ressources graphiques | Aucune exhaustivité déduite du seul `.bp` ; demander uniquement des annexes synthétiques effectivement utilisées |

**Confirmé dans la référence locale**, en lecture seule : mêmes familles de fichiers que l'inventaire initial ; recherche de `*.bp*` sous `reference-bankperfect/` sans résultat ; l'export XML parcourt les comptes et opérations mais omet notamment UID/liens, dates historiques supplémentaires et données de scripts. Les scripts de transfert écrivent les liens réciproques dans Extra ; les scripts d'échéancier lisent un espace logique sérialisé. Cela ne valide pas à lui seul les octets d'un fichier de comptes.

## Différences de sûreté à ne pas recopier

Le parseur public peut ignorer une ligne illisible, substituer aujourd'hui à une date invalide, remplacer un montant invalide par zéro, générer des identifiants et appliquer des valeurs par défaut. Ces choix sont visibles dans `v5_text.rs`, **pas adoptés par Moneta**. Notre futur staging doit préserver le brut et produire un diagnostic bloquant/partiel explicite, sans fabrication de date, montant, monnaie ou relation. Ses taux utilisent également des flottants ; les règles Moneta `decimal` et exactes restent obligatoires.

La documentation publique et le parseur ne décrivent pas exactement les mêmes garanties de récupération. Leur existence fournit une piste bien plus solide que les seules API de scripts, **pas un oracle de complétude**. Aucun test upstream n'a été exécuté et aucune donnée de leurs fichiers d'exemple n'a été reprise comme corpus synthétique Moneta.

## Corpus et méthode suivante

**Corpus natif local vérifié : zéro fichier dans la référence inspectée.** Aucun original personnel recherché ailleurs, aucun `identity.dat` lu. Les `.moneta` temporaires de qualification ne sont pas des fixtures `.bp`. Les tailles, hashes, offsets et résultats d'analyse binaire du corpus restent **non mesurés / non observés**, jamais zéro par défaut.

Réutiliser les cas **BP-F00 à BP-F22** de [la stratégie existante](BANKPERFECT_IMPORT_STRATEGY.md#imp-06--corpus-strictement-fictif-à-demander-pas-à-fabriquer-hors-bp), sans les renuméroter ni inventer de binaires. Première remise : version/build et extensions, BP-F00/F01/F02/F03, sauvegardes répétées r1/r2/r3, manifeste d'attendus et exports neutres. Ensuite comptes multiples, virements/frais, NP/P/R, catégories/ventilation, devises, échéances/budgets et annexes. À chaque mutation : un seul changement ou snapshots intermédiaires ; deux opérations identiques restent légitimes.

La présence de l'EXE local ne suffit pas à garantir une exécution isolée sans ouverture de profil/données ou automatismes. **Aucun BankPerfect lancé.** Faire produire ce corpus par l'utilisateur dans son usage légitime, ou obtenir une autorisation précise pour un environnement réellement isolé ; pas de modification des protections Windows pour exécuter un binaire.

À réception : établir provenance intégralement fictive, copies fermées/stables et empreintes avant/après ; inspection bornée en lecture seule seulement. Une méthode touchant une protection/mot de passe ou dépassant l'analyse autorisée requiert validation préalable ; aucune tentative n'est faite ici. La lecture d'un code public ne vaut pas consentement au contournement d'une protection d'un fichier réel.

## Porte de sortie vers la mission 10

1. Vérifier avec le titulaire la portée de la spécification publique pour **8.0.0.373** et obtenir le corpus fictif minimal.
2. Confronter les descriptions publiées aux fichiers et aux attendus indépendants ; préciser les versions réellement couvertes, les cas inconnus et les limites de ressource.
3. Valider la [correspondance métier](BANKPERFECT_MAPPING.md), spécialement monnaie, initial, dates historiques, identités et R héritées.
4. Seulement après cette preuve et autorisation : lecteur C# indépendant, passif/borné, staging neutre et rapport ; aucune conversion finale avant contrôles complets et couverture des domaines présents.

**Ne sont pas commencés :** lecteur `.bp`, décodeur de conteneur, reprise de mot de passe, désérialiseur de données historiques, budgets, échéanciers ou migrations de provenance. Le format Moneta reste SQLite schéma 5.
