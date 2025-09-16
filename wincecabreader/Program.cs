using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace wincecabreader
{
    internal class Program
    {
        static string To8_3DosName(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("Filename cannot be empty.", nameof(filename));

            string name = Path.GetFileName(filename);

            string baseName = Path.GetFileNameWithoutExtension(name);
            string extension = Path.GetExtension(name).TrimStart('.');

            // Allowed characters: A–Z 0–9 and certain symbols
            Regex invalidChars = new(@"[^A-Z0-9\$%'\-_@~`!()\{\}^#&]", RegexOptions.Compiled);

            baseName = baseName.ToUpperInvariant();
            extension = extension.ToUpperInvariant();

            baseName = invalidChars.Replace(baseName, "_");
            extension = invalidChars.Replace(extension, "_");

            if (baseName.Length > 8) baseName = baseName.Substring(0, 8);
            if (extension.Length > 3) extension = extension.Substring(0, 3);

            return string.IsNullOrEmpty(extension) ? baseName : $"{baseName}.{extension}";
        }

        enum Arch : uint
        {
            Unspecified = 0,
            SHx_SH3 = 103,
            SHx_SH4 = 104,
            i386 = 386,
            i486 = 486,
            Pentium = 586,
            PowerPC_601 = 601,
            PowerPC_603 = 603,
            PowerPC_604 = 604,
            PowerPC_620 = 620,
            Motorola_821 = 821,
            ARM_720 = 1824,
            ARM_820 = 2080,
            ARM_920 = 2336,
            StrongARM = 2577,
            MIPS_R4000 = 4000,
            Hitachi_SH3 = 10003,
            Hitachi_SH3E = 10004,
            Hitachi_SH4 = 10005,
            Alpha_21064 = 21064,
            ARM_7TDMI = 70001
        }

        [Flags]
        enum FileFlags : uint
        {
            None = 0,
            WarnIfSkipped = 1 << 0,
            DoNotSkip = 1 << 1,
            DoNotOverwriteIfExists = 1 << 4,
            CopyOnlyIfTargetExists = 1 << 10,
            SelfRegisterDll = 1 << 28,
            DoNotOverwriteIfNewer = 1 << 29,
            AlwaysOverwrite = 1 << 30,
            SharedReferenceCountingFile = 1u << 31
        }

        enum LinkType : ushort
        {
            Directory = 0,
            File = 1
        }

        [Flags]
        enum RegKeyFlags : uint
        {
            TYPE_DWORD = 0x00010001,
            TYPE_SZ = 0x00000000,
            TYPE_MULTI_SZ = 0x00010000,
            TYPE_BINARY = 0x00000001,
            FLAG_NOCLOBBER = 0x00000002
        }

        static void DumpFileFlags(FileFlags flags)
        {
            Console.WriteLine($"Flags value: {flags} ({(uint)flags:X8})");

            if (flags == FileFlags.None)
            {
                Console.WriteLine("No flags are set.");
                return;
            }

            foreach (FileFlags flag in Enum.GetValues(typeof(FileFlags)))
            {
                if (flag != FileFlags.None && flags.HasFlag(flag))
                {
                    Console.WriteLine($"- {flag}");
                }
            }
        }

        static void DumpRegKeyFlags(RegKeyFlags flags)
        {
            Console.WriteLine($"Flags value: {flags} ({(uint)flags:X8})");

            foreach (RegKeyFlags flag in Enum.GetValues(typeof(RegKeyFlags)))
            {
                if (flags.HasFlag(flag))
                {
                    Console.WriteLine($"- {flag}");
                }
            }
        }

        static readonly Dictionary<string, string> DirPlaceholders = new()
        {
            { "CE0", @"InstallDir" },
            { "CE1", @"\Program Files" },
            { "CE2", @"\Windows" },
            { "CE3", @"\Windows\Desktop" },
            { "CE4", @"\Windows\StartUp" },
            { "CE5", @"\My Documents" },
            { "CE6", @"\Program Files\Accessories" },
            { "CE7", @"\Program Files\Communications" },
            { "CE8", @"\Program Files\Games" },
            { "CE9", @"\Program Files\Pocket Outlook" },
            { "CE10", @"\Program Files\Office" },
            { "CE11", @"\Windows\Programs" },
            { "CE12", @"\Windows\Programs\Accessories" },
            { "CE13", @"\Windows\Programs\Communications" },
            { "CE14", @"\Windows\Programs\Games" },
            { "CE15", @"\Windows\Fonts" },
            { "CE16", @"\Windows\Recent" },
            { "CE17", @"\Windows\Favorites" }
        };
        private static uint _keyNameAndData_Length;

        static void DumpDictionary(Dictionary<ushort, string> dict)
        {
            foreach (var kvp in dict)
            {
                Console.WriteLine($"| {kvp.Key,-8} | \"{kvp.Value.ToString() + '"',-50} |");
            }
        }

        public static string ReplaceVariables(string input, Dictionary<string, string> variables)
        {
            string placeholder = "\xF1"; // unlikely to appear in text... invalid in filenames
            string temp = input.Replace("%%", placeholder);

            temp = Regex.Replace(temp, @"%(?<var>\w+)%", match =>
            {
                string varName = match.Groups["var"].Value;
                return variables.TryGetValue(varName, out var value) ? value : match.Value;
            });

            return temp.Replace(placeholder, "%");
        }

        static uint ReadUInt32(byte[] data, ref uint offset)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan<uint>(offset, int.MaxValue);

            offset += 4;
            return BitConverter.ToUInt32(data.AsSpan((int)offset - 4, 4));
        }

        static ushort ReadUInt16(byte[] data, ref uint offset)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan<uint>(offset, int.MaxValue);

            offset += 2;
            return BitConverter.ToUInt16(data.AsSpan((int)offset - 2, 2));
        }

        static string ReadString(byte[] data, ref uint offset, uint length)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan<uint>(offset, int.MaxValue);
            ArgumentOutOfRangeException.ThrowIfGreaterThan<uint>(length, int.MaxValue);

            offset += length;

            return Encoding.ASCII.GetString(data.AsSpan((int)(offset - length), (int)length));
        }

        static void ReadStringsDictionary(byte[] data, ref uint offset, int dictLength, out Dictionary<ushort, string> dictionary)
        {
            dictionary = [];
            for (int i = 0; i < dictLength; i++)
            {
                ushort _key = ReadUInt16(data, ref offset);
                ushort _valLength = ReadUInt16(data, ref offset);
                string _val = ReadString(data, ref offset, _valLength);

                dictionary.Add(_key, _val);
            }
        }

        /*
                bool sharedReferenceCountingFile = flags.HasFlag(FileFlags.SharedReferenceCountingFile);
         */
        record FileEntry(string DestinationPath, FileFlags Flags,
            bool WarnIfSkipped, bool DoNotSkip, bool DoNotOverwriteTarget,
            bool CopyOnlyIfTargetExists, bool SelfRegisterDll,
            bool DoNotOverwriteIfNewer, bool AlwaysOverwrite,
            bool SharedReferenceCountingFile);

        enum RegHiveID : ushort
        {
            HKEY_CLASSES_ROOT = 0,
            HKEY_CURRENT_USER = 1,
            HKEY_LOCAL_MACHINE = 2,
            HKEY_USERS = 3
        }

        record RegHiveEntry(RegHiveID HiveID, string[] Spec);

        record RegKeyEntry(string Name, RegKeyFlags Flags, byte[] RawData, bool NoClobber);

        static void Main(string[] args)
        {
            try
            {
                Console.Write("Paste here the path to a \"*.000\" file: ");
                string cabPath = Console.ReadLine() ?? throw new IOException("I/O Error while reading path from stdio");
                Parse(File.ReadAllBytes(cabPath));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.Message);
                Console.ResetColor();
                Environment.Exit(1);
            }
        }

        static void Parse(byte[] data)
        {
            uint offset = 0;

            ArgumentOutOfRangeException.ThrowIfGreaterThan<uint>(offset, int.MaxValue);
            Span<byte> _signature = data.AsSpan((int)offset, 4);
            offset += 4;
            if (Encoding.ASCII.GetString(_signature) != "MSCE") throw new Exception("Invalid signature");

            _ = ReadUInt32(data, ref offset); // ignore

            uint _headerSize = ReadUInt32(data, ref offset);
            Console.WriteLine($"Size: {_headerSize}");

            _ = ReadUInt32(data, ref offset); // ignore
            _ = ReadUInt32(data, ref offset); // ignore

            Arch _targetArch = (Arch)ReadUInt32(data, ref offset);
            Console.WriteLine($"Target Architecture: {_targetArch}");

            uint _minVers_maj = ReadUInt32(data, ref offset);
            uint _minVers_min = ReadUInt32(data, ref offset);
            uint _maxVers_maj = ReadUInt32(data, ref offset);
            uint _maxVers_min = ReadUInt32(data, ref offset);
            uint _minVers_build = ReadUInt32(data, ref offset);
            uint _maxVers_build = ReadUInt32(data, ref offset);

            Console.WriteLine($"Windows CE Minimum version: {_minVers_maj}.{_minVers_min}.{_minVers_build}");
            Console.WriteLine($"Windows CE Maximum version : {_maxVers_maj}.{_maxVers_min}.{_maxVers_build}");

            ushort _stringsCount = ReadUInt16(data, ref offset);
            ushort _dirsCount = ReadUInt16(data, ref offset);
            ushort _filesCount = ReadUInt16(data, ref offset);
            ushort _regHivesCount = ReadUInt16(data, ref offset);
            ushort _regKeysCount = ReadUInt16(data, ref offset);
            ushort _linksCount = ReadUInt16(data, ref offset);

            Console.WriteLine("Strings Count: " + _stringsCount);
            Console.WriteLine("Directories Count: " + _dirsCount);
            Console.WriteLine("Files Count: " + _filesCount);
            Console.WriteLine("Registry Hives Count: " + _regHivesCount);
            Console.WriteLine("Registry Keys Count: " + _regKeysCount);
            Console.WriteLine("Links Count: " + _linksCount);

            uint _stringsOffset = ReadUInt32(data, ref offset);
            uint _dirsOffset = ReadUInt32(data, ref offset);
            uint _filesOffset = ReadUInt32(data, ref offset);
            uint _regHivesOffset = ReadUInt32(data, ref offset);
            uint _regKeysOffset = ReadUInt32(data, ref offset);
            uint _linksOffset = ReadUInt32(data, ref offset);

            Console.WriteLine("Strings Offset: 0x" + _stringsOffset.ToString("X8"));
            Console.WriteLine("Directories Offset: 0x" + _dirsOffset.ToString("X8"));
            Console.WriteLine("Files Offset: 0x" + _filesOffset.ToString("X8"));
            Console.WriteLine("Registry Hives Offset: 0x" + _regHivesOffset.ToString("X8"));
            Console.WriteLine("Registry Keys Offset: 0x" + _regKeysOffset.ToString("X8"));
            Console.WriteLine("Links Offset: 0x" + _linksOffset.ToString("X8"));

            ushort _appnameStringOffset = ReadUInt16(data, ref offset);
            ushort _appnameStringLength = ReadUInt16(data, ref offset);
            ushort _providerStringOffset = ReadUInt16(data, ref offset);
            ushort _providerStringLength = ReadUInt16(data, ref offset);

            offset += 8;

            offset = _appnameStringOffset;
            string appName = ReadString(data, ref offset, _appnameStringLength);
            Console.WriteLine($"App Name: \"{appName}\"");

            offset = _providerStringOffset;
            string provider = ReadString(data, ref offset, _providerStringLength);
            Console.WriteLine($"Provider: \"{provider}\"");

            Console.WriteLine();
            Console.WriteLine("STRINGS:");
            offset = _stringsOffset;
            ReadStringsDictionary(data, ref offset, _stringsCount, out var STRINGS);
            DumpDictionary(STRINGS);

            Console.WriteLine();
            Console.WriteLine("DIRS:");
            offset = _dirsOffset;
            Dictionary<ushort, string> DIRS = [];
            for (int i = 0; i < _dirsCount; i++)
            {
                StringBuilder valSb = new();

                ushort _key = ReadUInt16(data, ref offset);
                _ = ReadUInt16(data, ref offset);

                List<ushort> _valEncoded = [];
                while (true)
                {
                    ushort _strId = ReadUInt16(data, ref offset);
                    if (_strId == 0)
                    {
                        break;
                    }
                    else
                    {
                        _valEncoded.Add(_strId);
                    }
                }

                foreach (ushort _stringId in _valEncoded)
                {
                    if (!STRINGS.TryGetValue(_stringId, out var str))
                    {
                        throw new Exception($"Invalid STRING requested by the DIRS section: {_stringId}");
                    }

                    valSb.Append(str);
                }


                DIRS.Add(_key, ReplaceVariables($"{valSb}", DirPlaceholders));
            }
            DumpDictionary(DIRS);

            Console.WriteLine();
            Console.WriteLine("FILES:");
            offset = _filesOffset;
            Dictionary<ushort, FileEntry> FILES = [];
            for (int i = 0; i < _filesCount; i++)
            {
                ushort _fileId = ReadUInt16(data, ref offset);
                ushort _destDirId = ReadUInt16(data, ref offset);
                _ = ReadUInt16(data, ref offset);

                FileFlags flags = (FileFlags)ReadUInt32(data, ref offset);

                bool warnIfSkipped = flags.HasFlag(FileFlags.WarnIfSkipped);
                bool doNotSkip = flags.HasFlag(FileFlags.DoNotSkip);
                bool doNotOverwriteIfExists = flags.HasFlag(FileFlags.DoNotOverwriteIfExists);
                bool copyOnlyIfTargetExists = flags.HasFlag(FileFlags.CopyOnlyIfTargetExists);
                bool selfRegisterDll = flags.HasFlag(FileFlags.SelfRegisterDll);
                bool doNotOverwriteIfNewer = flags.HasFlag(FileFlags.DoNotOverwriteIfNewer);
                bool alwaysOverwrite = flags.HasFlag(FileFlags.AlwaysOverwrite);
                bool sharedReferenceCountingFile = flags.HasFlag(FileFlags.SharedReferenceCountingFile);


                ushort _destPathLenght = ReadUInt16(data, ref offset);
                string _destPath = ReadString(data, ref offset, _destPathLenght);
                FileEntry fileEntry = new(_destPath, flags, warnIfSkipped, doNotSkip,
                                            doNotOverwriteIfExists, copyOnlyIfTargetExists,
                                            selfRegisterDll, doNotOverwriteIfNewer,
                                            alwaysOverwrite, sharedReferenceCountingFile);
                FILES.Add(_fileId, fileEntry);
            }

            foreach (var kvp in FILES)
            {
                Console.WriteLine($"{kvp.Key}: {/*ToDosName*/(kvp.Value.DestinationPath)}");
                DumpFileFlags(kvp.Value.Flags);
                Console.WriteLine();
            }

            Console.WriteLine("REGHIVES:");
            offset = _regHivesOffset;
            Dictionary<ushort, RegHiveEntry> REGHIVES = [];
            for (int i = 0; i < _regHivesCount; i++)
            {
                ushort _regHiveId = ReadUInt16(data, ref offset);
                RegHiveID rootHiveID = (RegHiveID)ReadUInt16(data, ref offset);

                _ = ReadUInt16(data, ref offset);

                ushort _specLength = ReadUInt16(data, ref offset);

                List<string> _spec = [];
                List<ushort> _specEncoded = [];
                int n = 0;
                while (true)
                {
                    ushort _strId = ReadUInt16(data, ref offset);
                    n++;
                    if (_strId == 0 || n >= _specLength - 1)
                    {
                        break;
                    }
                    else
                    {
                        _specEncoded.Add(_strId);
                    }
                }

                foreach (ushort _stringId in _specEncoded)
                {
                    if (!STRINGS.TryGetValue(_stringId, out var str))
                    {
                        throw new Exception($"Invalid STRING requested by the REGHIVES section: {_stringId}");
                    }

                    _spec.Add(str);
                }

                REGHIVES.Add(_regHiveId, new(rootHiveID, [.. _spec]));
            }

            foreach (var kvp in REGHIVES)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value.HiveID}");
                foreach (string s in kvp.Value.Spec)
                {
                    Console.WriteLine(s);
                }
            }

            Console.WriteLine();

            Console.WriteLine("REGKEYS:");
            try
            {
                // TODO: this section was buggy, so I "exception-ed" it out for now, since I did not need it.
                throw new Exception("Reading REGKEYS is not implemented...");

                Dictionary<ushort, RegKeyEntry> REGKEYS = [];
                for (int i = 0; i < _regKeysCount; i++)
                {
                    ushort _regKeyId = ReadUInt16(data, ref offset);
                    ushort _regHiveId = ReadUInt16(data, ref offset);
                    if (!REGHIVES.TryGetValue(_regHiveId, out var _regHive))
                    {
                        throw new Exception($"Invalid REGHIVE ID requested in REGKEYS section: {_regHiveId}");
                    }

                    bool _performValueSub = ReadUInt16(data, ref offset) switch
                    {
                        0 => false,
                        1 => true,
                        _ => throw new Exception($"Invalid value specified for {nameof(_performValueSub)} in REGKEYS section.")
                    };

                    if (_performValueSub)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Error.WriteLine("Warning: variable substitution is not implemented in REGKEYS section.");
                        Console.ResetColor();
                    }

                    RegKeyFlags _flags = (RegKeyFlags)ReadUInt32(data, ref offset);

                    bool _noClobber = _flags.HasFlag(RegKeyFlags.FLAG_NOCLOBBER);

                    ushort _keyNameAndDataLength = ReadUInt16(data, ref offset);

                    byte[] _keyNameAndData = [.. data.AsSpan((int)offset, _keyNameAndDataLength)];
                    offset += _keyNameAndDataLength;

                    int _nullIndex = Array.IndexOf(_keyNameAndData, 0);
                    if (_nullIndex < 0)
                        throw new InvalidDataException("Key name not null-terminated in REGKEYS section.");

                    string _keyName = Encoding.ASCII.GetString(_keyNameAndData[.._nullIndex]);
                    byte[] _keyData = _keyNameAndData[(_nullIndex + 1)..];

                    //if (_flags.HasFlag(RegKeyFlags.TYPE_DWORD))
                    //{

                    //}
                    //else if (_flags.HasFlag(RegKeyFlags.TYPE_SZ))
                    //{

                    //}
                    //else if (_flags.HasFlag(RegKeyFlags.TYPE_MULTI_SZ))
                    //{

                    //}
                    //else if (_flags.HasFlag(RegKeyFlags.TYPE_BINARY))
                    //{

                    //}

                    REGKEYS.Add(_regKeyId, new(_keyName, _flags, _keyData, _noClobber));
                }

                foreach (var r in REGKEYS)
                {
                    Console.WriteLine($"{r.Key}: {r.Value.Name}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"While reading REGKEYS: {ex}");
            }

            Console.WriteLine();

            Console.WriteLine("LINKS:");
            Dictionary<ushort, LinkEntry> LINKS = [];
            offset = _linksOffset;
            for (int i = 0; i < _linksCount; i++)
            {
                ushort _linkID = ReadUInt16(data, ref offset);
                _ = ReadUInt16(data, ref offset);
                ushort _baseDirectoryID = ReadUInt16(data, ref offset);
                string _baseDirectory = ReplaceVariables($"%CE{_baseDirectoryID}%", DirPlaceholders);
                ushort _targetId = ReadUInt16(data, ref offset);
                LinkType _linkType = (LinkType)ReadUInt16(data, ref offset);

                string _targetPath = _linkType switch
                {
                    LinkType.Directory => DIRS.TryGetValue(_targetId, out var _d) ? _d : throw new Exception("Invalid DIR requested in LINKS section"),
                    LinkType.File => FILES.TryGetValue(_targetId, out var _f) ? _f.DestinationPath : throw new Exception("Invalid FILE requested in LINKS section"),
                    _ => string.Empty
                };

                if (!Enum.IsDefined(_linkType)) Console.Error.WriteLine("Invalid LINK type");

                ushort _specLength = ReadUInt16(data, ref offset);

                List<string> _spec = [];
                List<ushort> _specEncoded = [];
                int n = 0;
                while (true)
                {
                    ushort _strId = ReadUInt16(data, ref offset);
                    n++;
                    if (_strId == 0 || n >= _specLength - 1)
                    {
                        break;
                    }
                    else
                    {
                        _specEncoded.Add(_strId);
                    }
                }

                foreach (ushort _stringId in _specEncoded)
                {
                    if (!STRINGS.TryGetValue(_stringId, out var str))
                    {
                        throw new Exception($"Invalid STRING requested by the LINKS section: {_stringId}");
                    }

                    _spec.Add(str);
                }

                LINKS.Add(_linkID, new(_baseDirectory, _linkType, _targetPath, [.. _spec]));
            }

            foreach (var kvp in LINKS)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value}");
            }
        }

        record LinkEntry(string BaseDirectory, LinkType Type, string TargetPath, string[] Spec);
    }
}
