# RecNet Patcher

RecNet Patcher patches server URLs and Photon IDs within Rec Room global metadata,
resources, and managed DLLs. This repository contains a shared patching library,
a command-line interface, and a WPF powered GUI.

## Projects

- `RecNetPatcher.Core` - reusable patching library
- `RecNetPatcher.CLI` - cross-platform command-line interface
- `RecNetPatcher.GUI` - Windows GUI built with WPF
- `RecNetPatcher.Tests` - xUnit integration tests using compressed fixtures

## Usage

### Dll Patching

When patching Dlls it is required you either run it in the managed folder or have all the Dlls next to the input Dll. Depending on the url you put in, the patcher will automatically patch it for http or https as well.

`RecNetPatcher.exe dll <input> <output> "url"`

### Metadata Patching

Metadata patching works on all il2cpp builds (even after July 4th 2023) except for anything past August 20th 2024 as the metadata file is just a dummy file from then on.

`RecNetPatcher.exe metadata <input> <output> "url"`

#### How? I thought metadata was encrypted after May?

The metadata isn't actually encrypted at all, its just in a different format

### Photon Id Patching

Photon Id patching should work on literally any file since it directly changes the bytes, the only thing that could prevent this is something like EAC checking the hash.

`RecNetPatcher.exe photon <input> <output> "top" "bottom" [--original-ids "top" "bottom]`

## Building

**Requirements**:

- .net 10 SDK

**Building**:

Build all four projects from the repository root:

`dotnet build RecNetPatcher.slnx`

Run the CLI:

`dotnet run --project RecNetPatcher.CLI -- --help`

Run the GUI on Windows:

`dotnet run --project RecNetPatcher.GUI`

Run the tests:

`dotnet test RecNetPatcher.slnx`

## License

[AGPL-3.0](LICENSE)
