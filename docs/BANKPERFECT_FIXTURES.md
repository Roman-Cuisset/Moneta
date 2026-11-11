# Corpus BankPerfect — fixtures réellement produites en mission 10A

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

Suite : compléter ces cas, approuver ouverture/date/monnaie/provenance et le traitement bloquant de F03 avant conversion financière. **Aucun importeur Moneta ni début de mission 10B.**
