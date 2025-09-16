# wincecabreader

An utility to get installation information Windows CE CAB files.

## Usage

1. Extract files from the CAB archive, using 7-Zip or Windows Explorer or similar.
2. Locate the .000 file.
3. Launch wincecabreader.exe and follow the instructions.
4. To get a working CE app installation, rename each file based on its extension. For example, Rename File.001 to the name specified in File number one.

This tool is still in early development (and to be honest, it might not be developed further, as it already does what I need it to), so it provides more information than needed.  
Generally, you only need general information (Target Architecture, Target OS version, App Name, App Provider, ...) and information contained in the FILES section. You might occasionally need information from the LINKS section.  
Output of other sections is there for completeness, but not really useful at the moment (and not even _really_ complete).

Example output, from the "Total Commander" CAB:
```
Paste here the path to a "*.000" file: C:\Users\carlo\Desktop\tcmdwincearm\CECMDA~1.000
Size: 823
Target Architecture: StrongARM
Windows CE Minimum version: 2.0.0
Windows CE Maximum version : 7.0.3758096384
Strings Count: 12
Directories Count: 4
Files Count: 5
Registry Hives Count: 7
Registry Keys Count: 7
Links Count: 3
Strings Offset: 0x00000091
Directories Offset: 0x0000013A
Files Offset: 0x0000015A
Registry Hives Offset: 0x000001C7
Registry Keys Offset: 0x00000225
Links Offset: 0x0000030D
App Name: "Total Commander"
Provider: "C. Ghisler & Co."

STRINGS:
| 1        | "%CE1%\Total Commander"                            |
| 2        | "%CE2%"                                            |
| 3        | "%CE11%"                                           |
| 4        | ".ZIP"                                             |
| 5        | "ZIPArchive"                                       |
| 6        | "Shell"                                            |
| 7        | "Open"                                             |
| 8        | "Command"                                          |
| 9        | "DefaultIcon"                                      |
| 10       | ".TCFOLDER"                                        |
| 11       | "tcfolder"                                         |
| 12       | "Total Commander.lnk"                              |

DIRS:
| 2        | "\Windows"                                         |
| 3        | "\Program Files\Total Commander"                   |
| 4        | "\Windows\Programs"                                |
| 1        | "\Program Files\Total Commander"                   |

FILES:
1: cecmd.htm
Flags value: WarnIfSkipped (00000001)
- WarnIfSkipped

2: cecmd.exe
Flags value: None (00000000)
No flags are set.

3: ftp.tfx
Flags value: None (00000000)
No flags are set.

4: registry.tfx
Flags value: None (00000000)
No flags are set.

5: LAN.tfx
Flags value: None (00000000)
No flags are set.

REGHIVES:
3: HKEY_CURRENT_USER
ZIPArchive
Shell
Open
Command
4: HKEY_CURRENT_USER
ZIPArchive
DefaultIcon
7: HKEY_CURRENT_USER
tcfolder
DefaultIcon
1: HKEY_CURRENT_USER
.ZIP
2: HKEY_CURRENT_USER
ZIPArchive
5: HKEY_CURRENT_USER
.TCFOLDER
6: HKEY_CURRENT_USER
tcfolder

REGKEYS:
While reading REGKEYS: System.Exception: Reading REGKEYS is not fully implemented...
   at wincecabreader.Program.Parse(Byte[] data) in C:\Users\carlo\source\repos\wincecabreader\wincecabreader\Program.cs:line 459

LINKS:
1: LinkEntry { BaseDirectory = InstallDir, Type = File, TargetPath = cecmd.exe, Spec = System.String[] }
2: LinkEntry { BaseDirectory = \Windows\Programs, Type = File, TargetPath = cecmd.exe, Spec = System.String[] }
3: LinkEntry { BaseDirectory = \Windows\Desktop, Type = File, TargetPath = cecmd.exe, Spec = System.String[] }

```

Here, you'd need to rename `CECMD_~2.001` to `cecmd.htm`, `000cecmd.002` to `cecmd.exe`, `00000ftp.003` to `ftp.tfx`, `registry.004` to `registry.tfx` and `00000LAN.005` to `LAN.tfx`.
The only purpose of the .000 file is to provide the installation information, and it can be discarded after extracting the other files.