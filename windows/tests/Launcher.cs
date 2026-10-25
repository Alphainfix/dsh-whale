using DshWhale;
class Launcher {
    static void Main(string[] args) { using (var p = Native.Launch(new LaunchSpec { Node = args[0], Entry = args[1] }, false, args[2], System.IO.Path.Combine(args[2], ".dsh", "logs"), "web --no-open")) { } }
}
