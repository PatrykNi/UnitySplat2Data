import os
import numpy as np
import cv2
from PIL import Image
import traceback
from rembg import remove, new_session
import subprocess
import sys
import shutil
import json 


ROOT_DIR = os.path.dirname(os.path.dirname(os.path.realpath(__file__)))

def run_command_live(command, cwd=None, status_callback=None):
    try:
        env = os.environ.copy()
        env["PYTHONUNBUFFERED"] = "1"

        process = subprocess.Popen(
            command,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, 
            text=True,
            bufsize=1,     
            env=env         
        )

        while True:
            # Czytamy linia po linii
            line = process.stdout.readline()
            if not line and process.poll() is not None:
                break
            if line:
                stripped_line = line.strip()
                print(stripped_line) 
                if status_callback and stripped_line:
                    status_callback(stripped_line)

        return process.returncode

    except Exception as e:
        error_msg = f"Error executing command {' '.join(command)}: {str(e)}"
        print(error_msg)
        if status_callback:
            status_callback(error_msg)
        return -1


def apply_mask_to_image(image_array, mask_array):
    if image_array is None or mask_array is None:
        return None
    if image_array.shape[:2] != mask_array.shape[:2]:
        try:
            target_height, target_width = image_array.shape[:2]
            mask_resized = cv2.resize(mask_array, (target_width, target_height),
                                     interpolation=cv2.INTER_NEAREST)
            mask_array = mask_resized
        except Exception as e:
            raise RuntimeError(
                f"Error while scaling the mask: {e}. Skipping mask application.") from e
    masked_image_array = image_array.copy()
    masked_image_array[mask_array == 0] = 0
    return masked_image_array


def run_processing(project_folder, model_name, max_dimension,
                    do_rembg, do_depth,
                    save_preprocessed, save_nobg, save_mask,
                    save_depth, save_depth_nobg,
                    status_callback=None, progress_callback=None,
                    depth_anything_path="",
                    run_colmap=False,
                    run_colmap2nerf=False,
                    gs_iterations=7000,
                    nvdiffrec_iterations=1000):
    processed_files = 0
    failed_files = 0
    rembg_session = None
    depth_model_initialized = False
    input_folder = os.path.join(project_folder, "input")
    if not os.path.isdir(input_folder):
        msg = f"Error: Input folder not found at {input_folder}"
        if status_callback: status_callback(msg)
        print(msg)
        if progress_callback: progress_callback(1.0)
        if status_callback:
             summary = f"Zakończono. Przetworzono: {processed_files}, Błędy: {failed_files + 1}"
             status_callback(summary)
        return 

    image_files = [f for f in os.listdir(input_folder) if f.lower().endswith(
        ('.png', '.jpg', '.jpeg', '.tiff', '.bmp'))]
    num_files = len(image_files)

    if num_files == 0:
        msg = f"No image files found in {input_folder}"
        if status_callback: status_callback(msg)
        print(msg)
        if progress_callback: progress_callback(1.0)
        if status_callback:
             summary = f"Zakończono. Przetworzono: {processed_files}, Błędy: {failed_files}"
             status_callback(summary)
        return 


    if do_depth and not depth_anything_path:
        if status_callback:
            status_callback(
                "Depth Anything V2 path not set. Skipping depth estimation.")
        print("Depth Anything V2 path not set. Skipping depth estimation.")
        do_depth = False
    elif do_depth:
        from .depth_estimation import initialize_depth_model, generate_depth_map, save_depth_map
        depth_model_initialized = initialize_depth_model(
            depth_anything_path, status_callback)
        if not depth_model_initialized:
                        msg = "Depth Anything V2 initialization failed. Skipping depth estimation."
                        if status_callback: status_callback(msg)
                        print(msg)
                        do_depth = False 


    for i, filename in enumerate(image_files):
        filepath = os.path.join(input_folder, filename)
        output_png_filename = os.path.splitext(filename)[0] + ".png"
        success = True 
        image_array = None
        rembg_mask_array = None 
        depth_map_array = None

        if status_callback:
            status_callback(f"Processing image {i + 1}/{num_files}: {filename}")
            print(f"Processing image {i + 1}/{num_files}: {filename}")
        try:
            try:
                image = Image.open(filepath)
                if max_dimension:
                    image.thumbnail((max_dimension, max_dimension))
                if image.mode != 'RGB':
                     image = image.convert('RGB')
                image_array = np.array(image)
            except Exception as e:
                if status_callback:
                    status_callback(f"Error loading image {filename}: {e}")
                    print(f"Error loading image {filename}: {e}")
                success = False
                failed_files += 1
                continue
            if do_rembg:
                try:
                    if rembg_session is None:
                        rembg_session = new_session(model_name)
                        if status_callback:
                            status_callback(f"Initialized rembg session with model: {model_name}")
                            print(f"Initialized rembg session with model: {model_name}")

                    output_image_rembg = remove(Image.fromarray(image_array),
                                                session=rembg_session)
                    rembg_mask_array = np.array(output_image_rembg)[:, :, 3]

                    if save_nobg:
                        output_folder_nobg = os.path.join(
                            project_folder, "images_without_background")
                        os.makedirs(output_folder_nobg, exist_ok=True)
                        nobg_image_path = os.path.join(
                            output_folder_nobg, output_png_filename)
                        output_image_rembg.save(nobg_image_path, format="PNG")

                    if save_mask:
                        output_folder_mask = os.path.join(
                            project_folder, "rembg_masks")
                        os.makedirs(output_folder_mask, exist_ok=True)
                        mask_image = Image.fromarray(rembg_mask_array, mode='L')
                        mask_image_path = os.path.join(
                            output_folder_mask, output_png_filename)
                        mask_image.save(mask_image_path, format="PNG")

                except Exception as e:
                    if status_callback:
                        status_callback(
                            f"Error processing rembg for {filename}: {e}\n{traceback.format_exc()}")
                        print(f"Error processing rembg for {filename}: {e}\n{traceback.format_exc()}")
                    success = False
                    rembg_mask_array = None


            if do_depth and depth_model_initialized:
                try:
                    if image_array is not None: 
                        image_pil_for_depth = Image.fromarray(image_array)

                        depth_map_array = generate_depth_map(image_pil_for_depth)

                        if depth_map_array is not None:
                            if save_depth:
                                output_folder_depth = os.path.join(
                                    project_folder, "depth_maps")
                                os.makedirs(output_folder_depth, exist_ok=True)
                                depth_output_path = os.path.join(
                                    output_folder_depth, output_png_filename)

                                save_depth_map(depth_map_array, depth_output_path)

                            if save_depth_nobg and do_rembg and rembg_mask_array is not None:
                                try:
                                    depth_nobg_array = apply_mask_to_image(
                                        depth_map_array, rembg_mask_array)
                                    if depth_nobg_array is not None:
                                        output_folder_depth_nobg = os.path.join(
                                            project_folder, "depth_maps_without_background")
                                        os.makedirs(output_folder_depth_nobg, exist_ok=True)
                                        depth_nobg_output_path = os.path.join(
                                            output_folder_depth_nobg, output_png_filename)
                                        save_depth_map(depth_nobg_array,
                                                       depth_nobg_output_path)
                                except Exception as e:
                                    msg = f"Error saving depth_nobg {filename}: {e}"
                                    if status_callback: status_callback(msg)
                                    print(msg)

                        else:
                            msg = f"Depth fail: generate_depth_map returned None for {filename}."
                            if status_callback: status_callback(msg)
                            print(msg)


                except Exception as e:
                    msg = f"Error generating depth map {filename}: {e}\n{traceback.format_exc()}"
                    if status_callback:
                        status_callback(msg)
                    print(msg)
                    success = False
                    failed_files += 1

            if save_preprocessed and image_array is not None:
                try:
                    output_folder_preprocessed = os.path.join(
                        project_folder, "preprocessed_images")
                    os.makedirs(output_folder_preprocessed, exist_ok=True)
                    preprocessed_image = Image.fromarray(image_array)
                    preprocessed_image_path = os.path.join(
                        output_folder_preprocessed, output_png_filename)
                    preprocessed_image.save(preprocessed_image_path, format="PNG")
                except Exception as e:
                     msg = f"Error saving preprocessed image {filename}: {e}\n{traceback.format_exc()}"
                     if status_callback: status_callback(msg)
                     print(msg)

        except Exception as e:
            if status_callback:
                status_callback(
                    f"!!! Unexpected error processing {filename}:{e}\n{traceback.format_exc()}")
            success = False
            failed_files += 1

        if success:
            processed_files += 1
        if progress_callback:
            progress_callback((i + 1) / num_files)


    colmap_text_path = None 
    if run_colmap:
        try:
            if status_callback:
                status_callback("Running COLMAP reconstruction...")

            colmap_command = [
                sys.executable,
                os.path.join(os.path.dirname(__file__), "colmap_runner.py"),
                "-s", project_folder,
                "--auto_reconstruct"
            ]

            if status_callback:
                status_callback(f"Executing COLMAP: {' '.join(colmap_command)}")

            return_code = run_command_live(colmap_command, cwd=ROOT_DIR, status_callback=status_callback)

            if return_code != 0:
                 if status_callback:
                     status_callback(f"COLMAP execution failed with return code {return_code}.")
                 failed_files += 1
            else:
                 colmap_text_path = os.path.join(project_folder, "distorted", "sparse", "0")
                 if not os.path.isdir(colmap_text_path) or not os.listdir(colmap_text_path):
                     colmap_text_path = os.path.join(project_folder, "sparse", "0")
                     if status_callback:
                          status_callback(f"Using {colmap_text_path} for subsequent steps (distorted/sparse/0 not found or empty).")
                 else:
                     if status_callback:
                         status_callback(f"COLMAP finished. Using output: {colmap_text_path}")


        except Exception as e:
            if status_callback:
                status_callback(f"Error running COLMAP command: {e}\n{traceback.format_exc()}")
            failed_files += 1

    output_json_path = None
    if run_colmap2nerf:
        try:
            if status_callback:
                status_callback("Running colmap2nerf conversion...")

            colmap2nerf_script = os.path.join(os.path.dirname(__file__), "colmap2nerf.py")
            if not os.path.exists(colmap2nerf_script):
                 raise FileNotFoundError(f"colmap2nerf.py not found at {colmap2nerf_script}")

            images_for_nerf_path = os.path.join(project_folder, "images_without_background")
            if not os.path.isdir(images_for_nerf_path) or not os.listdir(images_for_nerf_path):
                 raise FileNotFoundError(
                     f"Required images folder without background not found or is empty: {images_for_nerf_path}. "
                     "Cannot run colmap2nerf conversion without images without background."
                 )
            else:
                 if status_callback:
                      status_callback(f"Using images without background for colmap2nerf: {images_for_nerf_path}")
            colmap_text_path_for_colmap2nerf = None
            potential_colmap_path_distorted_for_colmap2nerf = os.path.join(project_folder, "distorted", "sparse", "0")
            potential_colmap_path_undistorted_for_colmap2nerf = os.path.join(project_folder, "sparse", "0")

            if os.path.isdir(potential_colmap_path_distorted_for_colmap2nerf) and os.listdir(potential_colmap_path_distorted_for_colmap2nerf):
                colmap_text_path_for_colmap2nerf = potential_colmap_path_distorted_for_colmap2nerf
                if status_callback:
                     status_callback(f"Found COLMAP text output for colmap2nerf at: {colmap_text_path_for_colmap2nerf}")
            elif os.path.isdir(potential_colmap_path_undistorted_for_colmap2nerf) and os.listdir(potential_colmap_path_undistorted_for_colmap2nerf):
                colmap_text_path_for_colmap2nerf = potential_colmap_path_undistorted_for_colmap2nerf
                if status_callback:
                     status_callback(f"Found COLMAP text output for colmap2nerf at: {colmap_text_path_for_colmap2nerf} (using undistorted path)")
            else:
                 raise FileNotFoundError(
                     f"COLMAP text output folder not found or is empty in expected locations ({potential_colmap_path_distorted_for_colmap2nerf} or {potential_colmap_path_undistorted_for_colmap2nerf}). "
                     "Cannot run colmap2nerf conversion without COLMAP results."
                 )

            output_json_path = os.path.join(project_folder, "transforms_train.json")

            colmap2nerf_command = [
                sys.executable,
                colmap2nerf_script,
                "--images", images_for_nerf_path,
                "--text", colmap_text_path_for_colmap2nerf,
                "--out", output_json_path,
                "--colmap_camera_model", "SIMPLE_PINHOLE"
            ]
            if status_callback:
                 status_callback(f"Executing command: {' '.join(colmap2nerf_command)}")
                 print(f"Executing command: {' '.join(colmap2nerf_command)}")


            process = subprocess.Popen(
                colmap2nerf_command,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE
            )
            stdout_bytes, stderr_bytes = process.communicate()
            return_code = process.returncode

            stdout_str = stdout_bytes.decode('utf-8', errors='replace')
            stderr_str = stderr_bytes.decode('utf-8', errors='replace')


            if status_callback:
                if stdout_str:
                    status_callback(f"colmap2nerf Output:\n{stdout_str}")

                if stderr_str: 
                    status_callback(f"colmap2nerf Errors:\n{stderr_str}")

            if return_code != 0:
                 if status_callback:
                     status_callback(f"colmap2nerf process failed with return code {return_code}.")
                 failed_files += 1
            else:
                 if status_callback:
                      status_callback("colmap2nerf conversion successful.")
                      transforms_test_path = os.path.join(project_folder, "transforms_test.json")
                      try:
                          shutil.copy(output_json_path, transforms_test_path)
                          status_callback(f"Copied {output_json_path} to {transforms_test_path}")
                      except Exception as e:
                          status_callback(f"Error copying transforms_train.json to transforms_test.json: {e}")
                          print(f"Error copying transforms_train.json to transforms_test.json: {e}")


        except FileNotFoundError as e:
            msg = f"Error: Required file or folder not found for colmap2nerf. {e}"
            if status_callback: status_callback(msg)
            print(msg)
            failed_files += 1
        except Exception as e:
            if status_callback:
                status_callback(f"An unexpected error occurred while running colmap2nerf: {e}\n{traceback.format_exc()}")
            failed_files += 1

    depths_dir_path = os.path.join(project_folder, "depth_maps")


    if os.path.isdir(depths_dir_path) and os.listdir(depths_dir_path): 
        try:
            if status_callback:
                status_callback("Running make_depth_scale.py...")
            make_depth_scale_script = os.path.join(ROOT_DIR, "gaussian-splatting", "utils", "make_depth_scale.py")
            if not os.path.isdir(depths_dir_path) or not os.listdir(depths_dir_path):
                 raise FileNotFoundError(
                     f"Required depth maps folder not found or is empty: {depths_dir_path}. "
                     "Cannot run make_depth_scale.py without these depths."
                 )
            else:
                 if status_callback:
                      status_callback(f"Using depth maps for make_depth_scale.py: {depths_dir_path}")


            colmap_text_path_for_depth_scale = None
            potential_colmap_path_distorted_for_depth_scale = os.path.join(project_folder, "distorted", "sparse", "0")
            potential_colmap_path_undistorted_for_depth_scale = os.path.join(project_folder, "sparse", "0")

            if os.path.isdir(potential_colmap_path_undistorted_for_depth_scale) and os.listdir(potential_colmap_path_undistorted_for_depth_scale):
                colmap_text_path_for_depth_scale = project_folder
                if status_callback:
                     status_callback(f"Found COLMAP text output for make_depth_scale.py at: {colmap_text_path_for_depth_scale} (using undistorted path)")
            else:
                 raise FileNotFoundError(
                     f"COLMAP text output folder not found or is empty in expected locations ({potential_colmap_path_distorted_for_depth_scale} or {potential_colmap_path_undistorted_for_depth_scale}). "
                     "Cannot run make_depth_scale.py conversion without COLMAP results."
                 )
            base_dir_path = colmap_text_path_for_depth_scale 
            make_depth_scale_command = [
                sys.executable,
                make_depth_scale_script,
                "--base_dir", base_dir_path,
                "--depths_dir", depths_dir_path
            ]

            if status_callback:
                 status_callback(f"Executing command: {' '.join(make_depth_scale_command)}")
                 print(f"Executing command: {' '.join(make_depth_scale_command)}")

            process = subprocess.Popen(
                make_depth_scale_command,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE
            )

            stdout_bytes, stderr_bytes = process.communicate()
            return_code = process.returncode

            stdout_str = stdout_bytes.decode('utf-8', errors='replace')
            stderr_str = stderr_bytes.decode('utf-8', errors='replace')

            if status_callback:
                if stdout_str:
                    status_callback(f"make_depth_scale.py Output:\n{stdout_str}")
                if stderr_str:
                    status_callback(f"make_depth_scale.py Errors:\n{stderr_str}")

            if return_code != 0:
                 if status_callback:
                     status_callback(f"make_depth_scale.py process failed with return code {return_code}.")
                 failed_files += 1
            else:
                 if stdout_str.strip() == "0":
                      status_callback("make_depth_scale.py execution successful.")

        except FileNotFoundError as e:
            msg = f"Error: Required file or folder not found for make_depth_scale.py. {e}"
            if status_callback: status_callback(msg)
            print(msg)
            failed_files += 1
        except Exception as e:
            if status_callback:
                status_callback(f"An unexpected error occurred while running make_depth_scale.py: {e}\n{traceback.format_exc()}")
            failed_files += 1

    if run_colmap2nerf: 
        try:
            if status_callback:
                status_callback("Generating nvdiffrec_config.json...")

            image_width = 1590
            image_height = 718

            if colmap_text_path and os.path.exists(os.path.join(colmap_text_path, "cameras.txt")):
                 try:
                      with open(os.path.join(colmap_text_path, "cameras.txt"), "r") as f:
                           for line in f:
                                if line.strip() and not line.strip().startswith("#"):
                                     els = line.split(" ")
                                     image_width = int(els[2])
                                     image_height = int(els[3])
                                     if status_callback:
                                          status_callback(f"Using image dimensions from COLMAP cameras.txt: {image_width}x{image_height}")
                                     break

                 except Exception as e:
                      if status_callback:
                           status_callback(f"Warning: Could not read image dimensions from cameras.txt for config: {e}. Using default values.")
                      print(f"Warning: Could not read image dimensions from cameras.txt for config: {e}. Using default values.")


            nvdiffrec_folder = os.path.join(ROOT_DIR, "nvdiffrec")
            ref_mesh_path_relative_to_nvdiffrec = os.path.relpath(project_folder, start=nvdiffrec_folder).replace("\\", "/")


            dataset_folder_name = os.path.basename(project_folder)
            output_folder_relative_to_nvdiffrec = os.path.relpath(
                os.path.join(ROOT_DIR, "output", dataset_folder_name, "Nvdiffrec-out"),
                start=nvdiffrec_folder
            ).replace("\\", "/")

            nvdiffrec_config = {
                "ref_mesh": ref_mesh_path_relative_to_nvdiffrec,
                "random_textures": True,
                "iter": nvdiffrec_iterations,
                "save_interval": 100,
                "texture_res": [214, 214],
                "train_res": [image_height, image_width],
                "batch": 2,
                "learning_rate": [0.03, 0.03],
                "dmtet_grid": 64,
                "mesh_scale": 6.0,
                "kd_min": [0.03, 0.03, 0.03],
                "kd_max": [0.8, 0.8, 0.8],
                "ks_min": [0, 0.08, 0.0],
                "ks_max": [0, 1.0, 1.0],
                "background": "white",
                "display": [{"bsdf": "kd"}, {"bsdf": "ks"}],
                "out_dir": output_folder_relative_to_nvdiffrec
            }

            config_output_path = os.path.join(project_folder, "nvdiffrec_config.json")

            with open(config_output_path, "w") as f:
                json.dump(nvdiffrec_config, f, indent=2)

            if status_callback:
                status_callback(f"Generated nvdiffrec_config.json at {config_output_path}")

        except Exception as e:
            if status_callback:
                status_callback(f"Error generating nvdiffrec_config.json: {e}\n{traceback.format_exc()}")
            print(f"Error generating nvdiffrec_config.json: {e}\n{traceback.format_exc()}")
            failed_files += 1

    if run_colmap:
        try:
            if status_callback:
                status_callback("Starting Gaussian Splatting training...")

            gs_train_script = os.path.join(ROOT_DIR, "gaussian-splatting", "train.py")
            
            if not os.path.exists(gs_train_script):
                raise FileNotFoundError(f"Gaussian Splatting train script not found at {gs_train_script}")

            dataset_name = os.path.basename(project_folder)
            model_output_path = os.path.join(ROOT_DIR, "output", dataset_name, "3DGS_Output")
            if os.path.isdir(depths_dir_path) and os.listdir(depths_dir_path): 
                gs_command = [
                    sys.executable,
                    gs_train_script,
                    "-s", project_folder,
                    "-m", model_output_path,
                    "--iterations", f"{gs_iterations}",
                    "--test_iterations", f"{gs_iterations}",
                    "--save_iterations", f"{gs_iterations}",
                    "--exposure_lr_init", "0.001",
                    "--exposure_lr_final", "0.0001",
                    "--exposure_lr_delay_steps", "5000",
                    "--exposure_lr_delay_mult", "0.001",
                    "--train_test_exp",
                    "-d", "depth_maps"
                ]
            else:
                    gs_command = [
                        sys.executable,
                        gs_train_script,
                        "-s", project_folder,
                        "-m", model_output_path,
                        "--iterations", gs_iterations,
                        "--test_iterations", f"{gs_iterations}",
                        "--save_iterations", f"{gs_iterations}"
                    ]
            print(gs_command)
            if status_callback:
                status_callback(f"Executing GS Training Command: {' '.join(gs_command)}")

            return_code = run_command_live(gs_command, cwd=ROOT_DIR, status_callback=status_callback)

            if return_code != 0:
                msg = f"Gaussian Splatting training failed with return code {return_code}."
                if status_callback: status_callback(msg)
                failed_files += 1
            else:
                success_msg = f"Gaussian Splatting training completed successfully. Model saved to: {model_output_path}"
                if status_callback: status_callback(success_msg)

        except Exception as e:
            msg = f"Error during Gaussian Splatting training: {e}\n{traceback.format_exc()}"
            if status_callback: status_callback(msg)
            print(msg)
            failed_files += 1

    config_output_path = os.path.join(project_folder, "nvdiffrec_config.json")
    
    if os.path.exists(config_output_path) and run_colmap2nerf: 
        try:
            if status_callback:
                status_callback("Starting Nvdiffrec training...")

            nvdiffrec_dir = os.path.join(ROOT_DIR, "nvdiffrec")
            nvdiffrec_script = "train.py"

            if not os.path.exists(os.path.join(nvdiffrec_dir, nvdiffrec_script)):
                raise FileNotFoundError(f"Nvdiffrec train script not found at {os.path.join(nvdiffrec_dir, nvdiffrec_script)}")

            abs_config_path = os.path.abspath(config_output_path)

            nvdiffrec_command = [
                sys.executable,
                nvdiffrec_script,
                "--config", abs_config_path
            ]

            if status_callback:
                status_callback(f"Executing Nvdiffrec (CWD: {nvdiffrec_dir}): {' '.join(nvdiffrec_command)}")

            return_code = run_command_live(nvdiffrec_command, cwd=nvdiffrec_dir, status_callback=status_callback)
            dataset_name = os.path.basename(project_folder)
            mesh_path_inner = os.path.join(ROOT_DIR, "nvdiffrec", "output", dataset_name, "Nvdiffrec-out", "dmtet_mesh", "mesh.obj")
            mesh_path_outer = os.path.join(ROOT_DIR, "output", dataset_name, "Nvdiffrec-out", "dmtet_mesh", "mesh.obj")

            final_mesh_path = None
            if os.path.exists(mesh_path_inner):
                final_mesh_path = mesh_path_inner
            elif os.path.exists(mesh_path_outer):
                final_mesh_path = mesh_path_outer

            if return_code != 0:
                if final_mesh_path:
                    warn_msg = (f"Nvdiffrec finished with error code {return_code} (likely texture gradient error), "
                                f"but the output mesh was found at: {final_mesh_path}. \n"
                                "Treating as SUCCESS.")
                    if status_callback: status_callback(warn_msg)
                    print(warn_msg)
                else:
                    msg = f"Nvdiffrec training failed with return code {return_code} and mesh was not found at expected paths."
                    if status_callback: status_callback(msg)
                    print(msg)
                    failed_files += 1
            else:
                success_msg = "Nvdiffrec training completed successfully."
                if status_callback: status_callback(success_msg)
                if final_mesh_path:
                    if status_callback: status_callback(f"Mesh saved at: {final_mesh_path}")
                else:
                    if status_callback: status_callback("Warning: Process finished successfully, but could not automatically verify mesh location.")

        except Exception as e:
            msg = f"Error during Nvdiffrec training: {e}\n{traceback.format_exc()}"
            if status_callback: status_callback(msg)
            print(msg)
            failed_files += 1

    mesh_align_script = os.path.join(ROOT_DIR, "Utils/Mesh_Alignment.py")
    splat_clean_script = os.path.join(ROOT_DIR, "Utils/rembg_splat.py")

    dataset_name = os.path.basename(project_folder)

    input_mesh_path = os.path.join(ROOT_DIR, "output", dataset_name, "Nvdiffrec-out", "dmtet_mesh", "mesh.obj")
    if not os.path.exists(input_mesh_path):
        input_mesh_path = os.path.join(ROOT_DIR, "nvdiffrec", "output", dataset_name, "Nvdiffrec-out", "dmtet_mesh", "mesh.obj")

    input_splat_path = os.path.join(model_output_path, "point_cloud", f"iteration_{gs_iterations}", "point_cloud.ply")
    transform_json_path = os.path.join(project_folder, "transform_meta.json")
    print(f"transform_json_path{transform_json_path}")
    print(f"input_splat_path{input_splat_path}")
    print(f"input_mesh_path{input_mesh_path}")
    if os.path.exists(input_mesh_path) and os.path.exists(transform_json_path):
        try:
            if status_callback: status_callback("Starting Mesh Alignment (Transformation)...")
            
            output_mesh_transformed = input_mesh_path.replace(".obj", "_transformed.obj")

            align_cmd = [
                sys.executable,
                mesh_align_script,
                "--mesh_in", input_mesh_path,
                "--mesh_out", output_mesh_transformed,
                "--meta_path", transform_json_path,
                "--rotate_x_deg", "90",
                "--rotate_y_deg", "0",
                "--rotate_z_deg", "0"
            ]
            run_command_live(align_cmd, cwd=ROOT_DIR, status_callback=status_callback)

            if os.path.exists(output_mesh_transformed):
                if status_callback: status_callback(f"Transformed mesh saved to: {output_mesh_transformed}")
            
        except Exception as e:
            msg = f"Error during Mesh Alignment: {e}"
            print(msg)
            if status_callback: status_callback(msg)
    else:
        if status_callback: status_callback("Skipping Mesh Alignment (Input mesh or transforms.json not found).")
    masks_dir = os.path.join(project_folder, "images_without_background")
    colmap_sparse_dir = os.path.join(project_folder, "sparse", "0")
    if not os.path.exists(colmap_sparse_dir):
         colmap_sparse_dir = os.path.join(project_folder, "distorted", "sparse", "0")

    if do_rembg and os.path.exists(input_splat_path) and os.path.exists(masks_dir) and os.path.exists(colmap_sparse_dir):
        try:
            if status_callback: status_callback("Starting Gaussian Splat Cleaning (Background Removal)...")
            
            output_splat_cleaned = input_splat_path.replace(".ply", "_3DGS.ply")

            clean_cmd = [
                sys.executable,
                splat_clean_script,
                "--input", input_splat_path,
                "--output", output_splat_cleaned,
                "--masks", masks_dir,
                "--colmap", colmap_sparse_dir,
                "--min_visibility", "30",
                "--background_threshold", "0.1"
            ]
            
            run_command_live(clean_cmd, cwd=ROOT_DIR, status_callback=status_callback)
            
            if os.path.exists(output_splat_cleaned):
                 if status_callback: status_callback(f"Cleaned splat saved to: {output_splat_cleaned}")

        except Exception as e:
            msg = f"Error during Splat Cleaning: {e}"
            print(msg)
            if status_callback: status_callback(msg)
    elif do_rembg:
         if status_callback: status_callback("Skipping Splat Cleaning - Missing masks or COLMAP data.")
         
    outFolder = os.path.join(ROOT_DIR, "output", dataset_name)
    nvdiffout = os.path.join(ROOT_DIR,"nvdiffrec", "output", dataset_name, "Nvdiffrec-out")
    nowy_splat = os.path.join(outFolder, f"{dataset_name}_3DGS.ply")
    nowy_mesh = os.path.join(outFolder, f"{dataset_name}_Mesh.obj")
    if do_rembg and os.path.exists(output_splat_cleaned):
        shutil.move(output_splat_cleaned, nowy_splat)
    else:
        shutil.move(input_splat_path, nowy_splat)
    if run_colmap2nerf:
        shutil.move(output_mesh_transformed, nowy_mesh)
        shutil.move(nvdiffout, outFolder)

    if progress_callback:
        progress_callback(1.0)
    if status_callback:
        summary = f"Finished. Processed: {processed_files}, Errors: {failed_files}"
        status_callback(summary)

