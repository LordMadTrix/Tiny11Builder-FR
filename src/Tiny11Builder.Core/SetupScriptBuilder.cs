using System.Text;

namespace Tiny11Builder.Core;

public static class SetupScriptBuilder
{
    public static void Write(string targetScriptPath, BuildRecipe recipe)
    {
        var directory = Path.GetDirectoryName(targetScriptPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("REM Tiny11 Builder - Script post-installation automatique");
        sb.AppendLine("echo [Tiny11] Configuration post-installation en cours... > \"%WINDIR%\\tiny11_setup.log\"");
        sb.AppendLine("sc config DiagTrack start= disabled >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        sb.AppendLine("sc stop DiagTrack >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        sb.AppendLine("sc config dmwappushservice start= disabled >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        sb.AppendLine("sc stop dmwappushservice >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        sb.AppendLine("schtasks /change /tn \"Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser\" /disable >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        sb.AppendLine("schtasks /change /tn \"Microsoft\\Windows\\Customer Experience Improvement Program\\Consolidator\" /disable >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        sb.AppendLine("schtasks /change /tn \"Microsoft\\Windows\\Customer Experience Improvement Program\\UsbCeip\" /disable >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        if (recipe.DisableDefender || recipe.Flavor == BuildFlavor.Core)
        {
            sb.AppendLine("REM Désactivation de Windows Defender");
            sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"Set-MpPreference -DisableRealtimeMonitoring $true -DisableBehaviorMonitoring $true -DisableIOAVProtection $true\" >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        }
        if (recipe.DisableUpdatesAtFirstBoot || recipe.Flavor == BuildFlavor.Core)
        {
            sb.AppendLine("REM Blocage du service Windows Update au premier démarrage");
            sb.AppendLine("net stop wuauserv >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
            sb.AppendLine("sc config wuauserv start= disabled >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        }
        if (!string.IsNullOrWhiteSpace(recipe.DefenderControlPath))
        {
            sb.AppendLine("REM Raccourci Defender Control sur le Bureau public");
            sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('C:\\Users\\Public\\Desktop\\Defender Control.lnk'); $s.TargetPath = 'C:\\Tools\\Defender Control\\dControl.exe'; $s.WorkingDirectory = 'C:\\Tools\\Defender Control'; $s.Save()\" >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        }
        if (recipe.InstallMadTweak && !string.IsNullOrWhiteSpace(recipe.MadTweakPath))
        {
            sb.AppendLine("REM Raccourci MadTweak sur le Bureau public");
            sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('C:\\Users\\Public\\Desktop\\MadTweak.lnk'); $s.TargetPath = 'C:\\Tools\\MadTweak\\MadTweak.exe'; $s.WorkingDirectory = 'C:\\Tools\\MadTweak'; $s.Description = 'Optimisation et personnalisation Windows par MadTweak'; $s.Save()\" >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
        }

        // Navigateur web
        var browserPackage = (recipe.SelectedBrowser ?? "").ToLowerInvariant() switch
        {
            "firefox" => "Mozilla.Firefox",
            "brave" => "Brave.Brave",
            "chrome" => "Google.Chrome",
            "librewolf" => "LibreWolf.LibreWolf",
            _ => null
        };

        var packages = new List<string>();
        if (!string.IsNullOrEmpty(browserPackage)) packages.Add(browserPackage);
        if (recipe.InstallVlc) packages.Add("VideoLAN.VLC");
        if (recipe.InstallSevenZip) packages.Add("7zip.7zip");
        if (recipe.InstallNotepadPlus) packages.Add("Notepad++.Notepad++");
        if (recipe.InstallLibreOffice) packages.Add("TheDocumentFoundation.LibreOffice");

        if (packages.Count > 0)
        {
            sb.AppendLine("REM Installation des logiciels open-source / gratuits via winget");
            sb.AppendLine("echo [Tiny11] Installation des applications en cours... >> \"%WINDIR%\\tiny11_setup.log\"");
            foreach (var pkg in packages)
            {
                sb.AppendLine($"winget install --id {pkg} -e --silent --accept-source-agreements --accept-package-agreements >> \"%WINDIR%\\tiny11_setup.log\" 2>&1");
            }
        }

        sb.AppendLine("del /F /Q \"%WINDIR%\\Panther\\*.log\" 2>nul");
        sb.AppendLine("echo [Tiny11] Configuration terminee avec succes. >> \"%WINDIR%\\tiny11_setup.log\"");
        sb.AppendLine("exit 0");

        File.WriteAllText(targetScriptPath, sb.ToString(), Encoding.ASCII);
    }
}
