using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using EspionSpotify.AudioSessions;
using EspionSpotify.Enums;
using EspionSpotify.Models;
using EspionSpotify.Native;
using Moq;
using NAudio.Wave;
using Xunit;

namespace EspionSpotify.Tests
{
    public class RecorderTests
    {
        private readonly IAudioThrottler _audioThrottler;
        private readonly IFrmEspionSpotify _formMock;
        private readonly UserSettings _userSettings;
        private IFileSystem _fileSystem;
        private IProcessManager _processManagerMock;
        private readonly IEncodeService _encodeServiceMock;

        public RecorderTests()
        {
            _formMock = new Mock<IFrmEspionSpotify>().Object;
            _fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>());
            _userSettings = new UserSettings();
            _processManagerMock = new Mock<IProcessManager>().Object;
            _encodeServiceMock = new Mock<IEncodeService>().Object;
            
            
            var audioThrottlerMock = new Mock<IAudioThrottler>();
            audioThrottlerMock.Setup(x => x.WaveFormat).Returns(new WaveFormat());
            _audioThrottler = audioThrottlerMock.Object;
        }

        [Fact]
        internal void IsSkipTrackActive_FalsyWhenTrackNotFound()
        {
            var userSettings = new UserSettings
            {
                RecordRecordingsStatus = RecordRecordingsStatus.Skip,
                OutputPath = @"C:\path",
                TrackTitleSeparator = "_",
                MediaFormat = MediaFormat.Mp3
            };
            var track = new Track {Artist = "Artist", Title = "Title"};
            
            var watcherTrackNotFound = new Recorder(
                _formMock,
                _audioThrottler,
                userSettings,
                ref track,
                _fileSystem,
                _encodeServiceMock,
                _processManagerMock,
                init: false);

            Assert.False(watcherTrackNotFound.IsSkipTrackActive);
        }

        [Fact]
        internal void IsSkipTrackActive_FalsyWhenTrackFoundButDuplicateEnabled()
        {
            var userSettingsCanDuplicate = new UserSettings
            {
                RecordRecordingsStatus = RecordRecordingsStatus.Duplicate,
                OutputPath = @"C:\path",
                TrackTitleSeparator = "_",
                MediaFormat = MediaFormat.Mp3
            };
            _fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                {@"C:\path\Artist_-_Dont_Overwrite_Me.mp3", new MockFileData(new byte[] {0x12, 0x34, 0x56, 0xd2})}
            });
            var track = new Track {Artist = "Artist", Title = "Dont Overwrite Me"};
            
            var watcherTrackFoundCanDuplicate = new Recorder(
                _formMock,
                _audioThrottler,
                userSettingsCanDuplicate,
                ref track,
                _fileSystem,
                _encodeServiceMock,
                _processManagerMock,
                init: false);

            Assert.False(watcherTrackFoundCanDuplicate.IsSkipTrackActive);
        }

        [Fact]
        internal void IsSkipTrackActive_FalsyWhenTrackFoundButOverwriteEnabled()
        {
            var userSettingsCanDuplicate = new UserSettings
            {
                RecordRecordingsStatus = RecordRecordingsStatus.Overwrite,
                OutputPath = @"C:\path",
                TrackTitleSeparator = "_",
                MediaFormat = MediaFormat.Mp3
            };
            _fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                {@"C:\path\Artist_-_Dont_Overwrite_Me.mp3", new MockFileData(new byte[] {0x12, 0x34, 0x56, 0xd2})}
            });
            var track = new Track {Artist = "Artist", Title = "Dont Overwrite Me"};
            
            var watcherTrackFoundCanDuplicate = new Recorder(
                _formMock,
                _audioThrottler,
                userSettingsCanDuplicate,
                ref track,
                _fileSystem,
                _encodeServiceMock,
                _processManagerMock,
                init: false);

            Assert.False(watcherTrackFoundCanDuplicate.IsSkipTrackActive);
        }

        [Fact]
        internal void IsTrackExists_TruthyWhenTrackFoundPlaying()
        {
            _fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                {@"C:\path\Artist_-_Existing_Track.mp3", new MockFileData(new byte[] {0x12, 0x34, 0x56, 0xd2})}
            });
            var userSettings = new UserSettings
                {OutputPath = @"C:\path", TrackTitleSeparator = "_", MediaFormat = MediaFormat.Mp3};
            var track = new Track {Artist = "Artist", Title = "Existing Track", Playing = true};
            
            var watcherTrackFound = new Recorder(
                _formMock,
                _audioThrottler,
                userSettings,
                ref track,
                _fileSystem,
                _encodeServiceMock,
                _processManagerMock,
                init: false);

            Assert.True(watcherTrackFound.IsSkipTrackActive);
        }
    }
}