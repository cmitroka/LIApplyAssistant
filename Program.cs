namespace CjmApplyAssistant;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var configuration = AppConfig.Load();
        Application.Run(new MainForm(configuration));
    }
}
