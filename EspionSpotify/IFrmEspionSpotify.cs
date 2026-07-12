using System.Resources;
using EspionSpotify.Enums;

namespace EspionSpotify
{
    public interface IFrmEspionSpotify
    {
        ResourceManager Rm { get; }
        void UpdateIconSpotify(bool isSpotifyPlaying, bool isRecording = false);
        void UpdatePlayingTitle(string text);
        void UpdatePlayingArt(string url);
        void QueueQualityAnalysis(string filePath);
        void UpdateRecordedTime(int? time);
        void UpdateStartButton();
        void StopRecording();
        void UpdateNumUp();
        void UpdateNumDown();
        void WriteIntoConsole(TranslationKeys resource, params object[] args);
        void UpdateExternalAPIToggle(ExternalAPIType value);
        void ShowFailedToUseSpotifyAPIMessage();
        void UpdateAudioDevicesDataSource();
        void SetSoundVolume(int volume);
        bool AskUpdate(string title, string message);
    }
}