# -*- coding: utf-8 -*-
import pathlib

p = pathlib.Path('native-print-spec.md')
text = p.read_text(encoding='utf-8')

# Use chr(96) for backtick to avoid heredoc parsing issues
BT = chr(96)  # backtick `

old4 = "| 4 | " + chr(0x2705) + " FlowDocument содержит те же данные, что и текущий HTML |"
new4 = ("| 4 | " + chr(0x2705) + " FlowDocument содержит те же данные, что и текущий HTML \u2014 must pass "
        + BT + "DataFidelityTests.BuildFlowDocument_Golden_*" + BT + " (\u00a711.4): "
        "\u043e\u0434\u0438\u043d \u043a\u0430\u043d\u043e\u043d\u0438\u0447\u0435\u0441\u043a\u0438\u0439 dataset \u0438\u0441\u043f\u043e\u043b\u044c\u0437\u0443\u0435\u0442\u0441\u044f "
        "\u0438 \u0432 HTML-\u0442\u0435\u0441\u0442\u0430\u0445, \u0438 \u0432 FlowDocument-\u0442\u0435\u0441\u0442\u0430\u0445, \u0430 "
        + BT + "FlowDocumentTextExtractor.ExtractText(doc)" + BT + " \u0441\u0440\u0430\u0432\u043d\u0438\u0432\u0430\u0435\u0442\u0441\u044f "
        "\u0441 \u044d\u0442\u0430\u043b\u043e\u043d\u043d\u044b\u043c\u0438 \u043f\u043e\u0434\u0441\u0442\u0440\u043e\u043a\u0430\u043c\u0438. "
        "\u0420\u0435\u0433\u0440\u0435\u0441\u0441\u0438\u044f \u0432 HTML-\u0444\u043e\u0440\u043c\u0430\u0442\u0435 \u0441\u0440\u0430\u0437\u0443 \u043f\u0430\u0434\u0430\u0435\u0442 \u043e\u0431\u0430 \u0442\u0435\u0441\u0442\u0430. |")

old5 = "| 5 | " + chr(0x2705) + " \u0427\u0435\u0440\u0442\u0451\u0436\u0438 \u0438\u0434\u0435\u043d\u0442\u0438\u0447\u043d\u044b SVG-\u0432\u0435\u0440\u0441\u0438\u0438 (\u0432\u0438\u0437\u0443\u0430\u043b\u044c\u043d\u0430\u044f \u043f\u0441\u043e\u0432\u0435\u0440\u043a\u0430) |"
new5 = ("| 5 | " + chr(0x2705) + " \u0427\u0435\u0440\u0442\u0451\u0436\u0438 \u0438\u0434\u0435\u043d\u0442\u0438\u0447\u043d\u044b SVG-\u0432\u0435\u0440\u0441\u0438\u0438 \u2014 double-check: "
        "\u0430\u0432\u0442\u043e\u043c\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438\u0439 " + BT + "DrawingsTest.GetDrawingImage_AllProducts_NotNull" + BT + " + bbox>0 + "
        "\u0440\u0443\u0447\u043d\u043e\u0439 side-by-side \u043f\u0440\u0438 \u0428\u0430\u0433\u0435 9 \u0432\u0435\u0440\u0438\u0444\u0438\u043a\u0430\u0446\u0438\u0438 (PNG \u043f\u0435\u0447\u0430\u0442\u044c + image-diff \u0432 Paint.NET / WinMerge). "
        "\u0410\u0432\u0442\u043e\u0442\u0435\u0441\u0442\u0430 \u043e\u0434\u043d\u043e\u0439 bbox \u043d\u0435\u0434\u043e\u0441\u0442\u0430\u0442\u043e\u0447\u043d\u043e \u2014 \u043d\u0443\u0436\u043d\u043e "
        "\u0447\u0435\u043b\u043e\u0432\u0435\u0447\u0435\u0441\u043a\u043e\u0435 \u043f\u043e\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0435\u043d\u0438\u0435 \u043f\u0440\u043e\u043f\u043e\u0440\u0446\u0438\u0439 / \u0442\u043e\u043b\u0449\u0438\u043d\u044b \u043b\u0438\u043d\u0438\u0439 / dash-patterns. |")

n4 = text.count(old4)
n5 = text.count(old5)
print(f"occurrences BEFORE: fac4={n4}, fac5={n5}")

if n4 != 1 or n5 != 1:
    raise SystemExit(f"Expected exactly 1 each, got fac4={n4} fac5={n5} -- ABORT, do NOT write.")

text = text.replace(old4, new4).replace(old5, new5)
p.write_text(text, encoding='utf-8')
print("written OK; new occurrences AFTER:")
print("  fac4:", text.count(new4))
print("  fac5:", text.count(new5))

# Cleanup
pathlib.Path('fix_spec.py').unlink()
print("cleaned up fix_spec.py")
