using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EspionSpotify.Models;
using Newtonsoft.Json;

namespace EspionSpotify.API
{
    /// <summary>
    /// Fetches a higher-resolution album cover than Spotify's 640px API cap by looking the album up on
    /// the free iTunes Search API and requesting the artwork at the preferred size (its URLs are
    /// resizable). Best-effort: any failure (no match, network error, odd URL) leaves the existing
    /// Spotify cover in place.
    /// </summary>
    public static class ITunesArtwork
    {
        private const string SEARCH_URL = "https://itunes.apple.com/search?entity=album&limit=5&term=";
        private static readonly Regex ArtworkSizePattern =
            new Regex(@"/\d+x\d+bb\.(jpg|png)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task ApplyHighResCoverAsync(Track track, int size)
        {
            // Playlist-as-album keeps the playlist's own cover; iTunes only has album art, and only
            // sizes above Spotify's 640 cap are worth an extra lookup.
            if (track == null || size <= 640 || track.IsPlaylistAlbum) return;
            if (string.IsNullOrWhiteSpace(track.Album) || string.IsNullOrWhiteSpace(track.Artist)) return;

            var url = await GetHighResCoverUrlAsync(track.Artist, track.Album, size).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(url)) track.AlbumArtUrl = url;
        }

        public static async Task<string> GetHighResCoverUrlAsync(string artist, string album, int size)
        {
            try
            {
                var term = Uri.EscapeDataString($"{artist} {album}".Trim());
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var request = (HttpWebRequest) WebRequest.Create(SEARCH_URL + term);
                request.Method = "GET";
                request.UserAgent = Constants.SPYTIFY;

                using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var result = JsonConvert.DeserializeObject<ItunesResponse>(body);
                    var match = PickBestMatch(result?.results, artist, album);
                    return match == null ? null : UpscaleArtworkUrl(match.artworkUrl100, size);
                }
            }
            catch
            {
                return null;
            }
        }

        // Only accept a result whose artist AND album both confidently match the Spotify track, so a
        // fuzzy iTunes search can never key a wrong album's art (a cover, a compilation, a same-name
        // release by another artist) into the file. No confident match => null, and the caller keeps
        // Spotify's own correct 640px cover rather than risking a mismatch.
        private static ItunesAlbum PickBestMatch(ItunesAlbum[] results, string artist, string album) =>
            results?.FirstOrDefault(r => IsConfidentMatch(artist, album, r.artistName, r.collectionName));

        /// <summary>
        /// True only when the iTunes result's artist and album both match the Spotify track's, after
        /// normalising case/punctuation/edition suffixes. Requires BOTH to match: matching just one
        /// (same artist, different album, or same title by another artist) is not enough.
        /// </summary>
        public static bool IsConfidentMatch(string spotifyArtist, string spotifyAlbum,
            string itunesArtist, string itunesAlbum) =>
            IsFieldMatch(spotifyArtist, itunesArtist) && IsFieldMatch(spotifyAlbum, itunesAlbum);

        // Normalised equality, or containment either way for edition suffixes ("Album" vs
        // "Album (Deluxe)" / "Album - EP"). Containment needs >=3 shared chars so short titles
        // don't collide ("1" in "10"); short strings must match exactly.
        private static bool IsFieldMatch(string spotify, string itunes)
        {
            var s = Norm(spotify);
            var i = Norm(itunes);
            if (s.Length == 0 || i.Length == 0) return false;
            if (s == i) return true;
            var shorter = s.Length <= i.Length ? s : i;
            return shorter.Length >= 3 && (s.Contains(i) || i.Contains(s));
        }

        // "https://.../100x100bb.jpg" -> "https://.../{size}x{size}bb.jpg". Null when the URL isn't the
        // expected resizable form (so the caller keeps the Spotify cover rather than a 100px one).
        public static string UpscaleArtworkUrl(string artworkUrl100, int size)
        {
            if (string.IsNullOrEmpty(artworkUrl100) || !ArtworkSizePattern.IsMatch(artworkUrl100)) return null;
            return ArtworkSizePattern.Replace(artworkUrl100, $"/{size}x{size}bb.jpg");
        }

        // Lowercase, strip accents, drop punctuation (curly vs straight quotes, brackets, hyphens),
        // collapse whitespace. So "AWAKE - EP"/"AWAKE (EP)" and "Beyoncé"/"Beyonce" normalise alike.
        // Comparison-only: never written to a file, so folding accents here is safe.
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (char.IsWhiteSpace(ch)) sb.Append(' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private class ItunesResponse
        {
            public ItunesAlbum[] results { get; set; }
        }

        private class ItunesAlbum
        {
            public string artistName { get; set; }
            public string collectionName { get; set; }
            public string artworkUrl100 { get; set; }
        }
    }
}
