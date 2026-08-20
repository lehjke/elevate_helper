using PdfSharp.Fonts;

namespace ElevateHelperWinUI.Services.Reports;

internal sealed class ReportFontResolver : IFontResolver
{
    internal const string FamilyName = "Geologica";
    internal const string SemiBoldFamilyName = "Geologica SemiBold";
    internal const string ExtraBoldFamilyName = "Geologica ExtraBold";
    private const string RegularFace = "Geologica#Regular";
    private const string SemiBoldFace = "Geologica#SemiBold";
    private const string ExtraBoldFace = "Geologica#ExtraBold";

    private readonly IReadOnlyDictionary<string, byte[]> fonts;

    public ReportFontResolver(string fontDirectory)
    {
        fonts = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [RegularFace] = File.ReadAllBytes(Path.Combine(fontDirectory, "Geologica-Regular.ttf")),
            [SemiBoldFace] = File.ReadAllBytes(Path.Combine(fontDirectory, "Geologica-SemiBold.ttf")),
            [ExtraBoldFace] = File.ReadAllBytes(Path.Combine(fontDirectory, "Geologica-ExtraBold.ttf")),
        };
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        string? face = familyName switch
        {
            _ when familyName.Equals(FamilyName, StringComparison.OrdinalIgnoreCase) => RegularFace,
            _ when familyName.Equals(SemiBoldFamilyName, StringComparison.OrdinalIgnoreCase) => SemiBoldFace,
            _ when familyName.Equals(ExtraBoldFamilyName, StringComparison.OrdinalIgnoreCase) => ExtraBoldFace,
            _ => null,
        };

        if (face is null)
        {
            return null;
        }

        return new FontResolverInfo(face, mustSimulateBold: bold, mustSimulateItalic: italic);
    }

    public byte[]? GetFont(string faceName)
    {
        return fonts.TryGetValue(faceName, out byte[]? data) ? data : null;
    }
}
