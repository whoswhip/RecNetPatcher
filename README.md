# RecNet Patcher
RecNet Patcher is a cli tool used for patching server urls within RecRoom global metadata or managed Dlls.

## Usage
### Dll Patching
When patching Dlls it is required you either run it in the managed folder or have all the Dlls next to the input Dll. Depending on the url you put in, the patcher will automatically patch it for http or https as well.

`RecNetPatcher.exe dll <-input-> <-output-> "url"`
### Metadata Patching
Metadata patching works on all il2cpp builds (even past May 2023) except for anything past late 2024-ish as the metadata file is just a dummy file from then on.

`RecNetPatcher.exe metadata <-input-> <-output-> "url"`
#### How? I thought metadata was encrypted after May?
The metadata isn't actually encrypted at all, its just in a different format

## Building
**Requirements**:
- .net 10 SDK

**Building**:
Just run `dotnet build` in a cli, or you can use Visual Studio.


## License
[AGPL-3.0](LICENSE)