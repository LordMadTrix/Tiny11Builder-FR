namespace Tiny11Builder.App;

internal enum SupportDestination { Sponsors, Kofi, Patreon, PayPal }

internal static class ProjectSupport
{
    public const string Purpose = "Aidez à financer le temps de développement, les tests sur chaque version de Windows et les correctifs pour les problèmes signalés. Toutes les fonctionnalités de l'application restent gratuites.";

    public static Uri Link(SupportDestination destination) => new(destination switch
    {
        SupportDestination.Sponsors => "https://github.com/sponsors/ntdevlabs",
        SupportDestination.Kofi => "https://ko-fi.com/ntdev",
        SupportDestination.Patreon => "https://www.patreon.com/ntdev",
        SupportDestination.PayPal => "https://paypal.me/ntdev2",
        _ => throw new ArgumentOutOfRangeException(nameof(destination))
    });
}
