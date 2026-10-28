using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Tiny11Builder.Core;

namespace Tiny11Builder.App;

public enum OperationKind { Other, Choose, Check, Inspect, Inventory, Download, Build, Resume, Recover, Elevate, Recipe }

public enum NoticeLevel { Information, Success, Warning, Error }

internal sealed record OperationOutcome(OperationKind Kind, bool Cancelled, string Title, string Message,
    NoticeLevel Level, string? WorkDirectory, string Details, bool RecoveryRequired, string Warning = "")
{
    public bool IsBuildRun => Kind is OperationKind.Build or OperationKind.Resume;

    public string FullMessage => Warning.Length == 0 ? Message : Message + " " + Warning;
}

internal static class OperationOutcomes
{
    public static DownloadServiceException? DownloadServiceProblem(Exception error) => error switch
    {
        DownloadServiceException download => download,
        AggregateException { InnerExceptions.Count: > 0 } aggregate => DownloadServiceProblem(aggregate.InnerExceptions[0]),
        BuildFailureException { InnerException: { } inner } => DownloadServiceProblem(inner),
        _ => null
    };

    public static bool RequiresServicingTools(Exception error) => error switch
    {
        ServicingToolsRequiredException => true,
        AggregateException { InnerExceptions.Count: > 0 } aggregate => RequiresServicingTools(aggregate.InnerExceptions[0]),
        BuildFailureException { InnerException: { } inner } => RequiresServicingTools(inner),
        _ => false
    };

    private const int UserDeclinedElevation = 1223;

    public static OperationOutcome Classify(OperationKind kind, Exception error, bool canResume = false)
    {
        var failure = error as BuildFailureException;
        var cancelled = IsCancellation(kind, error);
        var recovery = failure is not null && NeedsRecovery(failure);
        var (title, message) = cancelled ? Cancelled(kind, canResume) : Failed(kind, ErrorText.Reason(error), canResume);
        if (recovery)
            message += " La copie montée de cette tâche n'a pas pu être nettoyée automatiquement. Utilisez 'Nettoyer l'espace de travail' et sélectionnez le fichier job.json correspondant.";
        return new(kind, cancelled, title, message, cancelled && !recovery ? NoticeLevel.Warning : NoticeLevel.Error,
            failure?.WorkDirectory, error.ToString(), recovery, failure is not null && !recovery ? CleanupProblems(failure) : "");
    }

    private static string CleanupProblems(BuildFailureException failure)
    {
        if (failure.InnerException is not AggregateException { InnerExceptions.Count: > 1 } aggregate) return "";
        var reasons = aggregate.InnerExceptions.Skip(1).Select(ErrorText.Reason).Distinct(StringComparer.Ordinal);
        return ErrorText.Clip("Certaines étapes de nettoyage ont signalé des anomalies : " + string.Join(" ", reasons), 320);
    }

    public static bool IsCancellation(OperationKind kind, Exception error) => error switch
    {
        OperationCanceledException => true,
        BuildFailureException failure => failure.IsCancellation,
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(inner => IsCancellation(kind, inner)),
        Win32Exception { NativeErrorCode: UserDeclinedElevation } => kind == OperationKind.Elevate,
        _ => false
    };

    private static bool NeedsRecovery(BuildFailureException failure) => failure.RecoveryRequired;

    private static (string Title, string Message) Cancelled(OperationKind kind, bool canResume) => kind switch
    {
        OperationKind.Build => ("Création annulée.", "Votre image source n'a pas été modifiée. Les fichiers de travail et journaux sont conservés." +
            (canResume ? " La progression enregistrée peut être reprise." : "")),
        OperationKind.Resume => ("Reprise annulée.", "La progression enregistrée et les journaux sont conservés." + (canResume ? " Vous pouvez reprendre à nouveau." : "")),
        OperationKind.Inspect => ("Analyse annulée.", "L'image n'a pas été modifiée."),
        OperationKind.Inventory => ("Liste des composants annulée.", "L'image n'a pas été modifiée."),
        OperationKind.Download => ("Téléchargement annulé.", "Les fichiers partiels sont conservés pour continuer ultérieurement. Aucun support source n'a été altéré."),
        OperationKind.Recover => ("Récupération annulée.", "Relancez 'Nettoyer l'espace de travail' pour terminer le nettoyage."),
        OperationKind.Elevate => ("Redémarrage annulé.", "L'application continue de s'exécuter sans privilèges d'administrateur."),
        _ => ("Opération annulée.", "Aucune modification n'a été effectuée.")
    };

    private static (string Title, string Message) Failed(OperationKind kind, string reason, bool canResume) => kind switch
    {
        OperationKind.Build => ("Échec de la création.", reason + (canResume ? " La progression enregistrée peut être reprise." : "")),
        OperationKind.Resume => ("Échec de la reprise.", reason + (canResume ? " La progression est conservée pour réessayer." : "")),
        OperationKind.Inspect => ("Impossible d'analyser l'image.", reason),
        OperationKind.Inventory => ("Impossible de charger la liste des composants.", reason),
        OperationKind.Download => ("Le téléchargement n'a pas abouti.", reason),
        OperationKind.Recover => ("La récupération n'a pas abouti.", reason),
        OperationKind.Elevate => ("L'application n'a pas pu redémarrer en tant qu'administrateur.", reason),
        _ => ("L'opération n'a pas abouti.", reason)
    };
}

internal static class ErrorText
{
    public const int NoticeLimit = 280;
    private static readonly Regex StackFrame = new(@"^at\s+\S+\(.*?\)", RegexOptions.CultureInvariant);
    private static readonly Regex Marker = new(@"^---.*---$", RegexOptions.CultureInvariant);
    private static readonly Regex TypePrefix = new(@"^(?:[A-Za-z_][\w.`+]*\.)?[A-Za-z_]\w*Exception:\s*", RegexOptions.CultureInvariant);
    private static readonly Regex Banner = new(@"\s*Deployment Image Servicing and Management tool\s*", RegexOptions.CultureInvariant);
    private static readonly Regex ToolNoise = new(
        @"^(?:(?:Image )?Version\s*:.*|For more information, refer to the log file\.?|The DISM log file can be found at.*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Reason(Exception error)
    {
        var summary = Summary(Primary(error).Message);
        return summary.Length > 0 ? summary : "Aucun détail supplémentaire n'a été signalé.";
    }

    public static string Summary(string? text, int limit = NoticeLimit)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var parts = new List<string>();
        foreach (var raw in BuildProgressText.ForDisplay(text).Split(['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = raw;
            var inner = line.IndexOf(" ---> ", StringComparison.Ordinal);
            if (inner >= 0) line = line[..inner];
            line = Banner.Replace(TypePrefix.Replace(line, ""), " ").Trim();
            if (line.Length == 0 || line.StartsWith("--->", StringComparison.Ordinal) || StackFrame.IsMatch(line) ||
                Marker.IsMatch(line) || ToolNoise.IsMatch(line)) continue;
            parts.Add(line);
        }
        return Clip(Regex.Replace(string.Join(" ", parts), @"\s+", " ").Trim(), limit);
    }

    public static string Clip(string text, int limit)
    {
        if (text.Length <= limit) return text;
        var cut = text[..(limit - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > limit / 2) cut = cut[..space];
        if (cut.Length > 0 && char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }

    private static Exception Primary(Exception error) => error switch
    {
        BuildFailureException { InnerException: { } inner and not AggregateException } => Primary(inner),
        AggregateException { InnerExceptions.Count: > 0 } aggregate => Primary(aggregate.InnerExceptions[0]),
        _ => error
    };
}

internal sealed class LiveLog(int capacity = 250)
{
    private readonly Queue<string> lines = new();
    public bool Dirty { get; private set; }
    public int Count => lines.Count;

    public void Add(string line)
    {
        lines.Enqueue(line);
        while (lines.Count > capacity) lines.Dequeue();
        Dirty = true;
    }

    public void Clear()
    {
        lines.Clear();
        Dirty = true;
    }

    public bool TryRender(bool visible, bool reading, bool force, out string text)
    {
        text = "";
        if (!Dirty || (!force && (!visible || reading))) return false;
        text = string.Join(Environment.NewLine, lines);
        Dirty = false;
        return true;
    }
}

internal sealed class LatestProgress<T> : IProgress<T> where T : class
{
    private T? latest;

    public void Report(T value) => Interlocked.Exchange(ref latest, value);

    public bool TryTake(out T value)
    {
        var taken = Interlocked.Exchange(ref latest, null);
        value = taken!;
        return taken is not null;
    }
}

internal sealed record ResumeCandidate(string JobFile, string JobDirectory, BuildRecipe? Recipe, string? Problem);

internal static class ResumeSupport
{
    public const string JobFileName = "job.json";

    public const string NoCheckpoint =
        "Cette création n'a aucune progression enregistrée à reprendre. Elle a été démarrée avant la prise en charge de la sauvegarde de progression ou s'est interrompue avant l'enregistrement de l'image. Aucun fichier n'a été altéré.";

    public static ResumeCandidate Inspect(string jobFile, Func<string, bool> hasCheckpoint, string recipeFileName = "recipe.json")
    {
        string file, directory;
        try
        {
            file = Path.GetFullPath(jobFile);
            directory = Path.GetDirectoryName(file) ?? "";
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(jobFile, "", null, "Le chemin du fichier de tâche n'est pas valide.");
        }
        if (!Path.GetFileName(file).Equals(JobFileName, StringComparison.OrdinalIgnoreCase))
            return new(file, directory, null, "Choisissez le fichier job.json à l'intérieur du dossier tiny11-… de la création.");
        if (!hasCheckpoint(directory)) return new(file, directory, null, NoCheckpoint);
        var recipeFile = Path.Combine(directory, recipeFileName);
        if (!File.Exists(recipeFile))
            return new(file, directory, null, "La recette enregistrée de cette tâche est introuvable, impossible de reprendre.");
        try { return new(file, directory, BuildRecipe.Read(recipeFile), null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return new(file, directory, null, "La recette enregistrée de cette tâche n'a pas pu être lue. " + ErrorText.Reason(error));
        }
    }

    public static string Describe(BuildRecipe recipe)
    {
        var mode = CleanupCatalog.ModeForRecipe(recipe) switch
        {
            BuildMode.Tiny11 => "Tiny11", BuildMode.Tiny11Core => "Tiny11 Core",
            _ => recipe.Flavor == BuildFlavor.Core ? "Personnalisé avec suppressions Core" : "Personnalisé"
        };
        var compression = recipe.Compression switch
        {
            Compression.Recovery => "ESD de récupération", Compression.Maximum => "WIM Maximum", _ => "WIM Rapide"
        };
        var text = new StringBuilder();
        text.AppendLine($"Fichier de sortie : {recipe.OutputPath}");
        text.AppendLine($"Mode : {mode} · {recipe.SelectedOptions.Length} groupes de suppression · {recipe.ExpertActions.Length} actions individuelles");
        text.AppendLine($"Compression : {compression}");
        text.AppendLine();
        text.AppendLine("Les choix de suppression de la création initiale restent inchangés et ne peuvent pas être modifiés lors d'une reprise.");
        if (recipe.Flavor == BuildFlavor.Core)
            text.AppendLine("Core : pas de Windows Update, pas de Microsoft Defender, pas d'environnement de récupération. À utiliser exclusivement dans une VM de test.");
        if (SetupLabels.Legacy(recipe) is { } legacy) text.AppendLine(legacy + ".");
        if (recipe.ExpertActions.Length > 0)
            text.AppendLine("Les suppressions individuelles incluent des éléments protégés. L'image pourrait ne pas démarrer.");
        text.AppendLine();
        text.Append("La progression enregistrée est d'abord vérifiée. En cas de différence constatée, aucune modification n'est effectuée.");
        return text.ToString();
    }
}

internal static class SetupLabels
{
    public static string? LegacyReason(BuildRecipe recipe)
    {
        if (!SetupInstaller.RequiresLegacy(recipe)) return null;
        var core = recipe.Flavor == BuildFlavor.Core;
        var payload = recipe.ExpertActions.Any(a => a.Kind == ImageItemKind.ComponentPayload && a.Mode == ExpertActionMode.Remove);
        return core && payload ? "Core et suppressions directes de composants" : core ? "Suppressions Core" :
            payload ? "suppressions directes de composants" : "les suppressions sélectionnées";
    }

    public static string? Legacy(BuildRecipe recipe) =>
        LegacyReason(recipe) is { } reason ? $"Installeur hérité : automatique (requis pour {reason})" : null;
}

internal static class StageSummary
{
    private static bool IsDone(BuildStageChoice stage) => stage.Status is "Terminé" or "Complete" or "Done earlier" or "Déjà terminé";

    public static string Describe(BuildStageBoard board)
    {
        var stages = board.Stages;
        var done = stages.Count(IsDone);
        var failed = stages.Count(stage => stage.Operations.Any(o => o.State == BuildOperationState.Failed));
        var cancelled = stages.Any(stage => stage.Operations.Any(o => o.State == BuildOperationState.Cancelled));
        var text = done == stages.Length ? $"Toutes les {stages.Length} étapes terminées" : $"{done} sur {stages.Length} étapes terminées";
        return failed > 0 ? $"{text} · {failed} échec(s)" : cancelled ? text + " · annulé" : text;
    }
}

public enum RunResult { Succeeded, Cancelled, Failed }

public enum CancelRequest { Requested, AlreadyRequested, Settled }

internal sealed record OperationEnd(OperationKind Kind, RunResult Result, bool RecoveryRequired, string Title, string Message,
    string? WorkDirectory, string Details, string Warning = "")
{
    public bool NeedsAttention => RecoveryRequired || Result == RunResult.Failed;

    public static OperationEnd Succeeded(OperationKind kind) =>
        new(kind, RunResult.Succeeded, false, "Terminé.", "", null, "");

    public static OperationEnd From(OperationOutcome outcome) =>
        new(outcome.Kind, outcome.Cancelled ? RunResult.Cancelled : RunResult.Failed, outcome.RecoveryRequired,
            outcome.Title, outcome.Message, outcome.WorkDirectory, outcome.Details, outcome.Warning);

    public static OperationEnd Unknown(OperationKind kind) =>
        new(kind, RunResult.Failed, false, "L'opération n'a pas abouti.",
            "Elle s'est arrêtée sans fournir de rapport. Consultez les journaux.", null, "");
}

internal sealed class OperationRun
{
    private static long counter;
    private readonly object gate = new();
    private readonly CancellationTokenSource source;
    private readonly TaskCompletionSource settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool requested;
    private bool done;
    private bool promptOpen;
    private OperationEnd? end;
    private string warning = "";

    public OperationRun(OperationKind kind) : this(kind, new CancellationTokenSource()) { }

    internal OperationRun(OperationKind kind, CancellationTokenSource source)
    {
        this.source = source;
        Kind = kind;
        Id = Interlocked.Increment(ref counter);
        Token = source.Token;
    }

    public long Id { get; }
    public OperationKind Kind { get; }
    public CancellationToken Token { get; }

    public Task Settled => settled.Task;

    public bool CancelRequested { get { lock (gate) return requested; } }
    public bool IsSettled { get { lock (gate) return done; } }
    public string Warning { get { lock (gate) return warning; } }

    public OperationEnd? End
    {
        get
        {
            lock (gate)
                return end is null || warning.Length == 0 ? end :
                    end with { Warning = end.Warning.Length == 0 ? warning : end.Warning + " " + warning };
        }
    }

    public CancelRequest RequestCancel()
    {
        lock (gate)
        {
            if (done) return CancelRequest.Settled;
            if (requested) return CancelRequest.AlreadyRequested;
            requested = true;
        }
        try { source.Cancel(); }
        catch (ObjectDisposedException) { return CancelRequest.Settled; }
        catch (AggregateException failure)
        {
            NoteWarning("La demande d'annulation a été envoyée, mais une étape réagissant à l'annulation a échoué : " + ErrorText.Reason(failure));
        }
        return CancelRequest.Requested;
    }

    public void Record(OperationEnd outcome) { lock (gate) end ??= outcome; }

    public void NoteWarning(string text)
    {
        lock (gate)
            if (!warning.Contains(text, StringComparison.Ordinal)) warning = warning.Length == 0 ? text : warning + " " + text;
    }

    public void Settle()
    {
        lock (gate)
        {
            if (done) return;
            done = true;
            end ??= OperationEnd.Unknown(Kind);
        }
        try { source.Dispose(); }
        catch (Exception error) { NoteWarning("Le jeton d'annulation n'a pas pu être libéré : " + ErrorText.Reason(error)); }
        finally { settled.TrySetResult(); }
    }

    internal bool TryOpenPrompt() { lock (gate) { if (promptOpen) return false; promptOpen = true; return true; } }

    internal void ClosePrompt() { lock (gate) promptOpen = false; }
}

internal sealed record CancelCopy(string Noun, string Question, string Explanation, string Confirm, string Keep,
    string Hint, string Stopping, string Waiting, string Finished, bool CleansUp);

internal static class CancelPrompts
{
    public const string ButtonLabel = "Annuler en toute sécurité";

    public static CancelCopy For(OperationKind kind) => kind switch
    {
        OperationKind.Build => new("création", "Annuler cette création ?",
            "La création s'arrête au prochain point sûr. Cela peut prendre quelques minutes, car l'opération d'image en cours doit se terminer proprement d'abord. " +
            "Votre source n'est jamais modifiée. La copie montée est démontée en toute sécurité ; les fichiers de travail, la progression et les journaux sont conservés.",
            "Annuler la création", "Poursuivre la création", "Arrête la création au prochain point sûr. Votre source n'est jamais modifiée.",
            "Annulation de la création en cours", "Attente de la fin de l'opération en cours", "L'ISO et le rapport de création ont été enregistrés.", true),
        OperationKind.Resume => new("reprise", "Annuler cette reprise ?",
            "La reprise s'arrête au prochain point sûr. Cela peut prendre quelques minutes afin de terminer proprement l'opération d'image en cours. " +
            "La copie montée est démontée en toute sécurité ; les fichiers de travail, la progression et les journaux sont conservés afin de pouvoir reprendre ultérieurement.",
            "Annuler la reprise", "Poursuivre la reprise", "Arrête la reprise au prochain point sûr. La progression enregistrée est conservée.",
            "Annulation de la reprise en cours", "Attente de la fin de l'opération en cours", "L'ISO et le rapport ont été enregistrés.", true),
        OperationKind.Download => new("téléchargement", "Annuler ce téléchargement ?",
            "Le téléchargement s'arrête. Le fichier partiel est conservé pour que vous puissiez continuer plus tard. Aucun média source n'est modifié.",
            "Annuler le téléchargement", "Poursuivre le téléchargement", "Arrête le téléchargement. Le fichier partiel est conservé.",
            "Annulation du téléchargement", "Arrêt du téléchargement", "Le téléchargement est terminé.", false),
        OperationKind.Inspect => new("analyse", "Annuler l'analyse de cette image ?",
            "L'analyse s'arrête après l'étape en cours. L'image n'est pas modifiée.",
            "Annuler l'analyse", "Continuer", "Arrête l'analyse de l'image. Rien n'est modifié.",
            "Annulation de l'analyse", "Attente de la fin de l'étape en cours", "L'image a été analysée.", false),
        OperationKind.Inventory => new("liste des composants", "Annuler le listage des composants ?",
            "Le listage s'arrête au prochain point sûr. Le montage en lecture seule de l'image est rejeté et l'image n'est pas modifiée.",
            "Annuler le listage", "Continuer", "Arrête le listage des composants. L'image n'est pas modifiée.",
            "Annulation du listage", "Attente de la fin de l'opération en cours", "La liste des composants a été chargée.", true),
        OperationKind.Recover => new("récupération", "Annuler ce nettoyage d'espace de travail ?",
            "La récupération s'arrête au prochain point sûr. Relancez 'Nettoyer l'espace de travail' à nouveau pour terminer le nettoyage.",
            "Annuler la récupération", "Poursuivre", "Arrête la récupération au prochain point sûr.",
            "Annulation de la récupération", "Attente de la fin de l'opération en cours", "L'espace de travail a été nettoyé.", false),
        OperationKind.Elevate => new("redémarrage", "Annuler le redémarrage ?",
            "L'application continue de s'exécuter sans privilèges d'administrateur.",
            "Annuler le redémarrage", "Continuer", "Arrête le redémarrage de l'application en administrateur.",
            "Annulation du redémarrage", "Attente de la fin de l'étape en cours", "L'application a redémarré.", false),
        OperationKind.Check => new("vérification", "Annuler la vérification ?",
            "La vérification s'arrête. Rien n'a été modifié.",
            "Annuler la vérification", "Continuer", "Arrête la vérification de vos choix. Rien n'est modifié.",
            "Annulation de la vérification", "Attente de la fin de l'étape en cours", "La vérification est terminée.", false),
        _ => new("opération", "Annuler cette opération ?",
            "L'opération s'arrête après l'étape en cours. Rien n'a été modifié.",
            "Annuler", "Continuer", "Arrête l'opération en cours.",
            "Annulation", "Attente de la fin de l'étape en cours", "L'opération est terminée.", false)
    };
}

internal static class CancelCleanup
{
    public const string Stage = "Cancel cleanup";
    public const string MountOperation = "cancel:mount";
    public const string LogsOperation = "cancel:logs";
}

internal enum CancelStage { Confirming, Cancelling, Ended, Closed }
internal enum CancelPhase { Waiting, DiscardingMount, SavingLogs }
internal enum CancelTone { Neutral, Done, Warning, Error }
internal enum CancelLink { None, OpenLogs, CopyDetails }

internal sealed record CancelView(CancelStage Stage, CancelTone Tone, string Title, string Body, string Status, string Detail,
    string Footnote, string Warning, bool ShowProgress, bool Indeterminate, double Percent, string? ConfirmText,
    string? CloseText, bool CanClose, CancelLink Link, string LinkText);

internal sealed class CancellationFlow
{
    private readonly OperationRun run;
    private readonly CancelCopy copy;
    private CancelPhase phase = CancelPhase.Waiting;
    private OperationEnd? end;
    private bool endedBeforeConfirm;
    private string current = "";
    private double? percent;
    private bool percentFinished;
    private string shown = "";

    private CancellationFlow(OperationRun run)
    {
        this.run = run;
        copy = CancelPrompts.For(run.Kind);
        if (run.IsSettled) Conclude(beforeConfirm: true);
    }

    public static CancellationFlow? TryBegin(OperationRun run) => run.TryOpenPrompt() ? new(run) : null;

    public event Action? Changed;

    public CancelStage Stage { get; private set; } = CancelStage.Confirming;

    public OperationRun Run => run;

    public bool CanClose => Stage != CancelStage.Cancelling;

    public bool Confirm()
    {
        if (Stage != CancelStage.Confirming) return false;
        switch (run.RequestCancel())
        {
            case CancelRequest.Settled:
                Conclude(beforeConfirm: true);
                return false;
            case CancelRequest.AlreadyRequested:
                Stage = CancelStage.Cancelling;
                shown = Display();
                Changed?.Invoke();
                return false;
            default:
                Stage = CancelStage.Cancelling;
                shown = Display();
                Changed?.Invoke();
                return true;
        }
    }

    public void Decline()
    {
        if (Stage == CancelStage.Confirming) Close();
    }

    public void Close()
    {
        if (Stage == CancelStage.Closed) return;
        Stage = CancelStage.Closed;
        run.ClosePrompt();
    }

    public void RunSettled()
    {
        if (Stage is CancelStage.Ended or CancelStage.Closed) return;
        Conclude(beforeConfirm: Stage == CancelStage.Confirming);
    }

    public void Observe(BuildProgress progress)
    {
        if (Stage is CancelStage.Ended or CancelStage.Closed) return;
        if (progress.Stage == CancelCleanup.Stage)
        {
            ObserveCleanup(progress);
            return;
        }
        if (progress.OperationId is null || phase != CancelPhase.Waiting) return;
        if (progress.State == BuildOperationState.Running)
        {
            current = progress.Message;
            percent = null;
            percentFinished = false;
        }
        else if (progress.State is null && progress.Percent is { } value && double.IsFinite(value) && !percentFinished)
        {
            if (current.Length == 0) current = progress.Message;
            percent = Math.Clamp(value, 0, 100);
            percentFinished = percent >= 100;
        }
        else return;
        if (Stage == CancelStage.Cancelling) RaiseIfDisplayChanged();
    }

    public CancelView View => Stage switch
    {
        CancelStage.Confirming => Asking(),
        CancelStage.Cancelling => Stopping(),
        _ => end is null ? Asking() : Ended(end)
    };

    private void ObserveCleanup(BuildProgress progress)
    {
        if (Stage != CancelStage.Cancelling || !copy.CleansUp || progress.State != BuildOperationState.Running) return;
        var next = progress.OperationId switch
        {
            CancelCleanup.MountOperation => CancelPhase.DiscardingMount,
            CancelCleanup.LogsOperation => CancelPhase.SavingLogs,
            _ => phase
        };
        if (next <= phase) return;
        phase = next;
        RaiseIfDisplayChanged();
    }

    private void RaiseIfDisplayChanged()
    {
        var now = Display();
        if (now == shown) return;
        shown = now;
        Changed?.Invoke();
    }

    private string Display() => StatusText() + "\n" + DetailText() + "\n" + run.Warning;

    private void Conclude(bool beforeConfirm)
    {
        end = run.End ?? OperationEnd.Unknown(run.Kind);
        endedBeforeConfirm = beforeConfirm;
        Stage = CancelStage.Ended;
        Changed?.Invoke();
    }

    private CancelView Asking() => new(CancelStage.Confirming, CancelTone.Neutral, copy.Question, copy.Explanation, "", "", "", "",
        false, false, 0, copy.Confirm, copy.Keep, true, CancelLink.None, "");

    private CancelView Stopping() => new(CancelStage.Cancelling, CancelTone.Neutral, copy.Stopping, "", StatusText(), DetailText(),
        copy.CleansUp ? "L'outil d'image n'est jamais interrompu brutalement, cela peut prendre quelques minutes. Ne fermez pas l'application." :
            "Ne fermez pas l'application avant la fin de l'opération.",
        run.Warning, true, true, 0, null, null, false, CancelLink.None, "");

    private string StatusText() => phase switch
    {
        CancelPhase.DiscardingMount => "Libération et nettoyage du montage de travail",
        CancelPhase.SavingLogs => "Enregistrement des journaux",
        _ => percentFinished ? "Finalisation de l'opération en cours" : copy.Waiting
    };

    private string DetailText() =>
        phase != CancelPhase.Waiting || current.Length == 0 ? "" :
        percent is { } value && !percentFinished ? $"Opération en cours : {current} · {Math.Floor(value):0}%" : $"Opération en cours : {current}";

    private CancelView Ended(OperationEnd e)
    {
        var tone = e.NeedsAttention ? CancelTone.Error : e.Warning.Length > 0 ? CancelTone.Warning : CancelTone.Done;
        var title = e.Title.TrimEnd('.');
        string heading, body, status;
        if (e.RecoveryRequired)
        {
            heading = title + (e.Result == RunResult.Cancelled ? ", mais une récupération est requise" : ", et une récupération est requise");
            (body, status) = (e.Message, "");
        }
        else if (e.Result == RunResult.Succeeded)
        {
            heading = endedBeforeConfirm ? $"L'opération de {copy.Noun} s'est déjà terminée" : "Terminé avant de pouvoir être annulé";
            body = copy.Finished + (endedBeforeConfirm ? " Elle s'est terminée avant que l'annulation ne soit prise en compte." :
                " La demande d'annulation est arrivée trop tard pour l'interrompre.");
            status = "Terminé.";
        }
        else
        {
            heading = title;
            body = e.Message;
            status = e.Result == RunResult.Cancelled ? copy.CleansUp ? "Nettoyage terminé." : "Arrêté." : "";
        }
        var link = tone != CancelTone.Done && e.WorkDirectory is not null ? CancelLink.OpenLogs :
            tone == CancelTone.Error && e.Details.Length > 0 ? CancelLink.CopyDetails : CancelLink.None;
        var complete = tone != CancelTone.Error;
        return new(CancelStage.Ended, tone, heading, body, complete ? status : "", "",
            tone != CancelTone.Done && e.WorkDirectory is { } directory ? "Dossier du projet et journaux : " + directory : "",
            e.Warning, complete, false, complete ? 100 : 0, null,
            tone == CancelTone.Error ? "Fermer" : "Terminé", true, link,
            link == CancelLink.OpenLogs ? "Ouvrir les journaux" : link == CancelLink.CopyDetails ? "Copier les détails" : "");
    }
}
