using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        private string _completeJourneyContextText = "No active site";
        private bool _completeJourneyIsBlocked;
        private bool _completeJourneyIsReady;

        public string CompleteJourneyContextText
        {
            get => _completeJourneyContextText;
            private set => SetProperty(ref _completeJourneyContextText, value);
        }

        public bool CompleteJourneyIsBlocked
        {
            get => _completeJourneyIsBlocked;
            private set => SetProperty(ref _completeJourneyIsBlocked, value);
        }

        public bool CompleteJourneyIsReady
        {
            get => _completeJourneyIsReady;
            private set => SetProperty(ref _completeJourneyIsReady, value);
        }

        internal void RefreshCanonicalJourneyState()
        {
            var hasSite = Sites.SelectedSite is not null;
            var hasSnapshot = hasSite && !IsNeverSynchronized(DashboardLastSiteSync);
            var hasAnalysis = hasSnapshot && !IsNotAnalyzed(DashboardSeoScoreState);
            var hasFindings = hasAnalysis && ReadOptionalPositiveCount(
                "DashboardCriticalIssues",
                "DashboardWarnings",
                "DashboardFindings") > 0;
            var hasRecommendations = hasAnalysis && DashboardAiSuggestions > 0;
            var hasApproval = IsCompletedStateValue(JourneyApprovalState);
            var hasBackup = ReadOptionalCompletedState(
                "JourneyBackupState",
                "BackupState",
                "SafetyBackupState",
                "LatestBackupState");
            var hasExecution = IsCompletedStateValue(JourneyExecuteState);
            var hasVerification = IsCompletedStateValue(JourneyVerifyState) ||
                                  IsCompletedStateValue(JourneyDoneState) ||
                                  DashboardJourneyProgress >= 100;
            var hasFailure = IsFailureState(JourneyExecuteState) ||
                             ReadOptionalFailureState("ExecutionState", "LatestExecutionState", "JourneyFailureState");
            var canRollback = hasBackup || ReadOptionalBoolean("CanRollback", "HasRollbackEvidence");
            var isArabic = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase) ||
                           CultureInfo.CurrentCulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase);

            // Older builds recorded preview completion before introducing an explicit backup state.
            // Treat it only as compatibility evidence when approval already exists.
            if (!hasBackup && hasApproval && IsCompletedStateValue(JourneyPreviewState))
                hasBackup = true;

            var result = JourneyStateResolver.Resolve(new JourneyStateInput(
                hasSite,
                hasSnapshot,
                hasAnalysis,
                hasFindings,
                hasRecommendations,
                hasApproval,
                hasBackup,
                hasExecution,
                hasVerification,
                hasFailure,
                canRollback,
                isArabic));

            CompleteJourneySteps.Clear();
            for (var index = 0; index < result.Stages.Count; index++)
            {
                var stage = result.Stages[index];
                CompleteJourneySteps.Add(new CompleteJourneyStep(
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    stage.Title,
                    stage.Description,
                    stage.Target,
                    stage.Status.ToString().ToUpperInvariant(),
                    stage.Status == JourneyStageStatus.Completed,
                    stage.Status is JourneyStageStatus.Current or JourneyStageStatus.Blocked));
            }

            var completed = result.Stages.Count(x => x.Status == JourneyStageStatus.Completed);
            CompleteJourneyHeadline = result.Headline;
            CompleteJourneySummary = result.Summary;
            CompleteJourneyPercent = result.ProgressPercent;
            CompleteJourneyIsBlocked = result.IsBlocked;
            CompleteJourneyIsReady = true;
            CompleteJourneyCompletedText = isArabic
                ? $"اكتملت {completed} من {result.Stages.Count} مراحل"
                : $"{completed} of {result.Stages.Count} stages completed";
            CompleteJourneyRemainingText = result.IsComplete
                ? isArabic ? "تم التحقق من رحلة ووردبريس" : "The WordPress journey is verified"
                : isArabic
                    ? $"متبقي {result.Stages.Count - completed} مراحل"
                    : $"{result.Stages.Count - completed} stage(s) remaining";

            var siteName = ReadSelectedSiteName();
            CompleteJourneyContextText = isArabic
                ? $"الموقع: {siteName} • آخر مزامنة: {DashboardLastSiteSync}"
                : $"Site: {siteName} • Last sync: {DashboardLastSiteSync}";

            if (!HasScheduledSyncPause)
            {
                var active = result.Stages.FirstOrDefault(x => x.Status is JourneyStageStatus.Current or JourneyStageStatus.Blocked)
                             ?? result.Stages[^1];
                CurrentJourneyStepTitle = active.Title;
                CurrentJourneyStepDescription = result.Summary;
                CurrentJourneyActionLabel = result.ActionLabel;
                CurrentJourneyTarget = result.Target;
            }
        }

        private string ReadSelectedSiteName()
        {
            var selected = Sites.SelectedSite;
            if (selected is null) return "—";

            foreach (var propertyName in new[] { "Name", "SiteName", "Title", "Url", "BaseUrl" })
            {
                var value = selected.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(selected)?.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return selected.ToString() ?? "—";
        }

        private bool ReadOptionalCompletedState(params string[] propertyNames) =>
            propertyNames.Any(name => IsCompletedStateValue(ReadOptionalProperty(name)?.ToString()));

        private bool ReadOptionalFailureState(params string[] propertyNames) =>
            propertyNames.Any(name => IsFailureState(ReadOptionalProperty(name)?.ToString()));

        private bool ReadOptionalBoolean(params string[] propertyNames) =>
            propertyNames.Any(name => ReadOptionalProperty(name) is bool value && value);

        private int ReadOptionalPositiveCount(params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var value = ReadOptionalProperty(name);
                if (value is int number) return number;
                if (value is long longNumber && longNumber <= int.MaxValue) return (int)longNumber;
                if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
            }

            return 0;
        }

        private object? ReadOptionalProperty(string propertyName) =>
            GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this);

        private static bool IsNeverSynchronized(string? value) =>
            string.IsNullOrWhiteSpace(value) ||
            value.Equals("Never synchronized", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Never", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("لم تتم المزامنة", StringComparison.OrdinalIgnoreCase);

        private static bool IsNotAnalyzed(string? value) =>
            string.IsNullOrWhiteSpace(value) ||
            value.Equals("NOT ANALYZED", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Not analyzed", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("لم يتم التحليل", StringComparison.OrdinalIgnoreCase);

        private static bool IsCompletedStateValue(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            (value.Equals("DONE", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("VERIFIED", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("SUCCEEDED", StringComparison.OrdinalIgnoreCase));

        private static bool IsFailureState(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            (value.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("ROLLBACK REQUIRED", StringComparison.OrdinalIgnoreCase));
    }
}

namespace AIWordPressManager.Desktop
{
    internal static class CanonicalJourneyStateBindingBootstrap
    {
        private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded),
                true);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
            if (Attached.TryGetValue(window, out _)) return;
            if (window.DataContext is not ViewModels.MainWindowViewModel main) return;

            var state = new State(window, main);
            Attached.Add(window, state);
            state.Attach();
        }

        private sealed class State(MainWindow window, ViewModels.MainWindowViewModel main)
        {
            public void Attach()
            {
                main.PropertyChanged += OnPropertyChanged;
                main.Sites.SelectedSiteChanged += OnSelectedSiteChanged;
                window.Closed += OnClosed;
                QueueRefresh();
            }

            private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.CurrentPage) || IsJourneyInput(e.PropertyName))
                    QueueRefresh();
            }

            private void OnSelectedSiteChanged(object? sender, EventArgs e) => QueueRefresh();

            private void QueueRefresh()
            {
                if (!window.IsLoaded || !main.CurrentPage.Equals("Dashboard", StringComparison.OrdinalIgnoreCase)) return;
                window.Dispatcher.BeginInvoke(new Action(main.RefreshCanonicalJourneyState), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }

            private static bool IsJourneyInput(string? propertyName) => propertyName is
                nameof(ViewModels.MainWindowViewModel.DashboardLastSiteSync) or
                nameof(ViewModels.MainWindowViewModel.DashboardSeoScoreState) or
                nameof(ViewModels.MainWindowViewModel.DashboardAiSuggestions) or
                nameof(ViewModels.MainWindowViewModel.JourneyPreviewState) or
                nameof(ViewModels.MainWindowViewModel.JourneyApprovalState) or
                nameof(ViewModels.MainWindowViewModel.JourneyExecuteState) or
                nameof(ViewModels.MainWindowViewModel.JourneyVerifyState) or
                nameof(ViewModels.MainWindowViewModel.JourneyDoneState) or
                nameof(ViewModels.MainWindowViewModel.DashboardJourneyProgress) or
                nameof(ViewModels.MainWindowViewModel.HasScheduledSyncPause);

            private void OnClosed(object? sender, EventArgs e)
            {
                main.PropertyChanged -= OnPropertyChanged;
                main.Sites.SelectedSiteChanged -= OnSelectedSiteChanged;
                window.Closed -= OnClosed;
            }
        }
    }
}
