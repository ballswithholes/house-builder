// Content specials: dialogue / encounter outcomes from content/*.json, run by the session through IContentContext.
namespace Lanternvale.Rules
{
    public static partial class Specials
    {
        static void RegisterContent()
        {
            Register(new RekindleLanterns());
        }
    }

    /// <summary>
    /// RekindleLanterns (dialogue outcome): sets flag lanterns_rekindled and raises the session event "RekindleLanterns";
    /// the presentation relights every lantern on the map (and on every map built later while the flag is set).
    /// </summary>
    public sealed class RekindleLanterns : SpecialHandler
    {
        public const string Flag = "lanterns_rekindled";
        public RekindleLanterns() : base("RekindleLanterns") { }

        public override bool RunContent(IContentContext ctx)
        {
            ctx.SetFlag(Flag, true);
            ctx.RaiseEvent(Name);
            return true;
        }
    }
}
