using System.Xml.Linq;

namespace Tiny11Builder.Core;

public static class UnattendBuilder
{
    public static string Create(WindowsImage image, BuildRecipe recipe)
    {
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        XNamespace wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";
        XElement Component(string name, params object[] children) => new(ns + "component",
            new XAttribute("name", name), new XAttribute("processorArchitecture", image.Architecture == "x64" ? "amd64" : "arm64"),
            new XAttribute("publicKeyToken", "31bf3856ad364e35"), new XAttribute("language", "neutral"),
            new XAttribute("versionScope", "nonSxS"), children);

        var oobe = new XElement(ns + "OOBE",
            new XElement(ns + "HideOEMRegistrationScreen", true),
            new XElement(ns + "HideWirelessSetupInOOBE", true),
            new XElement(ns + "HideEULAPage", true),
            new XElement(ns + "ProtectYourPC", 3),
            new XElement(ns + "NetworkLocation", "Work"));

        if (recipe.PreferLocalAccount)
        {
            oobe.Add(new XElement(ns + "HideOnlineAccountScreens", true));
            if (!string.IsNullOrWhiteSpace(recipe.UnattendUsername))
            {
                oobe.Add(new XElement(ns + "HideLocalAccountScreen", true));
            }
        }

        var shellComponents = new List<object> { oobe };

        if (!string.IsNullOrWhiteSpace(recipe.UnattendComputerName))
        {
            shellComponents.Add(new XElement(ns + "ComputerName", recipe.UnattendComputerName.Trim()));
        }

        if (recipe.PreferLocalAccount && !string.IsNullOrWhiteSpace(recipe.UnattendUsername))
        {
            var username = recipe.UnattendUsername.Trim();
            var password = recipe.UnattendPassword ?? "";

            var localAccount = new XElement(ns + "LocalAccount",
                new XAttribute(wcm + "action", "add"),
                new XElement(ns + "Description", "Compte Administrateur Local"),
                new XElement(ns + "DisplayName", username),
                new XElement(ns + "Group", "Administrators"),
                new XElement(ns + "Name", username),
                new XElement(ns + "Password",
                    new XElement(ns + "Value", password),
                    new XElement(ns + "PlainText", true)));

            var userAccounts = new XElement(ns + "UserAccounts",
                new XElement(ns + "LocalAccounts", localAccount));

            shellComponents.Add(userAccounts);

            if (recipe.AutoLogon)
            {
                var autoLogon = new XElement(ns + "AutoLogon",
                    new XElement(ns + "Password",
                        new XElement(ns + "Value", password),
                        new XElement(ns + "PlainText", true)),
                    new XElement(ns + "Enabled", true),
                    new XElement(ns + "LogonCount", 1),
                    new XElement(ns + "Username", username));

                shellComponents.Add(autoLogon);
            }
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "unattend", new XAttribute(XNamespace.Xmlns + "wcm", wcm),
                new XElement(ns + "settings", new XAttribute("pass", "windowsPE"),
                    Component("Microsoft-Windows-Setup",
                        new XElement(ns + "ImageInstall", new XElement(ns + "OSImage",
                            new XElement(ns + "Compact", recipe.CompactInstallation),
                            new XElement(ns + "WillShowUI", "OnError"),
                            new XElement(ns + "InstallFrom", new XElement(ns + "MetaData",
                                new XAttribute(wcm + "action", "add"), new XElement(ns + "Key", "/IMAGE/INDEX"),
                                new XElement(ns + "Value", 1))))),
                        new XElement(ns + "DynamicUpdate", new XElement(ns + "Enable", false)))),
                new XElement(ns + "settings", new XAttribute("pass", "oobeSystem"),
                    Component("Microsoft-Windows-Shell-Setup", shellComponents.ToArray())))).ToString();
    }
}
