"""
PVC Vision Recognition Bridge for A.R.C. Frame
Uses local FreeLLMAPI (http://127.0.0.1:31415/v1) to parse measurer sketches into structured JSON.
"""

import sys
import os
import json
import base64
import urllib.request
import sqlite3

# Ensure stdout and stderr always output UTF-8 so Russian text is never corrupted
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass


def get_freellmapi_api_key():
    """Retrieve unified API key from local FreeLLMAPI database if available."""
    db_path = os.path.expandvars(r"%APPDATA%\FreeLLMAPI\freeapi.db")
    if os.path.exists(db_path):
        try:
            con = sqlite3.connect(db_path)
            cur = con.cursor()
            row = cur.execute("SELECT value FROM settings WHERE key='unified_api_key';").fetchone()
            if row and row[0]:
                return row[0]
        except Exception:
            pass
    return "freellmapi-779e32050476fa3df7bdf534d37f19cd180dcbb38010cee6"

PROMPT = """Ты — эксперт оконного производства. Расшифруй чертёж окна из блокнота замерщика по правилам:
1. Адрес — разделитель заказа (всё ВЫШЕ адреса игнорировать).
2. Прочитай адрес (улица, дом/кв).
3. Размеры рамы: ширина L и высота H.
4. Конфигурация: деление на 2 части (левая глухая, правая поворотно-откидная створка).
5. Знак '#' в створке означает москитную сетку Anwis.
6. 'П' = подоконник (ширина x длина).
7. 'О' = отлив (ширина). Длина отлива = ширина рамы + 140 мм.
8. Монтаж 2365 руб/м2, демонтаж 100 руб/м.п., скидка №1 = 20%.

Верни ТОЛЬКО валидный JSON со структурой:
{
  "address": "...",
  "frame": {
    "width_mm": 1060,
    "height_mm": 1340
  },
  "configuration": {
    "left_part": "fixed",
    "right_part": "tilt_and_turn",
    "mosquito_net": "Anwis"
  },
  "accessories": {
    "windowsill": {
      "width_mm": 400,
      "length_mm": 1350
    },
    "flashing": {
      "width_mm": 130,
      "length_mm": 1200
    }
  },
  "services_and_discounts": {
    "installation_rate_rub_m2": 2365,
    "demolition_rate_rub_m_p": 100,
    "discount_1": "20%"
  }
}
"""

def parse_window_image(image_path: str, endpoint: str = "http://127.0.0.1:31415/v1", model: str = "auto") -> dict:
    if not os.path.exists(image_path):
        return {"error": f"Image file not found: {image_path}"}

    with open(image_path, "rb") as f:
        img_b64 = base64.b64encode(f.read()).decode("utf-8")

    api_key = get_freellmapi_api_key()

    payload = {
        "model": model,
        "messages": [
            {
                "role": "user",
                "content": [
                    {"type": "text", "text": PROMPT},
                    {
                        "type": "image_url",
                        "image_url": {
                            "url": f"data:image/jpeg;base64,{img_b64}"
                        }
                    }
                ]
            }
        ],
        "temperature": 0.1
    }

    url = f"{endpoint.rstrip('/')}/chat/completions"
    headers = {
        "Content-Type": "application/json",
        "Authorization": f"Bearer {api_key}"
    }

    req = urllib.request.Request(url, data=json.dumps(payload).encode("utf-8"), headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=40) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            raw_content = data["choices"][0]["message"]["content"].strip()
            # Clean markdown code blocks if present
            if raw_content.startswith("```"):
                lines = raw_content.splitlines()
                if lines[0].startswith("```"):
                    lines = lines[1:]
                if lines and lines[-1].startswith("```"):
                    lines = lines[:-1]
                raw_content = "\n".join(lines).strip()
            parsed = json.loads(raw_content)
            return {"success": True, "data": parsed}
    except Exception as e:
        return {"success": False, "error": str(e)}

if __name__ == "__main__":
    if len(sys.argv) > 1:
        img = sys.argv[1]
    else:
        img = r"C:/Users/Asus/.gemini/antigravity/brain/7de99700-a479-4519-b9b2-0a93c0e60d61/.user_uploaded/media_1793868413848_43fb19de.jpg"
    res = parse_window_image(img)
    print(json.dumps(res, ensure_ascii=False, indent=2))
