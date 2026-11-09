// Generated from config/resolutions.json. Do not edit by hand.
namespace SealHdInstaller
{
    internal sealed class ResolutionProfile
    {
        internal readonly int Id, Width, Height;
        internal readonly string Aspect;
        internal ResolutionProfile(int id, int width, int height, string aspect)
        { Id = id; Width = width; Height = height; Aspect = aspect; }
        public override string ToString() { return Width + " x " + Height + " (" + Aspect + ")"; }
    }

    internal static class ResolutionProfiles
    {
        internal static readonly ResolutionProfile[] All = {
            new ResolutionProfile(0, 800, 600, "4:3"),
            new ResolutionProfile(1, 1024, 768, "4:3"),
            new ResolutionProfile(2, 1920, 1080, "16:9"),
            new ResolutionProfile(3, 2560, 1440, "16:9"),
            new ResolutionProfile(4, 3840, 2160, "16:9"),
            new ResolutionProfile(5, 1280, 720, "16:9"),
            new ResolutionProfile(6, 1360, 768, "16:9"),
            new ResolutionProfile(7, 1366, 768, "16:9"),
            new ResolutionProfile(8, 1536, 864, "16:9"),
            new ResolutionProfile(9, 1600, 900, "16:9"),
            new ResolutionProfile(10, 1280, 800, "16:10"),
            new ResolutionProfile(11, 1440, 900, "16:10"),
            new ResolutionProfile(12, 1600, 1000, "16:10"),
            new ResolutionProfile(13, 1680, 1050, "16:10"),
            new ResolutionProfile(14, 1920, 1200, "16:10"),
            new ResolutionProfile(15, 2560, 1600, "16:10"),
            new ResolutionProfile(16, 2880, 1800, "16:10"),
            new ResolutionProfile(17, 3840, 2400, "16:10"),
        };
        internal static readonly ResolutionProfile[] Ordered = {
            All[0],
            All[1],
            All[5],
            All[10],
            All[6],
            All[7],
            All[11],
            All[8],
            All[9],
            All[12],
            All[13],
            All[2],
            All[14],
            All[3],
            All[15],
            All[16],
            All[4],
            All[17],
        };
        internal static ResolutionProfile Default { get { return All[2]; } }
        internal static ResolutionProfile ById(int id) { return id >= 0 && id < All.Length ? All[id] : null; }
        internal static ResolutionProfile Find(int width, int height)
        { foreach (var profile in All) if (profile.Width == width && profile.Height == height) return profile; return null; }
    }
}
