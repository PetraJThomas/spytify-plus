namespace EspionSpotify
{
    /// <summary>
    /// Holds the active UI implementation so engine components can call back into the
    /// running window (WinForms or WPF) without depending on a concrete form type.
    /// Each UI shell assigns <see cref="Form"/> to itself on startup.
    /// </summary>
    public static class Spytify
    {
        public static IFrmEspionSpotify Form { get; set; }
    }
}
