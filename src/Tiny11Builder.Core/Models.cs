using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tiny11Builder.Core;

public enum Compression { Recovery, Maximum, Fast }
public enum CleanupPreset { Balanced, Minimal, Original, Custom }
public enum BuildFlavor { Regular, Core }
public enum BuildMode { Tiny11, Tiny11Core, Custom }
public enum OptionKind { App, Feature, Capability, Policy, File }
public enum PolicyHive { Software, DefaultUser, System }
public enum ImageItemKind { ProvisionedApp, SystemApp, Feature, Capability, Package, Service, ComponentPayload }
public enum ExpertActionMode { Disable, Remove }
public sealed record ExpertAction(ImageItemKind Kind, string Identity, ExpertActionMode Mode);
public sealed record ImageInventoryItem(ImageItemKind Kind, string Identity, string Detail, bool Protected)
{
    public string Key => $"{Kind}:{Identity}";
}

public sealed record CleanupOption(
    string Id, string Title, string Description, string Category, OptionKind Kind,
    bool Balanced, bool Minimal, string[] Targets);

public sealed record WindowsImage(
    int Index, string Name, string Description, string Architecture,
    Version Version, string Edition, string Installation, long Size)
{
    public string Summary => $"{Name}  |  {Architecture}  |  version {Version.Build}.{Version.Revision}";
    public bool IsSupported => Index > 0 && Size > 0 && Version.Major == 10 && Version.Build >= 22621 &&
        Installation.Equals("Client", StringComparison.OrdinalIgnoreCase) && Architecture is "x64" or "arm64";

    public void Validate()
    {
        if (!IsSupported)
            throw new InvalidDataException(
                "Choisissez une image Windows 11 client, version 22621 (22H2) ou supérieure, au format x64 ou ARM64.");
    }
}

public sealed record ProvisionedApp(string DisplayName, string PackageName);
public enum BuildOperationState { Pending, Running, Completed, Skipped, Failed, Cancelled }
public sealed record BuildProgress(string Stage, string Message, double? Percent = null,
    string? OperationId = null, BuildOperationState? State = null, string? Detail = null);
public sealed record BuildResult(string IsoPath, string Sha256, string ReportPath, string WorkDirectory,
    TimeSpan Duration = default, long RemovedLogicalBytes = 0, long OmittedSetupBytes = 0)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
public sealed record PolicyValue(PolicyHive Hive, string Key, string Name, object Value);

public sealed record BuildRecipe
{
    public int SchemaVersion { get; init; } = 1;
    public string SourcePath { get; init; } = "";
    public int ImageIndex { get; init; } = 1;
    public string WorkspacePath { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public string DismPath { get; init; } = "";
    public string OscdimgPath { get; init; } = "";
    public Compression Compression { get; init; } = Compression.Recovery;
    public BuildFlavor Flavor { get; init; }
    public BuildMode? Mode { get; init; }
    public bool CoreAcknowledged { get; init; }
    public bool ExpertAcknowledged { get; init; }
    public ExpertAction[] ExpertActions { get; init; } = [];
    public string[] SelectedOptions { get; init; } = [];
    public bool EnableNetFx3 { get; init; }
    public bool CompactInstallation { get; init; } = true;
    public bool PreferLocalAccount { get; init; } = true;
    public string UnattendUsername { get; init; } = "";
    public string UnattendPassword { get; init; } = "";
    public string UnattendComputerName { get; init; } = "";
    public bool AutoLogon { get; init; } = true;
    public bool KeepWorkingFiles { get; init; }
    public bool TurboMode { get; init; }
    public bool RemoveRedundantData { get; init; }
    public bool OmitNetFx3SetupPayload { get; init; }
    public string SelectedBrowser { get; init; } = "firefox";
    public bool InstallVlc { get; init; } = true;
    public bool InstallSevenZip { get; init; } = true;
    public bool InstallNotepadPlus { get; init; }
    public bool InstallLibreOffice { get; init; }
    public string DriverPath { get; init; } = "";
    public bool GamerMode { get; init; } = true;
    public bool DisableDefender { get; init; }
    public bool DisableUpdatesAtFirstBoot { get; init; } = true;
    public string DefenderControlPath { get; init; } = "";
    public bool InstallMadTweak { get; init; } = true;
    public string MadTweakPath { get; init; } = @"C:\Users\madtr\Downloads\Devlopement\MadTweak\dist\MadTweak.exe";

    public void ValidateOptions()
    {
        if (SchemaVersion != 1 || !Enum.IsDefined(Compression) || !Enum.IsDefined(Flavor) ||
            (Mode is { } mode && !Enum.IsDefined(mode)))
            throw new InvalidDataException("Cette version de recette ou ce mode de compression n'est pas pris en charge.");
        if (Flavor == BuildFlavor.Core && !CoreAcknowledged)
            throw new InvalidDataException("Tiny11 Core requiert l'acceptation de la perte des mises à jour, de la récupération et de la protection antivirus.");
        if (ExpertActions is null || ExpertActions.Any(action => action is null) ||
            (ExpertActions.Length > 0 && !ExpertAcknowledged))
            throw new InvalidDataException("Les suppressions personnalisées requièrent l'acceptation du risque d'obtenir une image non amorçable.");
        if (ExpertActions.Select(a => (a.Kind, a.Identity)).Distinct().Count() != ExpertActions.Length)
            throw new InvalidDataException("Actions d'image personnalisées en double.");
        foreach (var action in ExpertActions)
            if (!Enum.IsDefined(action.Kind) || !Enum.IsDefined(action.Mode) || string.IsNullOrWhiteSpace(action.Identity) ||
                action.Identity.Contains('\\') || action.Identity.Contains('/') ||
                (action.Mode == ExpertActionMode.Disable && action.Kind is not (ImageItemKind.Feature or ImageItemKind.Service)))
                throw new InvalidDataException("Action d'image personnalisée non valide.");
        if (ImageIndex < 1 || SelectedOptions is null ||
            SelectedOptions.Length != SelectedOptions.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("Index d'image non valide ou options de nettoyage en double.");
        foreach (var id in SelectedOptions)
            if (!CleanupCatalog.Options.Any(o => o.Id == id))
                throw new InvalidDataException($"Option de nettoyage inconnue : {id}");
        if (Mode is BuildMode.Tiny11 or BuildMode.Tiny11Core &&
            (Flavor != (Mode == BuildMode.Tiny11Core ? BuildFlavor.Core : BuildFlavor.Regular) ||
             ExpertActions.Length > 0 ||
             !SelectedOptions.ToHashSet(StringComparer.Ordinal).SetEquals(CleanupCatalog.ForMode(Mode.Value))))
            throw new InvalidDataException("Utilisez le mode Personnalisé pour modifier les suppressions Tiny11 ou Tiny11 Core.");
    }

    public static BuildRecipe Read(string path)
    {
        var recipe = JsonSerializer.Deserialize<BuildRecipe>(File.ReadAllText(path), JsonDefaults.Options)
            ?? throw new InvalidDataException("La recette est vide.");
        recipe.ValidateOptions();
        return recipe;
    }

    public void Write(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonDefaults.Options));
    }
}

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
