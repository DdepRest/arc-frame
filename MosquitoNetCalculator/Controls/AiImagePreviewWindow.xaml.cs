using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MosquitoNetCalculator.Controls
{
    public partial class AiImagePreviewWindow : Window
    {
        private Point _dragStart;
        private double _hOffsetStart;
        private double _vOffsetStart;
        private bool _isDragging;
        private double _zoom = 1.0;

        public AiImagePreviewWindow(ImageSource? image, string? fileName = null, string? sizeLabel = null)
        {
            InitializeComponent();
            SetImage(image, fileName, sizeLabel);
        }

        public AiImagePreviewWindow(string dataUrlOrBase64, string? fileName = null, string? sizeLabel = null)
        {
            InitializeComponent();
            var imgSource = LoadImageFromDataUrl(dataUrlOrBase64);
            SetImage(imgSource, fileName, sizeLabel);
        }

        private void SetImage(ImageSource? image, string? fileName, string? sizeLabel)
        {
            PreviewImage.Source = image;
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                TxtTitle.Text = fileName;
            }
            if (!string.IsNullOrWhiteSpace(sizeLabel))
            {
                TxtImageInfo.Text = $"Размер: {sizeLabel} · Масштаб колесиком мыши · Перетаскивание зажатой кнопкой мыши";
            }
        }

        private static ImageSource? LoadImageFromDataUrl(string dataUrlOrBase64)
        {
            if (string.IsNullOrWhiteSpace(dataUrlOrBase64)) return null;
            try
            {
                string b64 = dataUrlOrBase64;
                int commaIdx = b64.IndexOf(',');
                if (commaIdx >= 0)
                {
                    b64 = b64[(commaIdx + 1)..];
                }
                var bytes = Convert.FromBase64String(b64);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = new MemoryStream(bytes);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return null;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (e.Delta > 0)
            {
                Zoom(1.15);
            }
            else
            {
                Zoom(1.0 / 1.15);
            }
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(1.25);
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(0.8);
        private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            _zoom = 1.0;
            ApplyZoom();
        }

        private void Zoom(double factor)
        {
            _zoom = Math.Clamp(_zoom * factor, 0.25, 8.0);
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            ImageScale.ScaleX = _zoom;
            ImageScale.ScaleY = _zoom;
            TxtZoomPercent.Text = $"{Math.Round(_zoom * 100)}%";
        }

        private void ScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ImgScrollViewer.IsMouseOver)
            {
                _isDragging = true;
                _dragStart = e.GetPosition(this);
                _hOffsetStart = ImgScrollViewer.HorizontalOffset;
                _vOffsetStart = ImgScrollViewer.VerticalOffset;
                ImgScrollViewer.CaptureMouse();
                Cursor = Cursors.SizeAll;
            }
        }

        private void ScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var cur = e.GetPosition(this);
                double deltaX = cur.X - _dragStart.X;
                double deltaY = cur.Y - _dragStart.Y;
                ImgScrollViewer.ScrollToHorizontalOffset(_hOffsetStart - deltaX);
                ImgScrollViewer.ScrollToVerticalOffset(_vOffsetStart - deltaY);
            }
        }

        private void ScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ImgScrollViewer.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
            }
        }
    }
}
