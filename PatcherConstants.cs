namespace RecNetPatcher;

internal static class PatcherConstants
{
    public const string MetadataUrl = "https://ns.rec.net";
    public const uint StandardMetadataMagic = 0xFAB11BAF;

    public static readonly string[] DllDomains = ["ns.rec.net", "recroom.againstgrav.com"];
}
