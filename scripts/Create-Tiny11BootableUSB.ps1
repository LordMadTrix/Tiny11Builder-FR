# =========================================================================
# Create-Tiny11BootableUSB.ps1
# Outil de création directe de clé USB bootable UEFI pour Tiny11 / Windows 11
# =========================================================================

[CmdletBinding()]
param(
    [string]$IsoPath
)

# Vérification des privilèges administrateur
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Ce script doit être exécuté dans une session PowerShell avec privilèges administrateur."
    exit 1
}

Clear-Host
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "     Créateur de Clé USB Bootable UEFI pour Tiny11        " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Sélection ou confirmation du fichier ISO
if (-not $IsoPath -or -not (Test-Path $IsoPath)) {
    $defaultIso = Join-Path $PSScriptRoot "tiny11.iso"
    if (Test-Path $defaultIso) {
        $IsoPath = $defaultIso
        Write-Host "Fichier ISO par défaut détecté : $IsoPath" -ForegroundColor Green
    } else {
        $coreIso = Join-Path $PSScriptRoot "tiny11core.iso"
        if (Test-Path $coreIso) {
            $IsoPath = $coreIso
            Write-Host "Fichier ISO par défaut détecté : $IsoPath" -ForegroundColor Green
        }
    }
}

while (-not $IsoPath -or -not (Test-Path $IsoPath)) {
    $IsoPath = Read-Host "Entrez le chemin complet du fichier ISO de Tiny11"
    if (-not (Test-Path $IsoPath)) {
        Write-Warning "Le fichier '$IsoPath' n'existe pas. Veuillez réessayer."
    }
}

Write-Host "ISO sélectionnée : $IsoPath" -ForegroundColor Green
Write-Host ""

# 2. Détection des lecteurs USB amovibles
Write-Host "Recherche des clés USB connectées..." -ForegroundColor Yellow
$usbDisks = Get-Disk | Where-Object { $_.BusType -eq 'USB' }

if (-not $usbDisks) {
    Write-Error "Aucun disque USB amovible n'a été détecté. Veuillez insérer une clé USB et relancer."
    exit 1
}

Write-Host "Disques USB disponibles :" -ForegroundColor Cyan
foreach ($disk in $usbDisks) {
    $sizeGB = [math]::Round($disk.Size / 1GB, 2)
    Write-Host "  -> Numéro de disque : $($disk.Number) | Modèle : $($disk.FriendlyName) | Taille : $sizeGB Go" -ForegroundColor White
}
Write-Host ""

$selectedDiskNumber = $null
while ($null -eq $selectedDiskNumber) {
    $inputNumber = Read-Host "Entrez le numéro du disque USB à formater"
    $match = $usbDisks | Where-Object { $_.Number -eq [int]$inputNumber }
    if ($match) {
        $selectedDiskNumber = [int]$inputNumber
        $targetDisk = $match
    } else {
        Write-Warning "Numéro de disque invalide. Choisissez parmi les numéros listés."
    }
}

# 3. Avertissement critique de formatage
Write-Host ""
Write-Host "ATTENTION : TOUTES LES DONNÉES SUR LE DISQUE $($targetDisk.Number) ($($targetDisk.FriendlyName)) SERONT DÉFINITIVEMENT EFFACÉES !" -ForegroundColor Red
$confirm = Read-Host "Êtes-vous certain de vouloir continuer ? Tapez 'OUI' pour confirmer"
if ($confirm -ne 'OUI' -and $confirm -ne 'oui') {
    Write-Host "Opération annulée par l'utilisateur." -ForegroundColor Yellow
    exit 0
}

# 4. Montage de l'image ISO
Write-Host ""
Write-Host "Montage de l'ISO Tiny11..." -ForegroundColor Yellow
$mountResult = Mount-DiskImage -ImagePath $IsoPath -StorageType ISO -PassThru
$isoVolume = Get-DiskImage -ImagePath $IsoPath | Get-Volume
$isoDrive = "$($isoVolume.DriveLetter):\"
Write-Host "ISO montée sur le lecteur $isoDrive" -ForegroundColor Green

# 5. Préparation et formatage de la clé USB (GPT / FAT32 pour compatibilité UEFI universelle)
Write-Host "Formatage du disque USB $($targetDisk.Number)..." -ForegroundColor Yellow
try {
    # Nettoyage du disque
    Clear-Disk -Number $targetDisk.Number -RemoveData -RemoveOEM -Confirm:$false
    Initialize-Disk -Number $targetDisk.Number -PartitionStyle GPT -ErrorAction SilentlyContinue

    # Création d'une partition primaire FAT32 active pour boot UEFI
    $partition = New-Partition -DiskNumber $targetDisk.Number -UseMaximumSize -AssignDriveLetter
    $usbDriveLetter = "$($partition.DriveLetter):\"
    Write-Host "Formatage en FAT32 (compatibilité UEFI)..." -ForegroundColor Yellow
    Format-Volume -DriveLetter $partition.DriveLetter -FileSystem FAT32 -NewFileSystemLabel "TINY11" -Confirm:$false | Out-Null
    Write-Host "Clé USB prête sur le lecteur $usbDriveLetter" -ForegroundColor Green
} catch {
    Write-Error "Erreur lors de la préparation du disque USB : $_"
    Dismount-DiskImage -ImagePath $IsoPath | Out-Null
    exit 1
}

# 6. Copie des fichiers et découpage de l'image si nécessaire
try {
    Write-Host "Copie des fichiers de démarrage et d'installation vers la clé USB..." -ForegroundColor Yellow

    # Copier tous les éléments sauf sources\install.wim / install.esd
    Get-ChildItem -Path $isoDrive | Where-Object { $_.Name -ne 'sources' } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $usbDriveLetter -Recurse -Force
    }

    # Créer le dossier sources sur la clé
    $usbSources = Join-Path $usbDriveLetter "sources"
    if (-not (Test-Path $usbSources)) { New-Item -ItemType Directory -Path $usbSources -Force | Out-Null }

    # Copier tout dans sources sauf le fichier image principal install.*
    Get-ChildItem -Path (Join-Path $isoDrive "sources") | Where-Object { $_.Name -notmatch '^install\.(wim|esd)$' } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $usbSources -Recurse -Force
    }

    # Vérification de l'image système
    $sourceWim = Join-Path $isoDrive "sources\install.wim"
    if (-not (Test-Path $sourceWim)) {
        $sourceWim = Join-Path $isoDrive "sources\install.esd"
    }

    if (Test-Path $sourceWim) {
        $wimSize = (Get-Item $sourceWim).Length
        $maxFat32 = 4294967295 # 4 Go - 1 octet

        if ($wimSize -gt $maxFat32) {
            Write-Host "L'image install.wim fait plus de 4 Go. Découpage automatique en fichiers .swm pour FAT32/UEFI..." -ForegroundColor Cyan
            $destSwm = Join-Path $usbSources "install.swm"
            & dism.exe /Split-Image "/ImageFile:$sourceWim" "/SWMFile:$destSwm" "/FileSize:3800"
            if ($LASTEXITCODE -ne 0) { throw "Échec du découpage de l'image avec DISM." }
            Write-Host "Image découpée avec succès." -ForegroundColor Green
        } else {
            Write-Host "Copie directe de l'image système ($([math]::Round($wimSize / 1GB, 2)) Go)..." -ForegroundColor Yellow
            Copy-Item -Path $sourceWim -Destination $usbSources -Force
            Write-Host "Image copiée avec succès." -ForegroundColor Green
        }
    }

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " Félicitations ! Votre clé USB bootable Tiny11 est prête." -ForegroundColor Green
    Write-Host " Vous pouvez redémarrer votre PC et démarrer dessus en UEFI." -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
} catch {
    Write-Error "Une erreur est survenue pendant la copie des fichiers : $_"
} finally {
    Write-Host "Démontage de l'image ISO..." -ForegroundColor Yellow
    Dismount-DiskImage -ImagePath $IsoPath | Out-Null
    Write-Host "Terminé." -ForegroundColor Cyan
}
