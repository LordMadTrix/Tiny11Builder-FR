<#
.SYNOPSIS
    Script pour créer une image allégée de Windows 11.

.DESCRIPTION
    Script automatisant la création d'une image épurée de Windows 11, similaire à tiny10.
    L'objectif principal est de n'utiliser que les utilitaires Microsoft officiels tels que DISM, sans outil tiers externe.
    Le seul exécutable externe utilisé est oscdimg.exe (fourni par Microsoft dans le Windows ADK) pour générer l'image ISO bootable.

.PARAMETER ISO
    Lettre de lecteur attribuée à l'image ISO montée (ex: E)

.PARAMETER SCRATCH
    Lettre de lecteur du disque de travail souhaité (ex: D)

.EXAMPLE
    .\tiny11maker.ps1 E D
    .\tiny11maker.ps1 -ISO E -SCRATCH D
    .\tiny11maker.ps1 -SCRATCH D -ISO E
    .\tiny11maker.ps1

    * Si vous utilisez des paramètres positionnels, le premier doit être l'ISO monté et le second le disque de travail.
    Il est préférable d'utiliser des paramètres nommés (ex: "-ISO") pour les spécifier dans l'ordre de votre choix.

.NOTES
    Auteur: ntdevlabs (Traduction et adaptation en français)
    Date: 09-07-25
#>

#---------[ Paramètres ]---------#
param (
    [ValidatePattern('^[c-zC-Z]$')][string]$ISO,
    [ValidatePattern('^[c-zC-Z]$')][string]$SCRATCH
)

if (-not $SCRATCH) {
    $ScratchDisk = $PSScriptRoot -replace '[\\]+$', ''
} else {
    $ScratchDisk = $SCRATCH + ":"
}

#---------[ Fonctions ]---------#
function Set-RegistryValue {
    param (
        [string]$path,
        [string]$name,
        [string]$type,
        [string]$value
    )
    try {
        & 'reg' 'add' $path '/v' $name '/t' $type '/d' $value '/f' | Out-Null
        Write-Output "Valeur de Registre définie : $path\$name"
    } catch {
        Write-Output "Erreur lors de la définition de la valeur de Registre : $_"
    }
}

function Remove-RegistryValue {
    param (
		[string]$path
	)
	try {
		& 'reg' 'delete' $path '/f' | Out-Null
		Write-Output "Valeur de Registre supprimée : $path"
	} catch {
		Write-Output "Erreur lors de la suppression de la valeur de Registre : $_"
	}
}

#---------[ Exécution ]---------#
# Vérifier si la stratégie d'exécution PowerShell est restreinte
if ((Get-ExecutionPolicy) -eq 'Restricted') {
    Write-Output "Votre stratégie d'exécution PowerShell est actuellement définie sur Restricted, ce qui empêche l'exécution de scripts. Voulez-vous la définir sur RemoteSigned ? (oui/non)"
    $response = Read-Host
    if ($response -match '^(oui|o|yes|y)$') {
        Set-ExecutionPolicy RemoteSigned -Scope CurrentUser -Confirm:$false
    } else {
        Write-Output "Le script ne peut pas s'exécuter sans modifier la stratégie d'exécution. Fermeture..."
        exit
    }
}

# Vérifier et exécuter le script en tant qu'administrateur si nécessaire
$adminSID = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-32-544")
$adminGroup = $adminSID.Translate([System.Security.Principal.NTAccount])
$myWindowsID=[System.Security.Principal.WindowsIdentity]::GetCurrent()
$myWindowsPrincipal=new-object System.Security.Principal.WindowsPrincipal($myWindowsID)
$adminRole=[System.Security.Principal.WindowsBuiltInRole]::Administrator
if (! $myWindowsPrincipal.IsInRole($adminRole))
{
    Write-Output "Redémarrage du créateur d'image Tiny11 en tant qu'administrateur dans une nouvelle fenêtre. Vous pouvez fermer celle-ci."
    $newProcess = new-object System.Diagnostics.ProcessStartInfo "PowerShell";
    $newProcess.Arguments = $myInvocation.MyCommand.Definition;
    $newProcess.Verb = "runas";
    [System.Diagnostics.Process]::Start($newProcess);
    exit
}

if (-not (Test-Path -Path "$PSScriptRoot/autounattend.xml")) {
    Invoke-RestMethod "https://raw.githubusercontent.com/ntdevlabs/tiny11builder/refs/heads/main/autounattend.xml" -OutFile "$PSScriptRoot/autounattend.xml"
}

# Démarrage du journal et configuration de la fenêtre
Start-Transcript -Path "$PSScriptRoot\tiny11_$(get-date -f yyyyMMdd_HHmms).log"

$Host.UI.RawUI.WindowTitle = "Créateur d'image Tiny11"
Clear-Host
Write-Output "Bienvenue dans le créateur d'image Tiny11 ! Version : 09-07-25"

$hostArchitecture = $Env:PROCESSOR_ARCHITECTURE
New-Item -ItemType Directory -Force -Path "$ScratchDisk\tiny11\sources" | Out-Null
do {
    if (-not $ISO) {
        $DriveLetter = Read-Host "Veuillez entrer la lettre de lecteur de l'image Windows 11 (ex: E)"
    } else {
        $DriveLetter = $ISO
    }
    if ($DriveLetter -match '^[c-zC-Z]$') {
        $DriveLetter = $DriveLetter + ":"
        Write-Output "Lettre de lecteur définie sur $DriveLetter"
    } else {
        Write-Output "Lettre de lecteur non valide. Veuillez entrer une lettre entre C et Z."
    }
} while ($DriveLetter -notmatch '^[c-zC-Z]:$')

if ((Test-Path "$DriveLetter\sources\boot.wim") -eq $false -or (Test-Path "$DriveLetter\sources\install.wim") -eq $false) {
    if ((Test-Path "$DriveLetter\sources\install.esd") -eq $true) {
        Write-Output "Fichier install.esd détecté, conversion en install.wim..."
        Get-WindowsImage -ImagePath $DriveLetter\sources\install.esd
        $index = Read-Host "Veuillez entrer l'index de l'image"
        Write-Output ' '
        Write-Output "Conversion de install.esd en install.wim. Cela peut prendre un certain temps..."
        Export-WindowsImage -SourceImagePath $DriveLetter\sources\install.esd -SourceIndex $index -DestinationImagePath $ScratchDisk\tiny11\sources\install.wim -Compressiontype Maximum -CheckIntegrity
    } else {
        Write-Output "Impossible de trouver les fichiers d'installation de Windows sur le lecteur spécifié."
        Write-Output "Veuillez entrer la lettre correcte du lecteur DVD ou de l'image ISO montée."
        exit
    }
}

Write-Output "Copie de l'image Windows en cours..."
Copy-Item -Path "$DriveLetter\*" -Destination "$ScratchDisk\tiny11" -Recurse -Force | Out-Null
Set-ItemProperty -Path "$ScratchDisk\tiny11\sources\install.esd" -Name IsReadOnly -Value $false > $null 2>&1
Remove-Item "$ScratchDisk\tiny11\sources\install.esd" > $null 2>&1
Write-Output "Copie terminée !"
Start-Sleep -Seconds 2
Clear-Host
Write-Output "Récupération des informations de l'image :"
$ImagesIndex = (Get-WindowsImage -ImagePath $ScratchDisk\tiny11\sources\install.wim).ImageIndex
while ($ImagesIndex -notcontains $index) {
    Get-WindowsImage -ImagePath $ScratchDisk\tiny11\sources\install.wim
    $index = Read-Host "Veuillez entrer l'index de l'image"
}
Write-Output "Montage de l'image Windows. Cela peut prendre un certain temps..."
$wimFilePath = "$ScratchDisk\tiny11\sources\install.wim"
& takeown "/F" $wimFilePath
& icacls $wimFilePath "/grant" "$($adminGroup.Value):(F)"
try {
    Set-ItemProperty -Path $wimFilePath -Name IsReadOnly -Value $false -ErrorAction Stop
} catch {
	Write-Error "$wimFilePath introuvable"
}
New-Item -ItemType Directory -Force -Path "$ScratchDisk\scratchdir" > $null
Mount-WindowsImage -ImagePath $ScratchDisk\tiny11\sources\install.wim -Index $index -Path $ScratchDisk\scratchdir

$imageIntl = & dism /English /Get-Intl "/Image:$($ScratchDisk)\scratchdir"
$languageLine = $imageIntl -split '\n' | Where-Object { $_ -match 'Default system UI language : ([a-zA-Z]{2}-[a-zA-Z]{2})' }

if ($languageLine) {
    $languageCode = $Matches[1]
    Write-Output "Code de langue par défaut de l'interface : $languageCode"
} else {
    Write-Output "Code de langue par défaut non trouvé."
}

$imageInfo = & 'dism' '/English' '/Get-WimInfo' "/wimFile:$($ScratchDisk)\tiny11\sources\install.wim" "/index:$index"
$lines = $imageInfo -split '\r?\n'

foreach ($line in $lines) {
    if ($line -like '*Architecture : *') {
        $architecture = $line -replace 'Architecture : ',''
        if ($architecture -eq 'x64') {
            $architecture = 'amd64'
        }
        Write-Output "Architecture : $architecture"
        break
    }
}

if (-not $architecture) {
    Write-Output "Informations d'architecture non trouvées."
}

Write-Output "Montage terminé ! Début de la suppression des applications..."

$packages = & 'dism' '/English' "/image:$($ScratchDisk)\scratchdir" '/Get-ProvisionedAppxPackages' |
    ForEach-Object {
        if ($_ -match 'PackageName : (.*)') {
            $matches[1]
        }
    }

$packagePrefixes = 'AppUp.IntelManagementandSecurityStatus',
'Clipchamp.Clipchamp', 
'DolbyLaboratories.DolbyAccess',
'DolbyLaboratories.DolbyDigitalPlusDecoderOEM',
'Microsoft.BingNews',
'Microsoft.BingSearch',
'Microsoft.BingWeather',
'Microsoft.Copilot',
'Microsoft.Windows.Ai.Copilot.Provider',
'Microsoft.Windows.Recall',
'Microsoft.Windows.CrossDevice',
'Microsoft.GamingApp',
'Microsoft.GetHelp',
'Microsoft.Getstarted',
'Microsoft.Microsoft3DViewer',
'Microsoft.MicrosoftOfficeHub',
'Microsoft.MicrosoftSolitaireCollection',
'Microsoft.MicrosoftStickyNotes',
'Microsoft.MixedReality.Portal',
'Microsoft.MSPaint',
'Microsoft.Office.OneNote',
'Microsoft.OfficePushNotificationUtility',
'Microsoft.OutlookForWindows',
'Microsoft.Paint',
'Microsoft.People',
'Microsoft.PowerAutomateDesktop',
'Microsoft.SkypeApp',
'Microsoft.StartExperiencesApp',
'Microsoft.Todos',
'Microsoft.Wallet',
'Microsoft.Windows.DevHome',
'Microsoft.Windows.DevHomeGitHubExtension',
'Microsoft.Windows.DevHomeAzureExtension',
'Microsoft.Windows.Copilot',
'Microsoft.Windows.Teams',
'Microsoft.WindowsAlarms',
'Microsoft.WindowsCamera',
'microsoft.windowscommunicationsapps',
'Microsoft.WindowsFeedbackHub',
'Microsoft.WindowsMaps',
'Microsoft.WindowsSoundRecorder',
'Microsoft.WindowsTerminal',
'Microsoft.Xbox.TCUI',
'Microsoft.XboxApp',
'Microsoft.XboxGameOverlay',
'Microsoft.XboxGamingOverlay',
'Microsoft.XboxIdentityProvider',
'Microsoft.XboxSpeechToTextOverlay',
'Microsoft.YourPhone',
'Microsoft.ZuneMusic',
'Microsoft.ZuneVideo',
'MicrosoftCorporationII.MicrosoftFamily',
'MicrosoftCorporationII.QuickAssist',
'MSTeams',
'MicrosoftTeams', 
'Microsoft.WindowsTerminal',
'Microsoft.549981C3F5F10'

$packagesToRemove = $packages | Where-Object {
    $packageName = $_
    $packagePrefixes -contains ($packagePrefixes | Where-Object { $packageName -like "*$_*" })
}
foreach ($package in $packagesToRemove) {
    & 'dism' '/English' "/image:$($ScratchDisk)\scratchdir" '/Remove-ProvisionedAppxPackage' "/PackageName:$package"
}

Write-Output "Suppression de Microsoft Edge..."
Remove-Item -Path "$ScratchDisk\scratchdir\Program Files (x86)\Microsoft\Edge" -Recurse -Force | Out-Null
Remove-Item -Path "$ScratchDisk\scratchdir\Program Files (x86)\Microsoft\EdgeUpdate" -Recurse -Force | Out-Null
Remove-Item -Path "$ScratchDisk\scratchdir\Program Files (x86)\Microsoft\EdgeCore" -Recurse -Force | Out-Null
& 'takeown' '/f' "$ScratchDisk\scratchdir\Windows\System32\Microsoft-Edge-Webview" '/r' | Out-Null
& 'icacls' "$ScratchDisk\scratchdir\Windows\System32\Microsoft-Edge-Webview" '/grant' "$($adminGroup.Value):(F)" '/T' '/C' | Out-Null
Remove-Item -Path "$ScratchDisk\scratchdir\Windows\System32\Microsoft-Edge-Webview" -Recurse -Force | Out-Null
Write-Output "Suppression de OneDrive..."
& 'takeown' '/f' "$ScratchDisk\scratchdir\Windows\System32\OneDriveSetup.exe" | Out-Null
& 'icacls' "$ScratchDisk\scratchdir\Windows\System32\OneDriveSetup.exe" '/grant' "$($adminGroup.Value):(F)" '/T' '/C' | Out-Null
Remove-Item -Path "$ScratchDisk\scratchdir\Windows\System32\OneDriveSetup.exe" -Force | Out-Null
Write-Output "Suppression terminée !"
Start-Sleep -Seconds 2
Clear-Host
Write-Output "Chargement des ruches du Registre..."
reg load HKLM\zCOMPONENTS $ScratchDisk\scratchdir\Windows\System32\config\COMPONENTS | Out-Null
reg load HKLM\zDEFAULT $ScratchDisk\scratchdir\Windows\System32\config\default | Out-Null
reg load HKLM\zNTUSER $ScratchDisk\scratchdir\Users\Default\ntuser.dat | Out-Null
reg load HKLM\zSOFTWARE $ScratchDisk\scratchdir\Windows\System32\config\SOFTWARE | Out-Null
reg load HKLM\zSYSTEM $ScratchDisk\scratchdir\Windows\System32\config\SYSTEM | Out-Null
Write-Output "Contournement des prérequis matériels (sur l'image système) :"
Set-RegistryValue 'HKLM\zDEFAULT\Control Panel\UnsupportedHardwareNotificationCache' 'SV1' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zDEFAULT\Control Panel\UnsupportedHardwareNotificationCache' 'SV2' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Control Panel\UnsupportedHardwareNotificationCache' 'SV1' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Control Panel\UnsupportedHardwareNotificationCache' 'SV2' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassCPUCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassRAMCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassSecureBootCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassStorageCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassTPMCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\MoSetup' 'AllowUpgradesWithUnsupportedTPMOrCPU' 'REG_DWORD' '1'
Write-Output "Désactivation des applications sponsorisées et suggestions :"
Set-RegistryValue 'HKLM\zNTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'OemPreInstalledAppsEnabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'PreInstalledAppsEnabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SilentInstalledAppsEnabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsConsumerFeatures' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'ContentDeliveryAllowed' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zSOFTWARE\Microsoft\PolicyManager\current\device\Start' 'ConfigureStartPins' 'REG_SZ' '{"pinnedList": [{}]}'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'FeatureManagementEnabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'PreInstalledAppsEverEnabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SoftLandingEnabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContentEnabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-310093Enabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338388Enabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338389Enabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338393Enabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-353694Enabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-353696Enabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SystemPaneSuggestionsEnabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\PushToInstall' 'DisablePushToInstall' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\MRT' 'DontOfferThroughWUAU' 'REG_DWORD' '1'
Remove-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\Subscriptions'
Remove-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SuggestedApps'
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableConsumerAccountStateContent' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableCloudOptimizedContent' 'REG_DWORD' '1'
Write-Output "Activation des comptes locaux et installation 100% hors-ligne lors de l'OOBE :"
Set-RegistryValue 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' 'BypassNRO' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' 'DisableOOBEUpdate' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' 'HideOnlineAccountScreens' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' 'DisableVoice' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\OOBE' 'DisablePrivacyExperience' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\OOBE' 'ProtectYourPC' 'REG_DWORD' '3'
Copy-Item -Path "$PSScriptRoot\autounattend.xml" -Destination "$ScratchDisk\scratchdir\Windows\System32\Sysprep\autounattend.xml" -Force | Out-Null

Write-Output "Désactivation du stockage réservé :"
Set-RegistryValue 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager' 'ShippedWithReserves' 'REG_DWORD' '0'
Write-Output "Désactivation du chiffrement automatique de périphérique BitLocker :"
Set-RegistryValue 'HKLM\zSYSTEM\ControlSet001\Control\BitLocker' 'PreventDeviceEncryption' 'REG_DWORD' '1'
Write-Output "Désactivation de l'icône de conversation (Chat) :"
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\Windows Chat' 'ChatIcon' 'REG_DWORD' '3'
Set-RegistryValue 'HKLM\zNTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarMn' 'REG_DWORD' '0'
Write-Output "Suppression des entrées de registre liées à Edge..."
Remove-RegistryValue "HKEY_LOCAL_MACHINE\zSOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge"
Remove-RegistryValue "HKEY_LOCAL_MACHINE\zSOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge Update"
Write-Output "Désactivation de la sauvegarde des dossiers OneDrive :"
Set-RegistryValue "HKLM\zSOFTWARE\Policies\Microsoft\Windows\OneDrive" "DisableFileSyncNGSC" "REG_DWORD" "1"
Write-Output "Désactivation de la télémétrie :"
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' 'Enabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\Privacy' 'TailoredExperiencesWithDiagnosticDataEnabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy' 'HasAccepted' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Input\TIPC' 'Enabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\InputPersonalization' 'RestrictImplicitInkCollection' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\InputPersonalization' 'RestrictImplicitTextCollection' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\InputPersonalization\TrainedDataStore' 'HarvestContacts' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\Personalization\Settings' 'AcceptedPrivacyPolicy' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zSYSTEM\ControlSet001\Services\dmwappushservice' 'Start' 'REG_DWORD' '4'
Set-RegistryValue 'HKLM\zSYSTEM\ControlSet001\Services\DiagTrack' 'Start' 'REG_DWORD' '4'

Write-Output "Application des optimisations de performances et gaming :"
Set-RegistryValue 'HKLM\zNTUSER\Software\Microsoft\GameBar' 'AllowAutoGameMode' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zNTUSER\System\GameConfigStore' 'GameDVR_Enabled' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\GameDVR' 'AllowGameDVR' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zDEFAULT\Control Panel\Desktop' 'MenuShowDelay' 'REG_SZ' '0'
Set-RegistryValue 'HKLM\zNTUSER\Control Panel\Desktop' 'MenuShowDelay' 'REG_SZ' '0'

$blockUpdates = Read-Host "Souhaitez-vous bloquer les mises à jour Windows au premier démarrage pour éviter les ralentissements ? [O/n]"
if ($blockUpdates -notmatch '^(n|non|no)$') {
    Write-Output "Configuration du blocage des mises à jour au premier démarrage..."
    Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'NoAutoUpdate' 'REG_DWORD' '1'
    Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'AUOptions' 'REG_DWORD' '2'
    Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' 'DisableWindowsUpdateAccess' 'REG_DWORD' '1'
}

$disableDefender = Read-Host "Souhaitez-vous désactiver Windows Defender (protection temps réel) pour économiser la RAM/CPU ? [o/N]"
if ($disableDefender -match '^(o|oui|y|yes)$') {
    Write-Output "Désactivation de Windows Defender hors-ligne..."
    Set-RegistryValue 'HKLM\zSYSTEM\ControlSet001\Services\WinDefend' 'Start' 'REG_DWORD' '4'
    Set-RegistryValue 'HKLM\zSYSTEM\ControlSet001\Services\WdNisSvc' 'Start' 'REG_DWORD' '4'
    Set-RegistryValue 'HKLM\zSYSTEM\ControlSet001\Services\WdNisDrv' 'Start' 'REG_DWORD' '4'
    Set-RegistryValue 'HKLM\zSYSTEM\ControlSet001\Services\WdFilter' 'Start' 'REG_DWORD' '4'
    Set-RegistryValue 'HKLM\zSYSTEM\ControlSet001\Services\Sense' 'Start' 'REG_DWORD' '4'
    Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows Defender' 'DisableAntiSpyware' 'REG_DWORD' '1'
    Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection' 'DisableRealtimeMonitoring' 'REG_DWORD' '1'
    Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection' 'DisableBehaviorMonitoring' 'REG_DWORD' '1'
    Set-RegistryValue 'HKLM\zSOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection' 'DisableOnAccessProtection' 'REG_DWORD' '1'
}

$defaultDefControl = "C:\Users\madtr\Downloads\Defender-Control-v2.1\Defender Control"
$embedDefControl = $false
if (Test-Path $defaultDefControl) {
    $askDefControl = Read-Host "L'outil Defender Control v2.1 a été détecté dans vos téléchargements. Souhaitez-vous l'embarquer dans l'image (C:\Tools\Defender Control + raccourci Bureau) ? [O/n]"
    if ($askDefControl -notmatch '^(n|non|no)$') {
        $embedDefControl = $true
        $toolsDir = "$ScratchDisk\scratchdir\Tools\Defender Control"
        New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
        Copy-Item -Path "$defaultDefControl\*" -Destination $toolsDir -Recurse -Force
        Write-Output "Defender Control copié avec succès dans l'image ($toolsDir)."
    }
}

Write-Host ""
Write-Host "Sélection du navigateur web à pré-installer au premier démarrage :" -ForegroundColor Cyan
Write-Host "  1 - Mozilla Firefox (Open-source, recommandé)"
Write-Host "  2 - Brave Browser (Open-source, bloqueur de pub et anti-pistage)"
Write-Host "  3 - Google Chrome"
Write-Host "  4 - LibreWolf (Open-source, axé sur la vie privée)"
Write-Host "  5 - Aucun (aucun navigateur supplémentaire)"
$bChoice = Read-Host "Votre choix [1-5] (défaut: 1)"

$browserPkg = switch ($bChoice) {
    "2" { "Brave.Brave" }
    "3" { "Google.Chrome" }
    "4" { "LibreWolf.LibreWolf" }
    "5" { $null }
    default { "Mozilla.Firefox" }
}

$installOpenSource = Read-Host "Souhaitez-vous pré-installer les outils libres essentiels (VLC, 7-Zip, Notepad++) ? [O/n]"

Write-Output "Génération du script post-installation SetupComplete.cmd..."
$scriptsDir = "$ScratchDisk\scratchdir\Windows\Setup\Scripts"
if (!(Test-Path $scriptsDir)) { New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null }

$setupScriptLines = @(
    "@echo off",
    "echo [Tiny11] Configuration post-installation en cours... > `"%WINDIR%\tiny11_setup.log`"",
    "sc config DiagTrack start= disabled >> `"%WINDIR%\tiny11_setup.log`" 2>&1",
    "sc stop DiagTrack >> `"%WINDIR%\tiny11_setup.log`" 2>&1",
    "sc config dmwappushservice start= disabled >> `"%WINDIR%\tiny11_setup.log`" 2>&1",
    "sc stop dmwappushservice >> `"%WINDIR%\tiny11_setup.log`" 2>&1"
)

if ($disableDefender -match '^(o|oui|y|yes)$') {
    $setupScriptLines += @(
        "REM Desactivation de Windows Defender",
        "powershell -NoProfile -ExecutionPolicy Bypass -Command `"Set-MpPreference -DisableRealtimeMonitoring `$true -DisableBehaviorMonitoring `$true`" >> `"%WINDIR%\tiny11_setup.log`" 2>&1"
    )
}
if ($blockUpdates -notmatch '^(n|non|no)$') {
    $setupScriptLines += @(
        "REM Blocage du service Windows Update au premier demarrage",
        "net stop wuauserv >> `"%WINDIR%\tiny11_setup.log`" 2>&1",
        "sc config wuauserv start= disabled >> `"%WINDIR%\tiny11_setup.log`" 2>&1"
    )
}
if ($embedDefControl) {
    $setupScriptLines += @(
        "REM Raccourci Defender Control sur le Bureau public",
        "powershell -NoProfile -ExecutionPolicy Bypass -Command `"`$ws = New-Object -ComObject WScript.Shell; `$s = `$ws.CreateShortcut('C:\Users\Public\Desktop\Defender Control.lnk'); `$s.TargetPath = 'C:\Tools\Defender Control\dControl.exe'; `$s.WorkingDirectory = 'C:\Tools\Defender Control'; `$s.Save()`" >> `"%WINDIR%\tiny11_setup.log`" 2>&1"
    )
}

$appsToInstall = @()
if ($browserPkg) { $appsToInstall += $browserPkg }
if ($installOpenSource -notmatch '^(n|non|no)$') {
    $appsToInstall += @("VideoLAN.VLC", "7zip.7zip", "Notepad++.Notepad++")
}

if ($appsToInstall.Count -gt 0) {
    $setupScriptLines += "echo [Tiny11] Installation des applications en cours... >> `"%WINDIR%\tiny11_setup.log`""
    foreach ($pkg in $appsToInstall) {
        $setupScriptLines += "winget install --id $pkg -e --silent --accept-source-agreements --accept-package-agreements >> `"%WINDIR%\tiny11_setup.log`" 2>&1"
    }
}

$setupScriptLines += @(
    "del /F /Q `"%WINDIR%\Panther\*.log`" 2>nul",
    "echo [Tiny11] Terminé. >> `"%WINDIR%\tiny11_setup.log`"",
    "exit 0"
)

[System.IO.File]::WriteAllLines("$scriptsDir\SetupComplete.cmd", $setupScriptLines)

Write-Host "Suppression des fichiers de tâches planifiées inutiles..."
$tasksPath = "$ScratchDisk\scratchdir\Windows\System32\Tasks"

# Application Compatibility Appraiser
Remove-Item -Path "$tasksPath\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser" -Force -ErrorAction SilentlyContinue

# Customer Experience Improvement Program
Remove-Item -Path "$tasksPath\Microsoft\Windows\Customer Experience Improvement Program" -Recurse -Force -ErrorAction SilentlyContinue

# Program Data Updater
Remove-Item -Path "$tasksPath\Microsoft\Windows\Application Experience\ProgramDataUpdater" -Force -ErrorAction SilentlyContinue

# Chkdsk Proxy
Remove-Item -Path "$tasksPath\Microsoft\Windows\Chkdsk\Proxy" -Force -ErrorAction SilentlyContinue

# Windows Error Reporting
Remove-Item -Path "$tasksPath\Microsoft\Windows\Windows Error Reporting\QueueReporting" -Force -ErrorAction SilentlyContinue
Write-Host "Fichiers de tâches supprimés."
Write-Host "Déchargement du Registre..."
reg unload HKLM\zCOMPONENTS | Out-Null
reg unload HKLM\zDEFAULT | Out-Null
reg unload HKLM\zNTUSER | Out-Null
reg unload HKLM\zSOFTWARE | Out-Null
reg unload HKLM\zSYSTEM | Out-Null

$addDrivers = Read-Host "Souhaitez-vous intégrer un dossier de pilotes personnalisés (.inf) dans l'image ? [o/N]"
if ($addDrivers -match '^(o|oui|y|yes)$') {
    $driverFolder = Read-Host "Indiquez le chemin d'accès au dossier contenant vos pilotes (.inf)"
    if (Test-Path $driverFolder) {
        Write-Output "Intégration des pilotes depuis $driverFolder..."
        dism.exe /Image:$ScratchDisk\scratchdir /Add-Driver /Driver:$driverFolder /Recurse
    } else {
        Write-Warning "Dossier introuvable : $driverFolder. Étape ignorée."
    }
}

Write-Output "Nettoyage des composants de l'image..."
dism.exe /Image:$ScratchDisk\scratchdir /Cleanup-Image /StartComponentCleanup /ResetBase
Write-Output "Nettoyage terminé."
Write-Output ' '
Write-Output "Démontage de l'image et enregistrement des modifications..."
Dismount-WindowsImage -Path $ScratchDisk\scratchdir -Save
Write-Host "Exportation et compression de l'image..."
Dism.exe /Export-Image /SourceImageFile:"$ScratchDisk\tiny11\sources\install.wim" /SourceIndex:$index /DestinationImageFile:"$ScratchDisk\tiny11\sources\install2.wim" /Compress:recovery
Remove-Item -Path "$ScratchDisk\tiny11\sources\install.wim" -Force | Out-Null
Rename-Item -Path "$ScratchDisk\tiny11\sources\install2.wim" -NewName "install.wim" | Out-Null
Write-Output "Image Windows terminée. Poursuite avec l'image de démarrage (boot.wim)."
Start-Sleep -Seconds 2

Clear-Host
Write-Output "Montage de l'image de démarrage (boot.wim) :"
$wimFilePath = "$ScratchDisk\tiny11\sources\boot.wim"
& takeown "/F" $wimFilePath | Out-Null
& icacls $wimFilePath "/grant" "$($adminGroup.Value):(F)"
Set-ItemProperty -Path $wimFilePath -Name IsReadOnly -Value $false
Mount-WindowsImage -ImagePath $ScratchDisk\tiny11\sources\boot.wim -Index 2 -Path $ScratchDisk\scratchdir
Write-Output "Chargement des ruches du Registre..."
reg load HKLM\zCOMPONENTS $ScratchDisk\scratchdir\Windows\System32\config\COMPONENTS
reg load HKLM\zDEFAULT $ScratchDisk\scratchdir\Windows\System32\config\default
reg load HKLM\zNTUSER $ScratchDisk\scratchdir\Users\Default\ntuser.dat
reg load HKLM\zSOFTWARE $ScratchDisk\scratchdir\Windows\System32\config\SOFTWARE
reg load HKLM\zSYSTEM $ScratchDisk\scratchdir\Windows\System32\config\SYSTEM

Write-Output "Contournement des prérequis matériels (sur l'image d'installation) :"
Set-RegistryValue 'HKLM\zDEFAULT\Control Panel\UnsupportedHardwareNotificationCache' 'SV1' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zDEFAULT\Control Panel\UnsupportedHardwareNotificationCache' 'SV2' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Control Panel\UnsupportedHardwareNotificationCache' 'SV1' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zNTUSER\Control Panel\UnsupportedHardwareNotificationCache' 'SV2' 'REG_DWORD' '0'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassCPUCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassRAMCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassSecureBootCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassStorageCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\LabConfig' 'BypassTPMCheck' 'REG_DWORD' '1'
Set-RegistryValue 'HKLM\zSYSTEM\Setup\MoSetup' 'AllowUpgradesWithUnsupportedTPMOrCPU' 'REG_DWORD' '1'
Write-Output "Optimisations terminées !"

Write-Output "Déchargement du Registre..."
reg unload HKLM\zCOMPONENTS | Out-Null
reg unload HKLM\zDEFAULT | Out-Null
reg unload HKLM\zNTUSER | Out-Null
reg unload HKLM\zSOFTWARE | Out-Null
reg unload HKLM\zSYSTEM | Out-Null

Write-Output "Démontage de l'image de démarrage et enregistrement..."
Dismount-WindowsImage -Path $ScratchDisk\scratchdir -Save
Clear-Host
Write-Output "L'image Tiny11 est maintenant terminée. Début de la création du fichier ISO..."
Write-Output "Copie du fichier d'automatisation pour contourner le compte Microsoft lors de l'OOBE..."
Copy-Item -Path "$PSScriptRoot\autounattend.xml" -Destination "$ScratchDisk\tiny11\autounattend.xml" -Force | Out-Null
Write-Output "Création de l'image ISO..."
$ADKDepTools = "C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\$hostarchitecture\Oscdimg"
$localOSCDIMGPath = "$PSScriptRoot\oscdimg.exe"

if ([System.IO.Directory]::Exists($ADKDepTools)) {
    Write-Output "Utilisation de oscdimg.exe depuis l'ADK Windows installé."
    $OSCDIMG = "$ADKDepTools\oscdimg.exe"
} else {
    Write-Output "Dossier ADK non trouvé. Utilisation de oscdimg.exe local..."
    $url = "https://msdl.microsoft.com/download/symbols/oscdimg.exe/3D44737265000/oscdimg.exe"

    if (-not (Test-Path -Path $localOSCDIMGPath)) {
        Write-Output "Téléchargement de oscdimg.exe..."
        Invoke-WebRequest -Uri $url -OutFile $localOSCDIMGPath

        if (Test-Path $localOSCDIMGPath) {
            Write-Output "oscdimg.exe téléchargé avec succès."
        } else {
            Write-Error "Échec du téléchargement de oscdimg.exe."
            exit 1
        }
    } else {
        Write-Output "oscdimg.exe existe déjà localement."
    }

    $OSCDIMG = $localOSCDIMGPath
}

& "$OSCDIMG" '-m' '-o' '-u2' '-udfver102' "-bootdata:2#p0,e,b$ScratchDisk\tiny11\boot\etfsboot.com#pEF,e,b$ScratchDisk\tiny11\efi\microsoft\boot\efisys.bin" "$ScratchDisk\tiny11" "$PSScriptRoot\tiny11.iso"

# Option de création de clé USB bootable
Write-Host ""
Write-Host "Création de l'ISO Tiny11 terminée avec succès : $PSScriptRoot\tiny11.iso" -ForegroundColor Green
$createUsb = Read-Host "Souhaitez-vous créer directement une clé USB d'installation bootable avec cette image ? [o/N]"
if ($createUsb -match '^(o|oui|y|yes)$') {
    if (Test-Path "$PSScriptRoot\scripts\Create-Tiny11BootableUSB.ps1") {
        & "$PSScriptRoot\scripts\Create-Tiny11BootableUSB.ps1" -IsoPath "$PSScriptRoot\tiny11.iso"
    } else {
        Write-Warning "Script de création USB introuvable."
    }
}

# Nettoyage et fin
Write-Output "Nettoyage des fichiers temporaires en cours..."
Remove-Item -Path "$ScratchDisk\tiny11" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$ScratchDisk\scratchdir" -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "Éjection du lecteur ISO..."
Get-Volume -DriveLetter $DriveLetter[0] | Get-DiskImage | Dismount-DiskImage -ErrorAction SilentlyContinue
Write-Output "Lecteur ISO éjecté."
Write-Output "Suppression des fichiers temporaires..."
Remove-Item -Path "$PSScriptRoot\oscdimg.exe" -Force -ErrorAction SilentlyContinue

Write-Output "Opération terminée avec succès."

# Arrêt du journal
Stop-Transcript

exit
