using GISSpatialChecker.ViewModels;
using Mapsui.Projections;
using System.Windows;

namespace GISSpatialChecker.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel();

            // Set Map langsung ke MapControl (tidak bisa via binding di Mapsui 5.x)
            MapView.Map = vm.MapControl;

            // Wire navigasi peta dari ViewModel ke MapControl
            vm.NavigateToCallback = (lat, lon, zoomLevel) =>
            {
                var (x, y) = SphericalMercator.FromLonLat(lon, lat);
                MapView.Map.Navigator.CenterOnAndZoomTo(
                    new Mapsui.MPoint(x, y),
                    MapView.Map.Navigator.Resolutions[zoomLevel]);
            };

            DataContext = vm;

            // Set posisi awal peta ke Surabaya setelah window load
            MapView.Loaded += (s, e) =>
            {
                var (x, y) = SphericalMercator.FromLonLat(112.7508, -7.2575);
                MapView.Map.Navigator.CenterOnAndZoomTo(
                    new Mapsui.MPoint(x, y),
                    MapView.Map.Navigator.Resolutions[14]);
            };
        }
    }
}

