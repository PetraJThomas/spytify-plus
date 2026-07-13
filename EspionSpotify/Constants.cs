namespace EspionSpotify
{
    public static class Constants
    {
        public const string SPYTIFY = "Spytify";

        public const string SPOTIFY = "Spotify";
        public const string SPOTIFYFREE = "Spotify Free";
        public const string SPOTIFYPREMIUM = "Spotify Premium";
        public const string ADVERTISEMENT = "Advertisement";

        // Spotify's AI DJ narrates between songs; those spoken segments surface as a window title
        // like "DJ X - Welcome", i.e. an artist of exactly "DJ X". Used to detect and skip them.
        public const string SPOTIFY_DJ_ARTIST = "DJ X";

        public const string UNTITLED_ALBUM = "Untitled";

        // Standard compilation album-artist tag, used when recording a playlist as one album.
        public const string VARIOUS_ARTISTS = "Various Artists";
    }
}