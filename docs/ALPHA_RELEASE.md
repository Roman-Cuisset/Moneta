# Préparer Moneta Alpha 0.1.0

## Construction du candidat Windows

Le paquet Inno Setup 6.7.4 contient Moneta, le runtime WindowsDesktop .NET 10 x64 et le lanceur de mise à jour autonome. L’amorce Inno est un PE x86. Le paquet impose Windows x64 et installe les exécutables x64. L’application s’installe dans `%LOCALAPPDATA%\Programs\Moneta`, par utilisateur, avec un raccourci Démarrer et une entrée de désinstallation dans HKCU. Les dossiers financiers restent à l’emplacement choisi, et les préférences, sauvegardes et cache demeurent dans les données du profil. Les migrations financières utilisent les copies contrôlées de Moneta.

```powershell
$compiler = & ./tools/Get-MonetaBuildTools.ps1
$artefacts = & ./tools/Build-MonetaCandidate.ps1 -Version 0.1.0 -InnoCompiler $compiler | Select-Object -Last 1
& ./tools/Sign-MonetaCandidate.ps1 -ArtifactDirectory $artefacts
```

Les trois scripts vérifient l’éditeur du compilateur, le runtime et la version du produit, les notices, les empreintes et la correspondance de la clé de signature. Les binaires et fichiers de qualification restent dans `private/mission18`. Le même script repart de sorties isolées à chaque exécution. Comparez les versions de SDK et de compilateur, les entrées et le résultat. Les horodatages et signatures font partie des limites de la comparaison octet à octet.

**Fichiers de livraison.** `Moneta-0.1.0-win-x64.exe`, `SHA256SUMS.txt`, `moneta-update.json` et le [guide de première utilisation](ALPHA_GUIDE.md). Les [notes de version](releases/0.1.0.md) alimentent la page de release et le catalogue signé. Le fichier `moneta-update.unsigned.json` reste un intermédiaire local. Le candidat utilise le schéma SQLite 8.

## Mises à jour et contrôle de sécurité

La production utilise le point d’accès GitHub `releases/latest/download/moneta-update.json` en HTTPS. Le client valide le catalogue avec une clé publique ECDSA P‑256 intégrée, limite les redirections aux hôtes GitHub nécessaires et refuse les versions égales ou antérieures. Le catalogue authentifie la taille, le SHA‑256 de l’installateur, le produit, l’architecture, la version et le texte de publication. Le lanceur répète les contrôles avant de lancer le fichier via Windows. Il garde le fichier ouvert en lecture pour empêcher son remplacement entre le contrôle et le lancement. Les téléchargements de production conservent la marque Internet pour les vérifications de Windows.

Moneta propose d’enregistrer les brouillons, attend la fin des sauvegardes et ferme le dossier. Le lanceur n’agit qu’après la décision de fermer et la fin du processus. L’installation attend sa propre fin avant de vérifier la version et relancer Moneta. En cas d’annulation ou d’échec, le résultat est affiché au redémarrage si l’application demeure disponible. L’installateur impose le dossier de programme, refuse la rétrogradation et demande la fermeture des autres instances.

La clé privée de production est non exportable dans le magasin CNG du compte signataire Windows, sous `Moneta.Release.Alpha.P256`. Git et les Actions disposent uniquement de la clé publique. Préserver l’accès au compte signataire fait partie de l’exploitation du canal Alpha. Si cet accès est perdu, une nouvelle chaîne de confiance et une réinstallation vérifiée seront nécessaires. Une signature Authenticode du binaire, si disponible, doit précéder le calcul définitif des empreintes et la signature du catalogue. Le candidat utilise actuellement la signature du catalogue et laisse Windows appliquer ses propres contrôles d’exécution.

Le laboratoire compile une autre clé, un autre identifiant d’installation et une URL fixe `http://127.0.0.1:18761/`. Les fichiers lab restent réservés à l’essai isolé 0.1.0 → 0.1.1. La clé de production signe uniquement les candidats issus du profil production.

## Publication après autorisation

Le workflow manuel **Préparer un candidat Moneta** produit des artefacts de contrôle sans clé privée. La première livraison utilise les fichiers scellés dans `private/mission18b/candidate/artifacts` sur le poste de qualification. Une reconstruction change potentiellement le SHA-256. Le propriétaire doit autoriser explicitement la visibilité publique du dépôt, la distribution et la publication de la release avant les commandes correspondantes. Un push sur `main` ne déclenche aucune publication.

Publier `v0.1.0` comme **préversion**. GitHub réserve `releases/latest` aux releases stables. Tant que seules des préversions existent, Moneta 0.1.0 traite la réponse HTTP 404 de ce canal comme une absence de mise à jour. Le catalogue signé est disponible sous l’URL du tag. La détection automatique des futures préversions demanderait un canal dédié. [Comportement de Latest](https://docs.github.com/en/rest/releases/releases#get-the-latest-release) · [Qualification et audit](ALPHA_QUALIFICATION.md)

### Vérification en PowerShell 7 depuis la racine du dépôt

```powershell
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repo = 'Roman-Cuisset/Moneta'
$tag = 'v0.1.0'
$dir = (Resolve-Path private/mission18b/candidate/artifacts).Path
$installer = Join-Path $dir 'Moneta-0.1.0-win-x64.exe'
$catalog = Join-Path $dir 'moneta-update.json'
$sums = Join-Path $dir 'SHA256SUMS.txt'
$notes = Join-Path $dir 'NOTES-DE-VERSION.md'
$sha = 'b367f55d5412d594f925611e24cf1ea6d520e86b56428af3042cef66630db4ae'
$catalogHash = '0a518befe8cfee671cccade2da97de23620eb8c1d7efe78c2ab59b8063ca87af'
gh auth status
$remote = gh repo view $repo --json visibility,defaultBranchRef | ConvertFrom-Json
if ($remote.visibility -ne 'PRIVATE' -or $remote.defaultBranchRef.name -ne 'main') {
    throw 'Visibilité ou branche par défaut inattendue.'
}
if (git status --porcelain) { throw 'Arbre Git non propre.' }
$commit = git rev-parse HEAD
$mainRef = git ls-remote origin refs/heads/main
if (($mainRef -split '\s+')[0] -ne $commit) { throw 'HEAD diffère de origin/main.' }
if (git ls-remote origin "refs/tags/$tag") { throw 'Tag déjà présent.' }
if ((gh api "repos/$repo/releases?per_page=100" --jq length) -ne '0') {
    throw 'Historique des releases à contrôler.'
}
if ((Get-Item -LiteralPath $installer).Length -ne 63380176 -or
    (Get-FileHash -LiteralPath $installer).Hash -ne $sha -or
    (Get-FileHash -LiteralPath $catalog).Hash -ne $catalogHash -or
    (Get-Content -Raw -LiteralPath $sums).Trim() -ne "$sha  Moneta-0.1.0-win-x64.exe") {
    throw 'Le lot ne correspond pas au candidat contrôlé.'
}
$envelope = Get-Content -Raw -LiteralPath $catalog | ConvertFrom-Json
$payload = [Convert]::FromBase64String($envelope.payload)
$signature = [Convert]::FromBase64String($envelope.signature)
$key = [Convert]::FromBase64String((Get-Content -Raw src/Moneta.Updates/release-key.spki).Trim())
$ecdsa = [Security.Cryptography.ECDsa]::Create()
try {
    $read = 0
    $ecdsa.ImportSubjectPublicKeyInfo($key, [ref]$read)
    if ($read -ne $key.Length -or $ecdsa.KeySize -ne 256 -or
        !$ecdsa.VerifyData($payload, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256)) {
        throw 'Signature du catalogue invalide.'
    }
} finally { $ecdsa.Dispose() }
$manifest = [Text.Encoding]::UTF8.GetString($payload) | ConvertFrom-Json
if ($manifest.format -ne 1 -or $manifest.version -ne '0.1.0' -or $manifest.architecture -ne 'win-x64' -or
    $manifest.installer -ne (Split-Path -Leaf $installer) -or $manifest.sha256 -ne $sha -or
    $manifest.size -ne (Get-Item -LiteralPath $installer).Length -or
    $manifest.releaseNotes -cne (Get-Content -Raw -LiteralPath $notes).Trim() -or
    $manifest.releaseNotes -cne (Get-Content -Raw docs/releases/0.1.0.md).Trim()) {
    throw 'Métadonnées du lot incohérentes.'
}
```

### Après autorisation du propriétaire

Dans la même session, contrôler que le commit est celui de l’audit, que GitHub reconnaît la licence MIT et que les surfaces exposées comme les journaux Actions ont été examinées. Préparer et vérifier le brouillon avant de rendre le dépôt public. [Visibilité GitHub](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility) · [Créer une release](https://cli.github.com/manual/gh_release_create)

```powershell
if ((gh api "repos/$repo/license" --jq .license.spdx_id) -ne 'MIT') {
    throw 'Licence MIT non reconnue.'
}
$publication = Join-Path $env:TEMP ('moneta-release-' + [guid]::NewGuid().ToString('N'))
$verified = Join-Path $publication 'verified'
$null = New-Item -ItemType Directory -Path $verified
$guide = Join-Path $publication 'GUIDE-DE-PREMIERE-UTILISATION.md'
$guideText = (Get-Content -Raw docs/ALPHA_GUIDE.md).Replace(
    '](BANKPERFECT_QUALIFICATION_10E.md)',
    "](https://github.com/$repo/blob/$commit/docs/BANKPERFECT_QUALIFICATION_10E.md)").Replace(
    '](../LICENSE)', "](https://github.com/$repo/blob/$commit/LICENSE)")
$license = (Get-Content -Raw LICENSE).Trim()
$licenseHeading = "`n`n## Texte de la licence MIT`n`n"
$fence = [string]::new([char]96, 3)
[IO.File]::WriteAllText($guide, $guideText.TrimEnd() + $licenseHeading + $fence + "`n$license`n" + $fence + "`n")
$assets = @($installer, $catalog, $sums, $guide)
gh release create $tag @assets --repo $repo --target $commit --draft --prerelease --latest=false --title 'Moneta Alpha 0.1.0' --notes-file $notes
$draft = gh release view $tag --repo $repo --json isDraft,isPrerelease,tagName,targetCommitish,body,apiUrl | ConvertFrom-Json
if (!$draft.isDraft -or !$draft.isPrerelease -or $draft.tagName -ne $tag -or $draft.targetCommitish -ne $commit -or
    $draft.body.Trim() -cne $manifest.releaseNotes) {
    throw 'Le brouillon ne correspond pas à la livraison prévue.'
}
$draftEndpoint = ([uri]$draft.apiUrl).AbsolutePath.TrimStart('/')
if ($draftEndpoint -notmatch "^repos/$repo/releases/[0-9]+$") {
    throw 'Point d’accès du brouillon inattendu.'
}
gh release download $tag --repo $repo --pattern '*' --dir $verified
foreach ($asset in $assets) {
    $roundTrip = Join-Path $verified (Split-Path -Leaf $asset)
    if ((Get-FileHash -LiteralPath $roundTrip).Hash -ne (Get-FileHash -LiteralPath $asset).Hash) {
        throw "Fichier GitHub différent de la copie locale : $roundTrip"
    }
}
gh repo edit $repo --visibility public --accept-visibility-change-consequences
if ((gh repo view $repo --json visibility --jq .visibility) -ne 'PUBLIC') {
    throw 'Visibilité publique non confirmée.'
}
$release = gh api --method PATCH $draftEndpoint -F draft=false -F prerelease=true -f make_latest=false | ConvertFrom-Json
if ($release.draft -or !$release.prerelease -or $release.tag_name -ne $tag) {
    throw 'Préversion publique non confirmée.'
}
```

Depuis un Windows 11 x64, ouvrir les deux URL en navigation sans compte GitHub. Télécharger l’installateur et le catalogue et comparer leurs SHA-256 aux valeurs ci-dessus. Noter la présence de la marque Internet et le texte de tout avertissement Windows. Suivre la protection affichée et interrompre le parcours en cas de blocage. Si Windows permet l’installation, vérifier le raccourci, le lancement et **Aide → Rechercher les mises à jour**. Au lancement de l’Alpha, seule la préversion étant publiée, la commande doit indiquer qu’aucune version plus récente n’est disponible. [Catalogue signé 0.1.0](https://github.com/Roman-Cuisset/Moneta/releases/download/v0.1.0/moneta-update.json) · [Installateur 0.1.0](https://github.com/Roman-Cuisset/Moneta/releases/download/v0.1.0/Moneta-0.1.0-win-x64.exe)

## Licences et redistribution

Le code source original de Moneta est sous [licence MIT](../LICENSE). Le guide livré avec l’installateur reprend le texte complet et la mention de copyright. L’identité graphique a été créée pour le projet. Les composants tiers gardent leurs propres licences.

Le dossier `ThirdPartyNotices` livré contient les licences et notices de WindowsDesktop/.NET 10, Microsoft.Data.Sqlite 10.0.12, SQLitePCLRaw 3.0.5 et SQLite 3.53.4, ainsi que la notice MIT du codec BankPerfect V5. Inno Setup sert à construire l’installateur. Les conditions du compilateur sont à vérifier en cas d’usage commercial. [Inno Setup](https://jrsoftware.org/inno-setup/downloads) · [SQLitePCLRaw 3.0.5](https://github.com/ericsink/SQLitePCL.raw/releases/tag/v3.0.5) · [SQLite](https://sqlite.org/copyright.html) · [Notice BankPerfect](../src/Moneta.BankPerfect/THIRD-PARTY-NOTICE.md)
