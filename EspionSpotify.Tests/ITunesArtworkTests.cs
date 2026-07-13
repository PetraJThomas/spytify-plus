using EspionSpotify.API;
using Xunit;

namespace EspionSpotify.Tests
{
    public class ITunesArtworkTests
    {
        [Theory]
        [InlineData("https://is1-ssl.mzstatic.com/image/thumb/abc/100x100bb.jpg", 2048,
            "https://is1-ssl.mzstatic.com/image/thumb/abc/2048x2048bb.jpg")]
        [InlineData("https://x/600x600bb.jpg", 1024, "https://x/1024x1024bb.jpg")]
        [InlineData("https://x/100x100bb.png", 2048, "https://x/2048x2048bb.jpg")]
        public void UpscaleArtworkUrl_ReplacesSizeToken(string input, int size, string expected)
        {
            Assert.Equal(expected, ITunesArtwork.UpscaleArtworkUrl(input, size));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("https://x/cover.jpg")] // not the resizable NxNbb form -> keep the Spotify cover
        public void UpscaleArtworkUrl_UnexpectedForm_ReturnsNull(string input)
        {
            Assert.Null(ITunesArtwork.UpscaleArtworkUrl(input, 2048));
        }

        [Theory]
        // exact
        [InlineData("Radiohead", "In Rainbows", "Radiohead", "In Rainbows")]
        // case / punctuation / edition suffix differences still match
        [InlineData("NiziU", "AWAKE", "NiziU", "AWAKE - EP")]
        [InlineData("Radiohead", "In Rainbows", "RADIOHEAD", "In Rainbows (Deluxe Edition)")]
        [InlineData("Beyoncé", "Renaissance", "Beyonce", "Renaissance")]
        public void IsConfidentMatch_ArtistAndAlbumMatch_True(string sa, string sal, string ia, string ial)
        {
            Assert.True(ITunesArtwork.IsConfidentMatch(sa, sal, ia, ial));
        }

        [Theory]
        // right artist, wrong album -> reject (would key in the wrong cover)
        [InlineData("Radiohead", "In Rainbows", "Radiohead", "OK Computer")]
        // right album title, wrong artist (cover / compilation) -> reject
        [InlineData("NiziU", "AWAKE", "Various Artists", "AWAKE")]
        // nothing matches
        [InlineData("Radiohead", "In Rainbows", "Coldplay", "Parachutes")]
        // short title must match exactly: "1" must not latch onto "10"
        [InlineData("Some Artist", "1", "Some Artist", "10")]
        // missing data -> no confident match
        [InlineData("Radiohead", "In Rainbows", "Radiohead", "")]
        public void IsConfidentMatch_Mismatch_False(string sa, string sal, string ia, string ial)
        {
            Assert.False(ITunesArtwork.IsConfidentMatch(sa, sal, ia, ial));
        }
    }
}
