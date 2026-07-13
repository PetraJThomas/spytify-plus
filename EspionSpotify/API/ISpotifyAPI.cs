using System.Threading.Tasks;
using EspionSpotify.Models;
using SpotifyAPI.Web.Models;

namespace EspionSpotify.API
{
    public interface ISpotifyAPI
    {
        void MapSpotifyTrackToTrack(Track track, FullTrack spotifyTrack);

        void MapSpotifyAlbumToTrack(Track track, FullAlbum spotifyAlbum);

        void MapSpotifyPlaylistToTrack(Track track, FullPlaylist playlist, int? position);

        // Builds a fully-enriched Track from an ISRC (exact library-refresh lookup, no playback).
        // Returns null when the API isn't ready or nothing matches.
        Task<Track> GetTrackByIsrcAsync(string isrc);

        // Exact lookup by Spotify track id (the fallback when isrc: search can't find the track).
        Task<Track> GetTrackByIdAsync(string trackId);
    }
}