using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace EspionSpotify.Wpf
{
    /// <summary>
    /// XAML markup extension for localized text: {l:Tr lblPath}. Resolves to a one-way binding
    /// against <see cref="Loc"/>, so text updates live when the language changes.
    /// </summary>
    public class TrExtension : MarkupExtension
    {
        public TrExtension() { }

        public TrExtension(string key) { Key = key; }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
