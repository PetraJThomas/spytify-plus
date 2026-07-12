using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EspionSpotify.Enums;
using EspionSpotify.Extensions;
using EspionSpotify.Models;
using EspionSpotify.Properties;
using EspionSpotify.Spotify;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;

namespace EspionSpotify.API
{
    public sealed class SpotifyAPI : ISpotifyAPI, IExternalAPI, IDisposable
    {
        public const string SPOTIFY_API_DEFAULT_REDIRECT_URL = "http://localhost:4002";
        public const string SPOTIFY_API_DASHBOARD_URL = "https://developer.spotify.com/dashboard";
        private readonly AuthorizationCodeAuth _auth;
        private readonly LastFMAPI _lastFmApi;
        private SpotifyWebAPI _api;
        private AuthorizationCodeAuth _authorizationCodeAuth;
        private bool _connectionDialogOpened;
        private bool _disposed;
        private string _refreshToken;
        private Token _token;

        // Playlist-as-album state: cache fetched playlists and keep a per-playlist running counter
        // (playback order) so a whole playlist records as one cohesive album.
        private readonly Dictionary<string, FullPlaylist> _playlistCache = new Dictionary<string, FullPlaylist>();
        private string _playlistAlbumId;
        private int _playlistAlbumCounter;
        private string _playlistAlbumLastTrackId;

        public SpotifyAPI()
        {
        }

        public SpotifyAPI(string clientId, string secretId, string redirectUrl = SPOTIFY_API_DEFAULT_REDIRECT_URL)
        {
            _lastFmApi = new LastFMAPI();

            if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(secretId))
            {
                _auth = new AuthorizationCodeAuth(clientId, secretId, redirectUrl, redirectUrl,
                    Scope.Streaming | Scope.PlaylistReadCollaborative | Scope.PlaylistReadPrivate |
                    Scope.UserReadCurrentlyPlaying | Scope.UserReadRecentlyPlayed | Scope.UserReadPlaybackState);
                _auth.AuthReceived += AuthOnAuthReceived;
                _auth.Start();
            }
        }


        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        public bool IsAuthenticated => _token != null;

        public ExternalAPIType GetTypeAPI => ExternalAPIType.Spotify;

        public async Task<bool> UpdateTrack(Track track)
        {
            return await UpdateTrack(track, false);
        }

        public async Task Authenticate()
        {
            await GetSpotifyWebAPI();
        }

        public void Reset()
        {
            _connectionDialogOpened = false;
        }

        public void MapSpotifyTrackToTrack(Track track, FullTrack spotifyTrack)
        {
            var performers = GetAlbumArtistFromSimpleArtistList(spotifyTrack.Artists);
            var (titleParts, separatorType) = SpotifyStatus.GetTitleTags(spotifyTrack.Name, 2);

            track.SetArtistFromApi(performers.FirstOrDefault());
            track.SetTitleFromApi(SpotifyStatus.GetTitleTag(titleParts, 1));
            track.SetTitleExtendedFromApi(SpotifyStatus.GetTitleTag(titleParts, 2), separatorType);

            track.AlbumPosition = spotifyTrack.TrackNumber;
            track.Performers = performers;
            track.Disc = spotifyTrack.DiscNumber;
            track.Length = spotifyTrack.DurationMs > 0 ? (int?) (spotifyTrack.DurationMs / 1000) : null;

            track.SpotifyTrackId = spotifyTrack.Id;
            track.SpotifyAlbumId = spotifyTrack.Album?.Id;
            if (spotifyTrack.ExternalIds != null && spotifyTrack.ExternalIds.TryGetValue("isrc", out var isrc))
                track.Isrc = isrc;
        }

        // Overrides the album identity so a playlist records as one compilation album:
        // Album = playlist name, album artist = Various Artists (keeps mixed artists in one folder),
        // cover = playlist cover, position = playback-order counter. Per-track artist is untouched.
        public void MapSpotifyPlaylistToTrack(Track track, FullPlaylist playlist, int? position)
        {
            if (playlist == null) return;

            if (!string.IsNullOrWhiteSpace(playlist.Name)) track.Album = playlist.Name;
            track.AlbumArtists = new[] {Constants.VARIOUS_ARTISTS};
            if (position.HasValue) track.AlbumPosition = position;

            if (playlist.Images != null && playlist.Images.Count > 0)
            {
                var cover = playlist.Images
                    .OrderByDescending(i => i.Width)
                    .Where(i => i.Width <= 640)
                    .Select(i => i.Url)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(cover)) track.AlbumArtUrl = cover;
            }
        }

        public void MapSpotifyAlbumToTrack(Track track, FullAlbum spotifyAlbum)
        {
            track.AlbumArtists = GetAlbumArtistFromSimpleArtistList(spotifyAlbum.Artists);
            track.Album = spotifyAlbum.Name;
            track.Genres = spotifyAlbum.Genres.ToArray();

            if (DateTime.TryParse(spotifyAlbum.ReleaseDate ?? "", out var date)) track.Year = date.Year;

            if (spotifyAlbum.Images?.Count > 0)
            {
                // Spotify offers 640 / 300 / 64 px; take the largest (typically 640) instead of capping at 300.
                track.AlbumArtUrl = spotifyAlbum.Images
                    .OrderByDescending(i => i.Width)
                    .Where(i => i.Width <= 640)
                    .Select(i => i.Url)
                    .FirstOrDefault();
            }
        }

        [Obsolete("It triggers too many web requests, ~ 60k per day")]
        public async Task<(string, bool)> GetCurrentPlayback()
        {
            var playing = false;
            string title = null;

            await GetSpotifyWebAPI();

            if (_api != null)
            {
                var playback = await _api.GetPlaybackWithoutExceptionAsync();
                if (playback != null && !playback.HasError())
                {
                    playing = playback.IsPlaying;

                    if (playing)
                        switch (playback.CurrentlyPlayingType)
                        {
                            case TrackType.Ad:
                                title = Constants.ADVERTISEMENT;
                                break;
                            case TrackType.Track when playback.Item != null:
                                title = string.Join(" - ", playback.Item.Artists.Select(x => x.Name).First(),
                                    playback.Item.Name);
                                break;
                        }
                }
            }

            return (title, playing);
        }

        #region Spotify Track updater

        private async Task<bool> UpdateTrack(Track track, bool retryDone = false)
        {
            await GetSpotifyWebAPI();

            if (_api == null) return false;

            await Task.Delay(100);

            var playback = await _api.GetPlaybackWithoutExceptionAsync();
            var hasNoPlayback = playback == null || playback.Item == null;

            if (!retryDone && hasNoPlayback)
            {
                await Task.Delay(1000);
                var res = await UpdateTrack(track, true);
                if (track.MetaDataUpdated != true)
                {
                    // open spotify authentication page if user is disconnected
                    // user might be connected with a different account that the one that granted rights
                    OpenAuthenticationDialog(true);           
                }
                return res;
            }

            if (hasNoPlayback || playback.HasError())
            {
                _api.Dispose();

                // fallback in case getting the playback did not work
                ExternalAPI.Instance = _lastFmApi;
                Settings.Default.app_selected_external_api_id = (int) ExternalAPIType.LastFM;
                Settings.Default.Save();

                _ = Task.Run(() =>
                {
                    Spytify.Form?.UpdateExternalAPIToggle(ExternalAPIType.LastFM);
                    Spytify.Form?.ShowFailedToUseSpotifyAPIMessage();
                });

                return await _lastFmApi.UpdateTrack(track);
            }

            // prevent changing track metadata with invalid ones (unknown track)
            if (!IsPlaybackTrackDetectedTrack(track, playback.Item))
            {
                if (retryDone) return false;
                
                await Task.Delay(1000);
                return await UpdateTrack(track, retryDone: true);
            }

            MapSpotifyTrackToTrack(track, playback.Item);

            if (playback.Item.Album?.Id == null) return false;

            var album = await _api.GetAlbumWithoutExceptionAsync(playback.Item.Album.Id);

            if (album.HasError()) return false;

            MapSpotifyAlbumToTrack(track, album);

            await TryApplyPlaylistAlbum(track, playback);

            return true;
        }

        // When "record the current playlist as one album" is on and playback comes from a playlist,
        // override the album identity with the playlist's. Cached per playlist; counter runs in
        // playback order and is guarded against UpdateTrack retries by the last-track-id check.
        private async Task TryApplyPlaylistAlbum(Track track, PlaybackContext playback)
        {
            if (!Settings.Default.advanced_playlist_as_album_enabled) return;

            var playlistId = GetPlaylistIdFromContext(playback?.Context);
            if (playlistId == null)
            {
                _playlistAlbumId = null;
                return;
            }

            var playlist = await GetCachedPlaylistAsync(playlistId);
            if (playlist == null) return;

            if (playlistId != _playlistAlbumId)
            {
                _playlistAlbumId = playlistId;
                _playlistAlbumCounter = 0;
                _playlistAlbumLastTrackId = null;
            }

            var trackId = playback?.Item?.Id;
            if (trackId != _playlistAlbumLastTrackId)
            {
                _playlistAlbumCounter++;
                _playlistAlbumLastTrackId = trackId;
            }

            MapSpotifyPlaylistToTrack(track, playlist, _playlistAlbumCounter);
        }

        private async Task<FullPlaylist> GetCachedPlaylistAsync(string playlistId)
        {
            if (_playlistCache.TryGetValue(playlistId, out var cached)) return cached;

            var playlist = await _api.GetPlaylistWithoutExceptionAsync(playlistId);
            if (playlist == null || playlist.HasError()) return null;

            _playlistCache[playlistId] = playlist;
            return playlist;
        }

        public static string GetPlaylistIdFromContext(Context context)
        {
            if (context == null || !string.Equals(context.Type, "playlist", StringComparison.OrdinalIgnoreCase))
                return null;

            var uri = context.Uri;
            if (string.IsNullOrEmpty(uri)) return null;

            // "spotify:playlist:{id}" (and the legacy "spotify:user:x:playlist:{id}")
            const string marker = "playlist:";
            var idx = uri.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var id = uri.Substring(idx + marker.Length);
            var colon = id.IndexOf(':');
            if (colon >= 0) id = id.Substring(0, colon);
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        #endregion Spotify Track updater

        private string[] GetAlbumArtistFromSimpleArtistList(List<SimpleArtist> artists)
        {
            return (artists ?? new List<SimpleArtist>()).Select(a => a.Name).ToArray();
        }

        private bool IsPlaybackTrackDetectedTrack(Track track, FullTrack spotifyTrack)
        {
            var (titleParts, separatorType) = SpotifyStatus.GetTitleTags(spotifyTrack.Name, 2);
            return titleParts.FirstOrDefault() == track.Title;
        }

        private async void AuthOnAuthReceived(object sender, AuthorizationCode payload)
        {
            _authorizationCodeAuth = (AuthorizationCodeAuth) sender;

            _authorizationCodeAuth.Stop();

            try
            {
                _token = await _authorizationCodeAuth.ExchangeCode(payload.Code);
                _refreshToken = _token.RefreshToken;
                _connectionDialogOpened = false;
            }
            catch
            {
                // ignored
            }
        }

        private void OpenAuthenticationDialog(bool refresh = false)
        {
            if (_connectionDialogOpened) return;

            if (refresh)
            {
                _auth.Stop();
                _token = null;
                _auth.Start();
            }

            _auth.ShowDialog = true;
            _auth.OpenBrowser();
            _connectionDialogOpened = true;
        }

        private async Task GetSpotifyWebAPI()
        {
            if (_token == null)
            {
                OpenAuthenticationDialog();
                return;
            }

            if (_token.IsExpired())
                try
                {
                    if (_api != null) _api.Dispose();
                    _api = null;
                    _token = await _authorizationCodeAuth.RefreshToken(_token.RefreshToken ?? _refreshToken);
                }
                catch
                {
                    // ignored
                }

            if (_api == null)
                try
                {
                    _api = new SpotifyWebAPI
                    {
                        AccessToken = _token.AccessToken,
                        TokenType = _token.TokenType
                    };
                }
                catch (Exception)
                {
                    _api = null;
                    _authorizationCodeAuth.Stop();
                }
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
                if (_api != null)
                    _api.Dispose();

            _disposed = true;
        }
    }
}