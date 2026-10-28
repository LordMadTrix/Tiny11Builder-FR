<#
.SYNOPSIS
    Script pour créer une image ultra-légère Tiny11 Core de Windows 11.

.DESCRIPTION
    Ce script génère une image considérablement allégée de Windows 11 (sans mise à jour possible, sans Defender, etc.).
    Conçu principalement pour des tests rapides ou des environnements de machines virtuelles (VM).

.NOTES
    Auteur: ntdevlabs (Traduction et adaptation en français)
    Version: BETA 09-05-25
#>

if ((Get-ExecutionPolicy) -eq 'Restricted') {
    Write-Host "Votre stratégie d'exécution PowerShell est actuellement définie sur Restricted, ce qui empêche l'exécution de scripts. Voulez-vous la définir sur RemoteSigned ? (oui/non)"
    $response = Read-Host
    if ($response -match '^(oui|o|yes|y)$') {
        Set-ExecutionPolicy RemoteSigned -Scope CurrentUser -Confirm:$false
    } else {
        Write-Host "Le script ne peut pas s'exécuter sans modifier la stratégie d'exécution. Fermeture..."
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
    Write-Host "Redémarrage du créateur Tiny11 Core en tant qu'administrateur dans une nouvelle fenêtre. Vous pouvez fermer celle-ci."
    $newProcess = new-object System.Diagnostics.ProcessStartInfo "PowerShell";
    $newProcess.Arguments = $myInvocation.MyCommand.Definition;
    $newProcess.Verb = "runas";
    [System.Diagnostics.Process]::Start($newProcess);
    exit
}

Start-Transcript -Path "$PSScriptRoot\tiny11core_$(get-date -f yyyyMMdd_HHmms).log"
Write-Host "Bienvenue dans le créateur Tiny11 Core ! BETA 09-05-25"
Write-Host "Ce script génère une image considérablement allégée de Windows 11. Toutefois, elle n'est pas adaptée à un usage régulier en raison de l'impossibilité de la maintenir à jour : vous ne pourrez pas ajouter de langues, de mises à jour ou de fonctionnalités après la création. Tiny11 Core n'est pas un remplacement complet de Windows 11 mais un outil de test ou de développement rapide, particulièrement utile pour les machines virtuelles (VM)."
Write-Host "Voulez-vous continuer ? (o/n)"
$input = Read-Host

if ($input -match '^(oui|o|yes|y)$') {
    Write-Host "C'est parti..."
    Start-Sleep -Seconds 2
    Clear-Host

    $mainOSDrive = $env:SystemDrive
    $hostArchitecture = $Env:PROCESSOR_ARCHITECTURE
    New-Item -ItemType Directory -Force -Path "$mainOSDrive\tiny11\sources" >null
    $DriveLetter = Read-Host "Veuillez entrer la lettre de lecteur de l'image Windows 11 (ex: E)"
    $DriveLetter = $DriveLetter.TrimEnd(':') + ":"

    if ((Test-Path "$DriveLetter\sources\boot.wim") -eq $false -or (Test-Path "$DriveLetter\sources\install.wim") -eq $false) {
        if ((Test-Path "$DriveLetter\sources\install.esd") -eq $true) {
            Write-Host "Fichier install.esd détecté, conversion en install.wim..."
            & 'dism' '/English' "/Get-WimInfo" "/wimfile:$DriveLetter\sources\install.esd"
            $index = Read-Host "Veuillez entrer l'index de l'image"
            Write-Host ' '
            Write-Host "Conversion de install.esd en install.wim. Cela peut prendre un certain temps..."
            & 'DISM' /Export-Image /SourceImageFile:"$DriveLetter\sources\install.esd" /SourceIndex:$index /DestinationImageFile:"$mainOSDrive\tiny11\sources\install.wim" /Compress:max /CheckIntegrity
        } else {
            Write-Host "Impossible de trouver les fichiers d'installation de Windows sur le lecteur spécifié."
            Write-Host "Veuillez entrer la lettre correcte du lecteur DVD ou de l'image ISO montée."
            exit
        }
    }

    Write-Host "Copie de l'image Windows en cours..."
    Copy-Item -Path "$DriveLetter\*" -Destination "$mainOSDrive\tiny11" -Recurse -Force > null
    Set-ItemProperty -Path "$mainOSDrive\tiny11\sources\install.esd" -Name IsReadOnly -Value $false > $null 2>&1
    Remove-Item "$mainOSDrive\tiny11\sources\install.esd" > $null 2>&1
    Write-Host "Copie terminée !"
    Start-Sleep -Seconds 2
    Clear-Host
    Write-Host "Récupération des informations de l'image :"
    & 'dism' '/English' "/Get-WimInfo" "/wimfile:$mainOSDrive\tiny11\sources\install.wim"
    $index = Read-Host "Veuillez entrer l'index de l'image"
    Write-Host "Montage de l'image Windows. Cela peut prendre un certain temps..."
    $wimFilePath = "$mainOSDrive\tiny11\sources\install.wim" 
    & takeown "/F" $wimFilePath 
    & icacls $wimFilePath "/grant" "$($adminGroup.Value):(F)"
    try {
        Set-ItemProperty -Path $wimFilePath -Name IsReadOnly -Value $false -ErrorAction Stop
    } catch {
    }
    New-Item -ItemType Directory -Force -Path "$mainOSDrive\scratchdir" > $null
    & dism /English "/mount-image" "/imagefile:$mainOSDrive\tiny11\sources\install.wim" "/index:$index" "/mountdir:$mainOSDrive\scratchdir"

    $imageIntl = & dism /English /Get-Intl "/Image:$mainOSDrive\scratchdir"
    $languageLine = $imageIntl -split '\n' | Where-Object { $_ -match 'Default system UI language : ([a-zA-Z]{2}-[a-zA-Z]{2})' }

    if ($languageLine) {
        $languageCode = $Matches[1]
        Write-Host "Code de langue par défaut de l'interface : $languageCode"
    } else {
        Write-Host "Code de langue par défaut non trouvé."
    }

    $imageInfo = & 'dism' '/English' '/Get-WimInfo' "/wimFile:$mainOSDrive\tiny11\sources\install.wim" "/index:$index"
    $lines = $imageInfo -split '\r?\n'

    foreach ($line in $lines) {
        if ($line -like '*Architecture : *') {
            $architecture = $line -replace 'Architecture : ',''
            if ($architecture -eq 'x64') {
                $architecture = 'amd64'
            }
            Write-Host "Architecture : $architecture"
            break
        }
    }

    if (-not $architecture) {
        Write-Host "Informations d'architecture non trouvées."
    }

    Write-Host "Montage terminé ! Début de la suppression des applications..."

    $packages = & 'dism' '/English' "/image:$mainOSDrive\scratchdir" '/Get-ProvisionedAppxPackages' |
        ForEach-Object {
            if ($_ -match 'PackageName : (.*)') {
                $matches[1]
            }
        }
    $packagePrefixes = 'Clipchamp.Clipchamp_', 'Microsoft.BingNews_', 'Microsoft.BingWeather_', 'Microsoft.GamingApp_', 'Microsoft.GetHelp_', 'Microsoft.Getstarted_', 'Microsoft.MicrosoftOfficeHub_', 'Microsoft.MicrosoftSolitaireCollection_', 'Microsoft.People_', 'Microsoft.PowerAutomateDesktop_', 'Microsoft.Todos_', 'Microsoft.WindowsAlarms_', 'microsoft.windowscommunicationsapps_', 'Microsoft.WindowsFeedbackHub_', 'Microsoft.WindowsMaps_', 'Microsoft.WindowsSoundRecorder_', 'Microsoft.Xbox.TCUI_', 'Microsoft.XboxGamingOverlay_', 'Microsoft.XboxGameOverlay_', 'Microsoft.XboxSpeechToTextOverlay_', 'Microsoft.YourPhone_', 'Microsoft.ZuneMusic_', 'Microsoft.ZuneVideo_', 'MicrosoftCorporationII.MicrosoftFamily_', 'MicrosoftCorporationII.QuickAssist_', 'MicrosoftTeams_', 'Microsoft.549981C3F5F10_', 'Microsoft.Windows.Copilot', 'MSTeams_', 'Microsoft.OutlookForWindows_', 'Microsoft.Windows.Teams_', 'Microsoft.Copilot_'

    $packagesToRemove = $packages | Where-Object {
        $packageName = $_
        $packagePrefixes -contains ($packagePrefixes | Where-Object { $packageName -like "$_*" })
    }
    foreach ($package in $packagesToRemove) {
        Write-Host "Suppression de $package..."
        & 'dism' '/English' "/image:$mainOSDrive\scratchdir" '/Remove-ProvisionedAppxPackage' "/PackageName:$package"
    }

    Write-Host "Suppression des applications système terminée ! Début de la suppression des packages système..."
    Start-Sleep -Seconds 1
    Clear-Host

    $scratchDir = "$mainOSDrive\scratchdir"
    $packagePatterns = @(
        "Microsoft-Windows-InternetExplorer-Optional-Package~31bf3856ad364e35",
        "Microsoft-Windows-Kernel-LA57-FoD-Package~31bf3856ad364e35~amd64",
        "Microsoft-Windows-LanguageFeatures-Handwriting-$languageCode-Package~31bf3856ad364e35",
        "Microsoft-Windows-LanguageFeatures-OCR-$languageCode-Package~31bf3856ad364e35",
        "Microsoft-Windows-LanguageFeatures-Speech-$languageCode-Package~31bf3856ad364e35",
        "Microsoft-Windows-LanguageFeatures-TextToSpeech-$languageCode-Package~31bf3856ad364e35",
        "Microsoft-Windows-MediaPlayer-Package~31bf3856ad364e35",
        "Microsoft-Windows-Wallpaper-Content-Extended-FoD-Package~31bf3856ad364e35",
        "Windows-Defender-Client-Package~31bf3856ad364e35~",
        "Microsoft-Windows-WordPad-FoD-Package~",
        "Microsoft-Windows-TabletPCMath-Package~",
        "Microsoft-Windows-StepsRecorder-Package~"
    )

    $allPackages = & dism /image:$scratchDir /Get-Packages /Format:Table
    $allPackages = $allPackages -split "`n" | Select-Object -Skip 1

    foreach ($packagePattern in $packagePatterns) {
        $packagesToRemove = $allPackages | Where-Object { $_ -like "$packagePattern*" }

        foreach ($package in $packagesToRemove) {
            $packageIdentity = ($package -split "\s+")[0]
            Write-Host "Suppression de $packageIdentity..."
            & dism /image:$scratchDir /Remove-Package /PackageName:$packageIdentity 
        }
    }

    Write-Host "Voulez-vous activer .NET 3.5 ? Cela ne pourra pas être fait après la création de l'image ! (o/n)"
    $inputNet = Read-Host

    if ($inputNet -match '^(oui|o|yes|y)$') {
        Write-Host "Activation de .NET 3.5..."
        & 'dism' "/image:$scratchDir" '/enable-feature' '/featurename:NetFX3' '/All' "/source:$mainOSDrive\tiny11\sources\sxs" 
        Write-Host ".NET 3.5 a été activé."
    }
    elseif ($inputNet -match '^(non|n|no)$') {
        Write-Host "Vous avez choisi de ne pas activer .NET 3.5. Poursuite..."
    }
    else {
        Write-Host "Entrée non valide. Poursuite sans installer .NET 3.5..."
    }
    Write-Host "Suppression de Microsoft Edge..."
    Remove-Item -Path "$mainOSDrive\scratchdir\Program Files (x86)\Microsoft\Edge" -Recurse -Force >null
    Remove-Item -Path "$mainOSDrive\scratchdir\Program Files (x86)\Microsoft\EdgeUpdate" -Recurse -Force >null
    Remove-Item -Path "$mainOSDrive\scratchdir\Program Files (x86)\Microsoft\EdgeCore" -Recurse -Force >null
    if ($architecture -eq 'amd64') {
        $folderPath = Get-ChildItem -Path "$mainOSDrive\scratchdir\Windows\WinSxS" -Filter "amd64_microsoft-edge-webview_31bf3856ad364e35*" -Directory | Select-Object -ExpandProperty FullName

        if ($folderPath) {
            & 'takeown' '/f' $folderPath '/r' >null
            & icacls $folderPath "/grant" "$($adminGroup.Value):(F)" '/T' '/C' >null
            Remove-Item -Path $folderPath -Recurse -Force >null
        } else {
            Write-Host "Dossier introuvable."
        }
    } elseif ($architecture -eq 'arm64') {
        $folderPath = Get-ChildItem -Path "$mainOSDrive\scratchdir\Windows\WinSxS" -Filter "arm64_microsoft-edge-webview_31bf3856ad364e35*" -Directory | Select-Object -ExpandProperty FullName >null

        if ($folderPath) {
            & 'takeown' '/f' $folderPath '/r'>null
            & icacls $folderPath "/grant" "$($adminGroup.Value):(F)" '/T' '/C' >null
            Remove-Item -Path $folderPath -Recurse -Force >null
        } else {
            Write-Host "Dossier introuvable."
        }
    } else {
        Write-Host "Architecture inconnue : $architecture"
    }
    & 'takeown' '/f' "$mainOSDrive\scratchdir\Windows\System32\Microsoft-Edge-Webview" '/r'
    & 'icacls' "$mainOSDrive\scratchdir\Windows\System32\Microsoft-Edge-Webview" '/grant' "$($adminGroup.Value):(F)" '/T' '/C'
    Remove-Item -Path "$mainOSDrive\scratchdir\Windows\System32\Microsoft-Edge-Webview" -Recurse -Force
    Write-Host "Suppression de l'environnement de récupération (WinRE)..."
    & 'takeown' '/f' "$mainOSDrive\scratchdir\Windows\System32\Recovery" '/r'
    & 'icacls' "$mainOSDrive\scratchdir\Windows\System32\Recovery" '/grant' 'Administrators:F' '/T' '/C'
    Remove-Item -Path "$mainOSDrive\scratchdir\Windows\System32\Recovery\winre.wim" -Recurse -Force
    New-Item -Path "$mainOSDrive\scratchdir\Windows\System32\Recovery\winre.wim" -ItemType File -Force
    Write-Host "Suppression de OneDrive..."
    & 'takeown' '/f' "$mainOSDrive\scratchdir\Windows\System32\OneDriveSetup.exe" >null
    & 'icacls' "$mainOSDrive\scratchdir\Windows\System32\OneDriveSetup.exe" '/grant' "$($adminGroup.Value):(F)" '/T' '/C' >null
    Remove-Item -Path "$mainOSDrive\scratchdir\Windows\System32\OneDriveSetup.exe" -Force >null
    Write-Host "Suppression terminée !"
    Start-Sleep -Seconds 2
    Clear-Host
    Write-Host "Prise de contrôle du dossier WinSxS. Cela peut prendre un certain temps..."
    & 'takeown' '/f' "$mainOSDrive\scratchdir\Windows\WinSxS" '/r'
    & 'icacls' "$mainOSDrive\scratchdir\Windows\WinSxS" '/grant' "$($adminGroup.Value):(F)" '/T' '/C'
    Write-Host "Terminé !"
    Start-Sleep -Seconds 2
    Clear-Host
    Write-Host "Préparation du filtrage WinSxS..."
    $folderPath = Join-Path -Path $mainOSDrive -ChildPath "\scratchdir\Windows\WinSxS_edit"
    $sourceDirectory = "$mainOSDrive\scratchdir\Windows\WinSxS"
    $destinationDirectory = "$mainOSDrive\scratchdir\Windows\WinSxS_edit"
    New-Item -Path $folderPath -ItemType Directory
    if ($architecture -eq "amd64") {
       $dirsToCopy = @(
            "x86_microsoft.windows.common-controls_6595b64144ccf1df_*",
            "x86_microsoft.windows.gdiplus_6595b64144ccf1df_*",    
            "x86_microsoft.windows.i..utomation.proxystub_6595b64144ccf1df_*",
            "x86_microsoft.windows.isolationautomation_6595b64144ccf1df_*",
            "x86_microsoft-windows-s..ngstack-onecorebase_31bf3856ad364e35_*",
            "x86_microsoft-windows-s..stack-termsrv-extra_31bf3856ad364e35_*",
            "x86_microsoft-windows-servicingstack_31bf3856ad364e35_*",
            "x86_microsoft-windows-servicingstack-inetsrv_*",
            "x86_microsoft-windows-servicingstack-onecore_*",
            "amd64_microsoft.vc80.crt_1fc8b3b9a1e18e3b_*",
            "amd64_microsoft.vc90.crt_1fc8b3b9a1e18e3b_*",
            "amd64_microsoft.windows.c..-controls.resources_6595b64144ccf1df_*",
            "amd64_microsoft.windows.common-controls_6595b64144ccf1df_*",
            "amd64_microsoft.windows.gdiplus_6595b64144ccf1df_*",
            "amd64_microsoft.windows.i..utomation.proxystub_6595b64144ccf1df_*",
            "amd64_microsoft.windows.isolationautomation_6595b64144ccf1df_*",
            "amd64_microsoft-windows-s..stack-inetsrv-extra_31bf3856ad364e35_*",
            "amd64_microsoft-windows-s..stack-msg.resources_31bf3856ad364e35_*",
            "amd64_microsoft-windows-s..stack-termsrv-extra_31bf3856ad364e35_*",
            "amd64_microsoft-windows-servicingstack_31bf3856ad364e35_*",
            "amd64_microsoft-windows-servicingstack-inetsrv_31bf3856ad364e35_*",
            "amd64_microsoft-windows-servicingstack-msg_31bf3856ad364e35_*",
            "amd64_microsoft-windows-servicingstack-onecore_31bf3856ad364e35_*",
            "Catalogs",
            "FileMaps",
            "Fusion",
            "InstallTemp",
            "Manifests",
            "x86_microsoft.vc80.crt_1fc8b3b9a1e18e3b_*",
            "x86_microsoft.vc90.crt_1fc8b3b9a1e18e3b_*",
            "x86_microsoft.windows.c..-controls.resources_6595b64144ccf1df_*",
            "x86_microsoft.windows.c..-controls.resources_6595b64144ccf1df_*"
        )
       foreach ($dir in $dirsToCopy) {
            $sourceDirs = Get-ChildItem -Path $sourceDirectory -Filter $dir -Directory
            foreach ($sourceDir in $sourceDirs) {
                $destDir = Join-Path -Path $destinationDirectory -ChildPath $sourceDir.Name
                Write-Host "Copie de $sourceDir.FullName vers $destDir"
                Copy-Item -Path $sourceDir.FullName -Destination $destDir -Recurse -Force
            }
        }
    }
    elseif ($architecture -eq "arm64") {
        $dirsToCopy = @(
            "arm64_microsoft-windows-servicingstack-onecore_31bf3856ad364e35_*",
            "Catalogs",
            "FileMaps",
            "Fusion",
            "InstallTemp",
            "Manifests",
            "SettingsManifests",
            "Temp",
            "x86_microsoft.vc80.crt_1fc8b3b9a1e18e3b_*",
            "x86_microsoft.vc90.crt_1fc8b3b9a1e18e3b_*",
            "x86_microsoft.windows.c..-controls.resources_6595b64144ccf1df_*",
            "x86_microsoft.windows.common-controls_6595b64144ccf1df_*",
            "x86_microsoft.windows.gdiplus_6595b64144ccf1df_*",
            "x86_microsoft.windows.i..utomation.proxystub_6595b64144ccf1df_*",
            "x86_microsoft.windows.isolationautomation_6595b64144ccf1df_*",
            "arm_microsoft.windows.c..-controls.resources_6595b64144ccf1df_*",
            "arm_microsoft.windows.common-controls_6595b64144ccf1df_*",
            "arm_microsoft.windows.gdiplus_6595b64144ccf1df_*",
            "arm_microsoft.windows.i..utomation.proxystub_6595b64144ccf1df_*",
            "arm_microsoft.windows.isolationautomation_6595b64144ccf1df_*",
            "arm64_microsoft.vc80.crt_1fc8b3b9a1e18e3b_*",
            "arm64_microsoft.vc90.crt_1fc8b3b9a1e18e3b_*",
            "arm64_microsoft.windows.c..-controls.resources_6595b64144ccf1df_*",
            "arm64_microsoft.windows.common-controls_6595b64144ccf1df_*",
            "arm64_microsoft.windows.gdiplus_6595b64144ccf1df_*",
            "arm64_microsoft.windows.i..utomation.proxystub_6595b64144ccf1df_*",
            "arm64_microsoft.windows.isolationautomation_6595b64144ccf1df_*",
            "arm64_microsoft-windows-servicing-adm_31bf3856ad364e35_*",
            "arm64_microsoft-windows-servicingcommon_31bf3856ad364e35_*",
            "arm64_microsoft-windows-servicing-onecore-uapi_31bf3856ad364e35_*",
            "arm64_microsoft-windows-servicingstack_31bf3856ad364e35_*",
            "arm64_microsoft-windows-servicingstack-inetsrv_31bf3856ad364e35_*",
            "arm64_microsoft-windows-servicingstack-msg_31bf3856ad364e35_*"
        )
        foreach ($dir in $dirsToCopy) {
            $sourceDirs = Get-ChildItem -Path $sourceDirectory -Filter $dir -Directory
            foreach ($sourceDir in $sourceDirs) {
                $destDir = Join-Path -Path $destinationDirectory -ChildPath $sourceDir.Name
                Write-Host "Copie de $sourceDir.FullName vers $destDir"
                Copy-Item -Path $sourceDir.FullName -Destination $destDir -Recurse -Force
            }
        }
    }

    Write-Host "Suppression du dossier WinSxS original. Cela peut prendre un certain temps..."
    Remove-Item -Path $mainOSDrive\scratchdir\Windows\WinSxS -Recurse -Force

    Rename-Item -Path $mainOSDrive\scratchdir\Windows\WinSxS_edit -NewName $mainOSDrive\scratchdir\Windows\WinSxS
    Write-Host "Terminé !"

    Write-Host "Chargement des ruches du Registre..."
    reg load HKLM\zCOMPONENTS $mainOSDrive\scratchdir\Windows\System32\config\COMPONENTS | Out-Null
    reg load HKLM\zDEFAULT $mainOSDrive\scratchdir\Windows\System32\config\default | Out-Null
    reg load HKLM\zNTUSER $mainOSDrive\scratchdir\Users\Default\ntuser.dat | Out-Null
    reg load HKLM\zSOFTWARE $mainOSDrive\scratchdir\Windows\System32\config\SOFTWARE | Out-Null
    reg load HKLM\zSYSTEM $mainOSDrive\scratchdir\Windows\System32\config\SYSTEM | Out-Null
    Write-Host "Contournement des prérequis matériels (sur l'image système) :"
    & 'reg' 'add' 'HKLM\zDEFAULT\Control Panel\UnsupportedHardwareNotificationCache' '/v' 'SV1' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zDEFAULT\Control Panel\UnsupportedHardwareNotificationCache' '/v' 'SV2' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Control Panel\UnsupportedHardwareNotificationCache' '/v' 'SV1' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Control Panel\UnsupportedHardwareNotificationCache' '/v' 'SV2' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassCPUCheck' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassRAMCheck' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassSecureBootCheck' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassStorageCheck' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassTPMCheck' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\MoSetup' '/v' 'AllowUpgradesWithUnsupportedTPMOrCPU' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    Write-Host "Désactivation des applications sponsorisées et suggestions :"
    & 'reg' 'add' 'HKLM\zNTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'OemPreInstalledAppsEnabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'PreInstalledAppsEnabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SilentInstalledAppsEnabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\CloudContent' '/v' 'DisableWindowsConsumerFeatures' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'ContentDeliveryAllowed' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\PolicyManager\current\device\Start' '/v' 'ConfigureStartPins' '/t' 'REG_SZ' '/d' '{"pinnedList": [{}]}' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'FeatureManagementEnabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'PreInstalledAppsEverEnabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SoftLandingEnabled' '/t' 'REG_DWORD' '/d' '0' '/f'| Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SubscribedContentEnabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SubscribedContent-310093Enabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SubscribedContent-338388Enabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SubscribedContent-338389Enabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SubscribedContent-338393Enabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SubscribedContent-353694Enabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SubscribedContent-353696Enabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' '/v' 'SystemPaneSuggestionsEnabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\PushToInstall' '/v' 'DisablePushToInstall' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\MRT' '/v' 'DontOfferThroughWUAU' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'delete' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\Subscriptions' '/f' | Out-Null
    & 'reg' 'delete' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\SuggestedApps' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\CloudContent' '/v' 'DisableConsumerAccountStateContent' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\CloudContent' '/v' 'DisableCloudOptimizedContent' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    Write-Host "Activation des comptes locaux et installation 100% hors-ligne lors de l'OOBE :"
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' '/v' 'BypassNRO' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' '/v' 'DisableOOBEUpdate' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' '/v' 'HideOnlineAccountScreens' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' '/v' 'DisableVoice' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\OOBE' '/v' 'DisablePrivacyExperience' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\OOBE' '/v' 'ProtectYourPC' '/t' 'REG_DWORD' '/d' '3' '/f' | Out-Null
    Copy-Item -Path "$PSScriptRoot\autounattend.xml" -Destination "$mainOSDrive\scratchdir\Windows\System32\Sysprep\autounattend.xml" -Force | Out-Null
    Write-Host "Désactivation du stockage réservé :"
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager' '/v' 'ShippedWithReserves' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    Write-Host "Désactivation du chiffrement automatique de périphérique BitLocker :"
    & 'reg' 'add' 'HKLM\zSYSTEM\ControlSet001\Control\BitLocker' '/v' 'PreventDeviceEncryption' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    Write-Host "Désactivation de l'icône de conversation (Chat) :"
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\Windows Chat' '/v' 'ChatIcon' '/t' 'REG_DWORD' '/d' '3' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' '/v' 'TaskbarMn' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    Write-Host "Suppression des entrées de registre liées à Edge..."
    reg delete "HKEY_LOCAL_MACHINE\zSOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge" /f | Out-Null
    reg delete "HKEY_LOCAL_MACHINE\zSOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge Update" /f | Out-Null
    Write-Host "Désactivation de la sauvegarde des dossiers OneDrive :"
    & 'reg' 'add' "HKLM\zSOFTWARE\Policies\Microsoft\Windows\OneDrive" '/v' 'DisableFileSyncNGSC' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    Write-Host "Désactivation de la télémétrie :"
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' '/v' 'Enabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Windows\CurrentVersion\Privacy' '/v' 'TailoredExperiencesWithDiagnosticDataEnabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy' '/v' 'HasAccepted' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Input\TIPC' '/v' 'Enabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\InputPersonalization' '/v' 'RestrictImplicitInkCollection' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\InputPersonalization' '/v' 'RestrictImplicitTextCollection' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\InputPersonalization\TrainedDataStore' '/v' 'HarvestContacts' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\Personalization\Settings' '/v' 'AcceptedPrivacyPolicy' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\DataCollection' '/v' 'AllowTelemetry' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSYSTEM\ControlSet001\Services\dmwappushservice' '/v' 'Start' '/t' 'REG_DWORD' '/d' '4' '/f' | Out-Null
    Write-Host "Blocage de l'installation de DevHome et Outlook :"
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Orchestrator\UScheduler\OutlookUpdate' '/v' 'workCompleted' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Orchestrator\UScheduler\DevHomeUpdate' '/v' 'workCompleted' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'delete' 'HKLM\zSOFTWARE\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe\OutlookUpdate' '/f' | Out-Null
    & 'reg' 'delete' 'HKLM\zSOFTWARE\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe\DevHomeUpdate' '/f' | Out-Null
    Write-Host "Désactivation de Windows Copilot :"
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsCopilot' '/v' 'TurnOffWindowsCopilot' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Edge' '/v' 'HubsSidebarEnabled' '/t' 'REG_DWORD' '/d' '0' '/f' | Out-Null
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\Explorer' '/v' 'DisableSearchBoxSuggestions' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    Write-Host "Blocage de l'installation de Teams :"
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Teams' '/v' 'DisableInstallation' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    Write-Host "Blocage de l'installation du nouvel Outlook :"
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\Windows Mail' '/v' 'PreventRun' '/t' 'REG_DWORD' '/d' '1' '/f' | Out-Null
    $tasksPath = "$mainOSDrive\scratchdir\Windows\System32\Tasks"

    Write-Host "Suppression des fichiers de tâches planifiées inutiles..."

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
    Write-Host "Désactivation définitive de Windows Update..."
    & 'reg' 'add' "HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" '/v' 'StopWUPostOOBE1' '/t' 'REG_SZ' '/d' 'net stop wuauserv' '/f'
    & 'reg' 'add' "HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" '/v' 'StopWUPostOOBE2' '/t' 'REG_SZ' '/d' 'sc stop wuauserv' '/f'
    & 'reg' 'add' "HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" '/v' 'StopWUPostOOBE3' '/t' 'REG_SZ' '/d' 'sc config wuauserv start= disabled' '/f'
    & 'reg' 'add' "HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" '/v' 'DisbaleWUPostOOBE1' '/t' 'REG_SZ' '/d' 'reg add HKLM\SYSTEM\CurrentControlSet\Services\wuauserv /v Start /t REG_DWORD /d 4 /f' '/f'
    & 'reg' 'add' "HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" '/v' 'DisbaleWUPostOOBE2' '/t' 'REG_SZ' '/d' 'reg add HKLM\SYSTEM\ControlSet001\Services\wuauserv /v Start /t REG_DWORD /d 4 /f' '/f'
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' '/v' 'DoNotConnectToWindowsUpdateInternetLocations' '/t' 'REG_DWORD' '/d' '1' '/f'
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' '/v' 'DisableWindowsUpdateAccess' '/t' 'REG_DWORD' '/d' '1' '/f' 
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' '/v' 'WUServer' '/t' 'REG_SZ' '/d' 'localhost' '/f' 
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' '/v' 'WUStatusServer' '/t' 'REG_SZ' '/d' 'localhost' '/f' 
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' '/v' 'UpdateServiceUrlAlternate' '/t' 'REG_SZ' '/d' 'localhost' '/f' 
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' '/v' 'UseWUServer' '/t' 'REG_DWORD' '/d' '1' '/f' 
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' '/v' 'DisableOnline' '/t' 'REG_DWORD' '/d' '1' '/f' 
    & 'reg' 'add' 'HKLM\zSYSTEM\ControlSet001\Services\wuauserv' '/v' 'Start' '/t' 'REG_DWORD' '/d' '4' '/f' 
    & 'reg' 'delete' 'HKLM\zSYSTEM\ControlSet001\Services\WaaSMedicSVC' '/f'
    & 'reg' 'delete' 'HKLM\zSYSTEM\ControlSet001\Services\UsoSvc' '/f'
    & 'reg' 'add' 'HKEY_LOCAL_MACHINE\zSOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' '/v' 'NoAutoUpdate' '/t' 'REG_DWORD' '/d' '1' '/f'
    Write-Host "Désactivation de Windows Defender..."
    $servicePaths = @(
        "WinDefend",
        "WdNisSvc",
        "WdNisDrv",
        "WdFilter",
        "Sense"
    )

    foreach ($path in $servicePaths) {
        Set-ItemProperty -Path "HKLM:\zSYSTEM\ControlSet001\Services\$path" -Name "Start" -Value 4
    }
    & 'reg' 'add' 'HKLM\zSOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' '/v' 'SettingsPageVisibility' '/t' 'REG_SZ' '/d' 'hide:virus;windowsupdate' '/f' 
    
    Write-Host "Application des optimisations de performances et télémétrie..."
    & 'reg' 'add' 'HKLM\zSYSTEM\ControlSet001\Services\DiagTrack' '/v' 'Start' '/t' 'REG_DWORD' '/d' '4' '/f'
    & 'reg' 'add' 'HKLM\zSYSTEM\ControlSet001\Services\dmwappushservice' '/v' 'Start' '/t' 'REG_DWORD' '/d' '4' '/f'
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\DataCollection' '/v' 'AllowTelemetry' '/t' 'REG_DWORD' '/d' '0' '/f'
    & 'reg' 'add' 'HKLM\zNTUSER\Software\Microsoft\GameBar' '/v' 'AllowAutoGameMode' '/t' 'REG_DWORD' '/d' '1' '/f'
    & 'reg' 'add' 'HKLM\zNTUSER\System\GameConfigStore' '/v' 'GameDVR_Enabled' '/t' 'REG_DWORD' '/d' '0' '/f'
    & 'reg' 'add' 'HKLM\zSOFTWARE\Policies\Microsoft\Windows\GameDVR' '/v' 'AllowGameDVR' '/t' 'REG_DWORD' '/d' '0' '/f'
    & 'reg' 'add' 'HKLM\zDEFAULT\Control Panel\Desktop' '/v' 'MenuShowDelay' '/t' 'REG_SZ' '/d' '0' '/f'
    & 'reg' 'add' 'HKLM\zNTUSER\Control Panel\Desktop' '/v' 'MenuShowDelay' '/t' 'REG_SZ' '/d' '0' '/f'

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

    Write-Host "Génération du script post-installation SetupComplete.cmd..."
    $scriptsDir = "$mainOSDrive\scratchdir\Windows\Setup\Scripts"
    if (!(Test-Path $scriptsDir)) { New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null }

    $setupScriptLines = @(
        "@echo off",
        "echo [Tiny11] Configuration post-installation en cours... > `"%WINDIR%\tiny11_setup.log`"",
        "sc config DiagTrack start= disabled >> `"%WINDIR%\tiny11_setup.log`" 2>&1",
        "sc stop DiagTrack >> `"%WINDIR%\tiny11_setup.log`" 2>&1",
        "sc config dmwappushservice start= disabled >> `"%WINDIR%\tiny11_setup.log`" 2>&1",
        "sc stop dmwappushservice >> `"%WINDIR%\tiny11_setup.log`" 2>&1"
    )

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

    Write-Host "Optimisations terminées !"
    Write-Host "Déchargement du Registre..."
    reg unload HKLM\zCOMPONENTS >null
    reg unload HKLM\zDEFAULT >null
    reg unload HKLM\zNTUSER >null
    reg unload HKLM\zSOFTWARE >null
    reg unload HKLM\zSYSTEM >null

    $addDrivers = Read-Host "Souhaitez-vous intégrer un dossier de pilotes personnalisés (.inf) dans l'image ? [o/N]"
    if ($addDrivers -match '^(o|oui|y|yes)$') {
        $driverFolder = Read-Host "Indiquez le chemin d'accès au dossier contenant vos pilotes (.inf)"
        if (Test-Path $driverFolder) {
            Write-Host "Intégration des pilotes depuis $driverFolder..."
            dism.exe /Image:$mainOSDrive\scratchdir /Add-Driver /Driver:$driverFolder /Recurse
        } else {
            Write-Warning "Dossier introuvable : $driverFolder. Étape ignorée."
        }
    }
    Write-Host "Nettoyage des composants de l'image..."
    & 'dism' '/English' "/image:$mainOSDrive\scratchdir" '/Cleanup-Image' '/StartComponentCleanup' '/ResetBase' >null
    Write-Host "Nettoyage terminé."
    Write-Host ' '
    Write-Host "Démontage de l'image et enregistrement des modifications..."
    & 'dism' '/English' '/unmount-image' "/mountdir:$mainOSDrive\scratchdir" '/commit'
    Write-Host "Exportation et compression de l'image..."
    & 'dism' '/English' '/Export-Image' "/SourceImageFile:$mainOSDrive\tiny11\sources\install.wim" "/SourceIndex:$index" "/DestinationImageFile:$mainOSDrive\tiny11\sources\install2.wim" '/compress:max'
    Remove-Item -Path "$mainOSDrive\tiny11\sources\install.wim" -Force >null
    Rename-Item -Path "$mainOSDrive\tiny11\sources\install2.wim" -NewName "install.wim" >null
    Write-Host "Image Windows terminée. Poursuite avec l'image de démarrage (boot.wim)."
    Start-Sleep -Seconds 2
    Clear-Host
    Write-Host "Montage de l'image de démarrage (boot.wim) :"
    $wimFilePath = "$mainOSDrive\tiny11\sources\boot.wim" 
    & takeown "/F" $wimFilePath >null
    & icacls $wimFilePath "/grant" "$($adminGroup.Value):(F)"
    Set-ItemProperty -Path $wimFilePath -Name IsReadOnly -Value $false
    & 'dism' '/English' '/mount-image' "/imagefile:$mainOSDrive\tiny11\sources\boot.wim" '/index:2' "/mountdir:$mainOSDrive\scratchdir"
    Write-Host "Chargement des ruches du Registre..."
    reg load HKLM\zCOMPONENTS $mainOSDrive\scratchdir\Windows\System32\config\COMPONENTS
    reg load HKLM\zDEFAULT $mainOSDrive\scratchdir\Windows\System32\config\default
    reg load HKLM\zNTUSER $mainOSDrive\scratchdir\Users\Default\ntuser.dat
    reg load HKLM\zSOFTWARE $mainOSDrive\scratchdir\Windows\System32\config\SOFTWARE
    reg load HKLM\zSYSTEM $mainOSDrive\scratchdir\Windows\System32\config\SYSTEM
    Write-Host "Contournement des prérequis matériels (sur l'image de configuration) :"
    & 'reg' 'add' 'HKLM\zDEFAULT\Control Panel\UnsupportedHardwareNotificationCache' '/v' 'SV1' '/t' 'REG_DWORD' '/d' '0' '/f' >null
    & 'reg' 'add' 'HKLM\zDEFAULT\Control Panel\UnsupportedHardwareNotificationCache' '/v' 'SV2' '/t' 'REG_DWORD' '/d' '0' '/f' >null
    & 'reg' 'add' 'HKLM\zNTUSER\Control Panel\UnsupportedHardwareNotificationCache' '/v' 'SV1' '/t' 'REG_DWORD' '/d' '0' '/f' >null
    & 'reg' 'add' 'HKLM\zNTUSER\Control Panel\UnsupportedHardwareNotificationCache' '/v' 'SV2' '/t' 'REG_DWORD' '/d' '0' '/f' >null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassCPUCheck' '/t' 'REG_DWORD' '/d' '1' '/f' >null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassRAMCheck' '/t' 'REG_DWORD' '/d' '1' '/f' >null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassSecureBootCheck' '/t' 'REG_DWORD' '/d' '1' '/f' >null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassStorageCheck' '/t' 'REG_DWORD' '/d' '1' '/f' >null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\LabConfig' '/v' 'BypassTPMCheck' '/t' 'REG_DWORD' '/d' '1' '/f' >null
    & 'reg' 'add' 'HKLM\zSYSTEM\Setup\MoSetup' '/v' 'AllowUpgradesWithUnsupportedTPMOrCPU' '/t' 'REG_DWORD' '/d' '1' '/f' >null
    & 'reg' 'add' 'HKEY_LOCAL_MACHINE\zSYSTEM\Setup' '/v' 'CmdLine' '/t' 'REG_SZ' '/d' 'X:\sources\setup.exe' '/f' >null
    Write-Host "Optimisations terminées !"
    Write-Host "Déchargement du Registre..."
    reg unload HKLM\zCOMPONENTS >null
    reg unload HKLM\zDEFAULT >null
    reg unload HKLM\zNTUSER >null
    reg unload HKLM\zSOFTWARE >null
    reg unload HKLM\zSYSTEM >null
    Write-Host "Démontage de l'image de démarrage..."
    & 'dism' '/English' '/unmount-image' "/mountdir:$mainOSDrive\scratchdir" '/commit'
    Clear-Host
    Write-Host "Exportation et compression ESD. Cela peut prendre un certain temps..."
    & dism /Export-Image /SourceImageFile:"$mainOSDrive\tiny11\sources\install.wim" /SourceIndex:1 /DestinationImageFile:"$mainOSDrive\tiny11\sources\install.esd" /Compress:recovery
    Remove-Item "$mainOSDrive\tiny11\sources\install.wim" > $null 2>&1
    Write-Host "L'image Tiny11 est terminée. Début de la création de l'image ISO..."
    Write-Host "Création de l'image ISO..."
    $ADKDepTools = "C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\$hostarchitecture\Oscdimg"
    $localOSCDIMGPath = "$PSScriptRoot\oscdimg.exe"

    if ([System.IO.Directory]::Exists($ADKDepTools)) {
        Write-Host "Utilisation de oscdimg.exe depuis l'ADK Windows installé."
        $OSCDIMG = "$ADKDepTools\oscdimg.exe"
    } else {
        Write-Host "Dossier ADK non trouvé. Utilisation de oscdimg.exe local..."
        $url = "https://msdl.microsoft.com/download/symbols/oscdimg.exe/3D44737265000/oscdimg.exe"

        if (-not (Test-Path -Path $localOSCDIMGPath)) {
            Write-Host "Téléchargement de oscdimg.exe..."
            Invoke-WebRequest -Uri $url -OutFile $localOSCDIMGPath

            if (Test-Path $localOSCDIMGPath) {
                Write-Host "oscdimg.exe téléchargé avec succès."
            } else {
                Write-Error "Échec du téléchargement de oscdimg.exe."
                exit 1
            }
        } else {
            Write-Host "oscdimg.exe existe déjà localement."
        }

        $OSCDIMG = $localOSCDIMGPath
    }

    & "$OSCDIMG" '-m' '-o' '-u2' '-udfver102' "-bootdata:2#p0,e,b$mainOSDrive\tiny11\boot\etfsboot.com#pEF,e,b$mainOSDrive\tiny11\efi\microsoft\boot\efisys.bin" "$mainOSDrive\tiny11" "$PSScriptRoot\tiny11.iso"

    # Option de création de clé USB bootable
    Write-Host ""
    Write-Host "Création de l'ISO Tiny11 Core terminée avec succès : $PSScriptRoot\tiny11.iso" -ForegroundColor Green
    $createUsb = Read-Host "Souhaitez-vous créer directement une clé USB d'installation bootable avec cette image ? [o/N]"
    if ($createUsb -match '^(o|oui|y|yes)$') {
        if (Test-Path "$PSScriptRoot\scripts\Create-Tiny11BootableUSB.ps1") {
            & "$PSScriptRoot\scripts\Create-Tiny11BootableUSB.ps1" -IsoPath "$PSScriptRoot\tiny11.iso"
        } else {
            Write-Warning "Script de création USB introuvable."
        }
    }

    # Nettoyage et fin
    Write-Host "Nettoyage des fichiers temporaires en cours..."
    Remove-Item -Path "$mainOSDrive\tiny11" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "$mainOSDrive\scratchdir" -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "Opération terminée avec succès." -ForegroundColor Green
    Stop-Transcript
    exit
}
elseif ($input -match '^(non|n|no)$') {
    Write-Host "Vous avez choisi de ne pas continuer. Fermeture du script."
    exit
}
else {
    Write-Host "Entrée non valide. Veuillez relancer et entrer 'o' pour continuer ou 'n' pour quitter."
}
