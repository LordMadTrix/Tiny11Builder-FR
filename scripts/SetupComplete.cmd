@echo off
REM =========================================================================
REM Tiny11 Builder - Script post-installation automatique (SetupComplete.cmd)
REM Exécuté automatiquement par Windows lors de la fin de l'installation OOBE
REM =========================================================================

echo [Tiny11] Configuration post-installation en cours... > "%WINDIR%\tiny11_setup.log"

REM 1. Optimisation des services de diagnostic et de télémétrie
sc config DiagTrack start= disabled >> "%WINDIR%\tiny11_setup.log" 2>&1
sc stop DiagTrack >> "%WINDIR%\tiny11_setup.log" 2>&1
sc config dmwappushservice start= disabled >> "%WINDIR%\tiny11_setup.log" 2>&1
sc stop dmwappushservice >> "%WINDIR%\tiny11_setup.log" 2>&1

REM 2. Désactivation des tâches planifiées de télémétrie restantes
schtasks /change /tn "Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser" /disable >> "%WINDIR%\tiny11_setup.log" 2>&1
schtasks /change /tn "Microsoft\Windows\Customer Experience Improvement Program\Consolidator" /disable >> "%WINDIR%\tiny11_setup.log" 2>&1
schtasks /change /tn "Microsoft\Windows\Customer Experience Improvement Program\UsbCeip" /disable >> "%WINDIR%\tiny11_setup.log" 2>&1

REM 3. Nettoyage des caches d'installation temporaires
del /F /Q "%WINDIR%\Panther\*.log" 2>nul
del /F /Q "%WINDIR%\inf\setupapi.offline.log" 2>nul

echo [Tiny11] Post-installation terminée avec succès. >> "%WINDIR%\tiny11_setup.log"
exit 0
