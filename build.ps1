<#
.SYNOPSIS
  Baut den ioBroker Backup Analyzer in beiden Verteilvarianten.

.DESCRIPTION
  Erzeugt unter dist/ zwei fertige Distributionen:

    1. ioBroker-Backup-Analyzer.exe
       Eine einzelne portable Datei (unkomprimiert, ~140 MB). Bequem, aber ein
       grosser neuer Binaerblob - mittleres Risiko fuer Virenscanner-Fehlalarme.

    2. ioBroker-Backup-Analyzer_Ordner.zip
       Self-contained als Ordner (viele Dateien). Der Grossteil sind bereits von
       Microsoft signierte .NET-Runtime-Dateien, die jeder Scanner kennt - neu und
       potenziell "verdaechtig" ist nur die kleine App-DLL plus Apphost. Das ist
       die scanner-freundlichste Variante ohne installiertes .NET.
       Zum Starten entpacken und die enthaltene EXE doppelklicken.

  Beide brauchen KEIN installiertes .NET. Details zur Abwaegung: siehe README.md.

.NOTES
  Braucht PowerShell 7 (pwsh). Unter Windows PowerShell 5.1 scheitert die Umwandlung
  app.ico -> AppIcon.icns fuer die macOS-Bundles; das Skript startet sich dort selbst
  mit pwsh neu.

.PARAMETER SkipVerify
  Ueberspringt den Verifikationslauf gegen die echten Backups in testdaten/.

.PARAMETER SkipSelftest
  Ueberspringt den Selbsttest der Avalonia-Oberflaeche.

  Nur fuer den Fall, dass Smart App Control ihn blockiert und an der Oberflaeche
  nachweislich nichts geaendert wurde: Blockiert wird dann nicht der eigene Code,
  sondern eine NuGet-DLL (Avalonia.Win32.dll) mit ueber alle Builds konstantem Hash -
  Wiederholen und der sonst uebliche Ausweg ueber -c Debug helfen dabei nicht.

  Der Selbsttest faengt XAML-Fehler ab, die der Compiler nicht sieht. Wer ihn
  ueberspringt, liefert die Oberflaeche ungeprueft aus. Vertretbar nur, solange keine
  .axaml-Datei gegenueber dem letzten gruen gebauten Stand angefasst wurde.
#>
[CmdletBinding()]
param(
    [switch]$SkipVerify,
    [switch]$SkipSelftest
)

# ---------------------------------------------------------------- PowerShell-Fassung
#
# Die Icon-Umwandlung app.ico -> AppIcon.icns (macOS) laeuft ueber System.Drawing. Dessen
# Verhalten unterscheidet sich zwischen Windows PowerShell 5.1 und PowerShell 7: unter 5.1
# wirft Icon.ToBitmap() eine ArgumentOutOfRangeException, und der Build bricht erst mitten
# in Variante 3 ab. Deshalb gleich hier unter pwsh neu starten.
if ($PSVersionTable.PSVersion.Major -lt 6) {
    $pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if (-not $pwsh) {
        throw "build.ps1 braucht PowerShell 7 (pwsh). Unter Windows PowerShell 5.1 scheitert " +
              "die Umwandlung app.ico -> AppIcon.icns fuer die macOS-Pakete."
    }
    Write-Host "Windows PowerShell 5.1 erkannt - starte mit PowerShell 7 neu." -ForegroundColor Yellow
    $argumente = @('-ExecutionPolicy','Bypass','-File',$PSCommandPath)
    if ($SkipVerify) { $argumente += '-SkipVerify' }
    if ($SkipSelftest) { $argumente += '-SkipSelftest' }
    & $pwsh @argumente
    exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = Join-Path $root 'src\IobBackupAnalyzer.App'
$dist = Join-Path $root 'dist'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

# dotnet finden (PATH oder Standard-Installationsort des User-Installers).
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $candidate = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $candidate) { $dotnet = $candidate }
}
if (-not $dotnet) { throw "dotnet-SDK nicht gefunden. Bitte .NET 8 SDK installieren." }
Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray

# ---------------------------------------------------------------- Hilfsfunktion: Symbol fuer macOS
#
# Ein .app-Bundle ohne Symbol zeigt im Finder und im Dock nur eine graue Kachel - genau das
# hat der macOS-Nutzer gemeldet. macOS erwartet dafuer eine .icns-Datei in Contents/Resources
# und einen Verweis darauf in der Info.plist.
#
# Erzeugt wird sie hier aus der app.ico der Windows-Fassung, damit es bei EINER Symbolquelle
# bleibt. Das Format ist simpel: Kopf 'icns' plus Gesamtlaenge, danach je Groesse ein Block
# aus Typkennung, Laenge und PNG-Daten - alle Laengen als Big Endian. Die grossen Eintraege
# der .ico liegen bereits als PNG vor und werden unveraendert uebernommen; 512 und 1024 (fuer
# Retina-Anzeigen) werden aus dem groessten vorhandenen Bild hochgerechnet.
function New-IcnsFromIco {
    param(
        [Parameter(Mandatory)][string]$IcoPath,
        [Parameter(Mandatory)][string]$Destination
    )

    Add-Type -AssemblyName System.Drawing

    $raw = [System.IO.File]::ReadAllBytes($IcoPath)
    $count = [BitConverter]::ToUInt16($raw, 4)
    $entries = @()
    for ($i = 0; $i -lt $count; $i++) {
        $o = 6 + $i * 16
        $w = $raw[$o]; if ($w -eq 0) { $w = 256 }   # 0 steht im .ico-Verzeichnis fuer 256
        $entries += [pscustomobject]@{
            Size   = [int]$w
            Offset = [BitConverter]::ToUInt32($raw, $o + 12)
            Length = [BitConverter]::ToUInt32($raw, $o + 8)
        }
    }

    function Get-IcnsPng([int]$edge) {
        $hit = $entries | Where-Object { $_.Size -eq $edge } | Select-Object -First 1
        if ($hit) {
            $data = New-Object byte[] $hit.Length
            [Array]::Copy($raw, $hit.Offset, $data, 0, $hit.Length)
            if ($data.Length -gt 8 -and $data[0] -eq 0x89 -and $data[1] -eq 0x50 -and
                $data[2] -eq 0x4E -and $data[3] -eq 0x47) { return $data }
        }
        $srcEdge = ($entries | Measure-Object -Property Size -Maximum).Maximum
        $icon = New-Object System.Drawing.Icon($IcoPath, $srcEdge, $srcEdge)
        $src = $icon.ToBitmap()
        $bmp = New-Object System.Drawing.Bitmap($edge, $edge)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($src, 0, 0, $edge, $edge)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose(); $src.Dispose(); $icon.Dispose()
        return $ms.ToArray()
    }

    $types = @(
        @{ Tag = 'icp4'; Edge = 16 },  @{ Tag = 'icp5'; Edge = 32 },
        @{ Tag = 'ic07'; Edge = 128 }, @{ Tag = 'ic08'; Edge = 256 },
        @{ Tag = 'ic09'; Edge = 512 }, @{ Tag = 'ic10'; Edge = 1024 }
    )

    $blocks = New-Object System.IO.MemoryStream
    foreach ($t in $types) {
        $png = Get-IcnsPng $t.Edge
        $lenBE = [BitConverter]::GetBytes([uint32](8 + $png.Length)); [Array]::Reverse($lenBE)
        $tag = [System.Text.Encoding]::ASCII.GetBytes($t.Tag)
        $blocks.Write($tag, 0, 4); $blocks.Write($lenBE, 0, 4); $blocks.Write($png, 0, $png.Length)
    }
    $body = $blocks.ToArray(); $blocks.Dispose()

    $totalBE = [BitConverter]::GetBytes([uint32](8 + $body.Length)); [Array]::Reverse($totalBE)
    $out = New-Object System.IO.MemoryStream
    $magic = [System.Text.Encoding]::ASCII.GetBytes('icns')
    $out.Write($magic, 0, 4); $out.Write($totalBE, 0, 4); $out.Write($body, 0, $body.Length)
    [System.IO.File]::WriteAllBytes($Destination, $out.ToArray())
    $out.Dispose()
}

# ---------------------------------------------------------------- Hilfsfunktion: tar mit Rechten
#
# tar.exe von Windows packt jede Datei ohne Ausfuehrbar-Bit - NTFS kennt keins. Unter macOS
# scheitert der Start dann mit "Launchd job spawn failed" (RBSRequestErrorDomain Code 5,
# POSIX 111), unter Linux mit "Permission denied". Ein blosses "chmod +x" auf das
# .app-Bundle hilft nicht: das trifft nur den Ordner, nicht die Binary darin.
#
# System.Formats.Tar (ab .NET 7, in PowerShell 7 enthalten) kann die Rechte setzen. Damit
# entsteht ein Archiv, das auf dem Zielsystem sofort startklar ist.
function New-UnixTarGz {
    param(
        # Ordner, der als oberste Ebene im Archiv erscheint (z. B. "... Analyzer.app").
        [Parameter(Mandatory)][string]$SourceDir,
        [Parameter(Mandatory)][string]$Destination
    )

    $modeExec = [System.IO.UnixFileMode]'UserRead,UserWrite,UserExecute,GroupRead,GroupExecute,OtherRead,OtherExecute'  # 755
    $modeData = [System.IO.UnixFileMode]'UserRead,UserWrite,GroupRead,OtherRead'                                        # 644

    # Ausfuehrbar muessen: der Apphost und createdump (beide ohne Endung), die nativen
    # Bibliotheken und das Startskript des Linux-Pakets. Bei .dylib/.so ist das Bit nicht
    # zwingend, aber auf Unix so ueblich.
    #
    # Das ".sh" steht hier bewusst mit drin: starte.sh hat einen Punkt im Namen und fiele
    # sonst unter die Datei-Regel (644). Das Skript liesse sich dann nicht aufrufen - und
    # ausgerechnet die Datei, die fehlende Rechte erklaeren soll, waere selbst gesperrt.
    $istAusfuehrbar = {
        param($name)
        $name -notmatch '\.' -or $name -match '\.(dylib|so)(\.\d+)*$' -or $name -match '\.sh$'
    }

    $basis = (Resolve-Path $SourceDir).Path
    $topName = Split-Path $basis -Leaf
    $zeit = [System.DateTimeOffset]::new([System.IO.Directory]::GetLastWriteTimeUtc($basis), [System.TimeSpan]::Zero)

    $out = [System.IO.File]::Create($Destination)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new($out, [System.IO.Compression.CompressionLevel]::Optimal)
        try {
            $writer = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $true)
            try {
                $wurzel = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::Directory, "$topName/")
                $wurzel.Mode = $modeExec
                $wurzel.ModificationTime = $zeit
                $writer.WriteEntry($wurzel)

                foreach ($item in Get-ChildItem $basis -Recurse -Force | Sort-Object FullName) {
                    $rel = $item.FullName.Substring($basis.Length).TrimStart('\', '/').Replace('\', '/')
                    $mtime = [System.DateTimeOffset]::new($item.LastWriteTimeUtc, [System.TimeSpan]::Zero)

                    if ($item.PSIsContainer) {
                        $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::Directory, "$topName/$rel/")
                        $entry.Mode = $modeExec
                        $entry.ModificationTime = $mtime
                        $writer.WriteEntry($entry)
                        continue
                    }

                    $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, "$topName/$rel")
                    $entry.Mode = if (& $istAusfuehrbar $item.Name) { $modeExec } else { $modeData }
                    $entry.ModificationTime = $mtime
                    $daten = [System.IO.File]::OpenRead($item.FullName)
                    try {
                        $entry.DataStream = $daten
                        $writer.WriteEntry($entry)
                    } finally { $daten.Dispose() }
                }
            } finally { $writer.Dispose() }
        } finally { $gzip.Dispose() }
    } finally { $out.Dispose() }
}

# ---------------------------------------------------------------- Verifikation

if (-not $SkipVerify) {
    Write-Host "`n=== Verifikationslauf gegen testdaten/ ===" -ForegroundColor Cyan
    & $dotnet run --project (Join-Path $root 'src\IobBackupAnalyzer.Verify') -c Release
    if ($LASTEXITCODE -ne 0) { throw "Verifikationslauf fehlgeschlagen (ExitCode $LASTEXITCODE). Build abgebrochen." }
}

# ---------------------------------------------------------------- Aufraeumen

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null

$stageSingle = Join-Path $dist '_single'
$stageFolder = Join-Path $dist '_ordner\ioBroker-Backup-Analyzer'

# ---------------------------------------------------------------- Variante 1: Single-File

Write-Host "`n=== Variante 1: portable Einzeldatei ===" -ForegroundColor Cyan
& $dotnet publish $app -c Release -o $stageSingle `
    -p:PublishSingleFile=true -p:SelfContained=true | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) { throw "Single-File-Publish fehlgeschlagen." }

Copy-Item (Join-Path $stageSingle 'ioBroker-Backup-Analyzer.exe') $dist -Force

# Die MIT-Lizenz verlangt, dass Lizenztext und Copyright-Hinweis jeder Kopie beiliegen.
# Bei der Einzeldatei geht das nur als separate Datei daneben.
$license = Join-Path $root 'LICENSE'
Copy-Item $license $dist -Force

# ---------------------------------------------------------------- Variante 2: Ordner + ZIP

Write-Host "`n=== Variante 2: Ordner (scanner-freundlich) ===" -ForegroundColor Cyan
& $dotnet publish $app -c Release -o $stageFolder `
    -p:PublishSingleFile=false -p:SelfContained=true | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) { throw "Ordner-Publish fehlgeschlagen." }

# In der Ordner-Variante liegt die Lizenz direkt neben der EXE.
Copy-Item $license $stageFolder -Force

# Eine kurze LIESMICH gehoert ins Paket. Der Hinweis auf das Ladeprotokoll steht zwar auch
# im Tab "Hilfe" - aber genau dann, wenn man ihn braucht, kommt man dort nicht mehr hin:
# Bleibt das Fenster stehen, ist die Hilfe unerreichbar. Deshalb liegt der Pfad hier
# daneben, in einer Datei, die man ohne das Programm lesen kann.
$liesmichWin = @"
====================================================================
  ioBroker Backup Analyzer - Windows
====================================================================

Starten
-------
ioBroker-Backup-Analyzer.exe doppelklicken. Keine Installation noetig,
kein .NET erforderlich - die Laufzeitumgebung liegt im Ordner.

Dann oben links "Backup oeffnen ..." und die von ioBroker erzeugte
Datei waehlen (iobroker_*_backupiobroker.tar.gz). Das Programm liest
sie nur; es veraendert nichts und sendet nichts.

Nimm den ganzen Ordner
----------------------
Die EXE braucht die Dateien neben sich. Einzeln kopiert startet sie
nicht. Wer eine einzelne Datei moechte, nimmt die separate
ioBroker-Backup-Analyzer.exe aus dem uebergeordneten Ordner - die ist
allerdings unter Smart App Control nicht zuverlaessig.

Wenn das Programm beim Laden stehen bleibt
------------------------------------------
Bei jedem Ladevorgang wird ein Protokoll geschrieben - jede Zeile
sofort, damit auch ein ueber den Task-Manager beendetes Programm die
Stelle hinterlaesst, an der es nicht weiterkam:

  ladeprotokoll.txt   neben dieser EXE
                      (bei schreibgeschuetztem Ordner stattdessen
                       %APPDATA%\ioBroker-Backup-Analyzer\)

Das Protokoll enthaelt nichts aus deiner Anlage: nur Schritte, Zeiten,
Groessen und die Namensraeume der Adapter - keine Objekt-IDs, keine
Werte, keine Namen von Skripten, Ansichten oder Geraeten und keine
vollstaendigen Pfade. Es kann bedenkenlos weitergegeben werden.

Mit KI erstellt
---------------
Programmcode, Auswertungslogik und saemtliche Texte stammen von Claude
(Anthropic). Vor jeder Auslieferung laeuft ein Verifikationslauf gegen
echte Backups. Die Listen sind Pruef-, keine Loeschlisten - was
geloescht oder geaendert wird, entscheidest du.

Lizenz: MIT (siehe beiliegende Datei LICENSE).
"@
$liesmichWin | Set-Content -Path (Join-Path $stageFolder 'LIESMICH.txt') -Encoding UTF8

# Neben der Einzeldatei liegt dieselbe Erklaerung - dort gibt es keinen Ordner, in dem
# sie sonst zu finden waere.
$liesmichWin | Set-Content -Path (Join-Path $dist 'LIESMICH_Windows.txt') -Encoding UTF8

# Den App-Ordner selbst als oberste Ebene ins ZIP legen, damit beim Entpacken ein
# sauberer Ordner "ioBroker-Backup-Analyzer" entsteht statt eines Stage-Namens.
$zip = Join-Path $dist 'ioBroker-Backup-Analyzer_Ordner.zip'
Compress-Archive -Path $stageFolder -DestinationPath $zip -Force

# ---------------------------------------------------------------- Aufraeumen der Stages

Remove-Item $stageSingle -Recurse -Force
Remove-Item (Join-Path $dist '_ordner') -Recurse -Force

# ------------------------------------------- Variante 3: plattformuebergreifend (Avalonia)
#
# Zweite Oberflaeche auf Avalonia, aus demselben Core. Laeuft unter Windows, macOS und
# Linux. Die WinForms-EXE oben bleibt die gepflegte Windows-Fassung - diese hier kommt
# zusaetzlich dazu, vor allem fuer macOS und Linux.
#
# Der Cross-Publish erfolgt von Windows aus; getestet werden kann hier nur die
# Windows-Variante. Die macOS-/Linux-Pakete sind ungetestet, solange sie niemand auf dem
# Zielsystem startet.

Write-Host "`n=== Variante 3: plattformuebergreifend (Windows, macOS, Linux) ===" -ForegroundColor Cyan

$avalonia = Join-Path $root 'src\IobBackupAnalyzer.Avalonia'
$xplat = Join-Path $dist 'plattformuebergreifend'
New-Item -ItemType Directory -Force $xplat | Out-Null

# Version fuer Info.plist aus der csproj lesen - eine Quelle, kein zweiter Pflegeort.
$csproj = Join-Path $avalonia 'IobBackupAnalyzer.Avalonia.csproj'
$appVersion = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $appVersion) { throw "Version konnte nicht aus $csproj gelesen werden." }

# Selbsttest der Oberflaeche: XAML-Fehler sieht der Compiler nicht, sie schlagen erst
# beim Laden zu. Lieber hier abbrechen als ein kaputtes Paket ausliefern.
#
# Gebaut und gestartet wird er ausserhalb des Projektordners. Smart App Control blockiert
# frisch kompilierten, unsignierten Code aus einem OneDrive-Ordner - "Eine Anwendungs-
# steuerungsrichtlinie hat diese Datei blockiert" (0x800711C7). Massgeblich ist der Ort
# der Uebersetzung: eine dort erzeugte DLL bleibt auch nach dem Kopieren blockiert. Ohne
# diesen Umweg bricht der Build hier ab, obwohl mit der Oberflaeche alles in Ordnung ist.
if ($SkipSelftest) {
    Write-Host "`n=== Selbsttest der Oberflaeche uebersprungen (-SkipSelftest) ===" -ForegroundColor Yellow
    Write-Host "  Die Avalonia-Oberflaeche geht ungeprueft ins Paket." -ForegroundColor Yellow
}
else {
    $selftestRoot = Join-Path $env:LOCALAPPDATA 'IobBackupAnalyzer\selftest'
    # Aufraeumen darf den Build nie kippen: MSBuild haelt nach dem Lauf noch kurz Handles auf
    # den Ordner, dann bleiben leere Huellen zurueck. Die stoeren nicht und werden ueberschrieben.
    if (Test-Path $selftestRoot) { Remove-Item -Recurse -Force $selftestRoot -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force $selftestRoot | Out-Null

    # Nur die Quellen spiegeln, ohne bin/obj - das sind wenige hundert Kilobyte.
    Get-ChildItem (Join-Path $root 'src') -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object {
            $ziel = Join-Path $selftestRoot $_.FullName.Substring($root.Length + 1)
            New-Item -ItemType Directory -Force (Split-Path $ziel) | Out-Null
            Copy-Item $_.FullName $ziel -Force
        }
    $nuget = Join-Path $root 'NuGet.config'
    if (Test-Path $nuget) { Copy-Item $nuget $selftestRoot -Force }

    & $dotnet run --project (Join-Path $selftestRoot 'src\IobBackupAnalyzer.Avalonia') `
                  -c Release -- --selftest | Select-Object -Last 1
    $selftestCode = $LASTEXITCODE
    & $dotnet build-server shutdown | Out-Null   # gibt die Datei-Handles des Compilers frei
    Remove-Item -Recurse -Force $selftestRoot -ErrorAction SilentlyContinue
    if ($selftestCode -ne 0) {
        throw "Selbsttest der Avalonia-Oberflaeche fehlgeschlagen. Build abgebrochen. " +
              "Blockiert Smart App Control eine NuGet-DLL (0x800711C7) und ist an der " +
              "Oberflaeche nichts geaendert, hilft -SkipSelftest."
    }
}

$targets = @(
    @{ Rid = 'win-x64';   Paket = 'ioBroker-Backup-Analyzer_Windows-x64';        Art = 'zip' }
    @{ Rid = 'osx-arm64'; Paket = 'ioBroker-Backup-Analyzer_macOS-AppleSilicon'; Art = 'app' }
    @{ Rid = 'osx-x64';   Paket = 'ioBroker-Backup-Analyzer_macOS-Intel';        Art = 'app' }
    @{ Rid = 'linux-x64'; Paket = 'ioBroker-Backup-Analyzer_Linux-x64';          Art = 'targz' }
)

# Eigenstaendige, weitergabefaehige Anleitung nur fuer macOS. Sie liegt versioniert im
# Projekt (nicht im Skript), weil sie sich aus echten Rueckmeldungen von Mac-Nutzern
# speist und laufend praeziser wird. Sie muss INS Archiv - wer den Link zu einer
# einzelnen .tar.gz weitergibt, gibt sonst eine App ohne Anleitung weiter, und genau
# dann tritt der Fall ein, den sie verhindern soll: Doppelklick, "zsh: killed",
# Programm gilt als kaputt.
$macReadme = Join-Path $root 'LIESMICH_macOS.txt'
if (-not (Test-Path $macReadme)) {
    Write-Warning "LIESMICH_macOS.txt fehlt - das macOS-Paket geht ohne Anleitung raus."
}

# Dasselbe fuer Linux, aus demselben Grund - nur ist die Huerde dort eine andere: nicht
# Quarantaene und Signatur, sondern zwei Systembibliotheken (ICU und fontconfig), die auf
# schlanken Systemen fehlen. Der erste echte Linux-Test am 22.08.2026 lief in genau diese
# Wand: Das Programm bricht mit einem englischen .NET-Stapelabzug ab, aus dem niemand
# schliessen kann, welches Paket fehlt.
#
# starte.sh prueft das vorab und nennt den passenden Befehl; die Anleitung erklaert es in
# Ruhe nach. Beide liegen versioniert im Projekt, nicht hier im Skript - sie werden sich
# mit den Rueckmeldungen der Linux-Nutzer weiterentwickeln.
$linuxReadme = Join-Path $root 'LIESMICH_Linux.txt'
$linuxStart = Join-Path $root 'starte.sh'
if (-not (Test-Path $linuxReadme)) {
    Write-Warning "LIESMICH_Linux.txt fehlt - das Linux-Paket geht ohne Anleitung raus."
}
if (-not (Test-Path $linuxStart)) {
    Write-Warning "starte.sh fehlt - das Linux-Paket geht ohne Startskript raus."
}

foreach ($t in $targets) {
    Write-Host "  $($t.Rid) …" -ForegroundColor DarkGray
    # Der Publish landet in einem Unterordner mit sprechendem Namen, damit das Archiv
    # spaeter als "ioBroker-Backup-Analyzer/" entpackt und nicht als Stage-Kryptik.
    $stageParent = Join-Path $dist ("_x_" + $t.Rid)
    $stage = Join-Path $stageParent 'ioBroker-Backup-Analyzer'
    & $dotnet publish $avalonia -c Release -r $t.Rid --self-contained true -o $stage | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Publish fuer $($t.Rid) fehlgeschlagen." }

    Copy-Item $license $stage -Force

    switch ($t.Art) {
        'zip' {
            Compress-Archive -Path $stage -DestinationPath (Join-Path $xplat "$($t.Paket).zip") -Force
        }
        'targz' {
            # Linux: Startskript und Anleitung wandern mit ins Archiv, direkt neben das
            # Programm. Das Skript prueft die Systembibliotheken und startet dann erst.
            if (Test-Path $linuxStart) { Copy-Item $linuxStart $stage -Force }
            if (Test-Path $linuxReadme) { Copy-Item $linuxReadme $stage -Force }

            New-UnixTarGz -SourceDir $stage -Destination (Join-Path $xplat "$($t.Paket).tar.gz")
        }
        'app' {
            # macOS erwartet ein .app-Bundle: ein Ordner mit fester Innenstruktur, der vom
            # Finder als Programm behandelt wird. Ohne ihn waere es nur ein Terminal-Binary.
            #
            # Das Bundle liegt im Archiv nicht allein, sondern in einem Ordner mit dem
            # Paketnamen - zusammen mit Anleitung und Lizenz. Ein einziger Eintrag auf
            # oberster Ebene bleibt dabei erhalten, und das ist Absicht: Beim Entpacken
            # entsteht so ein vorhersagbarer Ordner, egal ob per Doppelklick oder per
            # "tar -xzf". Laegen mehrere Eintraege oben, schuettete tar im Terminal App
            # und Textdateien ins aktuelle Verzeichnis.
            $appRoot = Join-Path $dist ("_app_" + $t.Rid)
            $paket = Join-Path $appRoot $t.Paket
            $app = Join-Path $paket 'ioBroker Backup Analyzer.app'
            $macos = Join-Path $app 'Contents\MacOS'
            $resources = Join-Path $app 'Contents\Resources'
            New-Item -ItemType Directory -Force $macos | Out-Null
            New-Item -ItemType Directory -Force $resources | Out-Null
            Copy-Item (Join-Path $stage '*') $macos -Recurse -Force

            # Symbol aus der app.ico der Windows-Fassung, sonst zeigt der Finder nur eine
            # graue Kachel (gemeldet vom macOS-Nutzer).
            New-IcnsFromIco -IcoPath (Join-Path $root 'src\IobBackupAnalyzer.App\app.ico') `
                            -Destination (Join-Path $resources 'AppIcon.icns')

            $minOs = if ($t.Rid -eq 'osx-arm64') { '11.0' } else { '10.15' }
            @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>                <string>ioBroker Backup Analyzer</string>
    <key>CFBundleDisplayName</key>         <string>ioBroker Backup Analyzer</string>
    <key>CFBundleIdentifier</key>          <string>de.iobrokerbackupanalyzer.app</string>
    <key>CFBundleVersion</key>             <string>$appVersion</string>
    <key>CFBundleShortVersionString</key>  <string>$appVersion</string>
    <key>CFBundlePackageType</key>         <string>APPL</string>
    <key>CFBundleExecutable</key>          <string>ioBroker-Backup-Analyzer</string>
    <key>CFBundleIconFile</key>            <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>      <string>$minOs</string>
    <key>NSHighResolutionCapable</key>     <true/>
</dict>
</plist>
"@ | Set-Content -Path (Join-Path $app 'Contents\Info.plist') -Encoding UTF8

            # Anleitung und Lizenz sichtbar neben das Bundle - im Bundle selbst sieht sie
            # niemand, der Finder behandelt die .app als eine einzige Datei.
            if (Test-Path $macReadme) { Copy-Item $macReadme $paket -Force }
            Copy-Item $license $paket -Force

            New-UnixTarGz -SourceDir $paket -Destination (Join-Path $xplat "$($t.Paket).tar.gz")
            Remove-Item $appRoot -Recurse -Force
        }
    }

    Remove-Item $stageParent -Recurse -Force
}

# Das Archiv wird mit korrekten Unix-Rechten geschrieben (siehe New-UnixTarGz). Die
# Quarantaene-Markierung und die ad-hoc-Signatur bleiben Sache des Zielsystems - dafuer
# liegt die Anleitung dem Paket bei.
@"
ioBroker Backup Analyzer - plattformuebergreifende Fassung
==========================================================

Diese Pakete wurden auf einem Windows-Rechner erzeugt. Das Ausfuehrbar-Bit ist bereits im
Archiv gesetzt; macOS muss nur noch die Quarantaene loesen und die App ad hoc signieren.
Beides sind normale Terminal-Befehle, kein Bastelwerk.

macOS
-----
1. tar.gz doppelklicken. Es entsteht ein Ordner mit "ioBroker Backup Analyzer.app",
   der Anleitung LIESMICH_macOS.txt und der Lizenz.
2. Die App an einen lokalen Ort legen - NICHT in iCloud Drive
   ("Library/Mobile Documents/..."): dort ausgelagerte Dateien machen zusaetzlich Aerger.
3. Terminal oeffnen, in den Ordner wechseln, dann diese drei Befehle:

     xattr -cr "ioBroker Backup Analyzer.app"
     codesign --force --deep --sign - "ioBroker Backup Analyzer.app"
     open "ioBroker Backup Analyzer.app"

   TIPP - Anfuehrungszeichen nicht vergessen: Der Name enthaelt Leerzeichen. Ohne
   Anfuehrungszeichen sucht das Terminal nach drei Dateien "ioBroker", "Backup" und
   "Analyzer.app" und meldet, es finde sie nicht.
   Bequemer und fehlerfrei geht es per Ziehen: Den Befehl bis zum Leerzeichen tippen
   (z. B. "xattr -cr "), dann die App aus dem Finder ins Terminalfenster ziehen - der
   vollstaendige Pfad wird korrekt eingesetzt - und erst dann Enter druecken.

   Wozu die Schritte:
     xattr    entfernt die Quarantaene-Markierung heruntergeladener Dateien.
     codesign signiert die App "ad hoc", also ohne Zertifikat. Das ist KOSTENLOS und
              braucht kein Entwicklerkonto - aber auf Apple Silicon ist es PFLICHT:
              Der Kernel beendet unsignierte arm64-Programme sofort. Im Terminal sieht
              man dann nur "zsh: killed", ohne jede weitere Meldung. Der Cross-Build
              unter Windows kann diese Signatur nicht erzeugen, deshalb einmalig hier.
              "--deep" ist noetig, weil im Bundle rund 200 native Bibliotheken liegen,
              die alle mitsigniert werden muessen.

   Hinweis: Eine ad-hoc-Signatur gilt nur auf dem Rechner, auf dem sie erstellt wurde.
   Fuer eine weitergabefaehige, notarisierte App braeuchte es ein Apple-Entwicklerkonto
   (99 USD/Jahr) und einen Mac zum Signieren.

   Falls "Launch failed" (RBSRequestErrorDomain Code 5 / POSIX 111) erscheint, fehlt der
   Binary das Ausfuehrbar-Bit - das passiert bei Paketen aelterer Fassungen. Dann einmalig
   vor den beiden Befehlen oben:

     chmod +x "ioBroker Backup Analyzer.app/Contents/MacOS/ioBroker-Backup-Analyzer"

   Wichtig: chmod VOR codesign, sonst passt die Signatur nicht mehr zum Bundle. Ein
   "chmod +x" auf die .app allein genuegt nicht - das trifft nur den Ordner.

Linux
-----
     tar -xzf ioBroker-Backup-Analyzer_Linux-x64.tar.gz
     cd ioBroker-Backup-Analyzer
     ./starte.sh

   Das Startskript prueft zuerst, ob ICU und fontconfig vorhanden sind, und nennt sonst
   den passenden apt-Befehl. Diese beiden Bibliotheken stellt das System, nicht das
   Paket - auf einem Desktop-Linux sind sie da, auf einem schlanken System nicht:

     sudo apt install libicu76 libfontconfig1      (Debian 13; andere Fassungen siehe
                                                    LIESMICH_Linux.txt im Paket)

   Ohne sie bricht das Programm mit einem englischen .NET-Stapelabzug ab. Der direkte
   Aufruf ./ioBroker-Backup-Analyzer funktioniert weiterhin, nur ohne diese Vorpruefung.

Windows
-------
ZIP entpacken, ioBroker-Backup-Analyzer.exe starten. Fuer Windows ist die Fassung im
uebergeordneten Ordner (WinForms) die gepflegte Empfehlung - diese hier ist vor allem
fuer macOS und Linux gedacht.

Wenn das Programm beim Laden stehen bleibt
------------------------------------------
Bei jedem Ladevorgang wird ein Protokoll geschrieben - jede Zeile sofort, damit auch ein
abgebrochenes Programm die Stelle hinterlaesst, an der es nicht weiterkam:

  Linux/macOS   ladeprotokoll.txt im Programmordner, sonst
                ~/.config/ioBroker-Backup-Analyzer/ladeprotokoll.txt
  Windows       ladeprotokoll.txt neben der EXE, sonst
                %APPDATA%\ioBroker-Backup-Analyzer\ladeprotokoll.txt

Das Protokoll enthaelt nichts aus der Anlage: nur Schritte, Zeiten, Groessen und die
Namensraeume der Adapter - keine Objekt-IDs, keine Werte, keine Namen von Skripten,
Ansichten oder Geraeten, keine vollstaendigen Pfade. Es kann bedenkenlos weitergegeben
werden.

Stand der Portierung
--------------------
Seit v1.17.0 sind alle Tabs der Windows-Fassung vorhanden; die Auswertungslogik ist
ohnehin dieselbe (gemeinsame Core-Bibliothek). Unterschiedlich ist nur die Bedienung -
unter Windows bleibt die WinForms-Fassung die gepflegte Empfehlung.
"@ | Set-Content -Path (Join-Path $xplat 'LIESMICH.txt') -Encoding UTF8

Copy-Item $license $xplat -Force

# Dieselben Anleitungen zusaetzlich lose daneben - sie stecken bereits in den jeweiligen
# Archiven (siehe oben), hier liegen sie nur zum Nachlesen ohne Entpacken.
if (Test-Path $macReadme) { Copy-Item $macReadme $xplat -Force }
if (Test-Path $linuxReadme) { Copy-Item $linuxReadme $xplat -Force }

# ------------------------------------------------- Variante 4: Browser-Fassung (Blazor)
#
# Eine statische Seite fuer den eigenen Webserver. Sie enthaelt dieselbe Auswertung wie die
# Programme daneben - der Core ist derselbe -, laeuft aber vollstaendig im Browser des
# Anwenders. Auf den Server kommt kein Backup: Er liefert nur die Dateien aus.
#
# Das Ergebnis ist ein Ordner zum Hochladen und dasselbe noch einmal als ZIP. Beigelegt
# sind die .htaccess, eine Anleitung und die Pruefseite servertest.html, mit der sich ein
# Apache vor dem ersten Aufruf durchmessen laesst.

Write-Host "`n=== Variante 4: Browser-Fassung (statische Seite) ===" -ForegroundColor Cyan

$web = Join-Path $root 'src\IobBackupAnalyzer.Web'
$webBeilagen = Join-Path $web 'Server'
$webZiel = Join-Path $dist 'web'
$webStage = Join-Path $dist '_web_publish'

if (Test-Path $webStage) { Remove-Item $webStage -Recurse -Force }
if (Test-Path $webZiel) { Remove-Item $webZiel -Recurse -Force }

& $dotnet publish $web -c Release -o $webStage | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Publish der Browser-Fassung fehlgeschlagen." }

# Blazor legt die auszuliefernden Dateien unter wwwroot ab; alles daneben ist Beiwerk des
# Publish-Vorgangs und gehoert nicht auf den Server.
$webRoot = Join-Path $webStage 'wwwroot'
if (-not (Test-Path $webRoot)) { throw "Publish der Browser-Fassung: wwwroot fehlt." }

New-Item -ItemType Directory -Force $webZiel | Out-Null
Copy-Item (Join-Path $webRoot '*') $webZiel -Recurse -Force

# Die Beilagen fuer den Server. Copy-Item nimmt die .htaccess nur mit, wenn sie
# ausdruecklich genannt wird - Platzhalter uebergehen Dateien, die mit einem Punkt
# beginnen.
foreach ($datei in 'servertest.html', 'LIESMICH_Browser.txt') {
    $quelle = Join-Path $webBeilagen $datei
    if (Test-Path $quelle) { Copy-Item $quelle $webZiel -Force }
}

$htaccess = Join-Path $webBeilagen '.htaccess'
if (Test-Path $htaccess) { Copy-Item $htaccess (Join-Path $webZiel '.htaccess') -Force }

$pruefordner = Join-Path $webBeilagen 'servertest'
if (Test-Path $pruefordner) { Copy-Item $pruefordner $webZiel -Recurse -Force }

Copy-Item $license $webZiel -Force

Remove-Item $webStage -Recurse -Force

# Als ZIP zum Weitergeben. Compress-Archive laesst Dateien mit fuehrendem Punkt aus,
# deshalb ueber die ZIP-Bibliothek von .NET - sonst fehlte ausgerechnet die .htaccess.
$webZip = Join-Path $dist 'ioBroker-Backup-Analyzer_Browser.zip'
if (Test-Path $webZip) { Remove-Item $webZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($webZiel, $webZip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)

$webDateien = (Get-ChildItem $webZiel -Recurse -File).Count
$webMb = [math]::Round(((Get-ChildItem $webZiel -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "  dist\web: $webDateien Dateien, $webMb MB (mit den vorkomprimierten Fassungen)" -ForegroundColor DarkGray

# ---------------------------------------------------------------- Optionaler Defender-Scan

$mp = Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe'
if (Test-Path $mp) {
    Write-Host "`n=== Defender-Schnellscan der Einzeldatei ===" -ForegroundColor Cyan
    $exe = Join-Path $dist 'ioBroker-Backup-Analyzer.exe'
    & $mp -Scan -ScanType 3 -File $exe | Select-String -Pattern 'found|threat' | ForEach-Object { $_.Line }
}

# ---------------------------------------------------------------- Zusammenfassung

Write-Host "`n=== Fertig. Ergebnis in dist/ ===" -ForegroundColor Green
Get-ChildItem $dist | Select-Object Name, @{n='MB'; e = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize

Write-Host "Weitergabe-Empfehlung:" -ForegroundColor Yellow
Write-Host "  - VERLAESSLICH (empfohlen): das _Ordner.zip -- entpacken, enthaltene EXE starten."
Write-Host "    Laeuft zuverlaessig unter Smart App Control und ist scanner-freundlich."
Write-Host "  - Einzeldatei .exe: nur, wenn sie auf dem Zielrechner nachweislich startet."
Write-Host "    Unter Smart App Control ist sie pro Build ein Gluecksspiel (siehe README.md)."
