using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RecNetPatcher.Core;

internal static class DllPatcher
{
    public static bool TryPatch(
        string inputPath,
        string outputPath,
        string replacement,
        List<string> log
    )
    {
        ReplacementParts replacementParts = GetReplacementParts(replacement);
        bool patched = false;

        ReaderParameters readerParameters = new()
        {
            InMemory = true,
            ReadingMode = ReadingMode.Immediate,
        };

        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            inputPath,
            readerParameters
        );

        DomainProviders domainProviders = FindDomainProviders(assembly, replacementParts);

        patched |= PatchCustomAttributes(
            assembly.CustomAttributes,
            replacementParts,
            "assembly",
            log
        );

        foreach (ModuleDefinition module in assembly.Modules)
        {
            patched |= PatchCustomAttributes(
                module.CustomAttributes,
                replacementParts,
                module.Name,
                log
            );

            foreach (TypeDefinition type in module.Types)
                patched |= PatchType(type, replacementParts, domainProviders, log);
        }

        if (!patched)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        WriterParameters writerParameters = new() { WriteSymbols = false };

        try
        {
            assembly.Write(outputPath, writerParameters);
        }
        catch (AssemblyResolutionException ex)
        {
            throw new InvalidOperationException(
                $"Failed to resolve referenced assembly '{ex.AssemblyReference.FullName}'. Run this command from the Managed folder, or place the referenced DLL next to the input DLL.",
                ex
            );
        }
        catch (ResolutionException ex)
        {
            throw new InvalidOperationException(
                "Failed to resolve a referenced type while writing the patched DLL. Run this command from the Managed folder, or place the referenced DLLs next to the input DLL.",
                ex
            );
        }

        return true;
    }

    private static bool PatchType(
        TypeDefinition type,
        ReplacementParts replacementParts,
        DomainProviders domainProviders,
        List<string> log
    )
    {
        bool patched = false;

        patched |= PatchCustomAttributes(
            type.CustomAttributes,
            replacementParts,
            type.FullName,
            log
        );

        foreach (FieldDefinition field in type.Fields)
        {
            patched |= PatchConstant(field, field.FullName, replacementParts, log);
            patched |= PatchCustomAttributes(
                field.CustomAttributes,
                replacementParts,
                field.FullName,
                log
            );
        }

        foreach (PropertyDefinition property in type.Properties)
        {
            patched |= PatchConstant(property, property.FullName, replacementParts, log);
            patched |= PatchCustomAttributes(
                property.CustomAttributes,
                replacementParts,
                property.FullName,
                log
            );
        }

        foreach (EventDefinition eventDefinition in type.Events)
        {
            patched |= PatchCustomAttributes(
                eventDefinition.CustomAttributes,
                replacementParts,
                eventDefinition.FullName,
                log
            );
        }

        foreach (MethodDefinition method in type.Methods)
        {
            patched |= PatchCustomAttributes(
                method.CustomAttributes,
                replacementParts,
                method.FullName,
                log
            );

            foreach (ParameterDefinition parameter in method.Parameters)
            {
                string parameterOwner = $"{method.FullName} parameter {parameter.Name}";

                patched |= PatchConstant(parameter, parameterOwner, replacementParts, log);
                patched |= PatchCustomAttributes(
                    parameter.CustomAttributes,
                    replacementParts,
                    parameterOwner,
                    log
                );
            }

            if (!method.HasBody)
                continue;

            IList<Instruction> instructions = method.Body.Instructions;

            for (int i = 0; i < instructions.Count; i++)
            {
                Instruction instruction = instructions[i];

                if (instruction.OpCode != OpCodes.Ldstr || instruction.Operand is not string value)
                    continue;

                string patchedValue = PatchString(value, replacementParts);

                if (patchedValue == value)
                    patchedValue = PatchUrlPrefix(
                        value,
                        instructions,
                        i,
                        domainProviders,
                        replacementParts
                    );

                if (patchedValue == value)
                    continue;

                instruction.Operand = patchedValue;
                patched = true;

                log.Add($"{method.FullName}: \"{value}\" -> \"{patchedValue}\"");
            }
        }

        foreach (TypeDefinition nestedType in type.NestedTypes)
            patched |= PatchType(nestedType, replacementParts, domainProviders, log);

        return patched;
    }

    private static DomainProviders FindDomainProviders(
        AssemblyDefinition assembly,
        ReplacementParts replacementParts
    )
    {
        DomainProviders providers = new([], []);

        foreach (ModuleDefinition module in assembly.Modules)
        {
            foreach (TypeDefinition type in module.Types)
                FindDomainProviders(type, providers, replacementParts);
        }

        return providers;
    }

    private static void FindDomainProviders(
        TypeDefinition type,
        DomainProviders providers,
        ReplacementParts replacementParts
    )
    {
        foreach (MethodDefinition method in type.Methods)
        {
            if (ReturnsKnownDomainString(method, replacementParts))
                providers.Methods.Add(method.FullName);

            if (!method.IsConstructor || !method.IsStatic || !method.HasBody)
                continue;

            IList<Instruction> instructions = method.Body.Instructions;

            for (int i = 0; i < instructions.Count - 1; i++)
            {
                if (
                    instructions[i].OpCode == OpCodes.Ldstr
                    && instructions[i].Operand is string value
                    && IsKnownDomain(value, replacementParts)
                    && instructions[i + 1].OpCode == OpCodes.Stsfld
                    && instructions[i + 1].Operand is FieldReference field
                )
                {
                    providers.Fields.Add(field.FullName);
                }
            }
        }

        foreach (TypeDefinition nestedType in type.NestedTypes)
            FindDomainProviders(nestedType, providers, replacementParts);
    }

    private static bool ReturnsKnownDomainString(
        MethodDefinition method,
        ReplacementParts replacementParts
    )
    {
        if (!method.HasBody || method.ReturnType.MetadataType != MetadataType.String)
            return false;

        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (
                instruction.OpCode == OpCodes.Ldstr
                && instruction.Operand is string value
                && IsKnownDomain(value, replacementParts)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool PatchConstant(
        IConstantProvider constantProvider,
        string owner,
        ReplacementParts replacementParts,
        List<string> log
    )
    {
        if (!constantProvider.HasConstant || constantProvider.Constant is not string value)
            return false;

        string patchedValue = PatchString(value, replacementParts);

        if (patchedValue == value)
            return false;

        constantProvider.Constant = patchedValue;
        log.Add($"{owner} constant: \"{value}\" -> \"{patchedValue}\"");

        return true;
    }

    private static bool PatchCustomAttributes(
        IList<CustomAttribute> attributes,
        ReplacementParts replacementParts,
        string owner,
        List<string> log
    )
    {
        bool patched = false;

        foreach (CustomAttribute attribute in attributes)
        {
            for (int i = 0; i < attribute.ConstructorArguments.Count; i++)
            {
                CustomAttributeArgument argument = attribute.ConstructorArguments[i];
                CustomAttributeArgument patchedArgument = PatchAttributeArgument(
                    argument,
                    replacementParts,
                    out bool argumentPatched
                );

                if (!argumentPatched)
                    continue;

                attribute.ConstructorArguments[i] = patchedArgument;
                patched = true;
                log.Add($"{owner} attribute {attribute.AttributeType.FullName} argument patched");
            }

            for (int i = 0; i < attribute.Fields.Count; i++)
            {
                CustomAttributeNamedArgument argument = attribute.Fields[i];
                CustomAttributeArgument patchedArgument = PatchAttributeArgument(
                    argument.Argument,
                    replacementParts,
                    out bool argumentPatched
                );

                if (!argumentPatched)
                    continue;

                attribute.Fields[i] = new CustomAttributeNamedArgument(
                    argument.Name,
                    patchedArgument
                );
                patched = true;
                log.Add(
                    $"{owner} attribute {attribute.AttributeType.FullName}.{argument.Name} patched"
                );
            }

            for (int i = 0; i < attribute.Properties.Count; i++)
            {
                CustomAttributeNamedArgument argument = attribute.Properties[i];
                CustomAttributeArgument patchedArgument = PatchAttributeArgument(
                    argument.Argument,
                    replacementParts,
                    out bool argumentPatched
                );

                if (!argumentPatched)
                    continue;

                attribute.Properties[i] = new CustomAttributeNamedArgument(
                    argument.Name,
                    patchedArgument
                );
                patched = true;
                log.Add(
                    $"{owner} attribute {attribute.AttributeType.FullName}.{argument.Name} patched"
                );
            }
        }

        return patched;
    }

    private static CustomAttributeArgument PatchAttributeArgument(
        CustomAttributeArgument argument,
        ReplacementParts replacementParts,
        out bool patched
    )
    {
        patched = false;

        if (argument.Value is string value)
        {
            string patchedValue = PatchString(value, replacementParts);

            if (patchedValue == value)
                return argument;

            patched = true;
            return new CustomAttributeArgument(argument.Type, patchedValue);
        }

        if (argument.Value is not CustomAttributeArgument[] values)
            return argument;

        CustomAttributeArgument[] patchedValues = new CustomAttributeArgument[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            patchedValues[i] = PatchAttributeArgument(
                values[i],
                replacementParts,
                out bool itemPatched
            );
            patched |= itemPatched;
        }

        return patched ? new CustomAttributeArgument(argument.Type, patchedValues) : argument;
    }

    private static string PatchString(string value, ReplacementParts replacementParts)
    {
        string patched = value;
        bool replacedDomain = false;

        foreach (string domain in PatcherConstants.DllDomains)
        {
            string replaced = patched.Replace(
                domain,
                replacementParts.Domain,
                StringComparison.OrdinalIgnoreCase
            );

            if (replaced != patched)
                replacedDomain = true;

            patched = replaced;
        }

        if (!replacedDomain)
            return patched;

        if (patched.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return replacementParts.HttpPrefix + patched["https://".Length..];

        if (patched.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            return replacementParts.WebSocketPrefix + patched["wss://".Length..];

        return patched;
    }

    private static string PatchUrlPrefix(
        string value,
        IList<Instruction> instructions,
        int index,
        DomainProviders domainProviders,
        ReplacementParts replacementParts
    )
    {
        if (value.Equals("https://", StringComparison.Ordinal))
            return IsKnownDomainRootConcat(instructions, index, domainProviders, replacementParts)
                ? replacementParts.HttpPrefix
                : value;

        if (value.Equals("wss://", StringComparison.Ordinal))
            return IsKnownDomainRootConcat(instructions, index, domainProviders, replacementParts)
                ? replacementParts.WebSocketPrefix
                : value;

        return value;
    }

    private static bool IsKnownDomainRootConcat(
        IList<Instruction> instructions,
        int schemeIndex,
        DomainProviders domainProviders,
        ReplacementParts replacementParts
    )
    {
        if (schemeIndex + 3 >= instructions.Count)
            return false;

        Instruction domainInstruction = instructions[schemeIndex + 1];
        Instruction slashInstruction = instructions[schemeIndex + 2];

        if (
            slashInstruction.OpCode != OpCodes.Ldstr
            || slashInstruction.Operand is not string slash
        )
            return false;

        if (!slash.Equals("/", StringComparison.Ordinal))
            return false;

        if (
            domainInstruction.OpCode == OpCodes.Ldstr
            && domainInstruction.Operand is string domain
            && IsKnownDomain(domain, replacementParts)
        )
        {
            return IsStringConcatCall(instructions[schemeIndex + 3]);
        }

        if (
            domainInstruction.OpCode == OpCodes.Ldsfld
            && domainInstruction.Operand is FieldReference field
            && domainProviders.Fields.Contains(field.FullName)
        )
        {
            return IsStringConcatCall(instructions[schemeIndex + 3]);
        }

        if (
            domainInstruction.OpCode == OpCodes.Call
            && domainInstruction.Operand is MethodReference method
            && domainProviders.Methods.Contains(method.FullName)
        )
        {
            return IsStringConcatCall(instructions[schemeIndex + 3]);
        }

        return false;
    }

    private static bool IsStringConcatCall(Instruction instruction)
    {
        if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference method)
            return false;

        return method.DeclaringType.FullName == "System.String" && method.Name == "Concat";
    }

    private static bool IsKnownDomain(string value, ReplacementParts replacementParts)
    {
        return value.Equals(replacementParts.Domain, StringComparison.OrdinalIgnoreCase)
            || PatcherConstants.DllDomains.Any(domain =>
                value.Equals(domain, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static ReplacementParts GetReplacementParts(string replacement)
    {
        if (string.IsNullOrWhiteSpace(replacement))
            throw new InvalidOperationException("replacement cannot be empty.");

        if (
            Uri.TryCreate(replacement, UriKind.Absolute, out Uri? uri)
            && !string.IsNullOrWhiteSpace(uri.Host)
        )
        {
            return new ReplacementParts(
                uri.Host,
                GetHttpScheme(uri.Scheme) + "://",
                GetWebSocketScheme(uri.Scheme) + "://"
            );
        }

        string normalized = replacement.Trim();
        int slashIndex = normalized.IndexOf('/');

        if (slashIndex >= 0)
            normalized = normalized[..slashIndex];

        return new ReplacementParts(normalized, "https://", "wss://");
    }

    private static string GetHttpScheme(string scheme)
    {
        return scheme.ToLowerInvariant() switch
        {
            "http" => "http",
            "ws" => "http",
            "wss" => "https",
            _ => "https",
        };
    }

    private static string GetWebSocketScheme(string scheme)
    {
        return scheme.ToLowerInvariant() switch
        {
            "http" => "ws",
            "ws" => "ws",
            _ => "wss",
        };
    }

    private readonly record struct ReplacementParts(
        string Domain,
        string HttpPrefix,
        string WebSocketPrefix
    );

    private sealed record DomainProviders(HashSet<string> Fields, HashSet<string> Methods);
}
