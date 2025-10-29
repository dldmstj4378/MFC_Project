import socket
import struct
import json
import time
import threading
import cv2
import numpy as np
import torch
from ultralytics import YOLO

# ======================================
# YOLO 설정
# ======================================
DEVICE = 0 if torch.cuda.is_available() else "cpu"
YOLO_WEIGHTS = "best.pt"   # 학습한 모델 파일명 (절대/상대경로 모두 가능)

_yolo_model = None
def get_yolo():
    """YOLO 모델 로드 (최초 1회만 로드)"""
    global _yolo_model
    if _yolo_model is None:
        print(f"[YOLO] Loading {YOLO_WEIGHTS} on {DEVICE} ...")
        _yolo_model = YOLO(YOLO_WEIGHTS)
    return _yolo_model


# ======================================
# 라벨 매핑 (모델 클래스명 → 한글)
# ======================================
LABEL_MAP = {
    # 불량 클래스
    "top_dent": "상단찌그러짐",
    "top_no_tap": "뚜껑없음",
    "side_dent": "측면찌그러짐",
    "side_foreign": "스크래치",

    # 정상 클래스
    "top_normal": "정상(상단)",
    "side_normal": "정상(측면)"
}

# 카메라별 불량 라벨 기준
DEFECT_LABELS_TOP = {"상단찌그러짐", "뚜껑없음"}
DEFECT_LABELS_SIDE = {"측면찌그러짐", "스크래치"}


# ======================================
# 추론 함수
# ======================================
def infer_image(img_bgr, camera_type=None):
    """단일 이미지 추론 → 내부 공통 포맷 리턴"""
    t0 = time.time()

    if img_bgr is None or img_bgr.size == 0:
        return {
            "ok": False,
            "camera_type": camera_type,
            "result": "defective",
            "defect_score": 0.0,
            "classes": [],
            "bboxes": [],
            "inference_ms": 0.0,
            "model_version": f"yolo8-{YOLO_WEIGHTS}",
            "error": "invalid image"
        }

    img_rgb = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)
    model = get_yolo()

    # YOLO 추론
    results = model.predict(
        source=img_rgb,
        imgsz=640,
        conf=0.3,   # 탐지 민감도 (0.25~0.4 사이 권장)

        iou=0.45,
        device=DEVICE,
        verbose=False
    )

    r = results[0]
    boxes = r.boxes
    names = r.names

    bboxes = []
    max_conf = 0.0
    if boxes is not None and len(boxes) > 0:
        xyxy = boxes.xyxy.cpu().numpy()
        conf = boxes.conf.cpu().numpy()
        cls = boxes.cls.cpu().numpy().astype(int)

        for (x1, y1, x2, y2), c, k in zip(xyxy, conf, cls):
            raw_label = names.get(k, str(k))
            label = LABEL_MAP.get(raw_label, raw_label)  # 라벨 한글화

            w, h = int(x2 - x1), int(y2 - y1)
            bboxes.append({
                "x": int(x1),
                "y": int(y1),
                "w": w,
                "h": h,
                "label": label,
                "score": float(c)
            })
            if c > max_conf:
                max_conf = float(c)

    # ======== 추가 디버그 ========
    print("🔍 [DEBUG] Camera:", camera_type)
    print("🔍 [DEBUG] Detected:", [b["label"] for b in bboxes])
    print("🔍 [DEBUG] Scores:", [b["score"] for b in bboxes])
    # ===========================

    # 불량 판정 로직
    defect_detected = False
    THRESHOLD = 0.8  # 신뢰도 기준

    if camera_type == "top":
        defect_detected = any(b["label"] in DEFECT_LABELS_TOP and b["score"] >= THRESHOLD for b in bboxes)
    elif camera_type == "side":
        defect_detected = any(b["label"] in DEFECT_LABELS_SIDE and b["score"] >= THRESHOLD for b in bboxes)

    t1 = time.time()


    return {
        "ok": True,
        "camera_type": camera_type,
        "result": "defective" if defect_detected else "normal",
        "defect_score": float(max_conf),
        "classes": [{"name": b["label"], "score": b["score"]} for b in bboxes],
        "bboxes": bboxes,
        "inference_ms": (t1 - t0) * 1000.0,
        "model_version": f"yolo8-{YOLO_WEIGHTS}"
    }


# ======================================
# TCP 유틸리티 함수
# ======================================
def recv_all(sock, length: int):
    """정확히 length 바이트 수신 (중간 끊김 방지)"""
    buf = b""
    while len(buf) < length:
        chunk = sock.recv(length - len(buf))
        if not chunk:
            return None
        buf += chunk
    return buf


# ======================================
# 클라이언트 연결 처리
# ======================================
def handle_client(conn: socket.socket, addr):
    print(f"[AI Server] Accepted connection from {addr}")
    try:
        # 첫 바이트 = 모드
        mode_b = conn.recv(1)
        if not mode_b:
            print("[-] empty first byte, closing.")
            return

        mode = mode_b[0]

        # ----------------------------
        # 0x01 : Health Check
        # ----------------------------
        if mode == 0x01:
            print("[AI] Health check ...")
            conn.sendall(b"OK")
            print("[AI] Health check OK")
            return

        # ----------------------------
        # 0x02 : Dual 분석 (TOP + SIDE)
        # ----------------------------
        if mode == 0x02:
            print("[AI] Dual request...")

            # TOP 이미지 수신
            len_top_b = recv_all(conn, 4)
            if not len_top_b:
                print("[-] dual: no top len")
                return
            len_top = struct.unpack("<I", len_top_b)[0]
            top_bytes = recv_all(conn, len_top)
            if top_bytes is None:
                print("[-] dual: no top data")
                return
            img_top = cv2.imdecode(np.frombuffer(top_bytes, np.uint8), cv2.IMREAD_COLOR)

            # SIDE 이미지 수신
            len_side_b = recv_all(conn, 4)
            if not len_side_b:
                print("[-] dual: no side len")
                return
            len_side = struct.unpack("<I", len_side_b)[0]
            side_bytes = recv_all(conn, len_side)
            if side_bytes is None:
                print("[-] dual: no side data")
                return
            img_side = cv2.imdecode(np.frombuffer(side_bytes, np.uint8), cv2.IMREAD_COLOR)

            # AI 추론 수행
            res_top = infer_image(img_top, "top")
            res_side = infer_image(img_side, "side")

            final = {
                "result": "정상" if (res_top["result"] == "normal" and res_side["result"] == "normal") else "비정상",
                "top_result": "정상" if res_top["result"] == "normal" else "비정상",
                "side_result": "정상" if res_side["result"] == "normal" else "비정상",
                "det_top": [[c["name"], c["score"]] for c in res_top["classes"]],
                "det_side": [[c["name"], c["score"]] for c in res_side["classes"]],
            }

            payload = json.dumps(final, ensure_ascii=False).encode("utf-8")
            conn.sendall(payload)

            print("[AI] Dual done. Sent JSON & closing.")
            return

        # ----------------------------
        # 0x03 : Single 분석
        # ----------------------------
        if mode == 0x03:
            print("[AI] Single request...")

            # 라벨 길이
            len_label_b = recv_all(conn, 4)
            if not len_label_b:
                print("[-] single: no label len")
                return
            len_label = struct.unpack("<I", len_label_b)[0]
            label_b = recv_all(conn, len_label)
            if label_b is None:
                print("[-] single: no label data")
                return
            cam_label = label_b.decode("utf-8") if len_label > 0 else None

            # 이미지 수신
            len_img_b = recv_all(conn, 4)
            if not len_img_b:
                print("[-] single: no img len")
                return
            len_img = struct.unpack("<I", len_img_b)[0]
            img_b = recv_all(conn, len_img)
            if img_b is None:
                print("[-] single: no img data")
                return

            img = cv2.imdecode(np.frombuffer(img_b, np.uint8), cv2.IMREAD_COLOR)
            res = infer_image(img, cam_label)

            final = {
                "result": "정상" if res["result"] == "normal" else "비정상",
                ("det_top" if cam_label == "top" else "det_side"):
                    [[c["name"], c["score"]] for c in res["classes"]]
            }

            payload = json.dumps(final, ensure_ascii=False).encode("utf-8")
            conn.sendall(payload)

            print("[AI] Single done. Sent JSON & closing.")
            return

        # ----------------------------
        # 정의 안 된 모드
        # ----------------------------
        print(f"[AI] Unknown mode byte: {mode_b!r}. Closing.")
        return

    except Exception as e:
        print(f"[!] Error while handling {addr}: {e}")
    finally:
        try:
            conn.shutdown(socket.SHUT_RDWR)
        except:
            pass
        conn.close()
        print(f"[-] Disconnected {addr}")


# ======================================
# 메인 서버 루프
# ======================================
def main():
    HOST = "10.10.21.110"   # 환경에 맞게 변경 가능
    PORT = 8009

    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind((HOST, PORT))
    s.listen()

    print(f"[AI Server] Listening on {HOST}:{PORT}")
    while True:
        conn, addr = s.accept()
        threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()


if __name__ == "__main__":
    main()
