"""
Hand Tracker para Lenguaje de Señas
===================================
Este script captura video de la cámara, detecta los landmarks de la mano
usando MediaPipe, ejecuta inferencia con el modelo ONNX, y envía los datos
a Unity vía UDP.

Autor: Proyecto Final CV
Fecha: Diciembre 2024

Requisitos:
    pip install opencv-python mediapipe onnxruntime numpy

Uso:
    python hand_tracker.py
    
    Presiona 'q' para salir
    Presiona 'm' para alternar el modo espejo
"""

import cv2
import mediapipe as mp
import socket
import json
import numpy as np
import time
import argparse
from typing import Optional, Tuple, List

# =============================================================================
# CONFIGURACIÓN
# =============================================================================

# Configuración de red para Unity
UDP_IP = "127.0.0.1"
UDP_PORT = 5005

# Streaming de video (JPEG) a Unity
UDP_VIDEO_PORT = 5006

# Configuración de MediaPipe
MAX_NUM_HANDS = 1
MIN_DETECTION_CONFIDENCE = 0.7
MIN_TRACKING_CONFIDENCE = 0.5

import os

# Ruta al modelo ONNX (relativa a este script)
MODEL_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "model.onnx")

# Clases del modelo (letras del abecedario ASL)
# Nota: J, S, Z no están incluidas porque requieren movimiento
CLASSES = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'K', 
           'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'T', 'U', 'V', 
           'W', 'X', 'Y']

# =============================================================================
# CLASE PRINCIPAL
# =============================================================================

class HandTracker:
    """
    Clase principal que maneja la detección de manos y la comunicación con Unity.
    """
    
    def __init__(self, 
                 model_path: str = MODEL_PATH,
                 udp_ip: str = UDP_IP, 
                 udp_port: int = UDP_PORT,
                 udp_video_port: Optional[int] = UDP_VIDEO_PORT,
                 video_fps: float = 10.0,
                 video_width: int = 320,
                 video_jpeg_quality: int = 55,
                 camera_id: int = 0,
                 mirror_mode: bool = True,
                 show_window: bool = True):
        """
        Inicializa el tracker de manos.
        
        Args:
            model_path: Ruta al archivo model.onnx
            udp_ip: Dirección IP del servidor UDP (Unity)
            udp_port: Puerto UDP
            camera_id: ID de la cámara (0 = cámara predeterminada)
            mirror_mode: Si True, voltea la imagen horizontalmente
            show_window: Si True, muestra ventana de debug con OpenCV
        """
        self.model_path = model_path
        self.udp_ip = udp_ip
        self.udp_port = udp_port
        self.udp_video_port = udp_video_port
        self.video_fps = float(video_fps)
        self.video_width = int(video_width)
        self.video_jpeg_quality = int(video_jpeg_quality)
        self.camera_id = camera_id
        self.mirror_mode = mirror_mode
        self.show_window = show_window
        
        # Estado
        self.is_running = False
        self.current_letter = ""
        self.confidence = 0.0
        self.fps = 0.0

        # Video streaming state
        self._last_video_send_time = 0.0
        
        # Inicializar componentes
        self._init_mediapipe()
        self._init_onnx()
        self._init_socket()
        self._init_camera()
        
    def _init_mediapipe(self):
        """Inicializa MediaPipe Hands."""
        print("[INFO] Inicializando MediaPipe Hands...")
        self.mp_hands = mp.solutions.hands
        self.mp_drawing = mp.solutions.drawing_utils
        self.mp_drawing_styles = mp.solutions.drawing_styles
        
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=MAX_NUM_HANDS,
            min_detection_confidence=MIN_DETECTION_CONFIDENCE,
            min_tracking_confidence=MIN_TRACKING_CONFIDENCE
        )
        print("[OK] MediaPipe inicializado")
        
    def _init_onnx(self):
        """Carga el modelo ONNX."""
        print(f"[INFO] Cargando modelo ONNX desde: {self.model_path}")
        try:
            import onnxruntime as ort
            
            # Configurar opciones de sesión
            sess_options = ort.SessionOptions()
            sess_options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
            
            # Crear sesión de inferencia
            self.onnx_session = ort.InferenceSession(
                self.model_path,
                sess_options,
                providers=['CPUExecutionProvider']
            )
            
            # Obtener información del modelo
            self.input_name = self.onnx_session.get_inputs()[0].name
            self.input_shape = self.onnx_session.get_inputs()[0].shape
            
            print(f"[OK] Modelo cargado exitosamente")
            print(f"     Input: {self.input_name} - Shape: {self.input_shape}")
            print(f"     Outputs: {[o.name for o in self.onnx_session.get_outputs()]}")
            
        except FileNotFoundError:
            print(f"[ERROR] No se encontró el modelo: {self.model_path}")
            self.onnx_session = None
        except Exception as e:
            print(f"[ERROR] Error cargando modelo: {e}")
            self.onnx_session = None
            
    def _init_socket(self):
        """Inicializa el socket UDP."""
        print(f"[INFO] Configurando UDP socket: {self.udp_ip}:{self.udp_port}")
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        print("[OK] Socket UDP configurado")
        
    def _init_camera(self):
        """Inicializa la cámara."""
        print(f"[INFO] Abriendo cámara {self.camera_id}...")
        self.cap = cv2.VideoCapture(self.camera_id)
        
        if not self.cap.isOpened():
            print("[ERROR] No se pudo abrir la cámara")
            return
            
        # Configurar resolución (opcional)
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
        
        # Obtener resolución real
        width = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        height = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        print(f"[OK] Cámara abierta: {width}x{height}")
        
    def extract_landmarks(self, hand_landmarks) -> List[float]:
        """
        Extrae los 21 landmarks de la mano como una lista plana.
        
        Args:
            hand_landmarks: Landmarks de MediaPipe
            
        Returns:
            Lista de 63 valores (21 puntos x 3 coordenadas)
        """
        points = []
        for landmark in hand_landmarks.landmark:
            points.append(landmark.x)
            points.append(landmark.y)
            points.append(landmark.z)
        return points
    
    def landmarks_to_vectors(self, landmarks: List[float]) -> List[Tuple[float, float, float]]:
        """
        Convierte la lista plana de landmarks a una lista de vectores (x, y, z).
        
        Args:
            landmarks: Lista de 63 valores
            
        Returns:
            Lista de 21 tuplas (x, y, z)
        """
        vectors = []
        for i in range(0, len(landmarks), 3):
            vectors.append((landmarks[i], landmarks[i + 1], landmarks[i + 2]))
        return vectors
    
    def predict_letter(self, landmarks: List[float]) -> Tuple[str, float]:
        """
        Ejecuta el modelo ONNX para predecir la letra.
        
        Args:
            landmarks: Lista de 63 valores (21 puntos x 3 coordenadas)
            
        Returns:
            Tupla (letra_predicha, confianza)
        """
        if self.onnx_session is None:
            return ("?", 0.0)
            
        try:
            # Preparar entrada
            input_data = np.array([landmarks], dtype=np.float32)
            
            # Ejecutar inferencia
            outputs = self.onnx_session.run(None, {self.input_name: input_data})
            
            # El modelo SVM devuelve:
            # outputs[0]: label (string)
            # outputs[1]: probabilidades (dict)
            
            predicted_label = outputs[0][0]
            
            # Obtener probabilidades si están disponibles
            if len(outputs) > 1 and outputs[1] is not None:
                probabilities = outputs[1][0]
                if isinstance(probabilities, dict):
                    confidence = max(probabilities.values()) if probabilities else 0.0
                else:
                    confidence = 1.0
            else:
                confidence = 1.0
                
            return (str(predicted_label), float(confidence))
            
        except Exception as e:
            print(f"[ERROR] Error en predicción: {e}")
            return ("?", 0.0)
    
    def send_to_unity(self, landmarks: List[float], letter: str, confidence: float, 
                      hand_detected: bool):
        """
        Envía los datos a Unity vía UDP.
        
        Args:
            landmarks: Lista de 42 valores de landmarks
            letter: Letra predicha
            confidence: Confianza de la predicción
            hand_detected: Si se detectó una mano
        """
        # Crear mensaje JSON
        data = {
            "hand_detected": hand_detected,
            "landmarks": landmarks if hand_detected else [],
            "letter": letter,
            "confidence": confidence,
            "timestamp": time.time()
        }
        
        # Enviar por UDP
        try:
            message = json.dumps(data)
            self.sock.sendto(message.encode('utf-8'), (self.udp_ip, self.udp_port))
        except Exception as e:
            print(f"[ERROR] Error enviando UDP: {e}")

    def _send_video_frame_to_unity(self, frame_bgr: np.ndarray):
        """Envía un frame JPEG (como bytes) vía UDP a Unity en un puerto separado.

        Nota: UDP tiene límite de tamaño por datagrama (~65KB). Por eso se manda
        un frame reducido y con calidad moderada.
        """
        if self.udp_video_port is None:
            return

        now = time.time()
        if self.video_fps > 0 and (now - self._last_video_send_time) < (1.0 / self.video_fps):
            return

        try:
            frame = frame_bgr
            if self.video_width > 0 and frame.shape[1] > self.video_width:
                scale = self.video_width / float(frame.shape[1])
                new_h = max(1, int(frame.shape[0] * scale))
                frame = cv2.resize(frame, (self.video_width, new_h), interpolation=cv2.INTER_AREA)

            encode_params = [int(cv2.IMWRITE_JPEG_QUALITY), int(np.clip(self.video_jpeg_quality, 10, 95))]
            ok, buf = cv2.imencode('.jpg', frame, encode_params)
            if not ok:
                return

            payload = buf.tobytes()
            # Avoid sending oversized UDP packets
            if len(payload) > 65000:
                return

            self.sock.sendto(payload, (self.udp_ip, self.udp_video_port))
            self._last_video_send_time = now
        except Exception:
            # Silencioso: si el video falla, no queremos romper la detección
            return
    
    def draw_overlay(self, frame: np.ndarray, hand_landmarks, 
                     letter: str, confidence: float) -> np.ndarray:
        """
        Dibuja la información de debug sobre el frame.
        
        Args:
            frame: Frame de video
            hand_landmarks: Landmarks de MediaPipe (o None)
            letter: Letra predicha
            confidence: Confianza
            
        Returns:
            Frame con overlay dibujado
        """
        h, w, _ = frame.shape
        
        # Dibujar landmarks de la mano
        if hand_landmarks:
            self.mp_drawing.draw_landmarks(
                frame,
                hand_landmarks,
                self.mp_hands.HAND_CONNECTIONS,
                self.mp_drawing_styles.get_default_hand_landmarks_style(),
                self.mp_drawing_styles.get_default_hand_connections_style()
            )
        
        # Fondo semi-transparente para el texto
        overlay = frame.copy()
        cv2.rectangle(overlay, (10, 10), (250, 130), (0, 0, 0), -1)
        frame = cv2.addWeighted(overlay, 0.6, frame, 0.4, 0)
        
        # Información del estado
        status_color = (0, 255, 0) if hand_landmarks else (0, 0, 255)
        status_text = "MANO DETECTADA" if hand_landmarks else "SIN MANO"
        cv2.putText(frame, status_text, (20, 35), 
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, status_color, 2)
        
        # Letra predicha
        if hand_landmarks and letter:
            cv2.putText(frame, f"Letra: {letter}", (20, 65), 
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
            cv2.putText(frame, f"Conf: {confidence:.1%}", (20, 95), 
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 1)
        
        # FPS
        cv2.putText(frame, f"FPS: {self.fps:.1f}", (20, 120), 
                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, (150, 150, 150), 1)
        
        # Letra grande en la esquina
        if hand_landmarks and letter and letter != "?":
            # Dibujar letra grande
            cv2.putText(frame, letter, (w - 100, 80), 
                        cv2.FONT_HERSHEY_SIMPLEX, 3, (0, 255, 0), 4)
        
        # Instrucciones
        cv2.putText(frame, "Presiona 'q' para salir | 'm' para espejo", 
                    (10, h - 15), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (150, 150, 150), 1)
        
        return frame
    
    def run(self):
        """
        Bucle principal del tracker.
        """
        if not self.cap.isOpened():
            print("[ERROR] La cámara no está disponible")
            return
            
        print("\n" + "="*50)
        print("  HAND TRACKER INICIADO")
        print("="*50)
        print(f"  Enviando datos a: {self.udp_ip}:{self.udp_port}")
        print(f"  Modelo: {'Cargado' if self.onnx_session else 'NO DISPONIBLE'}")
        print(f"  Modo espejo: {'Sí' if self.mirror_mode else 'No'}")
        print("="*50 + "\n")
        
        self.is_running = True
        prev_time = time.time()
        frame_count = 0
        
        try:
            while self.is_running:
                # Capturar frame
                ret, frame = self.cap.read()
                if not ret:
                    print("[WARN] No se pudo leer frame")
                    continue
                
                # Modo espejo
                if self.mirror_mode:
                    frame = cv2.flip(frame, 1)
                
                # Convertir a RGB para MediaPipe
                rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                
                # Procesar con MediaPipe
                results = self.hands.process(rgb_frame)
                
                # Variables para este frame
                hand_detected = False
                landmarks = []
                letter = ""
                confidence = 0.0
                hand_landmarks_draw = None
                
                # Si se detectó una mano
                if results.multi_hand_landmarks:
                    hand_detected = True
                    hand_landmarks = results.multi_hand_landmarks[0]
                    hand_landmarks_draw = hand_landmarks
                    
                    # Extraer landmarks
                    landmarks = self.extract_landmarks(hand_landmarks)
                    
                    # Predecir letra
                    letter, confidence = self.predict_letter(landmarks)
                    
                    self.current_letter = letter
                    self.confidence = confidence
                
                # Enviar datos a Unity
                self.send_to_unity(landmarks, letter, confidence, hand_detected)
                
                # Calcular FPS
                frame_count += 1
                current_time = time.time()
                if current_time - prev_time >= 1.0:
                    self.fps = frame_count / (current_time - prev_time)
                    frame_count = 0
                    prev_time = current_time
                
                # Mostrar ventana de debug
                if self.show_window:
                    display_frame = self.draw_overlay(frame, hand_landmarks_draw, 
                                                       letter, confidence)
                    cv2.imshow('Hand Tracker - Lenguaje de Senas', display_frame)

                    # Enviar frame a Unity (si está habilitado)
                    self._send_video_frame_to_unity(display_frame)
                else:
                    # Aún sin ventana, podemos streamear el frame crudo si se desea
                    self._send_video_frame_to_unity(frame)
                    
                    # Manejar teclas
                    key = cv2.waitKey(1) & 0xFF
                    if key == ord('q'):
                        print("[INFO] Saliendo...")
                        break
                    elif key == ord('m'):
                        self.mirror_mode = not self.mirror_mode
                        print(f"[INFO] Modo espejo: {'Activado' if self.mirror_mode else 'Desactivado'}")
                        
        except KeyboardInterrupt:
            print("\n[INFO] Interrupción de teclado recibida")
        finally:
            self.cleanup()
    
    def cleanup(self):
        """Libera recursos."""
        print("[INFO] Limpiando recursos...")
        self.is_running = False
        
        if self.cap:
            self.cap.release()
        if self.hands:
            self.hands.close()
        if self.sock:
            self.sock.close()
            
        cv2.destroyAllWindows()
        print("[OK] Recursos liberados")


# =============================================================================
# PUNTO DE ENTRADA
# =============================================================================

def main():
    """Función principal."""
    parser = argparse.ArgumentParser(
        description='Hand Tracker para Lenguaje de Señas'
    )
    parser.add_argument(
        '--camera', '-c', 
        type=int, 
        default=0,
        help='ID de la cámara (default: 0)'
    )
    parser.add_argument(
        '--port', '-p', 
        type=int, 
        default=UDP_PORT,
        help=f'Puerto UDP (default: {UDP_PORT})'
    )
    parser.add_argument(
        '--video-port',
        type=int,
        default=UDP_VIDEO_PORT,
        help=f'Puerto UDP para video JPEG hacia Unity (default: {UDP_VIDEO_PORT}).'
    )
    parser.add_argument(
        '--no-video',
        action='store_true',
        help='Desactivar streaming de video por UDP'
    )
    parser.add_argument(
        '--video-fps',
        type=float,
        default=10.0,
        help='FPS máximo para streaming de video (default: 10)'
    )
    parser.add_argument(
        '--video-width',
        type=int,
        default=320,
        help='Ancho del frame enviado por UDP (se mantiene aspect ratio). 0 desactiva resize (default: 320)'
    )
    parser.add_argument(
        '--video-quality',
        type=int,
        default=55,
        help='Calidad JPEG 10-95 (default: 55)'
    )
    parser.add_argument(
        '--ip', '-i', 
        type=str, 
        default=UDP_IP,
        help=f'IP de destino (default: {UDP_IP})'
    )
    parser.add_argument(
        '--model', '-m', 
        type=str, 
        default=MODEL_PATH,
        help=f'Ruta al modelo ONNX (default: {MODEL_PATH})'
    )
    parser.add_argument(
        '--no-mirror', 
        action='store_true',
        help='Desactivar modo espejo'
    )
    parser.add_argument(
        '--no-window', 
        action='store_true',
        help='No mostrar ventana de OpenCV'
    )
    
    args = parser.parse_args()
    
    # Crear y ejecutar tracker
    tracker = HandTracker(
        model_path=args.model,
        udp_ip=args.ip,
        udp_port=args.port,
        udp_video_port=None if args.no_video else args.video_port,
        video_fps=args.video_fps,
        video_width=args.video_width,
        video_jpeg_quality=args.video_quality,
        camera_id=args.camera,
        mirror_mode=not args.no_mirror,
        show_window=not args.no_window
    )
    
    tracker.run()


if __name__ == "__main__":
    main()
