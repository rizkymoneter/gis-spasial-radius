using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GISSpatialChecker.Models;
using GISSpatialChecker.Services;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;

namespace GISSpatialChecker.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // ─── Properties ──────────────────────────────────────────
        [ObservableProperty] private double _radiusMeter = 100;
        [ObservableProperty] private string _statusBar = "Siap. Silakan import Entity A dan B.";
        [ObservableProperty] private string _jumlahA = "0 titik";
        [ObservableProperty] private string _jumlahB = "0 titik";
        [ObservableProperty] private string _jumlahDalam = "-";
        [ObservableProperty] private string _jumlahLuar = "-";
        [ObservableProperty] private bool _isLoading = false;

        // ─── Progress Properties ──────────────────────────────────
        [ObservableProperty] private double _progressValue = 0;
        [ObservableProperty] private string _progressText = "";
        [ObservableProperty] private string _etaText = "";
        [ObservableProperty] private string _rateText = "";
        [ObservableProperty] private bool _isProcessing = false;

        private CancellationTokenSource? _cts;

        // Auto-notify export commands setiap kali IsLoading berubah
        partial void OnIsLoadingChanged(bool value)
        {
            ExportExcelCommand.NotifyCanExecuteChanged();
            ExportCsvCommand.NotifyCanExecuteChanged();
        }

        public ObservableCollection<SpatialResult> HasilList { get; } = new();
        public Map MapControl { get; } = new Map();

        private List<EntityPoint> _listA = new();
        private List<EntityPoint> _listB = new();
        private List<SpatialResult> _allResults = new();

        private WritableLayer _layerA     = new WritableLayer { Name = "Entity A", Style = null };
        private WritableLayer _layerB     = new WritableLayer { Name = "Entity B", Style = null };
        private WritableLayer _layerRadius = new WritableLayer { Name = "Radius",   Style = null };

        // Callback dari View untuk navigasi peta (karena Navigator ada di MapControl, bukan Map)
        public Action<double, double, int>? NavigateToCallback { get; set; }

        public MainViewModel()
        {
            InitMap();
        }

        // ─── Map Init ─────────────────────────────────────────────
        private void InitMap()
        {
            MapControl.Layers.Add(OpenStreetMap.CreateTileLayer());
            MapControl.Layers.Add(_layerRadius);
            MapControl.Layers.Add(_layerA);
            MapControl.Layers.Add(_layerB);
        }

        // ─── Import Entity A ──────────────────────────────────────
        [RelayCommand]
        private async Task ImportEntityA()
        {
            var path = BukaFileDialog("Import Entity A");
            if (path == null) return;

            IsLoading = true;
            StatusBar = "Mengimport Entity A...";

            await Task.Run(() =>
            {
                _listA = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    ? FileService.ImportCsv(path, "A")
                    : FileService.ImportExcel(path, "A");
            });

            JumlahA = $"{_listA.Count} titik";
            StatusBar = $"Entity A berhasil diimport: {_listA.Count} titik.";
            IsLoading = false;

            RefreshMap();
        }

        // ─── Import Entity B ──────────────────────────────────────
        [RelayCommand]
        private async Task ImportEntityB()
        {
            var path = BukaFileDialog("Import Entity B");
            if (path == null) return;

            IsLoading = true;
            StatusBar = "Mengimport Entity B...";

            await Task.Run(() =>
            {
                _listB = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    ? FileService.ImportCsv(path, "B")
                    : FileService.ImportExcel(path, "B");
            });

            JumlahB = $"{_listB.Count} titik";
            StatusBar = $"Entity B berhasil diimport: {_listB.Count} titik.";
            IsLoading = false;

            RefreshMap();
        }

        // ─── Cek Spasial ─────────────────────────────────────────
        [RelayCommand]
        private async Task CekSpasial()
        {
            if (_listA.Count == 0 || _listB.Count == 0)
            {
                MessageBox.Show("Import Entity A dan Entity B terlebih dahulu!", "Perhatian",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            long totalKombinasi = (long)_listA.Count * _listB.Count;
            IsLoading = true;
            IsProcessing = true;
            ProgressValue = 0;
            ProgressText = $"0% (0 / {totalKombinasi:N0})";
            EtaText = "Menghitung...";
            RateText = "";
            HasilList.Clear();
            _allResults.Clear();
            StatusBar = $"🚀 Mencari pasangan terdekat untuk {_listA.Count:N0} titik Entity A dari {_listB.Count:N0} titik Entity B...";

            _cts = new CancellationTokenSource();

            var progress = new Progress<ProgressInfo>(info =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ProgressValue = info.Percentage;
                    ProgressText = $"{info.Percentage:F1}% ({info.Current:N0} / {info.Total:N0} titik A)";
                    EtaText = $"⏱ Sisa: {info.RemainingFormatted} | Berlalu: {info.ElapsedFormatted}";
                    RateText = $"⚡ {info.Rate:N0} titik/detik | ✅ Dalam: {info.CountDalam:N0} | ❌ Luar: {info.CountLuar:N0}";
                    StatusBar = $"Memproses... {info.Percentage:F1}% — {info.Rate:N0} ops/sec";
                });
            });

            SpatialSummary? summary = null;
            try
            {
                summary = await Task.Run(() =>
                    SpatialService.CekSpasial(_listA, _listB, RadiusMeter, progress, _cts.Token),
                    _cts.Token);

                _allResults = summary.HasilDalam;

                // Notify export buttons setelah data ada
                ExportExcelCommand.NotifyCanExecuteChanged();
                ExportCsvCommand.NotifyCanExecuteChanged();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    HasilList.Clear();
                    foreach (var r in _allResults)
                        HasilList.Add(r);

                    JumlahDalam = $"{summary.CountDalam:N0}";
                    JumlahLuar = $"{summary.CountLuar:N0}";
                    ProgressValue = 100;
                    ProgressText = "✅ Selesai!";
                    EtaText = $"Total kombinasi: {summary.TotalKombinasi:N0}";
                    RateText = $"✅ Match dalam radius: {summary.CountDalam:N0} | ❌ Luar: {summary.CountLuar:N0}";
                    StatusBar = $"✅ Selesai! Kombinasi: {summary.TotalKombinasi:N0} | Dalam: {summary.CountDalam:N0} | Luar: {summary.CountLuar:N0}";

                    // Aktifkan tombol export
                    ExportExcelCommand.NotifyCanExecuteChanged();
                    ExportCsvCommand.NotifyCanExecuteChanged();
                });

                RefreshMap();
            }
            catch (OperationCanceledException)
            {
                StatusBar = "⛔ Proses dibatalkan.";
                ProgressText = "Dibatalkan";
                EtaText = "";
                RateText = "";
            }
            finally
            {
                IsLoading = false;
                IsProcessing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ─── Cancel Proses ────────────────────────────────────────
        [RelayCommand]
        private void BatalkanProses()
        {
            _cts?.Cancel();
            StatusBar = "⛔ Membatalkan proses...";
        }


        // ─── Export Excel ─────────────────────────────────────────
        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task ExportExcel()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Excel Files|*.xlsx",
                FileName = $"Hasil_SpatialCheck_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (dialog.ShowDialog() != true) return;

            IsLoading = true;
            ExportExcelCommand.NotifyCanExecuteChanged();
            ExportCsvCommand.NotifyCanExecuteChanged();

            try
            {
                StatusBar = "Mengekspor ke Excel...";
                await Task.Run(() => FileService.ExportExcel(_allResults, dialog.FileName));
                StatusBar = $"✅ Export Excel berhasil: {dialog.FileName}";
                MessageBox.Show("Export Excel berhasil!", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusBar = $"❌ Gagal export Excel: {ex.Message}";
                MessageBox.Show($"Gagal export:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                ExportExcelCommand.NotifyCanExecuteChanged();
                ExportCsvCommand.NotifyCanExecuteChanged();
            }
        }

        // ─── Export CSV ───────────────────────────────────────────
        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task ExportCsv()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files|*.csv",
                FileName = $"Hasil_SpatialCheck_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            IsLoading = true;
            ExportExcelCommand.NotifyCanExecuteChanged();
            ExportCsvCommand.NotifyCanExecuteChanged();

            try
            {
                StatusBar = "Mengekspor ke CSV...";
                await Task.Run(() => FileService.ExportCsv(_allResults, dialog.FileName));
                StatusBar = $"✅ Export CSV berhasil: {dialog.FileName}";
                MessageBox.Show("Export CSV berhasil!", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusBar = $"❌ Gagal export CSV: {ex.Message}";
                MessageBox.Show($"Gagal export:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                ExportExcelCommand.NotifyCanExecuteChanged();
                ExportCsvCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanExport() => _allResults.Count > 0 && !IsLoading;

        // ─── Download Template CSV ────────────────────────────────
        [RelayCommand]
        private void DownloadTemplate()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files|*.csv",
                FileName = "Template_Entity.csv"
            };

            if (dialog.ShowDialog() != true) return;
            FileService.BuatTemplateCsv(dialog.FileName);
            MessageBox.Show($"Template CSV berhasil dibuat:\n{dialog.FileName}", "Sukses",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ─── Reset ────────────────────────────────────────────────
        [RelayCommand]
        private void Reset()
        {
            _listA.Clear();
            _listB.Clear();
            _allResults.Clear();
            HasilList.Clear();
            JumlahA = "0 titik";
            JumlahB = "0 titik";
            JumlahDalam = "-";
            JumlahLuar = "-";
            StatusBar = "Reset. Silakan import ulang data.";
            _layerA.Clear();
            _layerB.Clear();
            _layerRadius.Clear();
            MapControl.RefreshData();
        }

        // ─── Map Markers ─────────────────────────────────────────
        private const int MaxMarker = 5000; // max marker di peta

        private void RefreshMap()
        {
            _layerA.Clear();
            _layerB.Clear();

            // Tampilkan sample MERATA jika data > MaxMarker
            AddToLayer(_layerA, _listA, Color.FromArgb(200, 30, 144, 255));
            AddToLayer(_layerB, _listB, Color.FromArgb(200, 255, 140, 0));

            MapControl.RefreshData();

            // Navigasi ke tengah data (bukan hanya titik pertama)
            NavigateToCenter();

            int shownA = Math.Min(_listA.Count, MaxMarker);
            int shownB = Math.Min(_listB.Count, MaxMarker);
            if (_listA.Count > MaxMarker || _listB.Count > MaxMarker)
                StatusBar = $"⚠️ Peta: sample {shownA:N0}/{_listA.Count:N0} titik A, {shownB:N0}/{_listB.Count:N0} titik B. Data LENGKAP tetap diproses.";
        }

        private void AddToLayer(WritableLayer layer, List<EntityPoint> list, Color fill)
        {
            if (list.Count == 0) return;

            // Ambil sample merata jika melebihi MaxMarker
            int step = list.Count > MaxMarker ? list.Count / MaxMarker : 1;
            for (int i = 0; i < list.Count; i += step)
                layer.Add(MakeFeature(list[i], fill));
        }

        private void NavigateToCenter()
        {
            var all = _listA.Count > 0 ? _listA : _listB;
            if (all.Count == 0) return;

            // Hitung pusat bounding box semua data
            double minLat = all.Min(p => p.Latitude);
            double maxLat = all.Max(p => p.Latitude);
            double minLon = all.Min(p => p.Longitude);
            double maxLon = all.Max(p => p.Longitude);

            double centerLat = (minLat + maxLat) / 2;
            double centerLon = (minLon + maxLon) / 2;

            NavigateToCallback?.Invoke(centerLat, centerLon, 10);
        }

        private static PointFeature MakeFeature(EntityPoint p, Color fill)
        {
            // SphericalMercator.FromLonLat = konversi WGS84 → EPSG:3857 (benar untuk Mapsui)
            var (x, y)  = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
            var feature = new PointFeature(new MPoint(x, y));
            feature["name"] = p.Name;
            feature.Styles.Add(new SymbolStyle
            {
                Fill        = new Brush(fill),
                Outline     = new Pen(Color.White, 2),
                SymbolScale = 0.6
            });
            return feature;
        }

        // ─── Helper: File Dialog ──────────────────────────────────
        private static string? BukaFileDialog(string title)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = title,
                Filter = "CSV / Excel|*.csv;*.xlsx|CSV|*.csv|Excel|*.xlsx"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }
}
