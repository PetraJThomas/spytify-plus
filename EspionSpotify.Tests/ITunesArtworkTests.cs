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
    }
}
