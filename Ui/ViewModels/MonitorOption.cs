using System.Collections.Generic;

namespace TTMulti.Ui.ViewModels
{
    /// <summary>
    /// One entry in a layout region's monitor dropdown: a real monitor (0-based <see cref="Index"/> with a
    /// friendly label like "Monitor 1 (1920x1080)") or the "Custom area..." sentinel (<see cref="Index"/> = -1).
    /// </summary>
    public sealed class MonitorOption
    {
        public int Index { get; }
        public string Label { get; }
        public bool IsCustom => Index < 0;

        public MonitorOption(int index, string label)
        {
            Index = index;
            Label = label;
        }

        /// <summary>Build the dropdown list from the current monitors, followed by the "Custom area..." option.</summary>
        public static List<MonitorOption> BuildList()
        {
            var list = new List<MonitorOption>();
            var areas = Win32.GetAllMonitorWorkAreas();
            for (int i = 0; i < areas.Count; i++)
            {
                var r = areas[i];
                list.Add(new MonitorOption(i, "Monitor " + (i + 1) + " (" + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + ")"));
            }
            list.Add(new MonitorOption(-1, "Custom area..."));
            return list;
        }
    }
}
