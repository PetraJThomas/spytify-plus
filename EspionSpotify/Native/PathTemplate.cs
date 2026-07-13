using System.Linq;
using System.Text.RegularExpressions;
using EspionSpotify.Models;

namespace EspionSpotify.Native
{
    /// <summary>
    /// Resolves user path templates like "{albumartist}/{album} ({year})" and "{track2} {title}"
    /// into a folder path and a file name. Tokens are case-insensitive; unknown tokens are left
    /// untouched so typos are visible. Values are sanitised (invalid chars stripped, diacritics
    /// removed) to match the classic naming path. The audio itself is never affected.
    /// </summary>
    public static class PathTemplate
    {
        private static readonly Regex TokenPattern = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);
        private static readonly Regex WhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);
        private const int MAX_SEGMENT_LENGTH = 120;

        /// <summary>Available token names, for UI hints.</summary>
        public static readonly string[] Tokens =
        {
            "artist", "artists", "albumartist", "title", "titlefull",
            "album", "year", "track", "track2", "trackpad", "disc", "genre", "counter"
        };

        /// <summary>
        /// Resolves the folder template into a "Artist\Album" style relative path (null when the
        /// template is empty or every segment resolved to nothing).
        /// </summary>
        public static string ResolveFolders(string template, Track track, UserSettings userSettings)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;

            var segments = Resolve(template, track, userSettings)
                .Split('/', '\\')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => FileManager.GetCleanFileFolder(Normalize.RemoveDiacritics(s), MAX_SEGMENT_LENGTH))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            return segments.Length == 0 ? null : string.Join(@"\", segments);
        }

        /// <summary>
        /// Resolves the file-name template (without extension). Path separators in the result are
        /// flattened to spaces so a stray "/" can never create an unexpected folder.
        /// </summary>
        public static string ResolveFileName(string template, Track track, UserSettings userSettings)
        {
            var resolved = Resolve(template, track, userSettings)
                .Replace('/', ' ')
                .Replace('\\', ' ');
            resolved = WhitespacePattern.Replace(resolved, " ").Trim();

            return FileManager.GetCleanFileFolder(Normalize.RemoveDiacritics(resolved), -1);
        }

        private static string Resolve(string template, Track track, UserSettings userSettings)
        {
            return TokenPattern.Replace(template, m =>
            {
                var value = TokenValue(m.Groups[1].Value.ToLowerInvariant(), track, userSettings);
                return value ?? m.Value; // unknown token: leave "{foo}" so the mistake is visible
            });
        }

        private static string TokenValue(string token, Track track, UserSettings userSettings)
        {
            switch (token)
            {
                case "artist": return track.Artist ?? "";
                case "artists": return track.Artists ?? "";
                case "albumartist":
                    return track.AlbumArtists != null && track.AlbumArtists.Length > 0
                        ? string.Join(", ", track.AlbumArtists)
                        : track.Artist ?? "";
                case "title": return track.Title ?? "";
                case "titlefull": return track.ToTitleString();
                case "album": return string.IsNullOrEmpty(track.Album) ? Constants.UNTITLED_ALBUM : track.Album;
                case "year": return track.Year?.ToString() ?? "";
                case "track": return track.AlbumPosition?.ToString() ?? "";
                case "track2": return track.AlbumPosition.HasValue ? track.AlbumPosition.Value.ToString("00") : "";
                case "trackpad":
                {
                    // Zero-pad the position to the width of the album's total track count (min 2), so
                    // a 100-track album numbers 001..100 and sorts correctly everywhere, not just in
                    // natural-sort Explorer. Falls back to 2 digits when the total is unknown.
                    if (!track.AlbumPosition.HasValue) return "";
                    var digits = track.AlbumTotalTracks.HasValue && track.AlbumTotalTracks.Value > 0
                        ? track.AlbumTotalTracks.Value.ToString().Length
                        : 2;
                    if (digits < 2) digits = 2;
                    return track.AlbumPosition.Value.ToString(new string('0', digits));
                }
                case "disc": return track.Disc?.ToString() ?? "";
                case "genre": return track.Genres != null && track.Genres.Length > 0 ? track.Genres[0] : "";
                case "counter": return userSettings.InternalOrderNumber.ToString(userSettings.OrderNumberMask ?? "000");
                default: return null;
            }
        }
    }
}
