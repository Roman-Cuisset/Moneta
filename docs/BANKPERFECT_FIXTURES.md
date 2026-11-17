# Corpus BankPerfect — fixtures natives et qualification

## Variantes natives — mission 10E

Les fichiers ont été obtenus sur des copies fictives par les commandes de BankPerfect 8.0.0.373 dans le bac à sable Windows hors réseau, puis examinés avec le lecteur C# et l’importateur Moneta. Les huit empreintes de F00 à F07 correspondent aux sections ci-dessous avant et après les essais. F00, F01, F02, F04, F05, F06 et F07 ont de nouveau été importés. F03 est refusé pour `0,0001 EUR`. Les fichiers, les instantanés et les captures restent dans `private/mission10e-bp8/output`, hors Git.

### BP-F08 — Ouverture négative renommée et homonyme

Sur une copie de F04, ajouter un autre prélèvement NP de 250 EUR à la date de l’ouverture. Renommer le tiers de l’ouverture en « Départ atypique », comme celui du nouveau débit. Accepter la modification de la ligne R dans BP8. Supprimer son pointage, puis la pointer de nouveau. Les trois lignes de −250 EUR font un solde de −750 EUR. Les deux mouvements homonymes gardent les UID distincts 1 et 3. La ligne d’ouverture, créée en premier, apparaît physiquement après les UID 2 et 3 dans les instantanés.

| Instantané | État de l’UID 1 | SHA-256 |
|---|---|---|
| BP-F08-negative-r | R | `510019ED28114FB3EFA9EFD3FFA54B31A4DC027D9079C90E6A6DC36EEAABD66A` |
| BP-F08-negative-np | NP | `3131101A0C399BF249E378FDF3B99EB571EF33159886EE9AF0F1C75EF431F7ED` |
| BP-F08 | P | `85BD9AD8394B5402053F9D2C110D54C4A3CF0C82D4C9DE2165C8D0A24176B71D` |
| BP-F08-positive | R, variante distincte à +1 000 EUR | `D0515DC0F0E6B1AD13943890E2E0E60FB41FC68E0C3DC6D7F3129471C1520457` |

Moneta lit les états et les montants de chaque variante, calcule le solde de toutes les lignes, puis refuse `opening-uncertain` avant création. Seule la suite d’actions BP8 permet d’identifier l’UID 1 comme ouverture avec certitude dans ce scénario. Le nom, la position ou le montant ne fournissent pas cette preuve dans le fichier isolé.

La variante positive part d’une copie de F05. Renommer l’ouverture de 1 000 EUR en « Départ positif » préserve l’UID 1, l’état R et le solde de 1 087,67 EUR. Le même refus est vérifié.

### BP-F09 — Correction et suppression du virement

Sur une copie de F06, créer un quatrième virement de 75 EUR de Courant vers A — Étude. Les jambes UID 9 et 10 ont des réciproques. Depuis le crédit, choisir **Les 2 lignes** après correction du montant à 88,50 EUR, puis répéter pour la date au 18/11/2026 et le tiers du crédit. Pointer le crédit seul, puis utiliser **Rapprocher toutes les lignes pointées**. Le débit reste NP. Enfin, supprimer le crédit depuis le menu Édition et confirmer la suppression de la ligne rapprochée.

| Instantané | Observations sur les deux comptes | SHA-256 |
|---|---|---|
| BP-F09-created | 10 mouvements, quatrième paire à 75 EUR le 17/11/2026 | `350454558E8952DA0CA73C7AF64BC584C3C8AE0398A8B9924D2448BDCF8145E3` |
| BP-F09-amount-both | −88,50 et +88,50 EUR, mêmes UID et réciproques | `145397E20F89016F8B0EE080511FF53CEA2E8C70A7E8E909F5E5D2ADC0C7680F` |
| BP-F09-date-both | Dates d’opération avancées au 18/11/2026 sur les deux jambes. Le champ supplémentaire du débit reste au 17/11 | `5128BCEB41118C7666F47CE45B403B9B953BB389081BAE60BEF067EBD85FCB78` |
| BP-F09-label-both | Le tiers change uniquement sur le crédit. Le lien reste présent dans Extra même lorsque l’icône de lien disparaît sur cette ligne | `2791A1A7BE2E3E4A03F23F00FA547B327B51F452A8FD5CCD26D9454365EFF5DE` |
| BP-F09-pointing | Débit NP, crédit P au 17/11, la date d’opération du crédit reste le 18/11 | `39710F6A2278A8BE06CC9640C69DE4A98B51A520C433AADC06447283FB199EC3` |
| BP-F09-reconciled | Débit NP, crédit R, mêmes montants, dates et UID | `7415C9FEE7DF2FC0F85437B3374700329E51AEDAFA50D1AFCE7B5601555396B6` |
| BP-F09 | UID 10 absent. L’UID 9 demeure et pointe toujours vers le 10. Trois autres paires intactes | `ED825FA6194627C6955B0C429663F589A0407D34CE0CBCB64DC9F7448FDBC62D` |
| BP-F09-both-delete | Copie de l’instantané avant renommage, suppression explicite des deux jambes. Les UID 9 et 10 disparaissent et les trois autres paires restent intactes | `10D7517587705187F35F320B32FDFD7FD960C7F144251F7B359839B56F5AA447` |

Les six instantanés précédant la suppression sont importables. Après correction du montant, les soldes sont de 461,50 EUR et 738,50 EUR. Le fichier orphelin est refusé avant toute création avec `transfer-missing-target` et `transfer-missing-links`. Le plan exige une contrepartie attestée pour chaque lien. Sur la copie avant renommage, **Supprimer** propose les deux lignes. Après confirmation, la conversion retrouve huit mouvements, trois paires, aucun lien vers les UID 9 et 10 et les soldes de 550 EUR et 650 EUR.

### BP-F10 — Pointage manuel et date comptable future

Sur une copie de F05, choisir **Date de pointage** pour la recette de 100,01 EUR (UID 2) et passer du 31/12/2023 au 01/01/2024. Modifier ensuite la date comptable de la même opération au 02/01/2027 et accepter la modification de la ligne pointée. Saisir de nouveau une date de pointage, au 31/12/2026. Cliquer la case de pointage pour passer à R, puis sélectionner **Supprimer le pointage** pour arriver à NP et enregistrer.

| Instantané | Date d’opération | Date supplémentaire | État | SHA-256 |
|---|---|---|---|---|
| BP-F10-manual | 15/01/2024 | 01/01/2024 | P | `EAEE6C3416E74508500741BD3FE85397A1EDDF2C5C8831EA33384B3C6B274C05` |
| BP-F10-future-p | 02/01/2027 | 02/01/2027 | P | `AC505270715615C7A4BEBFFE4965676BD3C8ECEBFB6C2FC3304709EC8A54D36C` |
| BP-F10-year-end-p | 02/01/2027 | 31/12/2026 | P | `2DB18144B5D9229811B268C91B2308694B8E0637B5B5417A62E7C251FA8DAD0E` |
| BP-F10-future-r | 02/01/2027 | 17/11/2026 | R | `081B932C905E14BC99FAE0A5C79BFBA912C73B2AEC0FA645C3CD4150D4C7F98F` |
| BP-F10 | 02/01/2027 | 17/11/2026 | NP | `EC43A13D66FD2F205C5F79A92DE2DB2ACF19B648FEAE5B2F9448BEAF22E006BE` |

Les cinq états ont été importés et restaurés, avec trois lignes et un solde de 1 087,67 EUR. L’ouverture R garde son identité et la nouvelle date d’opération reste exacte. Le changement de date comptable, le passage à R et le dépointage montrent pourquoi la date supplémentaire ne peut pas être interprétée comme une date active dans chaque cas. Moneta garde les états en clair et le champ brut en provenance.

La comparaison au lecteur Rust figé couvre les montants, UID, dates, états et réciproques présents dans ces fichiers natifs. Les tests synthétiques de charge parcourent aussi des dates non monotones et vérifient la cohérence du registre après restauration.

## Qualification native — mission 10D

Les quatre sources F00 à F03 de la campagne historique ont été relues sans écriture. Leurs empreintes ci-dessous concordent avant et après les essais 10D. F00 à F02 ont de nouveau été converties et restaurées. F03 a donné un aperçu bloqué à cause de `0,0001 EUR`. Les deux instantanés précédant l’ajout de cette fraction, `BP-F03-P-auto.bp` et `BP-F03-P-manual-date.bp`, sont convertibles à 1 087,67 EUR et gardent les dates supplémentaires distinctes de la date comptable.

Les nouveaux fichiers fictifs sont sous `private/mission10d-bp8/output/Native`, répertoire local ignoré par Git. Création ou enregistrement avec BankPerfect 8.0.0.373 dans un bac à sable hors réseau. SHA-256 de l’exécutable `E81FA1973C2DC26C70DD596181B00208711917144B13D9DBD43AB2BF8216A0C6`.

| Fichier | Actions et attentes dans BP8 | Résultat Moneta | SHA-256 de qualification |
|---|---|---|---|
| BP-F04 | Créer le compte avec −250 EUR. Saisir un prélèvement NP de −250 EUR le 31/12/2024. Le débit NP précède physiquement l’ouverture R du 17/11/2026 | Deux lignes, solde −500 EUR, pointé −250 EUR, ouverture technique zéro | `ECAFE0FE4D56728A9D6F82C147CD29566A1247BEF586CF1357107C48014CCB09` |
| BP-F05 | Ouvrir une copie du pointage manuel historique, sélectionner la recette P de 100,01 EUR datée du 15/01/2024. Choisir **Date de pointage**, saisir 31/12/2023, enregistrer | Trois lignes NP/P/R, solde 1 087,67 EUR, pointé 1 100,01 EUR, champs bruts `45306` et `45291`, date de pointage cible vide | `D79BB048736EBBD20FAAF9BE890A991AE5AE4792C5B5B9D9684A2DE44E48C52E` |
| BP-F06 | Ouvrir une copie de F02. Avec le formulaire Transfert, ajouter deux virements de 150 EUR de Courant à A — Étude le 17/11/2026 | Huit lignes, trois virements, soldes 550 et 650 EUR, paires réciproques 3/4, 5/6 et 7/8. Les six jambes restent NP | `E475AD82783E123AA4C5AB3069EAA6A9A3743CA4121A92496F3BAD4A0A147E84` |
| BP-F07 | Ouvrir une copie de F05. Dans **Gestion des catégories**, insérer Énergie en racine et sous Salaire et Alimentation. Conserver les lignes avec tiers et modes intégrés | 37 catégories dont trois Énergie, trois UUID distincts, parents d’origine, trois lignes, 1 087,67 EUR | `912D865E8417C478A75B370B2C7F6638EFD52CE91F4B191EB513FF77E70390F6` |

Les comparaisons du lecteur C# au Rust figé couvrent comptes, UID, valeurs exactes (coefficient Rust à quatre décimales), dates, états, tiers, modes, catégories de ligne et réciproques sur F04 à F07. Aucune divergence sur ces champs. La qualification du dossier Moneta contrôle chaque mouvement et les soldes. Les quatre conversions ont passé la réouverture, la vérification de provenance, la sauvegarde et la restauration. Les UUID et parents des 37 catégories ont également été comparés aux positions et parents de F07.

Variantes encore à produire dans BP8. Modifier le libellé d’une ouverture sur une copie et tenter de retirer son pointage, puis vérifier si l’identité de nature reste attestée. Sur une copie de F06, créer des comptes aux noms proches, modifier ou supprimer une paire dans BP8, et enregistrer une paire dont les jambes ont des états distincts. Les cas synthétiques d’UID cassés, d’états P/R indépendants et de protection d’une paire ont une preuve automatisée distincte. La date bissextile du 29/02/2000 est présente dans F03, bloqué pour précision. Un cas bissextile convertible et une date d’opération future sont exercés sur des données synthétiques.

Pour compléter F07, enregistrer sur une copie une opération dans chacune des branches homonymes, un mode de paiement personnalisé et un modèle de tiers utilisé par une opération. La conversion de modes et la conservation brute de modèles de tiers ont des épreuves synthétiques. Les champs Extra de transfert de F06 servent à établir les liens. Les Extra inconnus de portée financière font l’objet de refus synthétiques.

## Provenance et conservation

**BP-F00 à BP-F03 ont été produits avec BankPerfect 8.0.0.373, sauvegardés par son propre moteur et comparés au lecteur Rust figé.** Aucun `.bp` fabriqué avec notre code ou `codec_v5::encode`. F03 dérive d'une copie binaire stable de F01, ensuite modifiée et sauvegardée dans BP8.

L'EXE utilisé et celui extrait du paquet officiel ont le même SHA-256 : `e81fa1973c2dc26c70dd596181b00208711917144b13d9dbd43ab2bf8216a0c6`. Installation originale et `reference-bankperfect/` inchangées. Le `identity.dat` de référence n'a été ni lu ni utilisé ; une ressource de distribution propre a été extraite du [paquet officiel](https://chelly.net/bankperfect/bp.exe), sans lire son contenu.

Windows Sandbox vérifié sous `WDAGUtilityAccount`, culture **fr-FR**, page ANSI **1252** ; réseau, presse-papiers, imprimantes, microphone et caméra désactivés. Seules une entrée de recherche readonly et une sortie synthétique dédiée sont mappées. Documents du **seul invité** redirigé vers cette sortie avant création ; aucune redirection de dossier hôte.

La copie minimale avait échoué faute d'`identity.dat`. Deux essais silencieux du paquet avaient renvoyé 2 ; le mode interactif a finalement permis l'installation dans le dossier de recherche. Une première sonde a été interrompue avant export de F00 : sa capture seule n'est pas comptée comme fixture livrée. La reprise a écrit immédiatement les fichiers natifs dans la sortie persistante. BP8 a été fermé proprement, puis le seul invité propriétaire arrêté ; aucune instance utilisateur Moneta ou BankPerfect fermée.

Corpus, snapshots, manifeste, journaux JSON du pilote et captures **hors Git** :

`%USERPROFILE%\Documents\Moneta-research\mission10a\bp8-persisted-4a37e33f129e48c4baa0e10355941b16\output\`

`Native/` contient les quatre fichiers ; `Snapshots/` les états intermédiaires ; `BP-Fxx-reader.json`, `manifest.json` et `corpus-checkpoint.json` les résultats privés de recherche. Aucun de ces fichiers ni aucune ressource propriétaire n'est distribué dans Moneta.

## Namespace et ancien protocole

Campagne : **`moneta.bp-study.10a.v1`**. L'ancien `moneta.bp-study.v1/BP-F00..BP-F22` reste dans [IMP-06](BANKPERFECT_IMPORT_STRATEGY.md#imp-06--corpus-strictement-fictif-à-demander-pas-à-fabriquer-hors-bp), sans renumérotation.

| Cas 10A | Correspondance avec l'ancien protocole |
|---|---|
| BP-F00 | Ancien F00, minimum accepté par Nouveau |
| BP-F01 | Ouverture F01, mouvements proches de F02/F03 |
| BP-F02 | F04/F05, comptes multiples et transfert |
| BP-F03 | F06/F07, date bissextile et précision ; **pas** ancien F03 |

## Inventaire mesuré

Toutes les monnaies natives sont le symbole **€**. « Lignes » inclut l'ouverture technique.

| Fixture | Octets | Comptes | Lignes / mouvements métier | Solde(s) natif(s) exact(s) | Lecture Rust |
|---|---:|---:|---|---|---|
| BP-F00 | 609 | 1 | 1 / 0 | Courant : 0,00 EUR | V5 ; un UID de ligne généré avec avertissement |
| BP-F01 | 1 015 | 1 | 3 / 2 | Courant : 1 087,67 EUR | V5 ; aucun avertissement |
| BP-F02 | 1 517 | 2 | 4 / 2 jambes | Courant : 850,00 ; A — Étude : 350,00 EUR | V5 ; relations réciproques vérifiées, aucun avertissement |
| BP-F03 | 1 099 | 1 | 4 / 3 | Courant : **1 087,6701 EUR** | V5 ; NP/P/R, dates et quatre décimales conservés, aucun avertissement |

| Fixture | SHA-256 avant/après lecture Rust et après fermeture de BP8 |
|---|---|
| BP-F00 | `95cf5eba7ccf9fe53ff4b7539f27e6e1286003032cfc6b2316b4c882d9a2a04d` |
| BP-F01 | `a38894d9b20b1df119eda68203b76b1531f7e2242eb5415e1193949c0f507436` |
| BP-F02 | `2fc20d990eabf6f2f7189f13e5972ebe8c653a54c3a52a9f9e7a2fd5cc8405a3` |
| BP-F03 | `c97285ee4c1d6648fdfdd30ef4b9fde3fdc0352d73ba313bf34cfde4b6b4fd3f` |

Empreintes **identiques après lecture**, sans sauvegarde ni UI BP9. Un hash contrôle la stabilité, pas la complétude métier ou l'authenticité cryptographique de V5.

## BP-F00 — minimum réellement accepté

Actions : **Comptes → Créer un nouveau fichier de comptes**, nom `BP-F00`, ouverture explicitement saisie `0,00`.

UI : compte `Courant`, €, solde et pointé 0,00 ; ligne « Création du compte courant », date 2026-11-11, mode Versement. **Zéro mouvement métier mais une ligne technique nulle** ; aucun zéro-compte forcé.

Fichier de création : clés `utilisateur`, `compte0`, `categ`, puis bloc avec sept champs :

`46337;R;40;Création du compte courant;;-1;0,00`

Date supplémentaire, UID de ligne, GUID et UID de compte absents dans cet état initial. Le lecteur attribue l'UID 1 avec avertissement ; sa `mark_date` est un repli, **pas une date stockée prouvée**. R est créé sans relevé bancaire : ne pas en fabriquer un à l'import.

## BP-F01 — ouverture, deux mouvements, accents et catégorie

1. Nouveau `BP-F01`, ouverture **1 000,00** ; compte réellement nommé `Courant`.
2. **Comptes → Gestion des catégories → Insérer une catégorie** : `Vie`, ID **33**.
3. Ligne compacte : **15/01/2024**, **Versement**, tiers `Revenu Démo`, détail `Recette d'étude`, Vie, **100,01** ; Entrée dans Montant ajoute une ligne. Sauvegarde native, snapshot intermédiaire.
4. **16/01/2024**, **Carte**, `Café Démo`, `Été à Noël`, Vie, magnitude **12,34** ; Entrée ajoute la dépense, sauvegarde native.

Attendu puis observé : 1 000,00 → 1 100,01 → **1 087,67 EUR**, UI et fichier concordants. Ouverture R/UID 1, recette NP/2, dépense NP/3. Dates supplémentaires des NP égales aux dates d'opération ; les deux mouvements référencent 33. Le lecteur conserve −12,34, pas une magnitude positive déduite du nom du mode.

Accents vérifiés avant validation. Une première saisie JSON du pilote avait affiché du mojibake, corrigé avant ajout par des commandes ASCII à échappements Unicode ; **pas une corruption BP8**. Les tiers sont libres dans les lignes, pas des modèles `whoN` qualifiés. L'ordre physique sauvegardé ne permet pas d'identifier l'ouverture comme « première ligne ».

## BP-F02 — deux comptes, deux jambes liées

1. Nouveau `BP-F02`, ouverture 1 000,00 ; **Gestion des comptes → Ajouter un compte**, ouverture 200,00, € conservé.
2. Noms effectivement écrits : **Courant** pour 1 000,00 et **A — Étude** pour 200,00. Les intentions de renommage A/B du pilote n'ont pas été établies dans le fichier : aucun « B — Réserve » inventé. Cause du défaut de validation des noms par le pilote non qualifiée.
3. Sauvegarde puis réouverture native **avant** transfert : l'ouverture du second compte n'avait pas d'UID. BP8 a réattribué ses identifiants à cette étape, sans relation de transfert préexistante.
4. Extension locale **Transfer/transfer.py**, rôle/source examinés avant exécution isolée ; barre **Transfert** : Courant → A — Étude, Virement, **Alimentation**, **150,00**, `Transfert d'étude`. Date effectivement affichée/utilisée **2026-11-11**, pas le 2024-01-20 du plan initial.
5. Validation et sauvegarde par BP8, jamais par Rust.

Attendus/lecture : Courant **−150 → 850,00**, destinataire **+150 → 350,00** ; total homogène 1 200,00. Vue du destinataire contrôlée ; deux montants natifs et soldes extraits comparés aux attendus.

Jambes UID **3 et 4**, `#sisterAccUid` et `#sisterLineUid` vérifiés **dans les deux sens**. UID de comptes opaques : leurs parties numériques ne sont pas une position fiable.

L'ouverture du second compte est aussi un **Virement reçu**, R, « Solde initial », **sans lien** : ne pas classifier sur le seul mode/nom. Donnée `fabiobptransfer` : **117 octets** conservés comme données inertes ; aucun pickle exécuté par le lecteur de diagnostic.

## BP-F03 — pointage, hiérarchie, date et précision limites

Parent : copie stable de F01 puis modifications/sauvegardes dans BP8. Nom de groupe/GUID hérités ; aucune nouvelle identité source inventée.

### Pointage isolé

- Recette UID 2 : opération **2024-01-15**, NP, date supplémentaire 2024-01-15 au départ.
- Clic unique colonne de pointage → **P**, sauvegarde : opération/montant inchangés, date supplémentaire **2026-11-11**.
- **Opérations → Date de pointage**, dialogue « Saisissez la date de pointage », saisie `31/01/24`, sauvegarde : champ supplémentaire relu **2024-01-31**, opération toujours 2024-01-15.
- Dépense UID 3 NP et ouverture UID 1 R inchangées. Avant cas limite : solde **1 087,67**, pointé **1 100,01**, NP **−12,34 EUR**, UI cohérente avec les lignes.

Conclusion **limitée au build/gestes exercés** : le huitième champ stocke ici la date de pointage. Description Delphi 2019 « valeur » non imposée à ce corpus ; autres versions/usages à qualifier. Une NP contient néanmoins cette colonne : pas de `MarkedOn` Moneta non nul fabriqué pour NP.

### Sous-catégorie réelle

Gestion des catégories, sélection/insertion puis `Énergie`. UI et fichier donnent **Salaire (30) → Énergie (34)**, après Paye/Revenus annexes et avant la racine Vie (33), **pas Vie → Énergie comme envisagé**. Brut : `34=     Énergie`, `is_child=true`, parent précédent correctement retrouvé.

Définition **inutilisée dans les lignes**. Cas utile pour préserver les catégories non référencées et leur ordre/indentation : l'ID numérique seul ne définit pas le parent. Attribution d'une opération à cette sous-catégorie restant à tester.

### Date bissextile et fraction sous le centime

**Outils → Options → Général → Précision (décimales)** : 2 observé, deux incréments puis **4** vérifié. Nouvelle recette par ligne compacte : **29/02/2000**, Versement, `Limite Démo`, `Quatre décimales`, Vie, **0,0001** ; ajout puis sauvegarde.

Ligne UID **4**, NP, date entière **36585**, date relue **2000-02-29**, montant **1 unité de 10⁻⁴**. Solde **1 087,6701 EUR** affiché et relu ; pointé **1 100,0100**, net NP **−12,3399**. Autres montants et date de pointage de la recette conservés.

**Non représentable en centimes EUR dans Moneta actuel.** Le futur import doit diagnostiquer/bloquer ou attendre une règle approuvée ; aucun arrondi ni changement silencieux de précision de devise.

## Portée de la confrontation et limites

Quatre fichiers : V5, comptes/lignes, sommes entières ×10⁻⁴, dates, textes, modes, états, catégories/références ; graphe réciproque F02, mutations de date et hiérarchie F03. Assertions au-delà des seuls soldes, fichiers inchangés après lecture.

Façade Rust appelée par une sonde externe, sans `file::save`, conversion V6, UI BP9 ou exécution de règles/payloads. Valeurs générées/réparées distinctes des données natives présentes.

**Non exercés :** relevé bancaire formel, échéances, budgets, ventilation, modèles de tiers, transferts BP multidevises, autres pages ANSI, protections/UCL/Shuffle, fichiers natifs malformés, sauvegardes répétées r1/r2/r3 d'un même état. Les snapshots ne couvrent ni tout F00..F22 ni tous les BP8.

## Qualification C# — mission 10B

Même corpus, mêmes SHA-256. Les lectures C# et Rust ont été rejouées. Le tableau compte les tiers distincts présents dans les lignes, ouverture comprise. Les quatre fichiers contiennent zéro modèle `whoN`.

| Fixture | Octets | Comptes | Lignes | Catégories | Tiers distincts | NP / P / R | Virements liés | Sommes sources et réserves |
|---|---:|---:|---:|---:|---:|---|---:|---|
| BP-F00 | 609 | 1 | 1 | 33 | 1 | 0 / 0 / 1 | 0 | 0,00 EUR. UID de dossier, compte, ligne et date supplémentaire absents. Catégories à position historique sans ID explicite |
| BP-F01 | 1 015 | 1 | 3 | 34 | 3 | 2 / 0 / 1 | 0 | 1 087,67 EUR. Accents, signe de la dépense et références de catégorie concordants |
| BP-F02 | 1 517 | 2 | 4 | 33 | 4 | 2 / 0 / 2 | 1 | 850,00 et 350,00 EUR. UID 3 ↔ 4 réciproques. Extension de 117 octets conservée encodée et opaque |
| BP-F03 | 1 099 | 1 | 4 | 35 | 4 | 2 / 1 / 1 | 0 | 1 087,6701 EUR. Opération UID 4 à 0,0001 EUR non représentable en centimes |

La comparaison couvre les comptes, devises, lignes et états, montants en unités de 10⁻⁴, textes, modes, dates, catégories et liens. En F00, Rust génère les identifiants manquants et reprend la date d’opération comme date de pointage. C# expose leur absence ainsi que les positions distinctes des catégories de l’ancien format. En F03, UID 2 conserve **2024-01-15** et **2024-01-31**. La ligne UID 4 conserve **2000-02-29**, et la catégorie Énergie (34) reste enfant de Salaire (30). Les ouvertures sont établies par les gestes suivis dans les fiches. Le lecteur qualifie les candidates comme probables à partir des seules lignes, y compris celle qui apparaît après les mouvements.

La sonde négative Rust figée a également été rejouée. En Debug elle substitue la date du jour et zéro, interprète `1-2` comme 12, régénère le doublon `7` en `8` et panique sur la clé `compté0` et l’UID `UInt64.MaxValue`. Les tests C# couvrent ces entrées ou leurs équivalents encodés, refusent les valeurs illisibles, conservent les doublons et l’UID maximal, et acceptent la clé inconnue en tant que donnée brute. Ils couvrent aussi troncature, encodage, version incompatible, limites de collections et références cassées.

Le Rust sondé refuse également `-922337203685477.5808`. C# conserve la valeur exacte qui correspond à la borne minimale signée du type historique à l’échelle 10 000 et diagnostique sa précision excessive en centimes EUR. Ce résultat concerne une épreuve synthétique.

Les builds Debug et Release se terminent sans erreur ni avertissement, et chaque configuration exécute **561 tests réussis**, dont les 516 existants et 45 nouveaux cas. Le moteur financier, l’application WPF et le schéma SQLite 5 sont identiques à leur état de départ.

Pour rejouer la qualification locale après génération de la CLI, fournir le dossier des quatre fichiers fictifs et, pour la comparaison, la sonde `moneta10a_fixture_dump.exe` construite sur la révision figée. Le script vérifie les SHA-256 et affiche uniquement les résultats d’assertion.

```powershell
./tools/Test-BankPerfectV5Corpus.ps1 -CorpusDirectory $corpus -RustProbe $sonde
```
