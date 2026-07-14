using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Single Source of Truth for product drawings (SVG strings + WPF geometry).
    /// Each product has two flavors of the SAME drawing:
    /// 1. SVG string, consumed by QuestPDF via canvas.Svg(svgString)
    ///    ⇒ pure vector PDF, infinite zoom on any printer.
    /// 2. WPF DrawingImage, consumed by FlowDocument preview cells.
    /// </summary>
    public static class DrawingService
    {
        /// <summary>
        /// SVG string for the product drawing, sized at 100×54 (or 100×36
        /// for cross-sections, 40×20 for text-only). Used by QuestPDF.
        /// </summary>
        public static string GetDrawingSvg(string name, double width, double height)
            => name switch
            {
                "Anwis" => SvgAnwis(width, height),
                "Дверная сетка" => SvgDvernayaSetka(width, height),
                "На навесах" => SvgNaNavesah(width, height),
                "Отлив" => SvgOtliv(width, height),
                "Козырёк" => SvgKozyrek(width, height),
                "Короб" => SvgKorob(width, height),
                "ПСУЛ" => SvgPsul(width, height),
                "Откос" => SvgOtkos(width, height),
                "Работа за откос" => SvgTextOnly("раб.отк"),
                "Работа" => SvgTextOnly("раб."),
                "Брус" => SvgTextOnly("брус"),
                "Пояс" => SvgTextOnly("пояс"),
                "Доставка" => SvgTextOnly("дост."),
                "Уплотнение" => SvgTextOnly("уплотн."),
                _ => SvgFallback(width, height)
            };

        /// <summary>WPF DrawingImage cells for FlowDocument preview.</summary>
        public static DrawingImage GetDrawingImage(string name, double width, double height)
        {
            var size = name switch
            {
                "Работа" or "Брус" or "Пояс" or "Доставка" or "Уплотнение" => new Size(40, 20),
                "Отлив" or "Откос" => new Size(100, 36),
                _ => new Size(100, 54)
            };
            return name switch
            {
                "Anwis" => BuildAnwis(size, width, height),
                "Дверная сетка" => BuildDvernayaSetka(size, width, height),
                "На навесах" => BuildNaNavesah(size, width, height),
                "Отлив" => BuildOtliv(size, width, height),
                "Козырёк" => BuildKozyrek(size, width, height),
                "Короб" => BuildKorob(size, width, height),
                "ПСУЛ" => BuildPsul(size, width, height),
                "Откос" => BuildOtkos(size, width, height),
                "Работа" => BuildTextOnly(size, "раб."),
                "Брус" => BuildTextOnly(size, "брус"),
                "Пояс" => BuildTextOnly(size, "пояс"),
                "Доставка" => BuildTextOnly(size, "дост."),
                "Уплотнение" => BuildTextOnly(size, "уплотн."),
                _ => BuildFallback(size, width, height)
            };
        }

        /// <summary>
        /// Гарантирует, что content центрируется по вертикали и горизонтали
        /// внутри всей доступной площади ячейки.
        /// </summary>
        public static UIElement WrapForCentering(UIElement content, double minHeight = 0)
        {
            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = minHeight
            };
            grid.Children.Add(content);
            return grid;
        }

        /// <summary>
        /// Builds a WPF Image для ячейки таблицы чертежа.
        /// </summary>
        public static Image CreateDrawingImageElement(string name, double width, double height, double displayWidth = 70)
        {
            var img = new Image
            {
                Source = GetDrawingImage(name, width, height),
                Width = displayWidth,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            // #hotfix: EdgeMode.Unspecified — на экране (96 DPI) сглаживание
            // делает линии плавными; Aliased давал видимую «лесенку» в превью.
            // Для реальной печати (600 DPI) вызывающий код (PrintPreviewControl)
            // перед SendToQueue временно меняет на Aliased — сохраняется резкость.
            RenderOptions.SetEdgeMode(img, EdgeMode.Unspecified);
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        // ═══════════════════════════════════════════════════════════════
        // SVG STRINGS — fed to QuestPDF Canvas.Svg(...)
        // ═══════════════════════════════════════════════════════════════

        private static string Num(double v) => v.ToString("F0", CultureInfo.InvariantCulture);

        private static string SvgAnwis(double w, double h) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='54' viewBox='0 0 100 54'>
  <rect x='12' y='4' width='76' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1.2' rx='1'/>
  <rect x='16' y='8' width='68' height='32' fill='none' stroke='#aaa' stroke-width='0.5' rx='0.5' stroke-dasharray='2,1.5'/>
  <rect x='12' y='4' width='8' height='4' fill='#ddd' stroke='#444' stroke-width='0.6'/>
  <rect x='80' y='4' width='8' height='4' fill='#ddd' stroke='#444' stroke-width='0.6'/>
  <rect x='12' y='40' width='8' height='4' fill='#ddd' stroke='#444' stroke-width='0.6'/>
  <rect x='80' y='40' width='8' height='4' fill='#ddd' stroke='#444' stroke-width='0.6'/>
  <line x1='12' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
  <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{Num(w)}</text>
  <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial' transform='rotate(-90,6,26)'>{Num(h)}</text>
  <text x='50' y='26' font-size='6' text-anchor='middle' fill='#555' font-family='Arial'>Anwis</text>
</svg>";

        private static string SvgDvernayaSetka(double w, double h) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='54' viewBox='0 0 100 54'>
  <rect x='16' y='4' width='72' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1' rx='1.5'/>
  <rect x='20' y='8' width='64' height='32' fill='none' stroke='#aaa' stroke-width='0.5' rx='0.5' stroke-dasharray='2,1.5'/>
  <circle cx='14' cy='10' r='2.5' fill='#ccc' stroke='#444' stroke-width='0.7'/>
  <circle cx='14' cy='38' r='2.5' fill='#ccc' stroke='#444' stroke-width='0.7'/>
  <line x1='14' y1='12' x2='14' y2='36' stroke='#666' stroke-width='0.6' stroke-dasharray='3,2'/>
  <line x1='16' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
  <text x='52' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{Num(w)}</text>
  <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial' transform='rotate(-90,6,26)'>{Num(h)}</text>
  <text x='52' y='26' font-size='5.5' text-anchor='middle' fill='#555' font-family='Arial'>двер.сетка</text>
</svg>";

        private static string SvgNaNavesah(double w, double h) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='54' viewBox='0 0 100 54'>
  <rect x='16' y='4' width='72' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1' rx='1.5'/>
  <rect x='20' y='8' width='64' height='32' fill='none' stroke='#aaa' stroke-width='0.5' rx='0.5' stroke-dasharray='2,1.5'/>
  <circle cx='14' cy='10' r='2.5' fill='#ccc' stroke='#444' stroke-width='0.7'/>
  <circle cx='14' cy='38' r='2.5' fill='#ccc' stroke='#444' stroke-width='0.7'/>
  <line x1='14' y1='12' x2='14' y2='36' stroke='#666' stroke-width='0.6' stroke-dasharray='3,2'/>
  <line x1='16' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
  <text x='52' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{Num(w)}</text>
  <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial' transform='rotate(-90,6,26)'>{Num(h)}</text>
  <text x='52' y='26' font-size='5.5' text-anchor='middle' fill='#555' font-family='Arial'>навесы</text>
</svg>";

        private static string SvgOtliv(double w, double h) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='36' viewBox='0 0 100 36'>
  <path d='M10,8 L90,8 L90,14 L70,14 L70,28 L30,28 L30,14 L10,14 Z' fill='#f0f0f0' stroke='#444' stroke-width='1' stroke-linejoin='round'/>
  <line x1='10' y1='30' x2='90' y2='30' stroke='#555' stroke-width='0.5'/>
  <text x='50' y='35' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{Num(w)}</text>
  <line x1='5' y1='8' x2='5' y2='28' stroke='#555' stroke-width='0.5'/>
  <text x='4' y='20' font-size='6' text-anchor='middle' fill='#333' font-family='Arial' transform='rotate(-90,4,20)'>{Num(h)}</text>
</svg>";

        private static string SvgKozyrek(double w, double h) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='54' viewBox='0 0 100 54'>
  <rect x='12' y='4' width='76' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1' rx='1.5'/>
  <line x1='12' y1='10' x2='88' y2='10' stroke='#999' stroke-width='0.5' stroke-dasharray='3,2'/>
  <line x1='12' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
  <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{Num(w)}</text>
  <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial' transform='rotate(-90,6,26)'>{Num(h)}</text>
  <text x='50' y='28' font-size='6.5' text-anchor='middle' fill='#555' font-family='Arial'>козырёк</text>
</svg>";

        private static string SvgKorob(double w, double h) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='54' viewBox='0 0 100 54'>
  <rect x='12' y='4' width='76' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1.5' rx='1'/>
  <rect x='16' y='8' width='68' height='32' fill='none' stroke='#888' stroke-width='1.2' rx='0.5'/>
  <line x1='12' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
  <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{Num(w)}</text>
  <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial' transform='rotate(-90,6,26)'>{Num(h)}</text>
  <text x='50' y='26' font-size='7' text-anchor='middle' fill='#444' font-family='Arial' font-weight='500'>короб</text>
</svg>";

        private static string SvgPsul(double w, double h) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='54' viewBox='0 0 100 54'>
  <rect x='10' y='2' width='80' height='44' fill='none' stroke='#444' stroke-width='3' rx='1'/>
  <rect x='16' y='8' width='68' height='32' fill='#f5f5f5' stroke='#999' stroke-width='0.5' rx='0.5' stroke-dasharray='2,1.5'/>
  <line x1='10' y1='51' x2='90' y2='51' stroke='#555' stroke-width='0.5'/>
  <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{Num(w)}</text>
  <text x='4' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial' transform='rotate(-90,4,26)'>{Num(h)}</text>
  <text x='50' y='26' font-size='5.5' text-anchor='middle' fill='#555' font-family='Arial'>ПСУЛ</text>
</svg>";

        private static string SvgOtkos(double w, double h) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='36' viewBox='0 0 100 36'>
  <rect x='10' y='4' width='80' height='28' fill='#f5f5f5' stroke='#444' stroke-width='1' rx='1'/>
  <line x1='10' y1='4' x2='20' y2='12' stroke='#666' stroke-width='0.8'/>
  <line x1='90' y1='4' x2='80' y2='12' stroke='#666' stroke-width='0.8'/>
  <line x1='10' y1='32' x2='20' y2='24' stroke='#666' stroke-width='0.8'/>
  <line x1='90' y1='32' x2='80' y2='24' stroke='#666' stroke-width='0.8'/>
  <line x1='10' y1='33' x2='90' y2='33' stroke='#555' stroke-width='0.5'/>
  <text x='50' y='35' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{Num(w)}</text>
  <text x='4' y='20' font-size='6' text-anchor='middle' fill='#333' font-family='Arial' transform='rotate(-90,4,20)'>{Num(h)}</text>
  <text x='50' y='20' font-size='5.5' text-anchor='middle' fill='#555' font-family='Arial'>откос</text>
</svg>";

        private static string SvgTextOnly(string label) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20' viewBox='0 0 40 20'>
  <text x='20' y='14' font-size='8' text-anchor='middle' fill='#666' font-family='Arial'>{label}</text>
</svg>";

        private static string SvgFallback(double w, double h) => $@"
<svg xmlns='http://www.w3.org/2000/svg' width='100' height='54' viewBox='0 0 100 54'>
  <rect x='12' y='4' width='76' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1' rx='1.5'/>
  <rect x='16' y='8' width='68' height='32' fill='none' stroke='#aaa' stroke-width='0.4' rx='0.5' stroke-dasharray='3,2'/>
  <line x1='12' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
  <text x='50' y='52' font-size='6.5' text-anchor='middle' fill='#333' font-family='Arial'>{Num(w)}</text>
  <text x='6' y='26' font-size='6' text-anchor='middle' fill='#333' font-family='Arial' transform='rotate(-90,6,26)'>{Num(h)}</text>
</svg>";

        // ═══════════════════════════════════════════════════════════════
        // WPF DrawingImage BUILDERS — fed into FlowDocument preview cells
        // ═══════════════════════════════════════════════════════════════

        private static Brush B(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        private static Pen P(byte r, byte g, byte b, double t) => new Pen(B(r, g, b), t);
        private static GeometryDrawing FillStroke(byte r, byte g, byte b, Pen? pen, Geometry geo)
            => new GeometryDrawing(B(r, g, b), pen, geo);

        private static DrawingImage BuildAnwis(Size size, double w, double h)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(FillStroke(0xF5, 0xF5, 0xF5, P(0x44, 0x44, 0x44, 1.2),
                new RectangleGeometry(new Rect(12, 4, 76, 40), 1, 1)));
            var dashedPen = P(0xAA, 0xAA, 0xAA, 0.5);
            dashedPen.DashStyle = new DashStyle(new[] { 2.0, 1.5 }, 0);
            dg.Children.Add(new GeometryDrawing(null, dashedPen,
                new RectangleGeometry(new Rect(16, 8, 68, 32), 0.5, 0.5)));
            foreach (var pt in new[] { (12.0, 4.0), (80.0, 4.0), (12.0, 40.0), (80.0, 40.0) })
                dg.Children.Add(FillStroke(0xDD, 0xDD, 0xDD, P(0x44, 0x44, 0x44, 0.6),
                    new RectangleGeometry(new Rect(pt.Item1, pt.Item2, 8, 4))));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(12, 49), new Point(88, 49))));
            dg.Children.Add(Text(Num(w), 6.5, B(0x33, 0x33, 0x33), new Point(50, 46)));
            dg.Children.Add(Text(Num(h), 6, B(0x33, 0x33, 0x33), new Point(6, 26), rotated: true));
            dg.Children.Add(Text("Anwis", 6, B(0x55, 0x55, 0x55), new Point(50, 25)));
            return ToImage(dg, size);
        }

        private static DrawingImage BuildDvernayaSetka(Size size, double w, double h)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(FillStroke(0xF5, 0xF5, 0xF5, P(0x44, 0x44, 0x44, 1),
                new RectangleGeometry(new Rect(16, 4, 72, 40), 1.5, 1.5)));
            var dashedPen = P(0xAA, 0xAA, 0xAA, 0.5);
            dashedPen.DashStyle = new DashStyle(new[] { 2.0, 1.5 }, 0);
            dg.Children.Add(new GeometryDrawing(null, dashedPen,
                new RectangleGeometry(new Rect(20, 8, 64, 32), 0.5, 0.5)));
            dg.Children.Add(FillStroke(0xCC, 0xCC, 0xCC, P(0x44, 0x44, 0x44, 0.7),
                new EllipseGeometry(new Point(14, 10), 2.5, 2.5)));
            dg.Children.Add(FillStroke(0xCC, 0xCC, 0xCC, P(0x44, 0x44, 0x44, 0.7),
                new EllipseGeometry(new Point(14, 38), 2.5, 2.5)));
            var hingePen = P(0x66, 0x66, 0x66, 0.6);
            hingePen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
            dg.Children.Add(new GeometryDrawing(null, hingePen,
                new LineGeometry(new Point(14, 12), new Point(14, 36))));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(16, 49), new Point(88, 49))));
            dg.Children.Add(Text(Num(w), 6.5, B(0x33, 0x33, 0x33), new Point(52, 46)));
            dg.Children.Add(Text(Num(h), 6, B(0x33, 0x33, 0x33), new Point(6, 26), rotated: true));
            dg.Children.Add(Text("двер.сетка", 5.5, B(0x55, 0x55, 0x55), new Point(52, 25)));
            return ToImage(dg, size);
        }

        private static DrawingImage BuildNaNavesah(Size size, double w, double h)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(FillStroke(0xF5, 0xF5, 0xF5, P(0x44, 0x44, 0x44, 1),
                new RectangleGeometry(new Rect(16, 4, 72, 40), 1.5, 1.5)));
            var dashedPen = P(0xAA, 0xAA, 0xAA, 0.5);
            dashedPen.DashStyle = new DashStyle(new[] { 2.0, 1.5 }, 0);
            dg.Children.Add(new GeometryDrawing(null, dashedPen,
                new RectangleGeometry(new Rect(20, 8, 64, 32), 0.5, 0.5)));
            dg.Children.Add(FillStroke(0xCC, 0xCC, 0xCC, P(0x44, 0x44, 0x44, 0.7),
                new EllipseGeometry(new Point(14, 10), 2.5, 2.5)));
            dg.Children.Add(FillStroke(0xCC, 0xCC, 0xCC, P(0x44, 0x44, 0x44, 0.7),
                new EllipseGeometry(new Point(14, 38), 2.5, 2.5)));
            var hingePen = P(0x66, 0x66, 0x66, 0.6);
            hingePen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
            dg.Children.Add(new GeometryDrawing(null, hingePen,
                new LineGeometry(new Point(14, 12), new Point(14, 36))));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(16, 49), new Point(88, 49))));
            dg.Children.Add(Text(Num(w), 6.5, B(0x33, 0x33, 0x33), new Point(52, 46)));
            dg.Children.Add(Text(Num(h), 6, B(0x33, 0x33, 0x33), new Point(6, 26), rotated: true));
            dg.Children.Add(Text("навесы", 5.5, B(0x55, 0x55, 0x55), new Point(52, 25)));
            return ToImage(dg, size);
        }

        private static DrawingImage BuildOtliv(Size size, double w, double h)
        {
            var dg = new DrawingGroup();
            var path = "M10,8 L90,8 L90,14 L70,14 L70,28 L30,28 L30,14 L10,14 Z";
            dg.Children.Add(FillStroke(0xF0, 0xF0, 0xF0, P(0x44, 0x44, 0x44, 1),
                StreamGeom(path)));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(10, 30), new Point(90, 30))));
            dg.Children.Add(Text(Num(w), 6.5, B(0x33, 0x33, 0x33), new Point(50, 30)));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(5, 8), new Point(5, 28))));
            dg.Children.Add(Text(Num(h), 6, B(0x33, 0x33, 0x33), new Point(4, 20), rotated: true));
            return ToImage(dg, size);
        }

        private static DrawingImage BuildKozyrek(Size size, double w, double h)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(FillStroke(0xF5, 0xF5, 0xF5, P(0x44, 0x44, 0x44, 1),
                new RectangleGeometry(new Rect(12, 4, 76, 40), 1.5, 1.5)));
            var dashedPen = P(0x99, 0x99, 0x99, 0.5);
            dashedPen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
            dg.Children.Add(new GeometryDrawing(null, dashedPen,
                new LineGeometry(new Point(12, 10), new Point(88, 10))));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(12, 49), new Point(88, 49))));
            dg.Children.Add(Text(Num(w), 6.5, B(0x33, 0x33, 0x33), new Point(50, 46)));
            dg.Children.Add(Text(Num(h), 6, B(0x33, 0x33, 0x33), new Point(6, 26), rotated: true));
            dg.Children.Add(Text("козырёк", 6.5, B(0x55, 0x55, 0x55), new Point(50, 24)));
            return ToImage(dg, size);
        }

        private static DrawingImage BuildKorob(Size size, double w, double h)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(FillStroke(0xF5, 0xF5, 0xF5, P(0x44, 0x44, 0x44, 1.5),
                new RectangleGeometry(new Rect(12, 4, 76, 40), 1, 1)));
            dg.Children.Add(new GeometryDrawing(null, P(0x88, 0x88, 0x88, 1.2),
                new RectangleGeometry(new Rect(16, 8, 68, 32), 0.5, 0.5)));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(12, 49), new Point(88, 49))));
            dg.Children.Add(Text(Num(w), 6.5, B(0x33, 0x33, 0x33), new Point(50, 46)));
            dg.Children.Add(Text(Num(h), 6, B(0x33, 0x33, 0x33), new Point(6, 26), rotated: true));
            dg.Children.Add(Text("короб", 7, B(0x44, 0x44, 0x44), new Point(50, 24)));
            return ToImage(dg, size);
        }

        private static DrawingImage BuildPsul(Size size, double w, double h)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(null, P(0x44, 0x44, 0x44, 3),
                new RectangleGeometry(new Rect(10, 2, 80, 44), 1, 1)));
            var dashedPen = P(0x99, 0x99, 0x99, 0.5);
            dashedPen.DashStyle = new DashStyle(new[] { 2.0, 1.5 }, 0);
            dg.Children.Add(FillStroke(0xF5, 0xF5, 0xF5, null,
                new RectangleGeometry(new Rect(16, 8, 68, 32), 0.5, 0.5)));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(10, 51), new Point(90, 51))));
            dg.Children.Add(Text(Num(w), 6.5, B(0x33, 0x33, 0x33), new Point(50, 46)));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(5, 2), new Point(5, 46))));
            dg.Children.Add(Text(Num(h), 6, B(0x33, 0x33, 0x33), new Point(4, 26), rotated: true));
            dg.Children.Add(Text("ПСУЛ", 5.5, B(0x55, 0x55, 0x55), new Point(50, 25)));
            return ToImage(dg, size);
        }

        private static DrawingImage BuildOtkos(Size size, double w, double h)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(FillStroke(0xF5, 0xF5, 0xF5, P(0x44, 0x44, 0x44, 1),
                new RectangleGeometry(new Rect(10, 4, 80, 28), 1, 1)));
            var slopePen = P(0x66, 0x66, 0x66, 0.8);
            dg.Children.Add(new GeometryDrawing(null, slopePen, new LineGeometry(new Point(10, 4), new Point(20, 12))));
            dg.Children.Add(new GeometryDrawing(null, slopePen, new LineGeometry(new Point(90, 4), new Point(80, 12))));
            dg.Children.Add(new GeometryDrawing(null, slopePen, new LineGeometry(new Point(10, 32), new Point(20, 24))));
            dg.Children.Add(new GeometryDrawing(null, slopePen, new LineGeometry(new Point(90, 32), new Point(80, 24))));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(10, 33), new Point(90, 33))));
            dg.Children.Add(Text(Num(w), 6.5, B(0x33, 0x33, 0x33), new Point(50, 30)));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(5, 4), new Point(5, 32))));
            dg.Children.Add(Text(Num(h), 6, B(0x33, 0x33, 0x33), new Point(4, 20), rotated: true));
            dg.Children.Add(Text("откос", 5.5, B(0x55, 0x55, 0x55), new Point(50, 18)));
            return ToImage(dg, size);
        }

        private static DrawingImage BuildTextOnly(Size size, string label)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(Text(label, 8, B(0x66, 0x66, 0x66), new Point(20, 10)));
            return ToImage(dg, size);
        }

        private static DrawingImage BuildFallback(Size size, double w, double h)
        {
            var dg = new DrawingGroup();
            dg.Children.Add(FillStroke(0xF5, 0xF5, 0xF5, P(0x44, 0x44, 0x44, 1),
                new RectangleGeometry(new Rect(12, 4, 76, 40), 1.5, 1.5)));
            var dashedPen = P(0xAA, 0xAA, 0xAA, 0.4);
            dashedPen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
            dg.Children.Add(new GeometryDrawing(null, dashedPen,
                new RectangleGeometry(new Rect(16, 8, 68, 32), 0.5, 0.5)));
            dg.Children.Add(new GeometryDrawing(null, P(0x55, 0x55, 0x55, 0.5),
                new LineGeometry(new Point(12, 49), new Point(88, 49))));
            dg.Children.Add(Text(Num(w), 6.5, B(0x33, 0x33, 0x33), new Point(50, 46)));
            dg.Children.Add(Text(Num(h), 6, B(0x33, 0x33, 0x33), new Point(6, 26), rotated: true));
            return ToImage(dg, size);
        }

        private static Geometry StreamGeom(string path)
        {
            var sg = StreamGeometry.Parse(path);
            sg.Freeze();
            return sg;
        }

        private static GeometryDrawing Text(string s, double size, Brush brush, Point origin, bool rotated = false)
        {
            var typeface = new Typeface(
                new FontFamily("Segoe UI"),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);
            var ft = new FormattedText(
                s ?? string.Empty,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                brush,
                pixelsPerDip: 1.0);
            Geometry g = ft.BuildGeometry(new Point(0, 0)).Clone();
            if (rotated)
            {
                var tg = new TransformGroup();
                tg.Children.Add(new TranslateTransform(-origin.X, -origin.Y));
                tg.Children.Add(new RotateTransform(-90));
                tg.Children.Add(new TranslateTransform(origin.X, origin.Y));
                g.Transform = tg;
            }
            else
            {
                g.Transform = new TranslateTransform(origin.X, origin.Y);
            }
            return new GeometryDrawing(brush, (Pen?)null!, g);
        }

        private static DrawingImage ToImage(DrawingGroup dg, Size size)
        {
            _ = size;
            dg.Freeze();
            return new DrawingImage(dg);
        }
    }
}
