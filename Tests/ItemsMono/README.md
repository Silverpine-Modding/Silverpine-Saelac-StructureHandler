# Native item persistence checks

`ItemsMono.csproj` targets the installed game's Mono runtime. Build the plugin,
this project, and `Host/Host.csproj`, then invoke the host with the game directory
and `bin/Release/net48/ItemsMono.exe` as arguments. It runs in a disposable process,
not in the game and not against a save file.

The checks use the real StructureHandler DLL and native game serializers. They
exercise all seven qualities, native deep clone/type resolution, inventory
save/load and back-references, rotation, letter text and gold without requiring
CustomItemLoader or SaltExtraDebug.
