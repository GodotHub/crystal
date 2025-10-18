using Godot;

namespace CrystalPhoenix.Scripts.LyricsLoader.Lrc
{
    [GlobalClass]
    public partial class LrcLine : Resource
    {
        [Export]
        public Godot.Collections.Array<double> Second {get; set;}
        
        [Export]
        public string Text { get; set; }
        
        public LrcLine() : this(new Godot.Collections.Array<double>(), "") {}

        public LrcLine(Godot.Collections.Array<double> seconds, string text)
        {
            Second = seconds;
            Text = text;
        }
        
    }
}
