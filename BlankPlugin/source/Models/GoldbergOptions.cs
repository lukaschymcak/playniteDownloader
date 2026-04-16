namespace BlankPlugin
{
    public enum GoldbergRunMode
    {
        Full,
        AchievementsOnly
    }

    /// <summary>
    /// User choices from the Goldberg options dialog (no Playnite references).
    /// </summary>
    public class GoldbergOptions
    {
        public string Arch { get; }
        public GoldbergRunMode Mode { get; }
        public bool CopyFiles { get; }

        public GoldbergOptions(string arch, GoldbergRunMode mode, bool copyFiles)
        {
            Arch = arch;
            Mode = mode;
            CopyFiles = copyFiles;
        }
    }
}
