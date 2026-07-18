namespace RecNetPatcher.Core;

internal static class PatcherConstants
{
    public const string MetadataUrl = "https://ns.rec.net";
    public const uint StandardMetadataMagic = 0xFAB11BAF;

    public static readonly string[] DllDomains = ["ns.rec.net", "recroom.againstgrav.com"];
    public static readonly string[] RecRoomPhotonIds =
    [
        "9372aa8d-d3f4-44a0-986d-419e145a2b83",
        "e93ae440-f238-4b6c-848f-1df89faf14f5",
    ];
}
