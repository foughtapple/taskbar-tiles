// Use Explorer's default app-button action where it exposes InvokePattern.
// No taskbar/tray private memory, remote messages, forced reveal or coordinates.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Automation;

namespace TaskbarTiles
{
    static class TaskbarDefaultAction
    {
        internal static bool SameButton(AppButton original, string id, string cls, string name, IntPtr root)
        {
            if (original == null || !TaskbarScanPolicy.KnownApp(id, cls, false, name)) return false;
            string expected = original.AppId;
            if (!string.IsNullOrWhiteSpace(expected))
                return string.Equals(id, "Appid:" + expected, StringComparison.OrdinalIgnoreCase);
            // Name-only selection is allowed only for the actual root/entry we
            // observed, not for a favourite that merely happens to have that name.
            return original.Taskbar != IntPtr.Zero && original.Taskbar == root &&
                string.Equals(original.Id ?? "", id ?? "", StringComparison.Ordinal) &&
                string.Equals(original.Name, name, StringComparison.Ordinal) &&
                string.Equals(original.ClassName, cls, StringComparison.Ordinal);
        }
        internal static bool InvokeOnce(Action action, LaunchOperation operation, out string error)
        {
            error = null; if (action == null) return false;
            if (!operation.TryDispatch()) return true;
            try { action(); }
            catch (Exception ex)
            {
                LaunchLog.Write(operation.Id, "taskbar default action uncertain; " + ex.GetType().Name + "; HRESULT=0x" + ex.HResult.ToString("X8"));
                error = "Windows did not confirm the taskbar action. The app may already be opening; no second launch was sent. Try the original taskbar if needed.";
            }
            return true;
        }
        internal static bool InvokeElement(AutomationElement element, LaunchOperation operation, out string error)
        {
            error = null; object pattern;
            try
            {
                if (element == null || !element.Current.IsEnabled || !element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) return false;
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            var invoke = pattern as InvokePattern; if (invoke == null) return false;
            return InvokeOnce(delegate { invoke.Invoke(); }, operation, out error);
        }
        internal static bool TryInvoke(AppButton original, LaunchOperation operation, out string error)
        {
            error = null; if (operation.Cancelled) return true;
            var roots = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                string cls = Native.Class(h);
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") roots.Add(h);
                return true;
            }, IntPtr.Zero);
            foreach (var root in roots.OrderByDescending(h => h == original.Taskbar))
            {
                try
                {
                    var condition = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                    var elements = AutomationElement.FromHandle(root).FindAll(TreeScope.Descendants, condition);
                    var matches = new List<AutomationElement>();
                    for (int i = 0; i < elements.Count; i++)
                    {
                        try
                        {
                            var current = elements[i].Current;
                            if (SameButton(original, current.AutomationId, current.ClassName, current.Name, root)) matches.Add(elements[i]);
                        }
                        catch (ElementNotAvailableException) { }
                    }
                    // An ungrouped set needs an explicit window choice; do not pick
                    // the first row simply because it shares an app ID.
                    if (matches.Count != 1) continue;
                    if (operation.Cancelled) return true;
                    if (Native.LaunchModifiersDown() || Native.Down(0x10) || Native.Down(1) || Native.Down(2))
                    { error = "Release the mouse buttons and keyboard modifiers, then try again. No action was sent."; return true; }
                    if (InvokeElement(matches[0], operation, out error))
                    {
                        LaunchLog.Write(operation.Id, "taskbar default Invoke action; no pointer movement or Shift modifier");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    // A failure after dispatch can be ambiguous; never follow it
                    // with ShellExecute or a second click.
                    if (operation.Dispatched)
                    { error = "Taskbar action status is uncertain. No second launch was sent."; return true; }
                    LaunchLog.Write(operation.Id, "taskbar action unavailable before dispatch: " + ex.GetType().Name);
                }
            }
            return false;
        }
    }
}
