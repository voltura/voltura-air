using System.Windows;
using VolturaAir.Host.Features.UsageTelemetry;

namespace VolturaAir.Host.Features.Diagnostics;

internal sealed class UsageStatisticsController(IUsageStatisticsControl usageStatistics)
{
    public UsageStatisticsView CreateView()
    {
        var view = new UsageStatisticsView();
        var transitionRunning = false;
        string? transitionMessage = null;

        void Refresh()
        {
            var state = usageStatistics.State;
            view.StateText.Text = state == UsageStatisticsRuntimeState.On ? "On" : "Off";
            view.ProfileText.Text = usageStatistics.Distribution == UsageStatisticsDistribution.Installed
                ? "Installed version"
                : "Portable version";
            view.ChangeStateButton.Content = state switch
            {
                UsageStatisticsRuntimeState.On => "Disable",
                UsageStatisticsRuntimeState.OffChoiceNotSaved => "Retry disable",
                _ => "Enable"
            };
            view.ChangeStateButton.IsEnabled = !transitionRunning;
            var visibleMessage = transitionMessage;
            if (visibleMessage is null && state == UsageStatisticsRuntimeState.OffChoiceNotSaved)
            {
                visibleMessage = "Off for now, but the setting was not saved. Retry before restarting.";
            }
            else if (visibleMessage is null && state == UsageStatisticsRuntimeState.OffIdentityCleanupPending)
            {
                visibleMessage = "Off. The old local ID could not be removed. Retry before enabling.";
            }
            view.TransitionStatusText.Text = visibleMessage ?? string.Empty;
            view.TransitionStatusText.Visibility = visibleMessage is null
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        void OnStateChanged(object? sender, EventArgs eventArgs)
        {
            _ = sender;
            _ = eventArgs;
            if (view.Dispatcher.CheckAccess())
            {
                Refresh();
            }
            else
            {
                _ = view.Dispatcher.BeginInvoke(Refresh);
            }
        }

        usageStatistics.StateChanged += OnStateChanged;
        view.Unloaded += (_, _) => usageStatistics.StateChanged -= OnStateChanged;
        view.PrivacyButton.Click += (_, _) => ProductWebsite.OpenPrivacy();
        view.ChangeStateButton.Click += async (_, _) =>
        {
            if (transitionRunning)
            {
                return;
            }

            transitionRunning = true;
            transitionMessage = null;
            Refresh();
            var enabling = usageStatistics.State is not
                (UsageStatisticsRuntimeState.On or UsageStatisticsRuntimeState.OffChoiceNotSaved);
            try
            {
                var result = await usageStatistics.SetEnabledAsync(enabling);
                if (!result.Saved)
                {
                    transitionMessage = enabling
                        ? "Still off. The setting could not be saved."
                        : "Off for now, but the setting was not saved. Retry before restarting.";
                }
                else if (!enabling && !result.IdentityRemoved)
                {
                    transitionMessage = "Off. The old local ID could not be removed. Retry before enabling.";
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                transitionMessage = enabling
                    ? "Still off. The change could not be completed."
                    : "Off for now, but the setting was not saved. Retry before restarting.";
            }
            finally
            {
                transitionRunning = false;
                Refresh();
            }
        };

        Refresh();
        return view;
    }
}
