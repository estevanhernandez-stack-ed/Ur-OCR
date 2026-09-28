namespace RoRoRo.UrOcr;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless: write a measured ring into triggers.json and exit, no window.
        if (args.Length > 0 && string.Equals(args[0], RingImportCommand.Flag, StringComparison.OrdinalIgnoreCase))
            return RingImportCommand.Run(args.Skip(1).ToArray());

        // Headless: write pulse loops into triggers.json and exit, no window.
        if (args.Length > 0 && string.Equals(args[0], PulseImportCommand.Flag, StringComparison.OrdinalIgnoreCase))
            return PulseImportCommand.Run(args.Skip(1).ToArray());

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
