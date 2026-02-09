import os
import sys
import torch
import traceback
import cv2
import numpy as np
from PIL import Image
import torchvision.transforms as transforms
from depth_anything_v2.dpt import DepthAnythingV2
from depth_anything_v2.util.transform import Resize, NormalizeImage, PrepareForNet

# Global variable for the model
depth_model = None
depth_model_initialized = False
DEVICE = 'cuda' if torch.cuda.is_available() else 'cpu'


def initialize_depth_model(depth_anything_path, status_callback=None):
    """Initializes the Depth Anything V2 model."""
    print(depth_anything_path)
    global depth_model, depth_model_initialized, DEVICE
    if depth_model_initialized:
        return True

    if not depth_anything_path or not os.path.isdir(depth_anything_path):
        if status_callback:
            status_callback(
                "Depth Anything V2 path not set/invalid. Skipping depth estimation.")
        else:
            print(
                "!!! Warning: Depth Anything V2 path is not set or invalid.")
        return False
    if depth_anything_path not in sys.path:
        sys.path.append(depth_anything_path)
        print(f"Added Depth Anything V2 path: {depth_anything_path}")

    try:
        if status_callback:
            status_callback("Loading Depth Anything V2 model...")
        else:
            print("Loading Depth Anything V2 model...")

        print(f"Device used for Depth Anything V2: {DEVICE}")
        encoder = 'vitl'
        model_configs = {
            'vits': {'encoder': 'vits', 'features': 64,
                     'out_channels': [48, 96, 192, 384]},
            'vitb': {'encoder': 'vitb', 'features': 128,
                     'out_channels': [96, 192, 384, 768]},
            'vitl': {'encoder': 'vitl', 'features': 256,
                     'out_channels': [256, 512, 1024, 1024]}
        }
        if encoder not in model_configs:
            msg = f"!!! Error: Unknown encoder '{encoder}'. Available: {list(model_configs.keys())}"
            if status_callback:
                status_callback(msg)
            else:
                print(msg)
            return False
        depth_model = DepthAnythingV2(**model_configs[encoder])
        model_weights_path = os.path.join(
            depth_anything_path, f'checkpoints/depth_anything_v2_{encoder}.pth')
        if not os.path.exists(model_weights_path):
            msg = f"!!! Error: Model weights file not found: {model_weights_path}"
            if status_callback:
                status_callback(msg)
            else:
                print(msg)
            return False
        try:
            depth_model.load_state_dict(
                torch.load(model_weights_path, map_location='cpu',
                           weights_only=True))
        except TypeError:
            import warnings

            warnings.warn(
                "Your PyTorch version does not support 'weights_only=True' in torch.load. Loading in default mode.",
                UserWarning)
            depth_model.load_state_dict(
                torch.load(model_weights_path, map_location='cpu'))
        depth_model = depth_model.to(DEVICE).eval()

        msg = "Depth Anything V2 model loaded."
        if status_callback:
            status_callback(msg)
        else:
            print(msg)

        depth_model_initialized = True
        return True
    except ImportError as e:
        msg = f"!!! Import error for Depth Anything V2: {e}"
        if status_callback:
            status_callback(msg)
        else:
            print(msg)
        return False
    except FileNotFoundError as e:
        msg = f"!!! File error during Depth Anything V2 initialization: {e}"
        if status_callback:
            status_callback(msg)
        else:
            print(msg)
        return False
    except Exception as e:
        msg = f"!!! Error initializing Depth Anything V2 model: {e}\n{traceback.format_exc()}"
        if status_callback:
            status_callback(msg)
        else:
            print(msg)
        return False


def generate_depth_map(image_path_or_pil):
    """Generates a depth map for the given image."""
    global depth_model, depth_model_initialized, DEVICE  # Access globals
    if not depth_model_initialized or depth_model is None:
        return None
    try:
        transform = transforms.Compose([
            Resize(width=518, height=518, resize_target=False,
                   keep_aspect_ratio=True, ensure_multiple_of=14,
                   resize_method='lower_bound',
                   image_interpolation_method=cv2.INTER_CUBIC),
            NormalizeImage(mean=[0.485, 0.456, 0.406],
                           std=[0.229, 0.224, 0.225]),
            PrepareForNet(),
        ])
        if isinstance(image_path_or_pil, str):
            if not os.path.exists(image_path_or_pil):
                return None
            raw_image = Image.open(image_path_or_pil).convert('RGB')
        elif isinstance(image_path_or_pil, Image.Image):
            raw_image = image_path_or_pil.convert('RGB')
        else:
            return None
            
        original_width, original_height = raw_image.size
            
        image_np = np.array(raw_image)
        if image_np.size == 0:
            return None
        image = image_np / 255.0
        try:
            transformed_data = transform({'image': image})
            image_tensor = transformed_data['image']
        except Exception as e:
            raise RuntimeError(
                f"Error during image transformation (depth): {e}") from e
        image_tensor = torch.from_numpy(image_tensor).unsqueeze(0).to(DEVICE)
        with torch.no_grad():
            depth = depth_model(image_tensor)
        depth = depth.squeeze().cpu().numpy()
        if depth.size == 0 or np.isnan(depth).all():
            return None
        min_depth = np.nanmin(depth)
        max_depth = np.nanmax(depth)
        if max_depth == min_depth:
            import warnings

            warnings.warn("Depth map has a constant value.", UserWarning)
            depth_16bit = np.full_like(depth, 32767, dtype=np.uint16)
        else:
            depth_safe = np.nan_to_num(depth, nan=min_depth)
            depth_normalized = ((depth_safe - min_depth) / (
                max_depth - min_depth)) * 65535.0
            depth_16bit = depth_normalized.astype(np.uint16)
            
        if depth_16bit.shape[:2] != (original_height, original_width):
            depth_16bit = cv2.resize(depth_16bit, (original_width, original_height), 
                                    interpolation=cv2.INTER_LINEAR)
            
        return depth_16bit
    except ImportError as e:
        raise ImportError(
            f"Import error while generating depth map: {e}") from e
    except Exception as e:
        raise RuntimeError(
            f"Error while generating depth map: {e}\n{traceback.format_exc()}") from e


def save_depth_map(depth_map_array, output_path):
    """Saves the depth map as a 16-bit PNG image."""
    if depth_map_array is None:
        return False
    try:
        depth_image = Image.fromarray(depth_map_array, mode='I;16')
        depth_image.save(output_path, format='PNG')
        return True
    except Exception as e:
        raise RuntimeError(
            f"Error saving depth map {output_path}: {e}") from e
