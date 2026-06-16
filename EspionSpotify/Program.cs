using System;
using System.Configuration;
using System.Linq;
using System.Windows.Forms;
using EspionSpotify.Properties;
using ExceptionReporting;

namespace EspionSpotify
{
    // The WinForms entry point (Main) was removed when the UI moved to WPF; the WPF
    // App owns startup now. This retains the shared exception reporter used across the
    // engine (Recorder, EncodeService) and wired up by the WPF App's exception handlers.
    internal static class Program
    {
        // Creates the error message and displays it.
        internal static void ReportException(Exception ex)
        {
            string template = null;
            try
            {
                template = @"# {{App.Title}}

**Version**:     {{App.Version}}  
**Region**:      {{App.Region}}  
{{#if App.User}}
**User**:        {{App.User}}  
{{/if}}    
**Date**: {{Error.Date}}  
**Time**: {{Error.Time}}  
{{#if Error.Explanation}}
**User Explanation**: {{Error.Explanation}}  
{{/if}}

**Error Message**: {{Error.Message}}
 
## Stack Traces
```shell
{{Error.FullStackTrace}} 
```

## Logs
```console
{{Logs}}
```

## Settings
{{Settings}}
 
## Assembly References
{{#App.AssemblyRefs}}
 - {{Name}}, Version={{Version}}  
{{/App.AssemblyRefs}}

## System Info  
```console
{{SystemInfo}}
```
".Replace("{{Logs}}", string.Join("\n", Settings.Default.app_console_logs.Split(';')))
                    .Replace("{{Settings}}", GetSettings());
            }
            catch
            {
                // ignored
            }

            var er = new ExceptionReporter
            {
                Config =
                {
                    AppName = Application.ProductName,
                    CompanyName = Application.CompanyName,
                    WebServiceUrl = "https://exception-mailer.herokuapp.com/send",
                    TitleText = "Exception Report",
                    TakeScreenshot = true,
                    SendMethod = ReportSendMethod.WebService,
                    TopMost = true,
                    ShowFlatButtons = true,
                    ShowLessDetailButton = true,
                    ReportCustomTemplate = !string.IsNullOrWhiteSpace(template) ? template : null,
                    ReportTemplateFormat = TemplateFormat.Markdown
                }
            };
            er.Show(ex);
        }

        private static string GetSettings()
        {
            var result = "";
            var settings = Settings.Default.Properties;

            foreach (SettingsProperty setting in settings)
            {
                if (setting.Name == nameof(Settings.Default.app_console_logs)) continue;

                var isSecret = new[]
                {
                    nameof(Settings.Default.app_spotify_api_client_id),
                    nameof(Settings.Default.app_spotify_api_client_secret)
                }.Contains(setting.Name);

                var value = Settings.Default[setting.Name].ToString();
                var secretValue = isSecret && !string.IsNullOrEmpty(value)
                    ? value.Substring(0, Math.Min(value.Length, 4)).PadRight(28, '*')
                    : value;

                result += $"**{setting.Name}**: {secretValue} \n";
            }

            return result;
        }
    }
}