# Teste descartável: guarda o JSON do stdin e mostra os limites do plano.
import sys, json, os
raw = sys.stdin.read()
here = os.path.dirname(os.path.abspath(__file__))
open(os.path.join(here, "statusline-capture.json"), "w", encoding="utf-8").write(raw)
try:
    rl = json.loads(raw).get("rate_limits") or {}
    f = (rl.get("five_hour") or {}).get("used_percentage")
    w = (rl.get("seven_day") or {}).get("used_percentage")
    print(f"5h: {f}% | 7d: {w}%" if f is not None or w is not None else "sem rate_limits ainda")
except Exception as e:
    print("erro:", e)
