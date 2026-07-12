using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Threading.Tasks;
using EspionSpotify.Enums;
using EspionSpotify.Extensions;
using EspionSpotify.Models;
using TagLib;
using File = TagLib.File;
using Tag = TagLib.Tag;

namespace EspionSpotify.API
{
    public class MapperID3
    {
        private readonly bool _extraTitleToSubtitleEnabled;
        private readonly bool _writeExtendedTags;

        private readonly IFileSystem _fileSystem;
        private readonly UserSettings _userSettings;
        
        internal MapperID3(string currentFile, Track track, UserSettings userSettings) :
            this(new FileSystem(), currentFile, track, userSettings)
        {
        }

        public MapperID3(IFileSystem fileSystem, string currentFile, Track track, UserSettings userSettings)
        {
            _userSettings = new UserSettings();
            userSettings.CopyAllTo(_userSettings);
            
            _fileSystem = fileSystem;
            CurrentFile = currentFile;
            Track = track;
            OrderNumberInMediaTagEnabled = _userSettings.OrderNumberInMediaTagEnabled;
            Count = _userSettings.OrderNumberAsTag;
            _extraTitleToSubtitleEnabled = _userSettings.ExtraTitleToSubtitleEnabled;
            _writeExtendedTags = _userSettings.WriteExtendedTags;
        }

        private string CurrentFile { get; }
        private int? Count { get; }
        private bool OrderNumberInMediaTagEnabled { get; }
        public Track Track { get; }


        private bool IsMovingExtraTitleToSubtitle
        {
            get
            {
                var separatorType = Track.TitleExtendedSeparatorType;
                return _extraTitleToSubtitleEnabled && separatorType != TitleSeparatorType.None;
            }
        }

        public async Task MapTags(Tag tags)
        {
            var trackNumber = GetTrackNumber();
            if (trackNumber.HasValue) tags.Track = (uint) trackNumber.Value;

            tags.Title = IsMovingExtraTitleToSubtitle ? Track.Title : Track.ToTitleString();
            tags.Subtitle = IsMovingExtraTitleToSubtitle ? Track.TitleExtended : null;

            tags.AlbumArtists = Track.AlbumArtists ?? new[] {Track.Artist};
            tags.Performers = Track.Performers ?? new[] {Track.Artist};

            tags.Album = Track.Album;
            tags.Genres = Track.Genres;

            tags.Disc = (uint) (Track.Disc ?? 0);
            tags.Year = (uint) (Track.Year ?? 0);

            if (_writeExtendedTags && !string.IsNullOrEmpty(Track.Isrc)) tags.ISRC = Track.Isrc;

            await FetchMediaPicture();
            var albumArtCover = GetAlbumCoverToPicture(Track.AlbumArtImage);
            tags.Pictures = albumArtCover != null ? new IPicture[] { albumArtCover } : null;
        }

        #region MP3 Tags updater

        internal async Task SaveMediaTags()
        {
            await Task.Delay(1000);
            using (var mp3 = File.Create(CurrentFile))
            {
                await MapTags(mp3.Tag);
                if (_writeExtendedTags) WriteCustomIdTags(mp3);

                if (_fileSystem.File.Exists(CurrentFile)) mp3.Save();
            }
        }

        // The Spotify IDs have no standard tag frame, so write them as custom fields: a TXXX frame
        // for ID3 (MP3/WAV) and a Vorbis comment for Xiph (FLAC/OPUS). ISRC is handled in MapTags.
        private void WriteCustomIdTags(File file)
        {
            if (file.GetTag(TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
            {
                SetTxxx(id3, "SPOTIFY_TRACK_ID", Track.SpotifyTrackId);
                SetTxxx(id3, "SPOTIFY_ALBUM_ID", Track.SpotifyAlbumId);
            }

            if (file.GetTag(TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
            {
                SetXiph(xiph, "SPOTIFY_TRACK_ID", Track.SpotifyTrackId);
                SetXiph(xiph, "SPOTIFY_ALBUM_ID", Track.SpotifyAlbumId);
            }
        }

        private static void SetTxxx(TagLib.Id3v2.Tag id3, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            TagLib.Id3v2.UserTextInformationFrame.Get(id3, key, true).Text = new[] {value};
        }

        private static void SetXiph(TagLib.Ogg.XiphComment xiph, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            xiph.SetField(key, value);
        }

        #endregion MP3 Tags updater

        private async Task FetchMediaPicture()
        {
            var image = await GetAlbumCover(Track.AlbumArtUrl);
            Track.AlbumArtImage = image;
        }

        private int? GetTrackNumber()
        {
            if (OrderNumberInMediaTagEnabled && Count.HasValue) return Count.Value;

            return Track.AlbumPosition;
        }

        private static Picture GetAlbumCoverToPicture(byte[] data)
        {
            if (data == null) return null;

            return new Picture
            {
                Type = PictureType.FrontCover,
                MimeType = MediaTypeNames.Image.Jpeg,
                Data = data
            };
        }

        public static async Task<byte[]> GetAlbumCover(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;

            try
            {
                var request = WebRequest.Create(link);
                using (var response = await request.GetResponseAsync())
                {
                    var stream = response.GetResponseStream();
                    if (stream == null) return null;
                    using (var reader = new BinaryReader(stream))
                    {
                        using (var memory = new MemoryStream())
                        {
                            var buffer = reader.ReadBytes(4096);
                            while (buffer.Length > 0)
                            {
                                await memory.WriteAsync(buffer, 0, buffer.Length);
                                buffer = reader.ReadBytes(4096);
                            }

                            return memory.ToArray();
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}