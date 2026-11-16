param(
    [Parameter(Mandatory)]
    [string]$CorpusDirectory,
    [string]$RustProbe,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$cli = Join-Path $PSScriptRoot "Moneta.BankPerfect.Diagnose/bin/$Configuration/net10.0/Moneta.BankPerfect.Diagnose.dll"
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    throw "Générer la CLI en $Configuration avant de lancer la qualification."
}
if ($RustProbe) {
    $RustProbe = (Resolve-Path -LiteralPath $RustProbe).Path
}

$truth = [ordered]@{
    'BP-F00' = @{ Sha256 = '95cf5eba7ccf9fe53ff4b7539f27e6e1286003032cfc6b2316b4c882d9a2a04d'; Sizes = @(1); Sums = @(0); Categories = 33; NP = 0; P = 0; R = 1 }
    'BP-F01' = @{ Sha256 = 'a38894d9b20b1df119eda68203b76b1531f7e2242eb5415e1193949c0f507436'; Sizes = @(3); Sums = @(10876700); Categories = 34; NP = 2; P = 0; R = 1 }
    'BP-F02' = @{ Sha256 = '2fc20d990eabf6f2f7189f13e5972ebe8c653a54c3a52a9f9e7a2fd5cc8405a3'; Sizes = @(2, 2); Sums = @(8500000, 3500000); Categories = 33; NP = 2; P = 0; R = 2 }
    'BP-F03' = @{ Sha256 = 'c97285ee4c1d6648fdfdd30ef4b9fde3fdc0352d73ba313bf34cfde4b6b4fd3f'; Sizes = @(4); Sums = @(10876701); Categories = 35; NP = 2; P = 1; R = 1 }
}

function Assert-Equal($Expected, $Actual, [string]$Property) {
    if ($Expected -cne $Actual) {
        throw "$fixture — écart sur $Property"
    }
}

function Compare-Readers($Document, $Reference) {
    Assert-Equal 'V5' $Reference.format 'format Rust'
    Assert-Equal $Document.accounts.Count $Reference.accounts.Count 'comptes Rust'
    Assert-Equal $Document.categories.Count $Reference.categories.Count 'catégories Rust'
    Assert-Equal $Document.payeeTemplates.Count $Reference.payees.Count 'modèles de tiers Rust'
    Assert-Equal $Document.extensions.Count $Reference.features.Count 'extensions Rust'
    for ($index = 0; $index -lt $Document.categories.Count; $index++) {
        $category = $Document.categories[$index]
        $other = $Reference.categories[$index]
        $referenceId = $category.id
        if ($null -eq $referenceId) { $referenceId = $category.legacyPositionId }
        Assert-Equal $referenceId $other.id 'identifiant ou position de catégorie Rust'
        Assert-Equal $category.name $other.name 'libellé de catégorie Rust'
        Assert-Equal ($null -ne $category.parentItem) $other.is_child 'hiérarchie Rust'
    }
    for ($index = 0; $index -lt $Document.accounts.Count; $index++) {
        $account = $Document.accounts[$index]
        $other = $Reference.accounts[$index]
        Assert-Equal $account.fields.name $other.name 'nom du compte Rust'
        Assert-Equal $account.fields.currency $other.currency 'devise Rust'
        Assert-Equal $account.fields.secondaryCurrency $other.secondary_currency 'devise secondaire Rust'
        Assert-Equal ([decimal]$account.ledgerSum * 10000) ([decimal]$other.balance_raw) 'somme Rust'
        Assert-Equal $account.operations.Count $other.operations.Count 'lignes Rust'
        if ($fixture -ne 'BP-F00') { Assert-Equal $account.fields.uid $other.uid 'UID de compte Rust' }
        for ($line = 0; $line -lt $account.operations.Count; $line++) {
            $row = $account.operations[$line]
            $rust = $other.operations[$line]
            if ($fixture -ne 'BP-F00') {
                Assert-Equal ([string]$row.uid) $rust.uid 'UID de ligne Rust'
                Assert-Equal $row.additionalDate.days $rust.mark_date_raw 'jour supplémentaire Rust'
            }
            $state = @{ NP = 'None'; P = 'Pointed'; R = 'Reconciled' }[$row.pointing]
            Assert-Equal $state $rust.state 'pointage Rust'
            Assert-Equal $row.date.days $rust.date_raw 'jour Delphi Rust'
            Assert-Equal $row.date.date $rust.date_iso 'date ISO Rust'
            Assert-Equal $row.mode $rust.mode 'mode Rust'
            Assert-Equal $row.payee $rust.payee 'tiers Rust'
            Assert-Equal $row.details $rust.details 'détail Rust'
            Assert-Equal $row.categoryId $rust.category 'catégorie de ligne Rust'
            Assert-Equal ([decimal]$row.amount.value * 10000) ([decimal]$rust.amount_raw) 'montant Rust'
            foreach ($edge in $row.extra | Where-Object { $_.key -like '#sister*' }) {
                $target = @($rust.extra | Where-Object { $_[0] -ceq $edge.key })
                Assert-Equal 1 $target.Count 'nombre de références sœurs Rust'
                Assert-Equal $edge.value $target[0][1] 'référence sœur Rust'
            }
        }
    }
}

foreach ($fixture in $truth.Keys) {
    $expected = $truth[$fixture]
    $file = (Resolve-Path -LiteralPath (Join-Path $CorpusDirectory "$fixture.bp")).Path
    $before = (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()
    Assert-Equal $expected.Sha256 $before 'empreinte historique'
    try {
        $result = dotnet $cli --details $file | ConvertFrom-Json
        if ($LASTEXITCODE -notin 0, 1 -or $null -eq $result.document) {
            throw "$fixture — lecture C# impossible"
        }
        $report = $result.report
        $document = $result.document
        Assert-Equal 'V5' $report.format 'format'
        Assert-Equal (Get-Item -LiteralPath $file).Length $report.byteLength 'taille'
        Assert-Equal $expected.Sizes.Count $report.accounts 'nombre de comptes'
        Assert-Equal ($expected.Sizes | Measure-Object -Sum).Sum $report.operations 'nombre de lignes'
        Assert-Equal $expected.Categories $report.categories 'catégories'
        Assert-Equal 0 $report.payeeTemplates 'modèles de tiers'
        foreach ($state in 'NP', 'P', 'R') {
            Assert-Equal $expected[$state] $report.$state "état $state"
        }
        Assert-Equal $report.operations $report.exactSourceAmounts 'montants lus exactement'
        Assert-Equal ([int]($fixture -eq 'BP-F02')) $report.linkedTransfers 'virements liés'
        Assert-Equal ([int]($fixture -eq 'BP-F03')) $report.unrepresentableAmounts 'montants non représentables'
        for ($index = 0; $index -lt $document.accounts.Count; $index++) {
            $account = $document.accounts[$index]
            Assert-Equal $expected.Sizes[$index] $account.operations.Count 'taille du registre'
            Assert-Equal $expected.Sums[$index] ([decimal]$account.ledgerSum * 10000) 'somme source'
            Assert-Equal 'Probable' $account.opening "qualification de la ligne d’ouverture"
        }
        switch ($fixture) {
            'BP-F00' {
                $row = $document.accounts[0].operations[0]
                Assert-Equal $null $row.uid 'UID source de ligne'
                Assert-Equal $null $document.accounts[0].fields.uid 'UID source de compte'
                Assert-Equal $false $row.additionalDate.present 'absence de date supplémentaire'
                Assert-Equal '0,00' $row.amount.raw 'montant brut'
            }
            'BP-F01' {
                $row = $document.accounts[0].operations | Where-Object uid -eq 3
                Assert-Equal 'Café Démo' $row.payee 'tiers accentué'
                Assert-Equal 'Été à Noël' $row.details 'détail accentué'
                Assert-Equal '-12,34' $row.amount.raw 'dépense brute'
            }
            'BP-F02' {
                Assert-Equal 117 $document.extensions[0].decodedByteCount 'extension opaque'
                Assert-Equal 2 $document.transfers.Count 'jambes du virement'
                foreach ($link in $document.transfers) {
                    Assert-Equal 'Linked' $link.status 'statut de la référence sœur'
                    Assert-Equal $true $link.reciprocal 'réciprocité'
                    $sisterUid = @{ '3' = 4; '4' = 3 }[[string]$link.sourceOperationUid]
                    Assert-Equal $sisterUid $link.targetOperationUid 'identités des jambes'
                }
            }
            'BP-F03' {
                $rows = $document.accounts[0].operations
                $marked = $rows | Where-Object uid -eq 2
                $fraction = $rows | Where-Object uid -eq 4
                Assert-Equal '2024-01-15' $marked.date.date "date d’opération"
                Assert-Equal '2024-01-31' $marked.additionalDate.date 'date de pointage'
                Assert-Equal '2000-02-29' $fraction.date.date 'date bissextile'
                Assert-Equal '0,0001' $fraction.amount.raw 'fraction brute'
                Assert-Equal 4 $fraction.amount.scale 'échelle brute'
                Assert-Equal 'ExcessPrecision' $fraction.amount.compatibility 'compatibilité centimes'
                $child = $document.categories | Where-Object id -eq 34
                Assert-Equal 30 $document.categories[$child.parentItem].id 'parent physique'
            }
        }
        if ($RustProbe) {
            $reference = & $RustProbe $file | ConvertFrom-Json
            if ($LASTEXITCODE -ne 0) { throw "$fixture — lecture Rust impossible" }
            Compare-Readers $document $reference
        }
    }
    finally {
        Assert-Equal $before (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() 'empreinte après lectures'
    }
    $comparison = if ($RustProbe) { 'C# et Rust' } else { 'C#' }
    Write-Output "$fixture — $comparison, valeurs vérifiées et empreinte identique"
}
