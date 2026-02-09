#
# Copyright (C) 2023, Inria
# GRAPHDECO research group, https://team.inria.fr/graphdeco
# All rights reserved.
#
# This software is free for non-commercial, research and evaluation use
# under the terms of the LICENSE.md file.
#
# For inquiries contact  george.drettakis@inria.fr
#
# Improved by Patryk Nizeniec pnizeniec@gmail.com



import os
import logging
import subprocess
import shutil

def run_colmap(source_path, colmap_executable="colmap", no_gpu=False, 
               skip_matching=False, camera="SIMPLE_PINHOLE", 
               magick_executable="magick", auto_reconstruct=True, resize=False,
               status_callback=None, progress_callback=None):
    """
    Run COLMAP processing on a project folder with real-time progress feedback.
    
    Args:
        source_path: Path to the project folder
        colmap_executable: Path to COLMAP executable
        no_gpu: If True, don't use GPU
        skip_matching: If True, skip feature matching
        camera: Camera model to use
        magick_executable: Path to ImageMagick executable
        auto_reconstruct: Use COLMAP automatic reconstruction
        resize: Resize images after processing
        status_callback: Function to call with status updates
        progress_callback: Function to call with progress updates (0-1)
    """
    # Configure logging
    logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
    
    # Update status
    def update_status(message, progress=None):
        logging.info(message)
        if status_callback:
            status_callback(message)
        if progress_callback and progress is not None:
            progress_callback(progress)
    
    update_status("Starting COLMAP processing...", 0.0)
    
    # Convenience method for command execution with real-time output
    def run_command(command):
        """
        Run a system command and stream output in real-time
        """
        update_status(f"Running command: {command}")
        try:
            # Use subprocess.Popen instead of subprocess.run to stream output in real-time
            process = subprocess.Popen(
                command,
                shell=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,  # Redirect stderr to stdout to capture all output
                text=True,
                bufsize=1  # Line buffered
            )
            
            # Stream output in real-time
            for line in iter(process.stdout.readline, ''):
                print(line, end='')  # Print directly to console
                if status_callback:
                    status_callback(line.strip())  # Update status with current line
                
            # Wait for process to complete and get return code
            process.stdout.close()
            return_code = process.wait()
            
            if return_code != 0:
                update_status(f"Command failed with code {return_code}")
            
            return return_code
        except Exception as e:
            update_status(f"Exception running command: {e}")
            return -1
    
    # Helper for COLMAP commands with GPU support
    def run_colmap_command(command_base, supports_gpu=True, fallback_to_cpu=True):
        """
        Run a COLMAP command with GPU support and fallback to CPU if needed
        """
        use_gpu = not no_gpu
        
        # First try with GPU if requested and supported
        if use_gpu and supports_gpu:
            gpu_command = f"{command_base} --use_gpu 1"
            exit_code = run_command(gpu_command)
            
            # If GPU command fails and fallback is enabled, try CPU
            if exit_code != 0 and fallback_to_cpu:
                update_status("GPU execution failed, falling back to CPU...")
                cpu_command = f"{command_base} --use_gpu 0"
                exit_code = run_command(cpu_command)
                if exit_code != 0:
                    update_status(f"Command failed on CPU with code {exit_code}")
                    return exit_code
                return exit_code
            return exit_code
        else:
            # Run without GPU flag if not supported or GPU not requested
            if supports_gpu:
                command = f"{command_base} --use_gpu 0"
            else:
                command = command_base
            
            exit_code = run_command(command)
            return exit_code

    colmap_command = f'"{colmap_executable}"' if len(colmap_executable) > 0 else "colmap"
    magick_command = f'"{magick_executable}"' if len(magick_executable) > 0 else "magick"

    # Define paths
    colmap_input_folder = os.path.join(source_path, "preprocessed_images")
    if not os.path.exists(colmap_input_folder):
        colmap_input_folder = os.path.join(source_path, "input")
    print(colmap_input_folder)
    update_status(f"Using input folder: {colmap_input_folder}")
    
    final_images_folder = os.path.join(source_path, "images")
    os.makedirs(final_images_folder, exist_ok=True)  # Ensure /images exists

    # Make sure distorted directory exists
    distorted_dir = os.path.join(source_path, "distorted")
    os.makedirs(distorted_dir, exist_ok=True)
    os.makedirs(os.path.join(distorted_dir, "sparse"), exist_ok=True)

    # Handle automatic reconstruction if requested
    progress = 0.05
    update_status("Setting up directories...", progress)
    
    if auto_reconstruct:
        progress = 0.1
        update_status("Starting automatic reconstruction...", progress)
        
        os.makedirs(os.path.join(source_path, "sparse"), exist_ok=True)
        auto_cmd_base = (
            f"{colmap_command} automatic_reconstructor "
            f"--image_path {colmap_input_folder} "
            f"--workspace_path {source_path} "
            f"--dense 0 "
            f"--quality HIGH "
            f"--single_camera 1 "
            f"--camera_model {camera}"
        )
        
        exit_code = run_colmap_command(auto_cmd_base)
        if exit_code != 0:
            update_status(f"Automatic reconstruction failed with code {exit_code}. Exiting.", 1.0)
            return

        progress = 0.5
        update_status("Copying reconstruction results...", progress)
        
        # Copy results to the distorted folder to match manual pipeline structure
        sparse_src_dir = os.path.join(source_path, "sparse", "0")
        sparse_dst_dir = os.path.join(distorted_dir, "sparse", "0")
        
        if os.path.exists(sparse_src_dir):
            # Create destination directory
            os.makedirs(sparse_dst_dir, exist_ok=True)
            
            # Copy all files from source to destination
            for file in os.listdir(sparse_src_dir):
                src_file = os.path.join(sparse_src_dir, file)
                dst_file = os.path.join(sparse_dst_dir, file)
                if os.path.isfile(src_file):
                    shutil.copy2(src_file, dst_file)
            
            update_status("Copied sparse reconstruction to distorted/sparse/0")
        else:
            update_status(f"Sparse reconstruction directory {sparse_src_dir} not found")
        
        # Copy database.db if it exists
        db_src = os.path.join(source_path, "database.db")
        db_dst = os.path.join(distorted_dir, "database.db")
        
        if os.path.exists(db_src):
            shutil.copy2(db_src, db_dst)
            update_status("Copied database.db to distorted/")
        else:
            update_status(f"Database file {db_src} not found")

    # Manual pipeline if not using auto_reconstruct and not skipping matching
    elif not skip_matching:
        progress = 0.1
        update_status("Starting feature extraction...", progress)
        
        # Feature extraction
        feat_extraction_cmd_base = (
            f"{colmap_command} feature_extractor "
            f"--database_path {os.path.join(distorted_dir, 'database.db')} "
            f"--image_path {colmap_input_folder} "
            f"--ImageReader.single_camera 1 "
            f"--ImageReader.camera_model {camera}"
        )
        
        exit_code = run_colmap_command(feat_extraction_cmd_base)
        if exit_code != 0:
            update_status(f"Feature extraction failed with code {exit_code}. Exiting.", 1.0)
            return

        progress = 0.3
        update_status("Starting feature matching...", progress)
        
        # Feature matching
        feat_matching_cmd_base = (
            f"{colmap_command} exhaustive_matcher "
            f"--database_path {os.path.join(distorted_dir, 'database.db')}"
        )
        
        exit_code = run_colmap_command(feat_matching_cmd_base)
        if exit_code != 0:
            update_status(f"Feature matching failed with code {exit_code}. Exiting.", 1.0)
            return

        progress = 0.5
        update_status("Starting bundle adjustment...", progress)
        
        # Bundle adjustment
        mapper_cmd_base = (
            f"{colmap_command} mapper "
            f"--database_path {os.path.join(distorted_dir, 'database.db')} "
            f"--image_path {colmap_input_folder} "
            f"--output_path {os.path.join(distorted_dir, 'sparse')} "
            f"--Mapper.ba_global_function_tolerance=0.000001"
        )
        
        exit_code = run_colmap_command(mapper_cmd_base)
        if exit_code != 0:
            update_status(f"Mapper failed with code {exit_code}. Exiting.", 1.0)
            return

    progress = 0.6
    update_status("Converting model format to TXT...", progress)
    
    # Convert BIN to TXT for NeRF compatibility - doesn't support GPU flag
    for sparse_path in [
        os.path.join(source_path, "sparse", "0"),
        os.path.join(distorted_dir, "sparse", "0")
    ]:
        if os.path.exists(sparse_path):
            txt_convert_cmd = (
                f"{colmap_command} model_converter "
                f"--input_path {sparse_path} "
                f"--output_path {sparse_path} "
                f"--output_type TXT"
            )

            exit_code = run_command(txt_convert_cmd)
            if exit_code != 0:
                update_status(f"TXT conversion failed for {sparse_path} with code {exit_code}. Continuing anyway.")

    progress = 0.7
    update_status("Starting image undistortion...", progress)
    
    # Image undistortion - doesn't support GPU flag
    input_sparse_path = os.path.join(distorted_dir, "sparse", "0")

    # Check if the input path exists
    if not os.path.exists(input_sparse_path):
        update_status(f"Sparse reconstruction path {input_sparse_path} does not exist. Cannot proceed with image undistortion.", 1.0)
        return

    img_undist_cmd = (
        f"{colmap_command} image_undistorter "
        f"--image_path {colmap_input_folder} "
        f"--input_path {input_sparse_path} "
        f"--output_path {source_path} "
        f"--output_type COLMAP"
    )

    exit_code = run_command(img_undist_cmd)
    if exit_code != 0:
        update_status(f"Image undistortion failed with code {exit_code}. Exiting.", 1.0)
        return

    progress = 0.8
    update_status("Moving undistorted images to /images folder...", progress)
    
    # Move undistorted images to /images folder
    undistorted_images_folder = os.path.join(source_path, "images")
    if os.path.exists(undistorted_images_folder):
        for filename in os.listdir(undistorted_images_folder):
            source_file = os.path.join(undistorted_images_folder, filename)
            destination_file = os.path.join(final_images_folder, filename)
            if os.path.isfile(source_file) and source_file != destination_file:
                os.makedirs(os.path.dirname(destination_file), exist_ok=True)
                shutil.move(source_file, destination_file)

    # Handle resizing if requested
    if resize:
        progress = 0.85
        update_status("Resizing images to different scales...", progress)
        
        # Create directories for resized images
        os.makedirs(os.path.join(source_path, "images_2"), exist_ok=True)
        os.makedirs(os.path.join(source_path, "images_4"), exist_ok=True)
        os.makedirs(os.path.join(source_path, "images_8"), exist_ok=True)
        
        # Get the list of files in the source directory (now from /images)
        files = os.listdir(final_images_folder)
        
        # Copy and resize each file
        for file in files:
            source_file = os.path.join(final_images_folder, file)
            if not os.path.isfile(source_file):
                continue

            # 50% resize
            destination_file = os.path.join(source_path, "images_2", file)
            shutil.copy2(source_file, destination_file)
            exit_code = run_command(f"{magick_command} mogrify -resize 50% {destination_file}")
            if exit_code != 0:
                update_status(f"50% resize failed with code {exit_code}. Exiting.", 1.0)
                return

            # 25% resize
            destination_file = os.path.join(source_path, "images_4", file)
            shutil.copy2(source_file, destination_file)
            exit_code = run_command(f"{magick_command} mogrify -resize 25% {destination_file}")
            if exit_code != 0:
                update_status(f"25% resize failed with code {exit_code}. Exiting.", 1.0)
                return

            # 12.5% resize
            destination_file = os.path.join(source_path, "images_8", file)
            shutil.copy2(source_file, destination_file)
            exit_code = run_command(f"{magick_command} mogrify -resize 12.5% {destination_file}")
            if exit_code != 0:
                update_status(f"12.5% resize failed with code {exit_code}. Exiting.", 1.0)
                return

    progress = 0.95
    update_status("Finalizing processing...", progress)
    
    # Final step: Copy images without background to /images if available
    '''
    if os.path.exists(nobg_images_folder):
        update_status(f"Copying images without background from {nobg_images_folder} to {final_images_folder}")
        for filename in os.listdir(nobg_images_folder):
            source_file = os.path.join(nobg_images_folder, filename)
            destination_file = os.path.join(final_images_folder, filename)
            if os.path.isfile(source_file):
                shutil.copy2(source_file, destination_file)
        update_status("Successfully copied images without background.")
    else:
        update_status("Folder with images without background (/input_nobg) not found. Using processed images with background.")
    '''
    progress = 1.0
    
    update_status("COLMAP processing completed successfully.", progress)

# When run directly as a script
if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser("Colmap converter")
    parser.add_argument("--no_gpu", action='store_true')
    parser.add_argument("--skip_matching", action='store_true')
    parser.add_argument("--source_path", "-s", required=True, type=str)
    parser.add_argument("--camera", default="SIMPLE_PINHOLE", type=str)
    parser.add_argument("--colmap_executable", default="", type=str)
    parser.add_argument("--resize", action="store_true")
    parser.add_argument("--magick_executable", default="", type=str)
    parser.add_argument("--auto_reconstruct", action="store_true", help="Use COLMAP automatic reconstruction with HIGH quality")
    args = parser.parse_args()
    
    run_colmap(
        source_path=args.source_path,
        colmap_executable=args.colmap_executable,
        no_gpu=args.no_gpu,
        skip_matching=args.skip_matching,
        camera=args.camera,
        magick_executable=args.magick_executable,
        auto_reconstruct=args.auto_reconstruct,
        resize=args.resize
    )