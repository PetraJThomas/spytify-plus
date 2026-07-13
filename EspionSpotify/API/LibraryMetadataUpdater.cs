using System.IO.Abstractions;
using System.Threading.Tasks;
using EspionSpotify.Models;

namespace EspionSpotify.API
{
    public enum MetadataUpdateOutcome
    {
        Updated,    // matched (ISRC or Spotify id) and re-tagged (+ cover art)
        NoIsrc,     // file has no identifier to match on, left untouched
        NoMatch,    // identifier present but Spotify returned nothing
        Unreadable  // couldn't open the file to read its tags
    }

    /// <summary>
    /// Refreshes a single existing file's tags and cover art directly from Spotify + iTunes, keyed by
    /// the file's embedded identifiers. No playback, no recorder. Exact match only (ISRC, then the
    /// Spotify track id as a fallback), so a file without an identifier, or with no Spotify hit, is
    /// left untouched and can never be mistagged.
    /// </summary>
    public static class LibraryMetadataUpdater
    {
        public static async Task<MetadataUpdateOutcome> UpdateFileFromSpotifyAsync(
            string filePath, ISpotifyAPI api, UserSettings settings)
        {
            var fileSystem = new FileSystem();
            string isrc, spotifyId;
            try
            {
                using (var f = TagLib.File.Create(filePath))
                {
                    isrc = f.Tag.ISRC;
                    spotifyId = ReadSpotifyTrackId(f);
                }
            }
            catch
            {
                return MetadataUpdateOutcome.Unreadable;
            }

            if (string.IsNullOrWhiteSpace(isrc) && string.IsNullOrWhiteSpace(spotifyId))
                return MetadataUpdateOutcome.NoIsrc;

            // ISRC search first; if it misses (Spotify's isrc: index doesn't cover every track, e.g.
            // DIY-distributor releases), fall back to an exact lookup by the embedded Spotify track
            // id, which resolves any track still on Spotify.
            Track track = null;
            if (!string.IsNullOrWhiteSpace(isrc))
                track = await api.GetTrackByIsrcAsync(isrc).ConfigureAwait(false);
            if (track == null && !string.IsNullOrWhiteSpace(spotifyId))
                track = await api.GetTrackByIdAsync(spotifyId).ConfigureAwait(false);

            if (track == null) return MetadataUpdateOutcome.NoMatch;

            var mapper = new MapperID3(fileSystem, filePath, track, settings);
            await mapper.SaveMediaTags(waitForFileRelease: false).ConfigureAwait(false);

            if (settings.SaveCoverFile)
            {
                var dir = fileSystem.Path.GetDirectoryName(filePath);
                await EncodeService.SaveCoverFilesToDirAsync(fileSystem, dir, track, overwrite: true)
                    .ConfigureAwait(false);
            }

            return MetadataUpdateOutcome.Updated;
        }

        // Reads the SPOTIFY_TRACK_ID we embed (Xiph field for FLAC/OPUS, a TXXX frame for MP3/WAV).
        private static string ReadSpotifyTrackId(TagLib.File f)
        {
            if (f.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
            {
                var v = xiph.GetFirstField("SPOTIFY_TRACK_ID");
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            if (f.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
            {
                var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3, "SPOTIFY_TRACK_ID", false);
                var text = frame?.Text;
                if (text != null && text.Length > 0 && !string.IsNullOrWhiteSpace(text[0])) return text[0];
            }
            return null;
        }
    }
}
