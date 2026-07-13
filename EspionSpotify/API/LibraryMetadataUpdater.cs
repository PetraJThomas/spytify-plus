using System.IO.Abstractions;
using System.Threading.Tasks;
using EspionSpotify.Models;

namespace EspionSpotify.API
{
    public enum MetadataUpdateOutcome
    {
        Updated,    // matched by ISRC and re-tagged (+ cover art)
        NoIsrc,     // file has no ISRC to match on, left untouched
        NoMatch,    // ISRC present but Spotify returned nothing
        Unreadable  // couldn't open the file to read its tags
    }

    /// <summary>
    /// Refreshes a single existing file's tags and cover art directly from Spotify + iTunes, keyed by
    /// the file's embedded ISRC. No playback, no recorder. Exact match only: a file without an ISRC,
    /// or with no Spotify hit for it, is left untouched, so this can never mistag a file.
    /// </summary>
    public static class LibraryMetadataUpdater
    {
        public static async Task<MetadataUpdateOutcome> UpdateFileFromSpotifyAsync(
            string filePath, ISpotifyAPI api, UserSettings settings)
        {
            var fileSystem = new FileSystem();
            string isrc;
            try
            {
                using (var f = TagLib.File.Create(filePath))
                    isrc = f.Tag.ISRC;
            }
            catch
            {
                return MetadataUpdateOutcome.Unreadable;
            }

            if (string.IsNullOrWhiteSpace(isrc)) return MetadataUpdateOutcome.NoIsrc;

            var track = await api.GetTrackByIsrcAsync(isrc).ConfigureAwait(false);
            if (track == null) return MetadataUpdateOutcome.NoMatch;

            var mapper = new MapperID3(fileSystem, filePath, track, settings);
            await mapper.SaveMediaTags().ConfigureAwait(false);

            if (settings.SaveCoverFile)
            {
                var dir = fileSystem.Path.GetDirectoryName(filePath);
                await EncodeService.SaveCoverFilesToDirAsync(fileSystem, dir, track, overwrite: true)
                    .ConfigureAwait(false);
            }

            return MetadataUpdateOutcome.Updated;
        }
    }
}
