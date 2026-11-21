# Préparer Moneta Alpha 0.1.0

## Construction du candidat Windows

Le paquet Inno Setup 6.7.4 contient Moneta, le runtime WindowsDesktop .NET 10 x64 et le lanceur de mise à jour autonome. L’application s’installe dans `%LOCALAPPDATA%\Programs\Moneta`, par utilisateur, avec un raccourci Démarrer et une entrée de désinstallation dans HKCU. Les dossiers financiers restent à l’emplacement choisi, et les préférences, sauvegardes et cache demeurent dans les données du profil. Les migrations financières utilisent les copies contrôlées de Moneta.

```powershell
$compiler = & ./tools/Get-MonetaBuildTools.ps1
$artefacts = & ./tools/Build-MonetaCandidate.ps1 -Version 0.1.0 -InnoCompiler $compiler | Select-Object -Last 1
& ./tools/Sign-MonetaCandidate.ps1 -ArtifactDirectory $artefacts
```

Les trois scripts vérifient l’éditeur du compilateur, le runtime et la version du produit, les notices, les empreintes et la correspondance de la clé de signature. Les binaires et fichiers de qualification restent dans `private/mission18`. Le même script repart de sorties isolées à chaque exécution. Comparez les versions de SDK et de compilateur, les entrées et le résultat. Les horodatages et signatures font partie des limites de la comparaison octet à octet.

**Fichiers de livraison.** `Moneta-0.1.0-win-x64.exe`, `SHA256SUMS.txt`, `moneta-update.json` et les notes de version. Le fichier `moneta-update.unsigned.json` sert au contrôle et à la signature. Il reste hors des assets publiés. Le moteur SQLite du candidat est celui du schéma 8 qualifié dans les missions précédentes.

## Mises à jour et contrôle de sécurité

La production pointe vers la dernière release publique du dépôt Moneta en HTTPS. Le client valide le catalogue avec une clé publique ECDSA P‑256 intégrée, limite les redirections aux hôtes GitHub nécessaires et refuse les versions égales ou antérieures. Le catalogue authentifie la taille, le SHA‑256 de l’installateur, le produit, l’architecture, la version et le texte de publication. Le lanceur répète les contrôles avant de lancer le fichier via Windows. Il garde le fichier ouvert en lecture pour empêcher son remplacement entre le contrôle et le lancement. Les téléchargements de production conservent la marque Internet pour les vérifications de Windows.

Moneta propose d’enregistrer les brouillons, attend la fin des sauvegardes et ferme le dossier. Le lanceur n’agit qu’après la décision de fermer et la fin du processus. L’installation attend sa propre fin avant de vérifier la version et relancer Moneta. En cas d’annulation ou d’échec, le résultat est affiché au redémarrage si l’application demeure disponible. L’installateur impose le dossier de programme, refuse la rétrogradation et demande la fermeture des autres instances.

La clé privée de production est non exportable dans le magasin CNG du compte signataire Windows, sous `Moneta.Release.Alpha.P256`. Git et les Actions disposent uniquement de la clé publique. Préserver l’accès au compte signataire fait partie de l’exploitation du canal Alpha. Si cet accès est perdu, une nouvelle chaîne de confiance et une réinstallation vérifiée seront nécessaires. Une signature Authenticode du binaire, si disponible, doit précéder le calcul définitif des empreintes et la signature du catalogue. Le candidat utilise actuellement la signature du catalogue et laisse Windows appliquer ses propres contrôles d’exécution.

Le laboratoire compile une autre clé, un autre identifiant d’installation et une URL fixe `http://127.0.0.1:18761/`. Les fichiers lab restent réservés à l’essai isolé 0.1.0 → 0.1.1. La clé de production signe uniquement les candidats issus du profil production.

## Publication après autorisation

Le workflow manuel **Préparer un candidat Moneta** compile, teste et produit des artefacts de contrôle sans clé privée. Le signataire prépare le catalogue définitif sur le poste de signature. Après accord sur l’audit et sur l’ouverture du dépôt, les notes et l’installateur vérifiés alimentent le tag `v0.1.0`, puis les futures releases. Le texte de la release doit correspondre aux notes signées. Un push sur `main` ne déclenche aucune publication.

Pour le canal configuré, publier l’Alpha comme la release GitHub marquée **Latest**, avec « Alpha » dans le titre. Le lien `releases/latest` de GitHub sélectionne une release publique normale. [Comportement de Latest](https://docs.github.com/en/rest/releases/releases#get-the-latest-release) · [Qualification et audit](ALPHA_QUALIFICATION.md)

Les binaires redistribués incluent les licences et notices en `ThirdPartyNotices`. Les versions utilisées sont WindowsDesktop/.NET 10, Microsoft.Data.Sqlite 10.0.12, SQLitePCLRaw 3.0.5 et SQLite 3.53.4. Le compilateur Inno est un outil de build. Les droits d’utilisation commerciale du compilateur doivent être vérifiés si la distribution devient commerciale. [Inno Setup](https://jrsoftware.org/inno-setup/downloads) · [SQLitePCLRaw 3.0.5](https://github.com/ericsink/SQLitePCL.raw/releases/tag/v3.0.5) · [SQLite](https://sqlite.org/copyright.html)
