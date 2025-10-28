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
YOLO_WEIGHTS = "yolov8n.pt"   # 네 학습 모델로 교체 가능

_yolo_model = None
def get_yolo():
    global _yolo_model
    if _yolo_model is None:
        print(f"[YOLO] Loading {YOLO_WEIGHTS} on {DEVICE} ...")
        _yolo_model = YOLO(YOLO_WEIGHTS)
    return _yolo_model

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
    results = model.predict(
        source=img_rgb,
        imgsz=640,
        conf=0.25,
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
            label = names.get(k, str(k))
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

    # 기본 룰: 박스 있으면 불량
    is_defect = (len(bboxes) > 0)

    t1 = time.time()
    return {
        "ok": True,
        "camera_type": camera_type,
        "result": "defective" if is_defect else "normal",
        "defect_score": float(max_conf),
        "classes": [{"name": b["label"], "score": b["score"]} for b in bboxes],
        "bboxes": bboxes,
        "inference_ms": (t1 - t0) * 1000.0,
        "model_version": f"yolo8-{YOLO_WEIGHTS}",
    }

# ======================================
# TCP 유틸
# ======================================

def recv_all(sock, length: int):
    """정확히 length 바이트 수신. 실패 시 None."""
    buf = b""
    while len(buf) < length:
        chunk = sock.recv(length - len(buf))
        if not chunk:
            return None
        buf += chunk
    return buf


def handle_client(conn: socket.socket, addr):
    """연결 1건 처리 (헬스/싱글/듀얼 중 하나만)"""
    print(f"[AI Server] Accepted connection from {addr}")
    try:
        # 가장 먼저 모드(1바이트) 받는다.
        mode_b = conn.recv(1)
        if not mode_b:
            print("[-] empty first byte, closing.")
            return

        mode = mode_b[0]

        # --------------------------------
        # 0x01 : Health Check
        # --------------------------------
        if mode == 0x01:
            print("[AI] Health check ...")
            conn.sendall(b"OK")
            print("[AI] Health check OK")
            return

        # --------------------------------
        # 0x02 : Dual 분석 (TOP + SIDE/FRONT)
        # 프로토콜:
        #   [0x02]
        #   [4바이트 TOP length  (little-endian)]
        #   [TOP bytes]
        #   [4바이트 SIDE length (little-endian)]
        #   [SIDE bytes]
        # 응답: UTF-8 JSON 문자열 한 번 쏘고 종료
        # --------------------------------
        if mode == 0x02:
            print("[AI] Dual request...")

            # TOP
            len_top_b = recv_all(conn, 4)
            if not len_top_b:
                print("[-] dual: no top len")
                return
            len_top = struct.unpack("<I", len_top_b)[0]

            top_bytes = recv_all(conn, len_top)
            if top_bytes is None:
                print("[-] dual: no top data")
                return
            img_top = cv2.imdecode(
                np.frombuffer(top_bytes, np.uint8),
                cv2.IMREAD_COLOR
            )

            # SIDE
            len_side_b = recv_all(conn, 4)
            if not len_side_b:
                print("[-] dual: no side len")
                return
            len_side = struct.unpack("<I", len_side_b)[0]

            side_bytes = recv_all(conn, len_side)
            if side_bytes is None:
                print("[-] dual: no side data")
                return
            img_side = cv2.imdecode(
                np.frombuffer(side_bytes, np.uint8),
                cv2.IMREAD_COLOR
            )

            # 추론
            res_top  = infer_image(img_top,  "top")
            res_side = infer_image(img_side, "side")

            final = {
                "result":     "정상" if (res_top["result"] == "normal" and res_side["result"] == "normal") else "비정상",
                "top_result": "정상" if res_top["result"]  == "normal" else "비정상",
                "side_result":"정상" if res_side["result"] == "normal" else "비정상",
                "det_top":  [[c["name"], c["score"]] for c in res_top["classes"]],
                "det_side": [[c["name"], c["score"]] for c in res_side["classes"]],
            }

            payload = json.dumps(final, ensure_ascii=False).encode("utf-8")
            conn.sendall(payload)

            print("[AI] Dual done. Sent JSON & closing.")
            return

        # --------------------------------
        # 0x03 : Single 분석
        # 프로토콜:
        #   [0x03]
        #   [4바이트 label 길이][label utf-8]
        #   [4바이트 image 길이][image bytes]
        # 응답: UTF-8 JSON 한 번 쏘고 종료
        # --------------------------------
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

            # 이미지
            len_img_b = recv_all(conn, 4)
            if not len_img_b:
                print("[-] single: no img len")
                return
            len_img = struct.unpack("<I", len_img_b)[0]

            img_b = recv_all(conn, len_img)
            if img_b is None:
                print("[-] single: no img data")
                return

            img = cv2.imdecode(
                np.frombuffer(img_b, np.uint8),
                cv2.IMREAD_COLOR
            )

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

        # --------------------------------
        # 정의 안 된 코드
        # --------------------------------
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


def main():
    HOST = "10.10.21.110"
    PORT = 8009

    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind((HOST, PORT))
    s.listen()

    print(f"[AI Server] Listening on {HOST}:{PORT}")
    while True:
        conn, addr = s.accept()
        # 연결마다 스레드
        threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()


if __name__ == "__main__":
    main()