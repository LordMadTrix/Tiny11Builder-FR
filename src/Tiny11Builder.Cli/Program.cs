using System.Diagnostics;
using Tiny11Builder.Core;

namespace Tiny11Builder.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
  _____ _             __ _ ____        _ _     _             ______ _____  
 |_   _(_)           / // |  _ \      (_) |   | |           |  ____|  __ \ 
   | |  _ _ __  _   /_//| | |_) |_   _ _| | __| | ___ _ __  | |__  | |__) |
   | | | | '_ \| | | |  | |  _ <| | | | | |/ _` |/ _ \ '__| |  __| |  _  / 
   | | | | | | | |_| |  | | |_) | |_| | | | (_| |  __/ |    | |    | | \ \ 
   |_| |_|_| |_|\__, |  |_|____/ \__,_|_|_|\__,_|\___|_|    |_|    |_|  \_\
                 __/ |                                                     
                |___/         Mode CLI / Headless Automation v0.5.0
");
        Console.ResetColor();

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        string? isoPath = GetArg(args, "--iso");
        string? outputPath = GetArg(args, "--out") ?? GetArg(args, "--output");
        string? presetStr = GetArg(args, "--preset")?.ToLowerInvariant() ?? "balanced";
        string? driversPath = GetArg(args, "--drivers");
        bool localAccount = !args.Contains("--no-local-account");
        bool gamerMode = args.Contains("--gamer");
        bool noUpdates = args.Contains("--no-updates");
        bool listImages = args.Contains("--list-images");
        int imageIndex = 1;

        if (int.TryParse(GetArg(args, "--index"), out int parsedIndex))
        {
            imageIndex = parsedIndex;
        }

        if (string.IsNullOrWhiteSpace(isoPath) || !File.Exists(isoPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERREUR] Veuillez spécifier une image ISO valide avec --iso \"chemin\\vers\\windows.iso\"");
            Console.ResetColor();
            return 1;
        }

        var preset = presetStr switch
        {
            "privacy" or "confidentialite" or "prive" => CleanupPreset.Privacy,
            "minimal" or "gamer" => CleanupPreset.Minimal,
            "original" or "stock" => CleanupPreset.Original,
            _ => CleanupPreset.Balanced
        };

        outputPath ??= Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(isoPath)) ?? Environment.CurrentDirectory,
            $"tiny11-{presetStr}.iso");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[+] Image source     : {isoPath}");
        Console.WriteLine($"[+] Image sortie     : {outputPath}");
        Console.WriteLine($"[+] Profil retenu    : {preset} ({presetStr})");
        Console.WriteLine($"[+] Compte local     : {(localAccount ? "Oui (Bypass OOBE)" : "Non")}");
        Console.WriteLine($"[+] Mode Gamer       : {(gamerMode ? "Activé" : "Désactivé")}");
        Console.WriteLine($"[+] Injection pilotes: {(string.IsNullOrEmpty(driversPath) ? "Aucun" : driversPath)}");
        Console.ResetColor();
        Console.WriteLine(new string('-', 70));

        Console.WriteLine("[*] Prêt pour l'automatisation. Tiny11Builder CLI configuré avec succès.");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"Utilisation : Tiny11Builder.Cli [options]

Options :
  --iso <chemin>         Chemin vers l'image ISO source Windows 10/11 (requis)
  --out, --output <path> Chemin de l'ISO générée (défaut : tiny11-<preset>.iso)
  --preset <profil>      Profil de nettoyage :
                           - privacy   : Confidentialité extrême (Recall, Copilot, Bing, Télémétrie)
                           - minimal   : Ultra-léger / Gaming (retrait maximal de bloatware)
                           - balanced  : Équilibré bureautique et stabilité (défaut)
                           - original  : Tiny11 standard classique
  --index <n>            Index de l'édition Windows dans le WIM/ESD (défaut : 1)
  --drivers <chemin>     Dossier contenant des pilotes .inf à injecter
  --gamer                Active les optimisations de latence et de jeu
  --no-updates           Empêche les mises à jour Windows Update au 1er boot
  --no-local-account     Ne configure pas le compte local automatique (conserve OOBE)
  -h, --help             Affiche cette aide");
    }

    private static string? GetArg(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
