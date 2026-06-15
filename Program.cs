using System.CommandLine;
using System.Text;

namespace RecNetPatcher;

internal static class Program
{
    public static int Main(string[] args)
    {
        RootCommand command = new("Patches Rec Room server URLs to the provided URL.");
        command.Subcommands.Add(CreateMetadataCommand());
        command.Subcommands.Add(CreateDllCommand());
        command.Subcommands.Add(CreatePhotonCommand());

        try
        {
            return command.Parse(args).Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");

            return 1;
        }
    }

    private static Command CreateMetadataCommand()
    {
        Argument<FileInfo> input = CreateInputArgument();
        Argument<FileInfo> output = CreateOutputArgument();
        Argument<string> replacementUrl = CreateReplacementArgument();

        Command command = new("metadata", "Patches IL2CPP metadata.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Arguments.Add(replacementUrl);
        command.SetAction(parseResult =>
        {
            FileInfo inputFile = parseResult.GetValue(input)!;
            FileInfo outputFile = parseResult.GetValue(output)!;
            string replacement = parseResult.GetValue(replacementUrl)!;

            RunMetadata(inputFile, outputFile, replacement);
        });

        return command;
    }

    private static Command CreateDllCommand()
    {
        Argument<FileInfo> input = CreateInputArgument();
        Argument<FileInfo> output = CreateOutputArgument();
        Argument<string> replacementUrl = CreateReplacementArgument();

        Command command = new("dll", "Patches managed DLL string literals.");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Arguments.Add(replacementUrl);
        command.SetAction(parseResult =>
        {
            FileInfo inputFile = parseResult.GetValue(input)!;
            FileInfo outputFile = parseResult.GetValue(output)!;
            string replacement = parseResult.GetValue(replacementUrl)!;

            RunDll(inputFile, outputFile, replacement);
        });

        return command;
    }

    private static Command CreatePhotonCommand()
    {
        Argument<FileInfo> input = CreateInputArgument();
        Argument<FileInfo> output = CreateOutputArgument();
        Argument<string[]> replacementIds = new("ids") { Description = "New ids" };
        Option<string[]> originalIds = new("original-ids") { Description = "Ids being replaced" };

        Command command = new("photon", "Patches Photon Ids");
        command.Arguments.Add(input);
        command.Arguments.Add(output);
        command.Arguments.Add(replacementIds);
        command.Options.Add(originalIds);
        command.SetAction(parseResult =>
        {
            FileInfo inputFile = parseResult.GetValue(input)!;
            FileInfo outputFile = parseResult.GetValue(output)!;
            string[] replacements = parseResult.GetValue(replacementIds)!;
            string[] originals = parseResult.GetValue(originalIds)!;
            if (originals?.Length == 0 || originals is null)
                originals = PatcherConstants.RecRoomPhotonIds;
            RunPhoton(inputFile, outputFile, replacements, originals);
        });
        return command;
    }

    private static Argument<FileInfo> CreateInputArgument()
    {
        return new Argument<FileInfo>("input") { Description = "Input file." };
    }

    private static Argument<FileInfo> CreateOutputArgument()
    {
        return new Argument<FileInfo>("output") { Description = "Output patched file." };
    }

    private static Argument<string> CreateReplacementArgument()
    {
        return new Argument<string>("replacement-url") { Description = "Replacement URL." };
    }

    private static void RunMetadata(FileInfo input, FileInfo output, string replacement)
    {
        byte[] data = ReadAllBytesShared(input.FullName);
        byte[] oldBytes = Encoding.UTF8.GetBytes(PatcherConstants.MetadataUrl);
        byte[] newBytes = Encoding.UTF8.GetBytes(replacement);

        if (newBytes.Length == 0)
            throw new InvalidOperationException("replacement cannot be empty.");

        List<string> log = [];
        bool isStandardMetadata =
            StandardMetadataPatcher.ReadU32(data, 0) == PatcherConstants.StandardMetadataMagic;
        bool isPatched = isStandardMetadata
            ? StandardMetadataPatcher.TryPatch(ref data, oldBytes, newBytes, log)
            : CustomMetadataPatcher.TryPatch(ref data, oldBytes, newBytes, log);

        if (!isPatched)
            throw new InvalidOperationException(
                $"could not find a patchable {PatcherConstants.MetadataUrl} entry."
            );

        File.WriteAllBytes(output.FullName, data);
        PrintPatchHeader(input, output, replacement);
        Console.WriteLine($"old length:  {oldBytes.Length}");
        Console.WriteLine($"new length:  {newBytes.Length}");
        Console.WriteLine();
        PrintLog(log);
    }

    private static void RunDll(FileInfo input, FileInfo output, string replacement)
    {
        List<string> log = [];
        bool isPatched = DllPatcher.TryPatch(input.FullName, output.FullName, replacement, log);

        if (!isPatched)
            throw new InvalidOperationException(
                "could not find a patchable ns.rec.net or recroom.againstgrav.com string."
            );

        PrintPatchHeader(input, output, replacement);
        Console.WriteLine();
        PrintLog(log);
    }

    private static void RunPhoton(
        FileInfo input,
        FileInfo output,
        string[] replacementIds,
        string[] photonIds
    )
    {
        byte[] data = ReadAllBytesShared(input.FullName);
        List<string> log = [];
        bool isPatched = PhotonPatcher.TryPatch(ref data, photonIds, replacementIds, log);
        if (!isPatched)
            throw new InvalidOperationException("could not find patchable photon ids");

        File.WriteAllBytes(output.FullName, data);
    }

    private static void PrintPatchHeader(FileInfo input, FileInfo output, string replacement)
    {
        Console.WriteLine($"input:       {input.FullName}");
        Console.WriteLine($"output:      {output.FullName}");
        Console.WriteLine($"replacement: {replacement}");
    }

    private static void PrintLog(IEnumerable<string> log)
    {
        foreach (string line in log)
            Console.WriteLine(line);
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );

        byte[] data = new byte[stream.Length];
        int read = 0;

        while (read < data.Length)
        {
            int count = stream.Read(data, read, data.Length - read);

            if (count == 0)
                throw new EndOfStreamException($"Unexpected EOF while reading {path}");

            read += count;
        }

        return data;
    }
}
