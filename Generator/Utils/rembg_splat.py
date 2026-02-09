import argparse
import numpy as np
import cv2
import os
from plyfile import PlyData, PlyElement
from tqdm import tqdm


def qvec2rotmat(qvec):
    return np.array([
        [1 - 2 * qvec[2]**2 - 2 * qvec[3]**2,
         2 * qvec[1] * qvec[2] - 2 * qvec[0] * qvec[3],
         2 * qvec[1] * qvec[3] + 2 * qvec[0] * qvec[2]],
        [2 * qvec[1] * qvec[2] + 2 * qvec[0] * qvec[3],
         1 - 2 * qvec[1]**2 - 2 * qvec[3]**2,
         2 * qvec[2] * qvec[3] - 2 * qvec[0] * qvec[1]],
        [2 * qvec[1] * qvec[3] - 2 * qvec[0] * qvec[2],
         2 * qvec[2] * qvec[3] + 2 * qvec[0] * qvec[1],
         1 - 2 * qvec[1]**2 - 2 * qvec[2]**2]])

def read_cameras_text(path):
    cameras = {}
    if not os.path.exists(path):
        print(f"Error: Camera file not found at {path}")
        return {}
        
    with open(path, "r") as fid:
        while True:
            line = fid.readline()
            if not line: break
            line = line.strip()
            if len(line) > 0 and line[0] != "#":
                elems = line.split()
                camera_id = int(elems[0])
                model = elems[1]
                width = int(elems[2])
                height = int(elems[3])
                params = np.array(tuple(map(float, elems[4:])))
                cameras[camera_id] = (model, width, height, params)
    return cameras

def read_images_text(path):
    images = {}
    if not os.path.exists(path):
        print(f"Error: Images file not found at {path}")
        return {}

    with open(path, "r") as fid:
        while True:
            line = fid.readline()
            if not line: break
            line = line.strip()
            if len(line) > 0 and line[0] != "#":
                elems = line.split()
                image_id = int(elems[0])
                qvec = np.array(tuple(map(float, elems[1:5])))
                tvec = np.array(tuple(map(float, elems[5:8])))
                camera_id = int(elems[8])
                image_name = elems[9]
                images[image_id] = (qvec, tvec, camera_id, image_name)
                fid.readline() 
    return images



def clean_splat(ply_path, output_path, mask_images_dir, colmap_dir, min_visibility, background_threshold):
    print(f"Loading PLY file: {ply_path}")
    if not os.path.exists(ply_path):
        print(f"Error: Input PLY file does not exist: {ply_path}")
        return

    ply_data = PlyData.read(ply_path)
    vertices = ply_data['vertex']

    positions = np.stack([vertices['x'], vertices['y'], vertices['z']], axis=-1)
    

    try:
        scales = np.exp(np.stack([vertices['scale_0'], vertices['scale_1'], vertices['scale_2']], axis=-1))
    except:
        scales = np.ones_like(positions)

    print(f"Loading COLMAP data from: {colmap_dir}")
    cameras = read_cameras_text(os.path.join(colmap_dir, "cameras.txt"))
    images = read_images_text(os.path.join(colmap_dir, "images.txt"))

    if not cameras or not images:
        print("Failed to load COLMAP data. Exiting.")
        return

    visible_counts = np.zeros(len(positions), dtype=int)
    background_counts = np.zeros(len(positions), dtype=int)

    print("Processing images and masks...")
    for image_id in tqdm(images):
        qvec, tvec, camera_id, image_name = images[image_id]
        
        mask_path = os.path.join(mask_images_dir, image_name)
        if not os.path.exists(mask_path):
             base, _ = os.path.splitext(image_name)
             mask_path = os.path.join(mask_images_dir, base + ".png")
        
        if not os.path.exists(mask_path):
            continue

        mask = cv2.imread(mask_path, cv2.IMREAD_UNCHANGED)
        if mask is None:
            continue

        model, colmap_width, colmap_height, params = cameras[camera_id]

        if len(mask.shape) == 3 and mask.shape[2] == 4:
            alpha = mask[:, :, 3]
        elif len(mask.shape) == 2:
            alpha = mask
        else:
            gray = cv2.cvtColor(mask, cv2.COLOR_BGR2GRAY)
            _, alpha = cv2.threshold(gray, 1, 255, cv2.THRESH_BINARY)

        h, w = alpha.shape
        if w != colmap_width or h != colmap_height:
             alpha = cv2.resize(alpha, (colmap_width, colmap_height), interpolation=cv2.INTER_NEAREST)

        R = qvec2rotmat(qvec)
        t = tvec

        xyz_cam = positions @ R.T + t
        z = xyz_cam[:, 2]
        valid_z = z > 0.001 
        
        x = xyz_cam[:, 0] / z
        y = xyz_cam[:, 1] / z

        if model == "PINHOLE" or model == "OPENCV":
            fx, fy, cx, cy = params[0], params[1], params[2], params[3]
        elif model == "SIMPLE_RADIAL" or model == "SIMPLE_PINHOLE":
            fx, cx, cy = params[0], params[1], params[2]
            fy = fx
        else:
            fx = params[0]
            fy = params[0]
            cx = colmap_width / 2
            cy = colmap_height / 2

        u = x * fx + cx
        v = y * fy + cy

        valid_coords = (u >= 0) & (u < colmap_width) & (v >= 0) & (v < colmap_height)
        valid_mask = valid_z & valid_coords
        
        indices = np.where(valid_mask)[0]
        
        if len(indices) == 0:
            continue


        u_int = np.round(u[indices]).astype(int)
        v_int = np.round(v[indices]).astype(int)
        
        u_int = np.clip(u_int, 0, colmap_width - 1)
        v_int = np.clip(v_int, 0, colmap_height - 1)

        mask_values = alpha[v_int, u_int]
        
        visible_counts[indices] += 1
        background_counts[indices[mask_values == 0]] += 1

    print("Filtering points...")
    
    keep_mask = visible_counts >= min_visibility
    print(f"Points remaining after visibility filter: {np.sum(keep_mask)} / {len(keep_mask)}")

    safe_visible = visible_counts.copy()
    safe_visible[safe_visible == 0] = 1 
    
    bg_ratios = background_counts / safe_visible
    bg_keep_mask = bg_ratios <= background_threshold
    
    final_keep_mask = keep_mask & bg_keep_mask
    print(f"Points remaining after background filter: {np.sum(final_keep_mask)} / {len(keep_mask)}")

    keep_indices = np.where(final_keep_mask)[0]

    if len(keep_indices) == 0:
        print("Warning: No points remained! Check parameters.")

    print("Saving cleaned PLY...")
    new_vertex_element = PlyElement.describe(vertices[keep_indices], 'vertex', comments=ply_data['vertex'].comments)
    
    new_elements = []
    for element in ply_data.elements:
        if element.name == 'vertex':
            new_elements.append(new_vertex_element)
        else:
            new_elements.append(element)

    new_ply_data = PlyData(new_elements, text=False, comments=ply_data.comments, obj_info=ply_data.obj_info)
    new_ply_data.write(output_path)
    print(f"Saved to: {output_path}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Clean Gaussian Splat based on masks.")
    parser.add_argument("--input", required=True, help="Input PLY file path")
    parser.add_argument("--output", required=True, help="Output PLY file path")
    parser.add_argument("--masks", required=True, help="Directory containing mask images")
    parser.add_argument("--colmap", required=True, help="Directory containing COLMAP sparse model")
    parser.add_argument("--min_visibility", type=int, default=30)
    parser.add_argument("--background_threshold", type=float, default=0.1)
    
    args = parser.parse_args()
    
    clean_splat(args.input, args.output, args.masks, args.colmap, args.min_visibility, args.background_threshold)