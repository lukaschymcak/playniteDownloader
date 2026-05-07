using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BlankPlugin
{
    /// <summary>
    /// Checks downloaded game executables for SteamStub DRM by analyzing PE headers.
    /// Detects if the entry point falls outside all PE sections (strong SteamStub indicator)
    /// combined with the presence of a PE overlay.
    /// </summary>
    public class SteamDrmChecker
    {
        public class DrmCheckResult
        {
            public List<string> DrmProtectedFiles = new List<string>();
            public List<string> NoDrmFiles = new List<string>();
            public bool HasDrm { get { return DrmProtectedFiles.Count > 0; } }
        }

        public static DrmCheckResult Check(string gameDirectory)
        {
            var result = new DrmCheckResult();
            if (!Directory.Exists(gameDirectory)) return result;

            foreach (var exe in Directory.GetFiles(gameDirectory, "*.exe", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                if (name.Contains("unins") || name.Contains("setup") ||
                    name.Contains("redist") || name.Contains("vc_") ||
                    name.Contains("directx") || name.Contains("dxsetup"))
                    continue;

                try
                {
                    var size = new FileInfo(exe).Length;
                    if (size < 100 * 1024) continue; // skip files under 100KB

                    if (HasSteamStub(exe))
                        result.DrmProtectedFiles.Add(Path.GetFileName(exe));
                    else
                        result.NoDrmFiles.Add(Path.GetFileName(exe));
                }
                catch { }
            }

            return result;
        }

        private static bool HasSteamStub(string exePath)
        {
            try
            {
                using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 64) return false;

                    // DOS Header — e_lfanew at offset 0x3C points to PE header
                    fs.Position = 0x3C;
                    int peOffset = br.ReadInt32();
                    if (peOffset <= 0 || peOffset >= fs.Length) return false;

                    // PE Signature — must be "PE\0\0"
                    fs.Position = peOffset;
                    uint peSig = br.ReadUInt32();
                    if (peSig != 0x4550) return false;

                    // COFF Header
                    br.ReadUInt16(); // Machine
                    ushort numSections = br.ReadUInt16();
                    fs.Position += 8;  // TimeDateStamp, PointerToSymbolTable, NumberOfSymbols
                    ushort sizeOfOptionalHeader = br.ReadUInt16();
                    fs.Position += 2;  // Characteristics

                    // Optional Header
                    long optStart = fs.Position;

                    // AddressOfEntryPoint is at offset 16 from Optional Header start
                    fs.Position = optStart + 16;
                    uint entryPointRva = br.ReadUInt32();

                    // Section Headers — each is 40 bytes, start right after Optional Header
                    long sectionStart = optStart + sizeOfOptionalHeader;
                    uint maxRawEnd = 0;
                    bool entryInNormalSection = false;

                    for (int i = 0; i < numSections; i++)
                    {
                        fs.Position = sectionStart + (i * 40);
                        fs.Position += 8;  // skip Name
                        uint virtualSize = br.ReadUInt32();
                        uint virtualAddress = br.ReadUInt32();
                        uint sizeOfRawData = br.ReadUInt32();
                        uint pointerToRawData = br.ReadUInt32();

                        uint rawEnd = pointerToRawData + sizeOfRawData;
                        if (rawEnd > maxRawEnd) maxRawEnd = rawEnd;

                        if (entryPointRva >= virtualAddress &&
                            entryPointRva < virtualAddress + virtualSize)
                        {
                            entryInNormalSection = true;
                        }
                    }

                    // SteamStub detection:
                    // 1. Entry point outside all sections (SteamStub redirects entry to overlay)
                    // 2. PE overlay exists (file larger than last section's raw end)
                    bool hasOverlay = fs.Length > maxRawEnd;

                    return !entryInNormalSection && entryPointRva > 0 && hasOverlay;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
