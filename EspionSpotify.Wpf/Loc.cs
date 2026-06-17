using System.ComponentModel;
using System.Resources;
using EspionSpotify.Enums;
using EspionSpotify.Translations;

namespace EspionSpotify.Wpf
{
    /// <summary>
    /// Localization source. Exposes a string indexer keyed by resx name (e.g. Loc.Instance["lblPath"])
    /// and refreshes all bindings when the language changes. Backed by the engine's resource managers.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc Instance { get; } = new Loc();

        private ResourceManager _rm;

        private Loc() { }

        public void SetLanguage(LanguageType lang)
        {
            _rm = new ResourceManager(Languages.GetResourcesManagerLanguageType(lang));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        public string this[string key] => _rm?.GetString(key) ?? key;

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
