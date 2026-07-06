using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TTMulti.Ui.ViewModels
{
    /// <summary>
    /// Editable view of one <see cref="LayoutRegion"/> (the WPF replacement for the old PropertyGrid). Writes
    /// through to the underlying region object so the on-screen overlay editors and the OK path see the edits
    /// directly. <see cref="DisplayName"/> is the region-list label.
    /// </summary>
    public sealed class LayoutRegionViewModel : INotifyPropertyChanged
    {
        public LayoutRegion Region { get; }

        internal LayoutRegionViewModel(LayoutRegion region)
        {
            Region = region;
        }

        /// <summary>0 = Monitor, 1 = Custom.</summary>
        public int SourceIndex
        {
            get => (int)Region.Source;
            set
            {
                Region.Source = (LayoutRegionSource)value;
                Changed();
                Changed(nameof(IsMonitor));
                Changed(nameof(IsCustom));
                Changed(nameof(DisplayName));
            }
        }

        public bool IsMonitor => Region.Source == LayoutRegionSource.Monitor;
        public bool IsCustom => Region.Source == LayoutRegionSource.Custom;

        public int MonitorIndex
        {
            get => Region.MonitorIndex;
            set { Region.MonitorIndex = value; Changed(); Changed(nameof(DisplayName)); }
        }

        public int CustomX { get => Region.CustomX; set { Region.CustomX = value; Changed(); } }
        public int CustomY { get => Region.CustomY; set { Region.CustomY = value; Changed(); } }
        public int CustomWidth { get => Region.CustomWidth; set { Region.CustomWidth = value; Changed(); } }
        public int CustomHeight { get => Region.CustomHeight; set { Region.CustomHeight = value; Changed(); } }

        public int Rows { get => Region.Rows; set { Region.Rows = value; Changed(); Changed(nameof(DisplayName)); } }
        public int Cols { get => Region.Cols; set { Region.Cols = value; Changed(); Changed(nameof(DisplayName)); } }

        public string RowWeightsText
        {
            get => WeightsToText(Region.RowWeights);
            set { Region.RowWeights = TextToWeights(value); Changed(); }
        }

        public string ColWeightsText
        {
            get => WeightsToText(Region.ColWeights);
            set { Region.ColWeights = TextToWeights(value); Changed(); }
        }

        public string DisplayName
        {
            get
            {
                string src = Region.Source == LayoutRegionSource.Monitor ? "Monitor " + (Region.MonitorIndex + 1) : "Custom";
                string grid = Region.Rows > 0 && Region.Cols > 0 ? Region.Rows + "×" + Region.Cols : "1×1";
                return src + " · " + grid;
            }
        }

        /// <summary>Re-raise everything after an external (on-screen overlay / monitor picker) mutation.</summary>
        internal void RefreshAll()
        {
            Changed(nameof(SourceIndex));
            Changed(nameof(IsMonitor));
            Changed(nameof(IsCustom));
            Changed(nameof(MonitorIndex));
            Changed(nameof(CustomX));
            Changed(nameof(CustomY));
            Changed(nameof(CustomWidth));
            Changed(nameof(CustomHeight));
            Changed(nameof(Rows));
            Changed(nameof(Cols));
            Changed(nameof(RowWeightsText));
            Changed(nameof(ColWeightsText));
            Changed(nameof(DisplayName));
        }

        private static string WeightsToText(double[] weights) =>
            weights == null || weights.Length == 0
                ? ""
                : string.Join(", ", weights.Select(w => w.ToString(CultureInfo.InvariantCulture)));

        private static double[] TextToWeights(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var list = text.Split(new[] { ',', ';', ' ' }, System.StringSplitOptions.RemoveEmptyEntries)
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
