using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop
{
    internal static class SeoAuditFirstJourneyExperience
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
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
                return;
            if (Attached.TryGetValue(window, out _))
                return;
            if (window.DataContext is not MainWindowViewModel main)
                return;

            var state = new State(window, main);
            Attached.Add(window, state);
            state.Attach();
        }

        private sealed class State(MainWindow window, MainWindowViewModel main)
        {
            private Border? _panel;
            private bool _installPending;

            public void Attach()
            {
                main.PropertyChanged += OnMainPropertyChanged;
                main.SeoAudit.PropertyChanged += OnAuditPropertyChanged;
                main.SeoAudit.Issues.CollectionChanged += OnAuditCollectionChanged;
                main.SeoAudit.History.CollectionChanged += OnAuditCollectionChanged;
                window.Closed += OnClosed;
                QueueInstallAndRefresh();
            }

            private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                    QueueInstallAndRefresh();
            }

            private void OnAuditPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(SeoAuditViewModel.Score) or
                    nameof(SeoAuditViewModel.AuditedItems) or
                    nameof(SeoAuditViewModel.HighIssues) or
                    nameof(SeoAuditViewModel.MediumIssues) or
                    nameof(SeoAuditViewModel.LowIssues) or
                    nameof(SeoAuditViewModel.IsRunning) or
                    nameof(SeoAuditViewModel.StatusMessage))
                {
                    main.SeoAudit.RefreshFirstJourneyReadiness();
                }
            }

            private void OnAuditCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            {
                main.SeoAudit.RefreshFirstJourneyReadiness();
                main.RefreshFirstJourneySidebar();
            }

            private void QueueInstallAndRefresh()
            {
                if (_installPending || window.Dispatcher.HasShutdownStarted)
                    return;

                _installPending = true;
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _installPending = false;
                    _panel ??= InstallPanel(window, main);
                    var visible = main.CurrentPage.Equals("SEO Audit", StringComparison.OrdinalIgnoreCase);
                    if (_panel is not null)
                    {
                        _panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                        _panel.IsHitTestVisible = visible;
                    }

                    if (visible)
                        main.SeoAudit.RefreshFirstJourneyReadiness();
                }));
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                main.PropertyChanged -= OnMainPropertyChanged;
                main.SeoAudit.PropertyChanged -= OnAuditPropertyChanged;
                main.SeoAudit.Issues.CollectionChanged -= OnAuditCollectionChanged;
                main.SeoAudit.History.CollectionChanged -= OnAuditCollectionChanged;
                window.Closed -= OnClosed;
            }
        }

        private static Border? InstallPanel(MainWindow window, MainWindowViewModel main)
        {
            var runButton = FindButtonForCommand(window, main.SeoAudit.RunAuditCommand);
            if (runButton is null)
                return null;

            var host = FindVerticalHost(runButton);
            if (host is null)
                return null;

            var existing = host.Children.OfType<Border>()
                .FirstOrDefault(item => Equals(item.Tag, "SeoAuditFirstJourneyPanel"));
            if (existing is not null)
                return existing;

            var panel = new Border
            {
                Tag = "SeoAuditFirstJourneyPanel",
                Margin = new Thickness(0, 8, 0, 14),
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = ResolveBrush(window, "BorderBrush", Brushes.DimGray),
                Background = ResolveBrush(window, "SurfaceAltBrush", Brushes.Transparent)
            };

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "STEP 3 · BUILD SEO BASELINE",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = ResolveBrush(window, "PrimaryBrush", Brushes.DodgerBlue)
            });

            var status = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 10),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            status.SetBinding(TextBlock.TextProperty, new Binding("SeoAudit.FirstJourneyStatus"));
            root.Children.Add(status);

            var metrics = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 11,
                Foreground = ResolveBrush(window, "TextSecondaryBrush", Brushes.Gray)
            };
            metrics.SetBinding(TextBlock.TextProperty, new MultiBinding
            {
                StringFormat = "Score {0}/100 · Audited {1} · High {2} · Medium {3} · Low {4}",
                Bindings =
                {
                    new Binding("SeoAudit.Score"),
                    new Binding("SeoAudit.AuditedItems"),
                    new Binding("SeoAudit.HighIssues"),
                    new Binding("SeoAudit.MediumIssues"),
                    new Binding("SeoAudit.LowIssues")
                }
            });
            root.Children.Add(metrics);

            var requirements = new ItemsControl();
            requirements.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("SeoAudit.FirstJourneyRequirements"));
            requirements.ItemTemplate = BuildRequirementTemplate();
            root.Children.Add(requirements);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var run = new Button
            {
                Content = "Run SEO Audit",
                MinWidth = 135,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 7, 12, 7)
            };
            run.SetBinding(Button.CommandProperty, new Binding("SeoAudit.RunAuditCommand"));
            actions.Children.Add(run);

            var continueButton = new Button
            {
                Content = "Continue to Suggested Changes",
                MinWidth = 210,
                Padding = new Thickness(12, 7, 12, 7),
                CommandParameter = "Suggested Changes"
            };
            continueButton.SetBinding(Button.CommandProperty, new Binding("NavigateCommand"));
            continueButton.SetBinding(UIElement.IsEnabledProperty, new Binding("SeoAudit.IsFirstJourneyReady"));
            actions.Children.Add(continueButton);
            root.Children.Add(actions);

            panel.Child = root;
            host.Children.Insert(0, panel);
            return panel;
        }

        private static DataTemplate BuildRequirementTemplate()
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 5));

            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetValue(TextBlock.WidthProperty, 24d);
            icon.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(SeoAuditJourneyRequirement.StatusIcon)));
            row.AppendChild(icon);

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            text.SetBinding(TextBlock.TextProperty, new MultiBinding
            {
                StringFormat = "{0} — {1}",
                Bindings =
                {
                    new Binding(nameof(SeoAuditJourneyRequirement.Title)),
                    new Binding(nameof(SeoAuditJourneyRequirement.Description))
                }
            });
            row.AppendChild(text);
            return new DataTemplate { VisualTree = row };
        }

        private static Button? FindButtonForCommand(DependencyObject parent, ICommand expectedCommand)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is Button button && ReferenceEquals(button.Command, expectedCommand))
                    return button;
                var nested = FindButtonForCommand(child, expectedCommand);
                if (nested is not null)
                    return nested;
            }
            return null;
        }

        private static Panel? FindVerticalHost(DependencyObject element)
        {
            var current = VisualTreeHelper.GetParent(element);
            while (current is not null)
            {
                if (current is StackPanel { Orientation: Orientation.Vertical } stack)
                    return stack;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
            => element.TryFindResource(key) is Brush brush ? brush : fallback;
    }
}
