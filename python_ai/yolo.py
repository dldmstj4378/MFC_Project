# train_yolo_can_multi.py
# 목적: Roboflow YOLO 포맷 데이터셋으로 Ultralytics YOLO를 "파이썬 코드"로 학습/검증/예측까지
#       5회 자동 반복 실행(각 런별 고유 run name, 결과 요약 포함)

import os
import datetime as dt
import traceback
import torch
from ultralytics import YOLO

# ===== ① 경로/하이퍼 파라미터 설정 =====
# data.yaml의 "실제 절대경로"를 넣으세요.
DATA_YAML = r"C:\Users\evita60\Downloads\can2.v9-can2.yolov8\data.yaml"  # ← 본인 경로 유지
MODEL      = "yolov8m.pt"     # 정확도 더 원하면 'yolov8s.pt' 또는 'yolov8m.pt'
IMGSZ      = 640              # 소형 결함이면 800~960도 고려
EPOCHS     = 100
BATCH      = 16               # GPU VRAM에 맞게 8~32로 조절
SEED_BASE  = 42               # 각 런마다 +i 해서 변형
PATIENCE   = 30               # Early Stopping
COS_LR     = True
WORKERS    = 0                # Windows DataLoader 충돌 회피 권장
DEVICE     = 0 if torch.cuda.is_available() else "cpu"  # GPU 있으면 0, 없으면 "cpu"

PROJECT = "runs"              # Ultralytics 기본 아웃풋 루트
RUN_TS  = dt.datetime.now().strftime("%Y%m%d_%H%M%S")  # 공통 타임스탬프

print(f"[BOOT] Torch CUDA available: {torch.cuda.is_available()}")
if torch.cuda.is_available():
    try:
        print(f"[BOOT] CUDA device[0]: {torch.cuda.get_device_name(0)}")
    except Exception:
        pass
print(f"[BOOT] Using DEVICE = {DEVICE}")
print(f"[BOOT] DATA_YAML = {DATA_YAML}")

# ===== ② 5회 자동 반복 실행 =====
N_RUNS = 5
summary = []  # (run_name, mAP5095, save_dir, best_w, pred_dir)

for i in range(1, N_RUNS + 1):
    run_suffix = f"run{i}"
    run_name   = f"can_train_{RUN_TS}_{run_suffix}"

    # 시드는 미세하게 변형(재현성과 분산 확보)
    seed_i = SEED_BASE + i

    print("\n" + "=" * 80)
    print(f"[RUN] START {run_name} | seed={seed_i} epochs={EPOCHS} batch={BATCH} imgsz={IMGSZ}")
    print("=" * 80)

    try:
        # --- 모델 로드 ---
        model = YOLO(MODEL)

        # --- 학습 ---
        print("[INFO] Training start ...")
        train_results = model.train(
            data=DATA_YAML,
            imgsz=IMGSZ,
            epochs=EPOCHS,
            batch=BATCH,
            device=DEVICE,
            workers=WORKERS,
            project=PROJECT,
            name=run_name,
            seed=seed_i,
            patience=PATIENCE,
            cos_lr=COS_LR,
            exist_ok=True,   # 동일 이름 디렉토리가 있어도 덮어쓰기 허용
        )

        save_dir = train_results.save_dir  # e.g., runs/detect/<run_name>
        print(f"[INFO] Train artifacts saved to: {save_dir}")

        # --- 검증 ---
        print("[INFO] Validation ...")
        val_metrics = model.val(
            data=DATA_YAML,
            imgsz=IMGSZ,
            device=DEVICE,
            project=PROJECT,
            name=f"{run_name}_val",
        )

        # 주요 지표 추출(mAP50-95)
        map5095 = None
        try:
            # Ultralytics 8.x results_dict 키는 버전에 따라 달라질 수 있어 안전하게 처리
            rd = getattr(val_metrics, "results_dict", {}) or {}
            # 통상 키: 'metrics/mAP50-95(B)' 또는 'metrics/mAP50-95'
            map5095 = float(
                rd.get("metrics/mAP50-95(B)", rd.get("metrics/mAP50-95", 0.0))
            )
            print(f"[METRICS] mAP50-95 = {map5095:.4f}")
        except Exception:
            print("[WARN] Could not parse mAP50-95 from results_dict.")

        # --- 최적 가중치로 예측(샘플 확인) ---
        best_w = os.path.join(save_dir, "weights", "best.pt")
        print(f"[INFO] Using best weights: {best_w}")
        best_model = YOLO(best_w)

        data_root = os.path.dirname(DATA_YAML)
        valid_images = os.path.join(data_root, "valid", "images")  # Roboflow 기본 구조
        print(f"[INFO] Predict source: {valid_images}")

        pred_results = best_model.predict(
            source=valid_images,
            imgsz=IMGSZ,
            conf=0.5,                 # 초기 임계치. 필요시 0.25~0.6 조정
            device=DEVICE,
            project=PROJECT,
            name=f"{run_name}_pred",
            save=True,
        )

        pred_dir = (
            pred_results[0].save_dir
            if isinstance(pred_results, list) and len(pred_results) > 0
            else os.path.join(PROJECT, "detect", f"{run_name}_pred")
        )

        # --- 요약 저장 ---
        summary.append((run_name, map5095, save_dir, best_w, pred_dir))
        print(f"[OK] RUN DONE: {run_name}")

    except KeyboardInterrupt:
        print("\n[STOP] 사용자가 중단(Ctrl+C). 마지막 체크포인트는 각 run 폴더의 weights/last.pt 에 있습니다.")
        break
    except Exception as e:
        print(f"[ERROR] {run_name} 실패: {e}")
        traceback.print_exc()
        # 실패 run도 summary에 남겨두면 사후 점검에 용이
        summary.append((run_name, None, None, None, None))
        # 다음 런으로 계속

# ===== ③ 전체 요약 출력 =====
print("\n" + "=" * 80)
print("[SUMMARY] 5회 자동 반복 결과 요약 (mAP50-95, best.pt, pred_dir)")
print("=" * 80)
for run_name, map5095, save_dir, best_w, pred_dir in summary:
    print(f"{run_name:35s} | mAP50-95: {map5095} | save: {save_dir} | best: {best_w} | pred: {pred_dir}")

print("\n[DONE] 모든 반복 학습이 종료되었습니다.")
