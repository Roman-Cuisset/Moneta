# Qualification du candidat Moneta Alpha 0.1.0

## Candidat retenu pour la publication

La Mission 18B a ajouté la notice MIT du codec BankPerfect V5 au paquet. Inno Setup 6.7.4 a réassemblé le candidat à partir des binaires déjà qualifiés. Tous les fichiers applicatifs hors `ThirdPartyNotices` ont été comparés octet à octet. Le lot retenu est `private/mission18b/candidate/artifacts`, sur le poste de qualification.

| Fichier | Taille | SHA-256 |
| --- | ---: | --- |
| `Moneta-0.1.0-win-x64.exe` | 63 380 176 octets | `b367f55d5412d594f925611e24cf1ea6d520e86b56428af3042cef66630db4ae` |
| `moneta-update.json` | 2 029 octets | `0a518befe8cfee671cccade2da97de23620eb8c1d7efe78c2ab59b8063ca87af` |

Le catalogue ECDSA P‑256 est vérifié par la clé de production du candidat. Il authentifie l’installateur Windows x64 et les notes « Quoi de neuf ». L’URL de téléchargement est déduite du canal HTTPS et de la version. Le format 1 définit l’ordre des mises à jour par version. Il prévoit sept champs et aucune date d’émission.

L’installateur est déclaré `NotSigned` par `Get-AuthenticodeSignature`. « Moneta » dans les métadonnées Windows est le nom déclaré de l’éditeur. La réputation SmartScreen dépend notamment de la provenance du téléchargement et de la politique Windows. La VM a installé une copie locale vérifiée. Un téléchargement via navigateur a été préparé sur le serveur loopback et n’a pas été observé pendant l’essai. Le comportement exact de SmartScreen et la marque Internet du téléchargement GitHub devront être contrôlés lors de la première publication. [Microsoft SmartScreen](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)

## Installation et préservation des dossiers en 18B

La VM Windows 11 Pro x64 existante a installé le paquet corrigé sous le compte standard. Les fichiers installés comparés au paquet comprennent les binaires Moneta, le moteur de mise à jour et la notice ajoutée. Le contrôle a retrouvé l’enregistrement HKCU 0.1.0, le raccourci Démarrer, l’icône et le runtime incorporé. Les binaires de cette installation ont restauré, rouvert et sauvegardé à nouveau le dossier fictif au schéma 8. Ils ont retrouvé les comptes EUR/BYN/RUB et l’échéancier. Le fichier source et son manifeste ont gardé leurs empreintes. Les deux checkpoints préexistants ont été conservés.

La reprise ciblée du scénario de mise à jour Lab a utilisé 0.1.1 → 0.1.2 avec le même ajout de notice. Le serveur loopback a observé les demandes du catalogue et de l’installateur. Le contrôle indépendant a relevé le fichier en version 0.1.2.0, l’enregistrement HKCU en 0.1.2 et Moneta Lab relancé avec la notice installée. Les empreintes du dossier fictif et de son manifeste sont restées identiques. Le canal HTTPS de production refuse la clé du Lab.

## API officielles et cache

Le 22 novembre 2026, les API NBRB et CBR ont répondu en HTTPS sur l’hôte pour une date historique et une date courante, avec DNS et validation TLS. Une sonde a chargé les binaires exacts du candidat et appelé le client officiel, la session de change de Moneta et le résolveur historique. Elle a observé la persistance des réponses, puis les a relues depuis un nouveau processus sans source réseau. [API NBRB](https://www.nbrb.by/apihelp/exrates) · [API CBR](https://www.cbr.ru/development/sxml/)

| Fournisseur | Date demandée | Date d’effet | Extrait des cotations observées |
| --- | --- | --- | --- |
| NBRB | 17/01/2026 | 17/01/2026 | 1 EUR = 3,3824 BYN · 100 RUB = 3,7144 BYN |
| NBRB | 18/01/2026 | 18/01/2026 | Valeurs du 17 janvier reconduites dans la réponse de la date demandée |
| NBRB | 22/11/2026 | 22/11/2026 | 1 EUR = 3,2913 BYN · 100 RUB = 3,6581 BYN |
| CBR | 17/01/2026 | 17/01/2026 | 1 EUR = 90,5366 RUB · 100 JPY = 49,1992 RUB |
| CBR | 18/01/2026 | 17/01/2026 | Cours du 17 janvier explicitement applicable, réponse identique |
| CBR | 22/11/2026 | 21/11/2026 | 1 EUR = 90,3445 RUB · 100 JPY = 51,5402 RUB |

En panne simulée, Moneta a conservé les dates d’effet et signalé le recours au cache quand une donnée applicable était présente. Pour une date sans donnée applicable, le résultat est resté indisponible. Dans la VM, la résolution DNS de `api.nbrb.by` a interrompu l’essai réseau avant l’appel CBR. L’adaptateur a retrouvé son état déconnecté initial. Les essais en ligne positifs reposent sur l’hôte et les binaires du candidat. Le test hors réseau de la VM a confirmé le signalement d’indisponibilité avec un cache vide.

## Qualification initiale de l’installateur

Candidat initial de Mission 18 de **63 379 873 octets**, avec WindowsDesktop .NET 10.0.12 intégré. Son SHA-256 est conservé comme référence pour les essais historiques.

```text
7406bf3a534d3b0e6895303a786ca5ea572aa08c539ff651e79c58a0ebb4d7fb
```

Un deuxième build en sortie isolée a reproduit à l’octet les quatre binaires comparés (`Moneta.App.exe`, `Moneta.App.dll`, `Moneta.Updates.dll` et `Moneta.UpdateHost.exe`). L’enveloppe Inno du second build a une taille et une empreinte différentes. Le catalogue signé correspond à l’installateur effectivement transféré et essayé.

La VM existante `MCW-Win11-x64-Lab` utilise **Windows 11 Pro x64, build 26300**. L’installation silencieuse, la désinstallation et la réinstallation ont été exécutées sous le compte Windows standard, sans SDK .NET visible. La version Windows, l’enregistrement par utilisateur dans HKCU, la cible du raccourci Démarrer et l’icône PE 32 × 32 ont été vérifiés. Le fichier financier fictif et le manifeste de sauvegarde sont restés identiques. La VM n’avait pas de commutateur réseau attaché. L’état initial a été conservé dans le checkpoint **Avant essais Moneta Alpha 0.1.0**. Le checkpoint préalable **01 - Windows 11 x64 propre** est toujours présent. Les essais se limitent à Moneta dans le profil de qualification.

## Cycle de mise à jour réel dans la VM

Le profil Moneta Lab 0.1.0 a détecté au démarrage le catalogue signé sur le serveur loopback. La fenêtre a affiché la version 0.1.1 et le texte signé « Quoi de neuf ? ». Après confirmation depuis Moneta, le serveur a observé le téléchargement complet. Le relevé indépendant donne **0.1.1.0** dans le fichier, **0.1.1** dans HKCU et un processus Moneta Lab actif après la sortie du lanceur. L’installation de production coexistante reste inscrite en 0.1.0. Le SHA-256 du fichier fictif et celui du manifeste correspondent aux valeurs antérieures à l’essai.

Après l’essai de restauration dans la VM, les captures utilisateur montrent la copie rouverte, les comptes EUR/BYN/RUB, le registre EUR de 109 opérations, le virement lié de 75 BYN et le solde natif de 575 BYN. Sans cours en cache pour cette paire, Moneta signale la conversion indisponible et affiche toujours le solde natif. Les captures restent locales.

La qualification utilise un catalogue HTTP local et une autre clé que la production HTTPS. Les refus de signatures et de fichiers altérés ont été testés séparément. La concurrence entre deux programmes d’installation, le refus de SmartScreen et une coupure pendant le remplacement des binaires restent des cas de terrain. La distribution Authenticode et la réputation SmartScreen n’ont pas été qualifiées.

## Contrôles techniques

- Builds Debug et Release de 18B avec zéro erreur et zéro avertissement. **673 tests réussis dans chaque configuration**, dont un test ajouté sur le refus d’un téléchargement interrompu avant la taille déclarée.
- Les suites couvrent les métadonnées et signatures modifiées, la mauvaise clé, l’architecture, les adresses et redirections inattendues, les tailles, l’annulation et la rétrogradation. Sur le candidat scellé, la sonde a vérifié les notes et les URL de production. Elle a aussi obtenu le rejet de l’installateur altéré à taille égale et celui d’une version PE incohérente malgré le bon fichier et la bonne empreinte.
- La préférence de vérification au démarrage est activée par défaut et modifiable dans les options. La vérification automatique est différée et asynchrone. Le menu d’aide conserve la recherche manuelle et la vérification automatique reste silencieuse quand la version est déjà courante.
- Les installateurs de production et de laboratoire ont été vérifiés avec le moteur Moneta. Les contrôles couvrent le catalogue signé, le SHA-256, la version PE et l’impossibilité d’écrire le fichier pendant sa vérification. La clé de production refuse le catalogue du laboratoire et toute signature altérée.
- Le dossier fictif de Windows dérive du parcours WPF antérieur et de sa sauvegarde vérifiée. Le parcours de base inclut les comptes EUR/BYN/RUB, le registre, le virement, le pointage et le rapprochement. Une sonde Release sur l’hôte a restauré puis rouvert cette même sauvegarde au schéma 8. Les trois comptes et l’échéancier ont été retrouvés et l’empreinte de l’archive source est identique.

## Audit avant ouverture du dépôt

Le point de départ `084da59` et les quatre références disponibles comptent **39 révisions et 229 chemins historiques**. L’inspection des objets n’a retrouvé aucun fichier financier Moneta ou BankPerfect, aucune archive de test et aucun binaire d’installateur suivi. La recherche de marqueurs de clés privées, de jetons GitHub et d’identifiants de services courants est restée négative. Les seules images historiques suivies appartiennent aux exports de marque. La clé publique Moneta est destinée à la distribution.

Les métadonnées d’auteur et de commit exposent une adresse **Gmail dans les 39 révisions préexistantes**, de `5576c66` à `084da59`. Le propriétaire a autorisé la visibilité de cette adresse lors d’une future ouverture du dépôt. L’historique a été conservé.

L’audit 18B couvre les **41 révisions et 254 chemins** accessibles au point de départ `d98f7b6`, ainsi que les modifications de qualification. Les chemins et marqueurs inspectés ne révèlent aucun dossier financier, fichier BankPerfect privé, archive, binaire de test, capture privée, clé privée ou jeton. GitHub est resté privé. L’inspection a trouvé zéro release et zéro exécution Actions à ce stade. Le projet ne porte actuellement aucune licence générale. Les conditions de distribution et les notices tierces sont détaillées dans la procédure, qui inclut le guide, la vérification du brouillon et les commandes de publication. [Procédure de publication](ALPHA_RELEASE.md)
