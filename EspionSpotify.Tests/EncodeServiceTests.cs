using EspionSpotify.Enums;
using EspionSpotify.Models;
using Xunit;

namespace EspionSpotify.Tests
{
    // Locks the FLAC/OPUS ffmetadata output to the same tag behaviour as the MP3/WAV MapperID3
    // path, so the "extra title as subtitle" and "counter number as track number" toggles apply
    // to every format (see EncodeService.BuildFfmetadataContent).
    public class EncodeServiceTests
    {
        private static Track TrackWithExtended() => new Track
        {
            Artist = "Artist",
            Title = "Song",
            TitleExtended = "feat. Other",
            TitleExtendedSeparatorType = TitleSeparatorType.Parenthesis,
            AlbumPosition = 7
        };

        [Fact]
        public void ExtraTitleDisabled_TitleCarriesExtended_NoSubtitle()
        {
            var content = EncodeService.BuildFfmetadataContent(
                TrackWithExtended(),
                new UserSettings {ExtraTitleToSubtitleEnabled = false});

            Assert.Contains("title=Song (feat. Other)\n", content);
            Assert.DoesNotContain("subtitle=", content);
        }

        [Fact]
        public void ExtraTitleEnabled_SplitsTitleAndSubtitle()
        {
            var content = EncodeService.BuildFfmetadataContent(
                TrackWithExtended(),
                new UserSettings {ExtraTitleToSubtitleEnabled = true});

            Assert.Contains("title=Song\n", content);
            Assert.Contains("subtitle=feat. Other\n", content);
        }

        [Fact]
        public void CounterAsTrackNumber_UsesCounterOverAlbumPosition()
        {
            var content = EncodeService.BuildFfmetadataContent(
                TrackWithExtended(), // AlbumPosition = 7
                new UserSettings {OrderNumberInMediaTagEnabled = true, InternalOrderNumber = 3});

            Assert.Contains("track=3\n", content);
            Assert.DoesNotContain("track=7\n", content);
        }

        [Fact]
        public void NoCounter_UsesAlbumPosition()
        {
            var content = EncodeService.BuildFfmetadataContent(
                TrackWithExtended(), // AlbumPosition = 7
                new UserSettings {OrderNumberInMediaTagEnabled = false});

            Assert.Contains("track=7\n", content);
        }

        [Fact]
        public void ExtendedTags_Enabled_WritesIsrcAndSpotifyIds()
        {
            var track = TrackWithExtended();
            track.Isrc = "USABC1234567";
            track.SpotifyTrackId = "trk";
            track.SpotifyAlbumId = "alb";

            var content = EncodeService.BuildFfmetadataContent(track, new UserSettings {WriteExtendedTags = true});

            Assert.Contains("ISRC=USABC1234567\n", content);
            Assert.Contains("SPOTIFY_TRACK_ID=trk\n", content);
            Assert.Contains("SPOTIFY_ALBUM_ID=alb\n", content);
        }

        [Fact]
        public void ExtendedTags_Disabled_OmitsIsrcAndSpotifyIds()
        {
            var track = TrackWithExtended();
            track.Isrc = "USABC1234567";
            track.SpotifyTrackId = "trk";

            var content = EncodeService.BuildFfmetadataContent(track, new UserSettings {WriteExtendedTags = false});

            Assert.DoesNotContain("ISRC=", content);
            Assert.DoesNotContain("SPOTIFY_TRACK_ID=", content);
        }

        [Fact]
        public void BuildM3uEntry_FormatsExtInf()
        {
            var track = new Track {Artist = "Dua Lipa", Title = "Levitating"};
            var entry = EncodeService.BuildM3uEntry(track, "01 Levitating.flac", 203);
            Assert.Equal("#EXTINF:203,Dua Lipa - Levitating\r\n01 Levitating.flac\r\n", entry);
        }

        [Fact]
        public void BuildM3uEntry_NoArtist_UsesTitleOnly()
        {
            var track = new Track {Title = "Untitled"};
            var entry = EncodeService.BuildM3uEntry(track, "Untitled.flac", 100);
            Assert.Equal("#EXTINF:100,Untitled\r\nUntitled.flac\r\n", entry);
        }

        [Fact]
        public void IsTruncatedCapture_Disabled_NeverTruncated()
        {
            Assert.False(EncodeService.IsTruncatedCapture(false, 10, 240));
        }

        [Fact]
        public void IsTruncatedCapture_UnknownLength_NotTruncated()
        {
            Assert.False(EncodeService.IsTruncatedCapture(true, 10, null));
            Assert.False(EncodeService.IsTruncatedCapture(true, 10, 0));
        }

        [Theory]
        [InlineData(120, 240, true)]  // half of the track -> truncated
        [InlineData(191, 240, true)]  // just under 80%
        [InlineData(192, 240, false)] // exactly 80% -> kept
        [InlineData(235, 240, false)] // near-complete (lost intro/crossfade) -> kept
        [InlineData(260, 240, false)] // longer than expected -> kept
        public void IsTruncatedCapture_ComparesAgainstEightyPercent(int captured, int expected, bool truncated)
        {
            Assert.Equal(truncated, EncodeService.IsTruncatedCapture(true, captured, expected));
        }
    }
}
