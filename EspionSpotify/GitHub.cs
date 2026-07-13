using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using EspionSpotify.Extensions;
using EspionSpotify.Models.GitHub;
using EspionSpotify.Properties;
using EspionSpotify.Translations;
using Newtonsoft.Json;

namespace EspionSpotify
{
    // Outcome of an update check, so a manual "Check for updates" click can give the user feedback
    // (an auto startup check just ignores this and stays silent unless a newer version is found).
    internal enum UpdateCheckResult
    {
        UpToDate,
        UpdateAvailable,
        Failed
    }

    internal static class GitHub
    {
        // This fork's own repo (releases feed the in-app updater). If the repo moves, change these
        // two lines here AND in EspionSpotify.Updater/Utilities/GitHub.cs.
        private const string REPO_OWNER = "PetraJThomas";
        private const string REPO_NAME = "spytify-plus";

        private const string API_LATEST_RELEASE_URL =
            "https://api.github.com/repos/" + REPO_OWNER + "/" + REPO_NAME + "/releases/latest";

        // FAQ / donate still point to the original author (jwallet) as attribution.
        public const string WEBSITE_FAQ_URL = "https://jwallet.github.io/spy-spotify/faq.html";

        public const string WEBSITE_FAQ_SPOTIFY_API_URL =
            "https://jwallet.github.io/spy-spotify/faq.html#media-tags-not-found";

        public const string WEBSITE_DONATE_URL = "https://jwallet.github.io/spy-spotify/donate.html";

        // Check GitHub for a newer release. When a newer one is found, prompt the user (and launch the
        // updater on confirmation). Pass manual: true for a user-initiated "Check for updates" click:
        // it re-prompts even for a version already declined once, and the caller can surface an
        // "up to date" / "check failed" message from the returned result. An automatic startup check
        // (manual: false) stays silent when there is nothing to update.
        public static async Task<UpdateCheckResult> GetVersion(bool manual = false)
        {
            if (!Uri.TryCreate(API_LATEST_RELEASE_URL, UriKind.Absolute, out var uri))
                return UpdateCheckResult.Failed;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = (HttpWebRequest) WebRequest.Create(uri);
            request.Method = WebRequestMethods.Http.Get;
            request.UserAgent = Constants.SPYTIFY;

            var content = new MemoryStream();

            try
            {
                using (var response = (HttpWebResponse) await request.GetResponseAsync())
                {
                    if (response.StatusCode != HttpStatusCode.OK) return UpdateCheckResult.Failed;

                    using (var reader = response.GetResponseStream())
                    {
                        if (reader != null) await reader.CopyToAsync(content);
                    }

                    var body = Encoding.UTF8.GetString(content.ToArray());
                    var release = JsonConvert.DeserializeObject<Release>(body);

                    // No usable stable release to compare against: treat as "nothing newer to install".
                    if (release == null || release.prerelease || release.draft) return UpdateCheckResult.UpToDate;

                    var githubTagVersion = release.tag_name.ToVersion();
                    var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;

                    if (githubTagVersion == null) return UpdateCheckResult.Failed;
                    if (githubTagVersion <= assemblyVersion) return UpdateCheckResult.UpToDate;

                    // Auto checks only nag once per version; a manual check always re-offers.
                    if (!manual && Settings.Default.app_last_version_prompt.ToVersion() == githubTagVersion)
                        return UpdateCheckResult.UpdateAvailable;

                    var form = Spytify.Form;
                    if (form?.Rm == null) return UpdateCheckResult.UpdateAvailable; // no UI registered to prompt the user

                    var dialogTitle = string.Format(form.Rm.GetString(I18NKeys.MsgNewVersionTitle), githubTagVersion);
                    var dialogMessage = form.Rm.GetString(I18NKeys.MsgNewVersionContent);

                    if (!string.IsNullOrEmpty(release.body))
                    {
                        var releaseBodySplit =
                            release.body.Split(new[] {"\r\n", "\r", "\n"}, StringSplitOptions.None);
                        dialogMessage =
                            $"{releaseBodySplit.TakeWhile(x => x.StartsWith("- ")).Take(5).Aggregate((current, next) => $"{current}\n{next}")}\r\n\r\n{dialogMessage}";
                    }

                    if (form.AskUpdate(dialogTitle, dialogMessage)) Update();

                    Settings.Default.app_last_version_prompt = githubTagVersion.ToString();
                    Settings.Default.Save();

                    return UpdateCheckResult.UpdateAvailable;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return UpdateCheckResult.Failed;
            }
        }

        public static void Update()
        {
            Process.Start(new ProcessStartInfo(Application.StartupPath + "/Updater/Updater.exe"));
            Application.Exit();
        }
    }
}