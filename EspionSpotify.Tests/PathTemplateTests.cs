using EspionSpotify.Models;
using EspionSpotify.Native;
using Xunit;

namespace EspionSpotify.Tests
{
    public class PathTemplateTests
    {
        private static Track Sample() => new Track
        {
            Artist = "Radiohead",
            Title = "15 Step",
            Album = "In Rainbows",
            Year = 2007,
            AlbumPosition = 1,
            Disc = 1,
            AlbumArtists = new[] {"Radiohead"},
            Genres = new[] {"Alternative"}
        };

        [Fact]
        public void ResolveFolders_ReplacesTokens()
        {
            var folders = PathTemplate.ResolveFolders("{albumartist}/{album} ({year})", Sample(), new UserSettings());
            Assert.Equal(@"Radiohead\In Rainbows (2007)", folders);
        }

        [Fact]
        public void ResolveFileName_ZeroPadsTrackNumber()
        {
            var file = PathTemplate.ResolveFileName("{track2} {title}", Sample(), new UserSettings());
            Assert.Equal("01 15 Step", file);
        }

        [Theory]
        [InlineData(5, 100, "005 15 Step")]    // 100-track album -> 3 digits
        [InlineData(5, 9, "05 15 Step")]       // small album -> min 2 digits
        [InlineData(5, null, "05 15 Step")]    // unknown total -> 2 digits
        [InlineData(7, 1000, "0007 15 Step")]  // 1000-track album -> 4 digits
        public void ResolveFileName_Trackpad_PadsToTotalWidth(int position, int? total, string expected)
        {
            var track = Sample();
            track.AlbumPosition = position;
            track.AlbumTotalTracks = total;
            Assert.Equal(expected, PathTemplate.ResolveFileName("{trackpad} {title}", track, new UserSettings()));
        }

        [Fact]
        public void ResolveFolders_EmptyTemplate_ReturnsNull()
        {
            Assert.Null(PathTemplate.ResolveFolders("", Sample(), new UserSettings()));
        }

        [Fact]
        public void ResolveFolders_DropsEmptyTokenSegments()
        {
            var track = Sample();
            track.Year = null;
            var folders = PathTemplate.ResolveFolders("{albumartist}/{year}/{album}", track, new UserSettings());
            Assert.Equal(@"Radiohead\In Rainbows", folders);
        }

        [Fact]
        public void ResolveFileName_Counter_UsesMaskedOrderNumber()
        {
            var settings = new UserSettings {InternalOrderNumber = 7, OrderNumberMask = "000"};
            var file = PathTemplate.ResolveFileName("{counter} {title}", Sample(), settings);
            Assert.Equal("007 15 Step", file);
        }

        [Fact]
        public void ResolveFileName_UnknownToken_LeftVisible()
        {
            var file = PathTemplate.ResolveFileName("{nope} {title}", Sample(), new UserSettings());
            Assert.Equal("{nope} 15 Step", file);
        }

        [Fact]
        public void ResolveFileName_FlattensPathSeparators()
        {
            var track = Sample();
            track.Title = "A/B\\C";
            var file = PathTemplate.ResolveFileName("{title}", track, new UserSettings());
            Assert.Equal("A B C", file);
        }

        [Fact]
        public void Resolve_KeepsDiacritics_LikeTheClassicPath()
        {
            // The app's Normalize only strips invalid filename chars (it does not flatten accents),
            // so templated names preserve diacritics just like the classic naming path.
            var track = Sample();
            track.Album = "Naïve";
            track.AlbumArtists = new[] {"Sigur Rós"};
            var folders = PathTemplate.ResolveFolders("{albumartist}/{album}", track, new UserSettings());
            Assert.Equal(@"Sigur Rós\Naïve", folders);
        }
    }
}
