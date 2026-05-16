namespace RoRoRo.UrOcr;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
