namespace Styx
{
    /// <summary>
    /// Game state enumeration — matches HB 4.3.4 values.
    /// Numeric lifecycle word at 0x00B6AA38 in build12340; 0x00B6A9E0 stores screen text.
    /// Note: ChangingFactionOrRace exists in WotLK (Faction Change 3.2.0, Race Change 3.3.3).
    /// ScanDllScanning is the Warden anti-cheat scan state.
    /// </summary>
    public enum GameState
    {
        Idling,
        LoggingIn,
        ChangingRealm,
        SelectingCharacter,
        QueryingRealmList,
        CreatingCharacter,
        DeletingCharacter,
        CharacterRename,
        CharacterDecline,
        ChangingFactionOrRace,
        Zoning,
        Exiting,
        NonPersonalInfoSurvey,
        Unknown,
        ScanDllScanning
    }
}
