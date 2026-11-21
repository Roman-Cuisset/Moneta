# Qualification du candidat Moneta Alpha 0.1.0

## Artefact et installation

Candidat Inno Setup 6.7.4 de **63 379 873 octets**, avec WindowsDesktop .NET 10.0.12 intégré et les notices de redistribution. Le SHA-256 de l’installateur essayé dans la VM est le suivant.

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

- Builds Debug et Release avec zéro erreur et zéro avertissement. **672 tests réussis dans chaque configuration**, dont six nouveaux sur le catalogue, les clés, les versions, les redirections, les tailles, l’annulation et le refus d’une empreinte incohérente.
- Les installateurs de production et de laboratoire ont été vérifiés avec le moteur Moneta. Les contrôles couvrent le catalogue signé, le SHA-256, la version PE et l’impossibilité d’écrire le fichier pendant sa vérification. La clé de production refuse le catalogue du laboratoire et toute signature altérée.
- Le dossier fictif de Windows dérive du parcours WPF antérieur et de sa sauvegarde vérifiée. Le parcours de base inclut les comptes EUR/BYN/RUB, le registre, le virement, le pointage et le rapprochement. Une sonde Release sur l’hôte a restauré puis rouvert cette même sauvegarde au schéma 8. Les trois comptes et l’échéancier ont été retrouvés et l’empreinte de l’archive source est identique.

## Audit avant ouverture du dépôt

Le point de départ `084da59` et les quatre références disponibles comptent **39 révisions et 229 chemins historiques**. L’inspection des objets n’a retrouvé aucun fichier financier Moneta ou BankPerfect, aucune archive de test et aucun binaire d’installateur suivi. La recherche de marqueurs de clés privées, de jetons GitHub et d’identifiants de services courants est restée négative. Les seules images historiques suivies appartiennent aux exports de marque. La clé publique Moneta est destinée à la distribution.

Les métadonnées d’auteur et de commit exposent une adresse **Gmail dans les 39 révisions préexistantes**, de `5576c66` à `084da59`. Cette exposition doit être décidée avant toute ouverture du dépôt. L’historique a été conservé. L’audit porte sur les références et les marqueurs examinés, et les licences des composants redistribués sont incluses dans le paquet.
