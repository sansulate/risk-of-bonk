# Reading Megabonk's own data (offline, development only)

The mod never loads Megabonk code; these tools only read numbers out of the player's Megabonk install.

1. Il2CppDumper 6.7.46 (github.com/Perfare/Il2CppDumper, run with `DOTNET_ROLL_FORWARD=Major`) on Megabonk's
   `GameAssembly.dll` and `Megabonk_Data/il2cpp_data/Metadata/global-metadata.dat` gives `dump.cs` (classes, field
   offsets, method addresses), `script.json` and `DummyDll/`. Keep the output outside the repo.
2. `dotnet run -c Release -- <Megabonk_Data> ../../lib/classdata.tpk <DummyDll> WeaponData,CharacterData` lists
   MonoBehaviour/ScriptableObject assets of those classes. With `RAW=1 BIN=<dir>` it writes each asset's raw bytes
   instead (the type-tree reading misreads LocalizedString, so the raw parsers below are the reliable route).
3. `python weapons.py <dir> out.json` and `python characters.py <dir>` decode WeaponData and CharacterData from those
   bytes (UnlockableBase fields, two LocalizedStrings, then the class's fields in declaration order; bools padded to 4).
4. `MBDUMP=<Il2CppDumper output> python dis.py 'WeaponUtility$$GetWeaponCooldown'` disassembles a method (pip install
   capstone) with call targets, type infos, strings and float constants named.
