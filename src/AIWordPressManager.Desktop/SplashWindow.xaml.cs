using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

public partial class SplashWindow : Window
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;

    public SplashWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => ElapsedText.Text = _stopwatch.Elapsed.ToString(@"mm\:ss");
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    public void Report(StartupProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Report(progress));
            return;
        }

        StartupProgressBar.Value = progress.Percentage;
        PercentageText.Text = $"{progress.Percentage}%";
        StageText.Text = progress.Stage;
        DetailText.Text = progress.Detail;
    }
}
