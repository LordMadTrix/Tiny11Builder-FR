<#
.SYNOPSIS
    Create-Tiny11BootableUSB.ps1
    Outil de création directe de clé USB bootable UEFI pour Tiny11 / Windows 11.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$IsoPath,

    [Parameter(Mandatory = $false)]
    [int]$DiskNumber = -1,

    [switch]$Force,

    [switch]$Quiet
)

$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Write-Step {
    param([string]$Message)
    Write-Host "[STATUS] $Message"
}

function Write-InfoLog {
    param([string]$Message)
    Write-Host "[INFO] $Message"
}

# 1. Vérification des privilèges administrateur
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "[ERREUR] Ce script doit être exécuté avec les privilèges administrateur."
    exit 1
}

# 2. Vérification de l'image ISO
if (-not $IsoPath -or -not (Test-Path $IsoPath)) {
    $defaultIso = Join-Path $PSScriptRoot "tiny11.iso"
    if (Test-Path $defaultIso) {
        $IsoPath = $defaultIso
    } else {
        $coreIso = Join-Path $PSScriptRoot "tiny11core.iso"
        if (Test-Path $coreIso) {
            $IsoPath = $coreIso
        }
    }
}

if (-not $IsoPath -or -not (Test-Path $IsoPath)) {
    if ($Quiet) {
        Write-Error "[ERREUR] Le fichier ISO '$IsoPath' est introuvable."
        exit 1
    }
    while (-not $IsoPath -or -not (Test-Path $IsoPath)) {
        $IsoPath = Read-Host "Entrez le chemin complet du fichier ISO"
    }
}

Write-InfoLog "ISO sélectionnée : $IsoPath"

# 3. Détection des lecteurs USB amovibles
$usbDisks = Get-Disk | Where-Object { $_.BusType -eq 'USB' }

if (-not $usbDisks) {
    Write-Error "[ERREUR] Aucun disque USB amovible n'a été détecté. Veuillez insérer une clé USB."
    exit 1
}

# 4. Sélection du disque
$targetDisk = $null
if ($DiskNumber -ge 0) {
    $targetDisk = $usbDisks | Where-Object { $_.Number -eq $DiskNumber }
    if (-not $targetDisk) {
        Write-Error "[ERREUR] Le disque USB numéro $DiskNumber n'a pas été trouvé."
        exit 1
    }
} else {
    if ($Quiet) {
        Write-Error "[ERREUR] Le paramètre -DiskNumber est requis en mode silencieux."
        exit 1
    }
    Write-Host "Disques USB disponibles :" -ForegroundColor Cyan
    foreach ($disk in $usbDisks) {
        $sizeGB = [math]::Round($disk.Size / 1GB, 2)
        Write-Host "  -> Numéro : $($disk.Number) | $($disk.FriendlyName) | $sizeGB Go"
    }
    while ($null -eq $targetDisk) {
        $inputNum = Read-Host "Entrez le numéro du disque USB à formater"
        $targetDisk = $usbDisks | Where-Object { $_.Number -eq [int]$inputNum }
    }
}

Write-InfoLog "Disque cible sélectionné : N°$($targetDisk.Number) - $($targetDisk.FriendlyName)"

# 5. Confirmation
if (-not $Force) {
    $confirm = Read-Host "ATTENTION : Toutes les données sur le disque $($targetDisk.Number) seront effacées ! Confirmer (OUI/NON)"
    if ($confirm -ne 'OUI' -and $confirm -ne 'oui') {
        Write-Host "Annulé par l'utilisateur."
        exit 0
    }
}

# 6. Montage de l'image ISO
Write-Step "Montage de l'image ISO en cours..."
$mountResult = Mount-DiskImage -ImagePath $IsoPath -StorageType ISO -PassThru
Start-Sleep -Seconds 2
$isoVolume = Get-DiskImage -ImagePath $IsoPath | Get-Volume
$isoDrive = "$($isoVolume.DriveLetter):\"
Write-InfoLog "ISO montée sur le lecteur $isoDrive"

try {
    # 7. Préparation et partitionnement GPT / FAT32 (Boot UEFI natif)
    Write-Step "Nettoyage et formatage du disque USB en GPT/FAT32..."
    Clear-Disk -Number $targetDisk.Number -RemoveData -RemoveOEM -Confirm:$false
    Initialize-Disk -Number $targetDisk.Number -PartitionStyle GPT -ErrorAction SilentlyContinue

    $partition = New-Partition -DiskNumber $targetDisk.Number -UseMaximumSize -AssignDriveLetter
    Start-Sleep -Seconds 2
    $usbDriveLetter = "$($partition.DriveLetter):\"
    
    Format-Volume -DriveLetter $partition.DriveLetter -FileSystem FAT32 -NewFileSystemLabel "TINY11" -Confirm:$false | Out-Null
    Write-InfoLog "Clé USB formatée avec succès sur le lecteur $usbDriveLetter"

    # 8. Copie des fichiers d'installation
    Write-Step "Copie des fichiers d'installation vers la clé USB..."
    $installWimPath = Join-Path $isoDrive "sources\install.wim"
    $installEsdPath = Join-Path $isoDrive "sources\install.esd"

    # Copie de tous les fichiers sauf sources\install.wim / install.esd
    robocopy $isoDrive $usbDriveLetter /E /XF "install.wim" "install.esd" /R:1 /W:1 /NP /NDL /NFL | Out-Null

    # Traitement de l'image système install.wim / install.esd (gestion limite FAT32 4 Go)
    $sourceImage = $null
    if (Test-Path $installWimPath) { $sourceImage = $installWimPath }
    elseif (Test-Path $installEsdPath) { $sourceImage = $installEsdPath }

    if ($sourceImage) {
        $sourceSize = (Get-Item $sourceImage).Length
        $maxFat32Size = 4294967295 # 4 Go - 1 octet
        $targetSourcesDir = Join-Path $usbDriveLetter "sources"
        if (-not (Test-Path $targetSourcesDir)) { New-Item -ItemType Directory -Path $targetSourcesDir -Force | Out-Null }

        if ($sourceSize -gt $maxFat32Size -and $sourceImage -like "*.wim") {
            Write-Step "Découpage de l'image WIM volumineuse en fichiers .swm pour compatibilité FAT32/UEFI..."
            $swmPath = Join-Path $targetSourcesDir "install.swm"
            dism /Split-Image /ImageFile:"$sourceImage" /SWMFile:"$swmPath" /FileSize:3800
        } else {
            Write-Step "Copie du fichier système $(Split-Path $sourceImage -Leaf)..."
            Copy-Item -Path $sourceImage -Destination (Join-Path $targetSourcesDir (Split-Path $sourceImage -Leaf)) -Force
        }
    }

    Write-Step "Félicitations ! Votre clé USB bootable Tiny11 est prête."
    Write-Host "[SUCCESS] Clé USB bootable prête avec succès !"
}
catch {
    Write-Error "[ERREUR] Échec de la création de la clé USB : $_"
    exit 1
}
finally {
    Write-Step "Démontage de l'image ISO..."
    Dismount-DiskImage -ImagePath $IsoPath -ErrorAction SilentlyContinue | Out-Null
}
