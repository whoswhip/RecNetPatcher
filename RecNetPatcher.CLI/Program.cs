using System.CommandLine;
using RecNetPatcher.Core;

namespace RecNetPatcher.CLI;

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
                originals = [.. PatcherDefaults.RecRoomPhotonIds];
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
        IReadOnlyList<string> log = Patcher.PatchMetadata(
            input.FullName,
            output.FullName,
            replacement
        );
        PrintPatchHeader(input, output, replacement);
        Console.WriteLine();
        PrintLog(log);
    }

    private static void RunDll(FileInfo input, FileInfo output, string replacement)
    {
        IReadOnlyList<string> log = Patcher.PatchDll(
            input.FullName,
            output.FullName,
            replacement
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
        Patcher.PatchPhoton(input.FullName, output.FullName, replacementIds, photonIds);
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

}
