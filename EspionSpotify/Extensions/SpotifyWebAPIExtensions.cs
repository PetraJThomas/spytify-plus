using System.Threading.Tasks;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Models;

namespace EspionSpotify.Extensions
{
    public static class SpotifyWebAPIExtensions
    {
        public static async Task<PlaybackContext> GetPlaybackWithoutExceptionAsync(this SpotifyWebAPI api)
        {
            PlaybackContext playback = null;
            try
            {
                playback = await api.GetPlaybackAsync();
            }
            catch
            {
                // ignored
            }

            return playback;
        }

        public static async Task<FullAlbum> GetAlbumWithoutExceptionAsync(this SpotifyWebAPI api, string id)
        {
            FullAlbum album = null;
            try
            {
                album = await api.GetAlbumAsync(id);
            }
            catch
            {
                // ignored
            }

            return album;
        }

        public static async Task<FullPlaylist> GetPlaylistWithoutExceptionAsync(this SpotifyWebAPI api, string id)
        {
            FullPlaylist playlist = null;
            try
            {
                playlist = await api.GetPlaylistAsync(id, "", "");
            }
            catch
            {
                // ignored
            }

            return playlist;
        }

        public static async Task<Paging<PlaylistTrack>> GetPlaylistTracksWithoutExceptionAsync(
            this SpotifyWebAPI api, string id, int offset)
        {
            try
            {
                return await api.GetPlaylistTracksAsync(id, "", 100, offset, "");
            }
            catch
            {
                return null;
            }
        }
    }
}