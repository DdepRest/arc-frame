"""
End-to-End Sketch to CCalc Pipeline
Orchestrates: Image -> Vision Recognition -> CCalc RPA Calculation -> A.R.C. Frame Result
"""

import sys
import os
import json
import argparse

# Ensure stdout and stderr always output UTF-8 so Russian text is never corrupted
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass

from pvc_vision import parse_window_image
from ccalc_bot import CCalcBot

def process_sketch(image_path: str, dry_run: bool = False, model: str = "auto", prog_path: str = "", user: str = "окно", password: str = "1234") -> dict:
    # 1. Vision recognition
    vision_res = parse_window_image(image_path, model=model)
    if not vision_res.get("success"):
        if dry_run:
            # Fallback deterministic test data for simulation/dry-run mode
            order_data = {
                "address": "Димитрова 45/46",
                "frame": {"width_mm": 1060, "height_mm": 1340},
                "configuration": {
                    "left_part": "fixed",
                    "right_part": "tilt_and_turn",
                    "mosquito_net": "Anwis"
                },
                "accessories": {
                    "windowsill": {"width_mm": 400, "length_mm": 1350},
                    "flashing": {"width_mm": 130, "length_mm": 1200}
                },
                "services_and_discounts": {
                    "installation_rate_rub_m2": 2365,
                    "demolition_rate_rub_m_p": 100,
                    "discount_1": "20%"
                }
            }
        else:
            return {
                "success": False,
                "stage": "vision",
                "error": vision_res.get("error", "Vision recognition failed")
            }
    else:
        order_data = vision_res["data"]

    # 2. CCalc Calculation
    bot = CCalcBot(dry_run=dry_run, prog_path=prog_path, username=user, password=password)
    calc_res = bot.run_calculation(order_data)
    if not calc_res.get("success"):
        return {
            "success": False,
            "stage": "ccalc",
            "error": calc_res.get("error", "CCalc calculation failed"),
            "recognized_data": order_data
        }

    return {
        "success": True,
        "recognized_data": order_data,
        "calculation": calc_res
    }

def main():
    parser = argparse.ArgumentParser(description="Process sketch image to CCalc order")
    parser.add_argument("image", type=str, help="Path to sketch image")
    parser.add_argument("--dry-run", action="store_true", help="Simulate CCalc without touching the UI")
    parser.add_argument("--model", type=str, default="auto", help="FreeLLMAPI model name")
    parser.add_argument("--prog-path", type=str, default="", help="Path to CCalc executable")
    parser.add_argument("--user", type=str, default="окно", help="CCalc login user")
    parser.add_argument("--password", type=str, default="1234", help="CCalc login password")
    args = parser.parse_args()

    res = process_sketch(args.image, dry_run=args.dry_run, model=args.model, prog_path=args.prog_path, user=args.user, password=args.password)
    print(json.dumps(res, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
