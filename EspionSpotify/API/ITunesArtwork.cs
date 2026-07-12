using System;
using System.IO;
using System.Linq;
using System.Net;
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
                    var match = PickBestMatch(result?.results, artist);
                    return match == null ? null : UpscaleArtworkUrl(match.artworkUrl100, size);
                }
            }
            catch
            {
                return null;
            }
        }

        // Prefer a result whose artist reasonably matches, so we don't grab a wrong album's art.
        private static ItunesAlbum PickBestMatch(ItunesAlbum[] results, string artist)
        {
            if (results == null || results.Length == 0) return null;
            var a = Norm(artist);
            var byArtist = results.FirstOrDefault(r =>
            {
                var n = Norm(r.artistName);
                return n.Length > 0 && (n.Contains(a) || a.Contains(n));
            });
            return byArtist ?? results[0];
        }

        // "https://.../100x100bb.jpg" -> "https://.../{size}x{size}bb.jpg". Null when the URL isn't the
        // expected resizable form (so the caller keeps the Spotify cover rather than a 100px one).
        public static string UpscaleArtworkUrl(string artworkUrl100, int size)
        {
            if (string.IsNullOrEmpty(artworkUrl100) || !ArtworkSizePattern.IsMatch(artworkUrl100)) return null;
            return ArtworkSizePattern.Replace(artworkUrl100, $"/{size}x{size}bb.jpg");
        }

        private static string Norm(string s) => (s ?? "").ToLowerInvariant().Trim();

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
