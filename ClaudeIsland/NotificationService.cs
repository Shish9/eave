using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace ClaudeIsland;

/// Shows other apps' Windows notifications in the island. Opt-in: Windows asks for permission the first time.
/// Whether this works for an unpackaged desktop app depends on the Windows version; Status says what happened.
public class NotificationService
{
    UserNotificationListener? listener;
    readonly HashSet<uint> seen = new();
    bool baselineDone, started, busy;
    DateTime retryAt = DateTime.MinValue;   // after a failure, try again later instead of giving up for good

    public string Status { get; private set; } = "Off";
    public event Action<string, string, string>? Received;   // app, title, body (raised on a background thread)

    public void Tick()
    {
        if (!AppSettings.Current.ShowNotifications)
        {
            if (started) { started = false; baselineDone = false; seen.Clear(); }
            Status = "Off";
            retryAt = DateTime.MinValue;
            return;
        }
        if (busy || DateTime.Now < retryAt) return;
        _ = Run();
    }

    async Task Run()
    {
        busy = true;
        try
        {
            if (!started)
            {
                started = true;
                listener = UserNotificationListener.Current;
                var access = await listener.RequestAccessAsync();
                Status = access switch
                {
                    UserNotificationListenerAccessStatus.Allowed => "On",
                    UserNotificationListenerAccessStatus.Denied => "Blocked in Windows settings (Privacy > Notifications)",
                    _ => "Waiting for your permission",
                };
                if (access != UserNotificationListenerAccessStatus.Allowed) { started = false; retryAt = DateTime.Now.AddSeconds(30); return; }
            }
            if (listener == null) return;

            var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var current = new HashSet<uint>(list.Select(n => n.Id));
            foreach (var n in list)
            {
                if (!seen.Add(n.Id) || !baselineDone) continue;   // the first pass only records what was already there
                try
                {
                    string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "Notification";
                    if (app.Equals(Brand.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                    var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new();
                    Received?.Invoke(app, texts.Count > 0 ? texts[0] : "", texts.Count > 1 ? string.Join(" ", texts.Skip(1)) : "");
                }
                catch { }
            }
            seen.RemoveWhere(id => !current.Contains(id));
            baselineDone = true;
        }
        catch (Exception ex)
        {
            // Keep the user's choice: turning the setting off here used to be saved with the next settings change,
            // which switched notifications off for good after a single hiccup.
            Status = "Not available right now (" + ex.GetType().Name + "), retrying in a few minutes";
            Diag.Log("notifications: " + ex.Message);
            started = false;
            baselineDone = false;
            retryAt = DateTime.Now.AddMinutes(3);
        }
        finally { busy = false; }
    }
}
