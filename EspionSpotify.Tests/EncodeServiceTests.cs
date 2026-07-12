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
    }
}
