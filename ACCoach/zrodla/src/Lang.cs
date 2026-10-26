using System;
using System.Globalization;
using System.IO;

namespace ACCoach
{
    // Language of everything the user sees and hears: English by default, Polish on request.
    // Texts sit in the code as L.T("polski", "english"), so the two versions never drift apart.
    public static class L
    {
        public static bool En = true;

        public static string Code { get { return En ? "en" : "pl"; } }

        // numbers in reports and speech: "0.12" in English, "0,12" in Polish, whatever Windows' region is
        public static CultureInfo Culture { get { return CultureInfo.GetCultureInfo(En ? "en-US" : "pl-PL"); } }

        public static string T(string pl, string en) { return En ? en : pl; }

        // the language chosen in the installer (language.txt next to the exe) when the user has not picked one yet
        public static string InstallerDefault()
        {
            try
            {
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "language.txt");
                if (File.Exists(f)) return File.ReadAllText(f).Trim().ToLowerInvariant() == "pl" ? "pl" : "en";
            }
            catch (Exception) { }
            return "en";
        }
    }
}
