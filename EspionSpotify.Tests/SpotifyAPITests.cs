using System;
using System.Collections.Generic;
using EspionSpotify.API;
using EspionSpotify.Enums;
using EspionSpotify.Models;
using SpotifyAPI.Web.Models;
using Xunit;
using Image = SpotifyAPI.Web.Models.Image;

namespace EspionSpotify.Tests
{
    public class SpotifyAPITests
    {
        private readonly Track _track;
        private readonly ISpotifyAPI _spotifyAPI;

        public SpotifyAPITests()
        {
            _track = new Track {Artist = "Artist", Title = "Title"};
            _spotifyAPI = new API.SpotifyAPI();
        }

        [Fact]
        internal void MapSpotifyEmptyTrackToTrack_ReturnsExpectedTrack()
        {
            var fullTrack = new FullTrack();

            _spotifyAPI.MapSpotifyTrackToTrack(_track, fullTrack);

            Assert.NotNull(_track.Title);
            Assert.Equal(0, _track.AlbumPosition);
            Assert.Equal(new string[] { }, _track.Performers);
            Assert.Equal(0, _track.Disc);
        }

        [Fact]
        internal void MapSpotifyTrackToTrack_ReturnsExpectedTrack()
        {
            var fulltrack = new FullTrack
            {
                Name = "Title",
                TrackNumber = 3,
                Artists = new List<SimpleArtist>
                {
                    new SimpleArtist {Name = "Artist"},
                    new SimpleArtist {Name = "Other Artist"}
                },
                DiscNumber = 12345
            };

            _spotifyAPI.MapSpotifyTrackToTrack(_track, fulltrack);

            Assert.Equal("Title", _track.Title);
            Assert.Equal("Artist", _track.Artist);
            Assert.Equal(3, _track.AlbumPosition);
            Assert.Equal(new[] {"Artist", "Other Artist"}, _track.Performers);
            Assert.Equal(12345, _track.Disc);
        }

        [Fact]
        internal void MapSpotifyTrackToTrack_OverwritesSpytifyTrack()
        {
            var fulltrack = new FullTrack
            {
                Name = "Updated Title",
                Artists = new List<SimpleArtist>
                {
                    new SimpleArtist {Name = "Updated Artist"},
                    new SimpleArtist {Name = "Other Artist"}
                }
            };

            _spotifyAPI.MapSpotifyTrackToTrack(_track, fulltrack);

            Assert.Equal("Updated Title", _track.Title);
            Assert.Equal("Updated Artist", _track.Artist);
        }

        [Fact]
        internal void MapSpotifyTrackToTrack_KeepSpytifyTrackIfEmpty()
        {
            var fulltrack = new FullTrack
            {
                Name = "",
                Artists = new List<SimpleArtist>
                {
                    new SimpleArtist {Name = ""}
                }
            };

            _spotifyAPI.MapSpotifyTrackToTrack(_track, fulltrack);

            Assert.Equal("Title", _track.Title);
            Assert.Equal("Artist", _track.Artist);
        }

        [Theory]
        [InlineData("Title", TitleSeparatorType.None, "Title", null)]
        [InlineData("Title - Live", TitleSeparatorType.Dash, "Title", "Live")]
        [InlineData("Title (feat. Other Artist)", TitleSeparatorType.Parenthesis, "Title", "feat. Other Artist")]
        [InlineData("Title (feat. Other Artist) - Live", TitleSeparatorType.Dash, "Title (feat. Other Artist)", "Live")]
        internal void MapSpotifyTrackToTrack_DefinesTitleExtended(
            string apiTitle,
            TitleSeparatorType expectedSeparator, string expectedTitle, string expectedTitleExtended)
        {
            var fullTrack = new FullTrack
            {
                Name = apiTitle
            };

            _spotifyAPI.MapSpotifyTrackToTrack(_track, fullTrack);

            Assert.Equal(expectedSeparator, _track.TitleExtendedSeparatorType);
            Assert.Equal(expectedTitle, _track.Title);
            Assert.Equal(expectedTitleExtended, _track.TitleExtended);
        }

        [Fact]
        internal void MapSpotifyEmptyAlbumToTrack_ReturnsExpectedTrack()
        {
            var fullAlbum = new FullAlbum
            {
                Artists = new List<SimpleArtist>(),
                Name = "",
                Genres = new List<string>(),
                Images = new List<Image>()
            };

            _spotifyAPI.MapSpotifyAlbumToTrack(_track, fullAlbum);

            Assert.Equal(Array.Empty<string>(), _track.AlbumArtists);
            Assert.Equal("", _track.Album);
            Assert.Equal(Array.Empty<string>(), _track.Genres);
            Assert.Null(_track.Year);
            Assert.Null(_track.AlbumArtUrl);
        }

        [Fact]
        internal void MapSpotifyAlbumToTrackMissingImages_ReturnsExpectedTrack()
        {
            var fullAlbum = new FullAlbum
            {
                Artists = new List<SimpleArtist>
                {
                    new SimpleArtist {Name = "Artist"},
                    new SimpleArtist {Name = "Other Artist"}
                },
                Name = "Album Name",
                Genres = new List<string> {"Reggae", "Rock", "Jazz"},
                ReleaseDate = "2010-10-10",
                Images = new List<Image>()
            };

            _spotifyAPI.MapSpotifyAlbumToTrack(_track, fullAlbum);

            Assert.Equal(new[] {"Artist", "Other Artist"}, _track.AlbumArtists);
            Assert.Equal("Album Name", _track.Album);
            Assert.Equal(new[] {"Reggae", "Rock", "Jazz"}, _track.Genres);
            Assert.Equal(2010, _track.Year);
            Assert.Null(_track.AlbumArtUrl);
        }

        [Fact]
        internal void MapSpotifyAlbumToTrackMissingImageSizes_ReturnsExpectedTrack()
        {
            var fullAlbum = new FullAlbum
            {
                Artists = new List<SimpleArtist>
                {
                    new SimpleArtist {Name = "Artist"},
                    new SimpleArtist {Name = "Other Artist"}
                },
                Name = "Album Name",
                Genres = new List<string> {"Reggae", "Rock", "Jazz"},
                ReleaseDate = "2010-10-10",
                Images = new List<Image>
                {
                    new Image
                    {
                        Height = 64,
                        Width = 64,
                        Url = "http://64x64.img"
                    },
                    new Image
                    {
                        Height = 256,
                        Width = 256,
                        Url = "http://256x256.img"
                    },
                    new Image
                    {
                    Height = 512,
                    Width = 512,
                    Url = "http://512x512.img"
                },
                }
            };

            _spotifyAPI.MapSpotifyAlbumToTrack(_track, fullAlbum);

            Assert.Equal(new[] {"Artist", "Other Artist"}, _track.AlbumArtists);
            Assert.Equal("Album Name", _track.Album);
            Assert.Equal(new[] {"Reggae", "Rock", "Jazz"}, _track.Genres);
            Assert.Equal(2010, _track.Year);
            Assert.Equal("http://512x512.img", _track.AlbumArtUrl);
        }

        [Fact]
        internal void MapSpotifyPlaylistToTrack_OverridesAlbumIdentity_KeepsTrackArtist()
        {
            var track = new Track
            {
                Artist = "Dua Lipa", Title = "Levitating",
                Album = "Future Nostalgia", AlbumArtists = new[] {"Dua Lipa"},
                AlbumArtUrl = "http://track-album.img"
            };
            var playlist = new FullPlaylist
            {
                Name = "Summer 2024",
                Images = new List<Image>
                {
                    new Image {Width = 300, Url = "http://300.img"},
                    new Image {Width = 640, Url = "http://640.img"}
                }
            };

            _spotifyAPI.MapSpotifyPlaylistToTrack(track, playlist, 5);

            Assert.Equal("Summer 2024", track.Album);
            Assert.Equal(new[] {"Various Artists"}, track.AlbumArtists);
            Assert.Equal(5, track.AlbumPosition);
            Assert.Equal("http://640.img", track.AlbumArtUrl);
            Assert.Equal("Dua Lipa", track.Artist); // per-track artist preserved
        }

        [Fact]
        internal void MapSpotifyPlaylistToTrack_NullPlaylist_IsNoOp()
        {
            var track = new Track {Artist = "Artist", Title = "Title", Album = "Real Album"};
            _spotifyAPI.MapSpotifyPlaylistToTrack(track, null, 1);
            Assert.Equal("Real Album", track.Album);
        }

        [Theory]
        [InlineData("playlist", "spotify:playlist:37i9dQZF1DX", "37i9dQZF1DX")]
        [InlineData("playlist", "spotify:user:abc:playlist:xyz123", "xyz123")]
        [InlineData("album", "spotify:album:zzz", null)]
        [InlineData("playlist", "", null)]
        internal void GetPlaylistIdFromContext_ParsesPlaylistId(string type, string uri, string expected)
        {
            var context = new Context {Type = type, Uri = uri};
            Assert.Equal(expected, API.SpotifyAPI.GetPlaylistIdFromContext(context));
        }

        [Fact]
        internal void GetPlaylistIdFromContext_NullContext_ReturnsNull()
        {
            Assert.Null(API.SpotifyAPI.GetPlaylistIdFromContext(null));
        }

        [Fact]
        internal void MapFullSpotifyAlbumToTrack_ReturnsExpectedTrack()
        {
            var fullAlbum = new FullAlbum
            {
                Artists = new List<SimpleArtist>
                {
                    new SimpleArtist {Name = "Artist"},
                    new SimpleArtist {Name = "Other Artist"}
                },
                Name = "Album Name",
                Genres = new List<string> {"Reggae", "Rock", "Jazz"},
                ReleaseDate = "2010-10-10",
                Images = new List<Image>
                {
                    new Image
                    {
                        Height = 128,
                        Width = 128,
                        Url = "http://128x128.img"
                    },
                    new Image
                    {
                        Height = 32,
                        Width = 32,
                        Url = "http://32x32.img"
                    },
                    new Image
                    {
                        Height = 16,
                        Width = 16,
                        Url = "http://16x16.img"
                    },
                    new Image
                    {
                        Height = 512,
                        Width = 512,
                        Url = "http://512x512.img"
                    },
                    new Image
                    {
                        Height = 64,
                        Width = 64,
                        Url = "http://64x64.img"
                    },
                    new Image
                    {
                        Height = 300,
                        Width = 300,
                        Url = "http://300x300.img"
                    }
                }
            };

            _spotifyAPI.MapSpotifyAlbumToTrack(_track, fullAlbum);

            Assert.Equal(new[] {"Artist", "Other Artist"}, _track.AlbumArtists);
            Assert.Equal("Album Name", _track.Album);
            Assert.Equal(new[] {"Reggae", "Rock", "Jazz"}, _track.Genres);
            Assert.Equal(2010, _track.Year);
            Assert.Equal("http://512x512.img", _track.AlbumArtUrl);
        }
    }
}