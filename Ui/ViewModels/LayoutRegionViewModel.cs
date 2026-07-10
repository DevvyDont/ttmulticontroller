using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TTMulti.Ui.ViewModels
{
    /// <summary>
    /// Editable view of one <see cref="LayoutRegion"/> as an inline "sentence" row: a Rows x Cols grid on a
    /// monitor (or a custom area). Writes through to the underlying region so the live preview, the on-screen
    /// overlay, and the OK path all see the edits directly. <paramref name="onChanged"/> bumps the preview.
    /// </summary>
    public sealed class LayoutRegionViewModel : INotifyPropertyChanged
    {
        private readonly Action _onChanged;

        public LayoutRegion Region { get; }
        public IReadOnlyList<MonitorOption> MonitorOptions { get; }

        internal LayoutRegionViewModel(LayoutRegion region, IReadOnlyList<MonitorOption> monitors, Action onChanged)
        {
            Region = region;
            MonitorOptions = monitors ?? new List<MonitorOption>();
            _onChanged = onChanged;
        }

        public MonitorOption SelectedMonitor
        {
            get
            {
                if (Region.Source == LayoutRegionSource.Custom)
                    return MonitorOptions.FirstOrDefault(m => m.IsCustom);
                return MonitorOptions.FirstOrDefault(m => m.Index == Region.MonitorIndex)
                    ?? MonitorOptions.FirstOrDefault(m => !m.IsCustom);
            }
            set
            {
                if (value == null)
                    return;
                if (value.IsCustom)
                {
                    if (Region.Source != LayoutRegionSource.Custom)
                    {
                        // Seed the custom rect from the region's current (monitor) rect so it isn't zero-size.
                        var r = LayoutPresetBuilder.GetRegionRect(Region);
                        Region.CustomX = r.X;
                        Region.CustomY = r.Y;
                        Region.CustomWidth = r.Width;
                        Region.CustomHeight = r.Height;
                        Region.Source = LayoutRegionSource.Custom;
                    }
                }
                else
                {
                    Region.Source = LayoutRegionSource.Monitor;
                    Region.MonitorIndex = value.Index;
                }
                RaiseAll();
            }
        }

        public bool IsCustom => Region.Source == LayoutRegionSource.Custom;

        private bool _moreOpen;
        /// <summary>Whether this row's advanced strip (row/col weights) is expanded.</summary>
        public bool IsMoreOpen { get => _moreOpen; set { _moreOpen = value; Changed(); } }

        public int Rows
        {
            get => Region.Rows;
            set { Region.Rows = Math.Max(1, value); Changed(); Changed(nameof(DisplayName)); _onChanged?.Invoke(); }
        }

        public int Cols
        {
            get => Region.Cols;
            set { Region.Cols = Math.Max(1, value); Changed(); Changed(nameof(DisplayName)); _onChanged?.Invoke(); }
        }

        public int CustomX { get => Region.CustomX; set { Region.CustomX = value; Changed(); _onChanged?.Invoke(); } }
        public int CustomY { get => Region.CustomY; set { Region.CustomY = value; Changed(); _onChanged?.Invoke(); } }
        public int CustomWidth { get => Region.CustomWidth; set { Region.CustomWidth = value; Changed(); _onChanged?.Invoke(); } }
        public int CustomHeight { get => Region.CustomHeight; set { Region.CustomHeight = value; Changed(); _onChanged?.Invoke(); } }

        public string RowWeightsText
        {
            get => WeightsToText(Region.RowWeights);
            set { Region.RowWeights = TextToWeights(value); Changed(); _onChanged?.Invoke(); }
        }

        public string ColWeightsText
        {
            get => WeightsToText(Region.ColWeights);
            set { Region.ColWeights = TextToWeights(value); Changed(); _onChanged?.Invoke(); }
        }

        public string DisplayName
        {
            get
            {
                string src = Region.Source == LayoutRegionSource.Monitor ? "Monitor " + (Region.MonitorIndex + 1) : "Custom area";
                string grid = Region.Rows > 0 && Region.Cols > 0 ? Region.Rows + "x" + Region.Cols : "1x1";
                return src + " (" + grid + ")";
            }
        }

        /// <summary>Re-raise everything after an external mutation (on-screen overlay drag).</summary>
        internal void RaiseAll()
        {
            Changed(nameof(SelectedMonitor));
            Changed(nameof(IsCustom));
            Changed(nameof(Rows));
            Changed(nameof(Cols));
            Changed(nameof(CustomX));
            Changed(nameof(CustomY));
            Changed(nameof(CustomWidth));
            Changed(nameof(CustomHeight));
            Changed(nameof(RowWeightsText));
            Changed(nameof(ColWeightsText));
            Changed(nameof(DisplayName));
            _onChanged?.Invoke();
        }

        private static string WeightsToText(double[] weights) =>
            weights == null || weights.Length == 0
                ? ""
                : string.Join(", ", weights.Select(w => w.ToString(CultureInfo.InvariantCulture)));

        private static double[] TextToWeights(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var list = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => double.TryParse(p.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? d : (double?)null)
                .Where(d => d.HasValue)
                .Select(d => d.Value)
                .ToArray();
            return list.Length > 0 ? list : null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Changed([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
