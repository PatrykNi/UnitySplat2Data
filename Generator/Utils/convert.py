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
import sys
from argparse import ArgumentParser
import shutil
import subprocess

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')

# This Python script is based on the shell converter script provided in the MipNerF 360 repository.
parser = ArgumentParser("Colmap converter")
parser.add_argument("--no_gpu", action='store_true')
parser.add_argument("--skip_matching", action='store_true')
parser.add_argument("--source_path", "-s", required=True, type=str)
parser.add_argument("--camera", default="SIMPLE_PINHOLE", type=str)
parser.add_argument("--colmap_executable", default="", type=str)
parser.add_argument("--resize", action="store_true")
parser.add_argument("--magick_executable", default="", type=str)
parser.add_argument("--auto_reconstruct", action="store_true", help="Use COLMAP automatic reconstruction with HIGH quality")
args = parser.parse_args()
colmap_command = '"{}"'.format(args.colmap_executable) if len(args.colmap_executable) > 0 else "colmap"
magick_command = '"{}"'.format(args.magick_executable) if len(args.magick_executable) > 0 else "magick"
use_gpu = not args.no_gpu

# Define paths
colmap_input_folder = os.path.join(args.source_path, "input_preprocessed")
print(colmap_input_folder)
if not os.path.exists(colmap_input_folder):
    colmap_input_folder = os.path.join(args.source_path, "input")
print(colmap_input_folder)
nobg_images_folder = os.path.join(args.source_path, "input_nobg")
final_images_folder = os.path.join(args.source_path, "images")
os.makedirs(final_images_folder, exist_ok=True)  # Ensure /images exists

# Make sure distorted directory exists
distorted_dir = os.path.join(args.source_path, "distorted")
os.makedirs(distorted_dir, exist_ok=True)
os.makedirs(os.path.join(distorted_dir, "sparse"), exist_ok=True)

def run_command(command):
    """
    Run a system command and stream output in real-time
    """
    logging.info(f"Running command: {command}")
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
            print(line, end='')  # Print directly to console in real-time
            
        # Wait for process to complete and get return code
        process.stdout.close()
        return_code = process.wait()
        
        if return_code != 0:
            logging.warning(f"Command failed with code {return_code}")
        
        return return_code
    except Exception as e:
        logging.error(f"Exception running command: {e}")
        return -1

def run_colmap_command(command_base, supports_gpu=True, fallback_to_cpu=True):
    """
    Run a COLMAP command with GPU support and fallback to CPU if needed
    
    Args:
        command_base: The base COLMAP command without GPU flag
        supports_gpu: Whether this command supports the --use_gpu flag
        fallback_to_cpu: Whether to try CPU if GPU fails
        
    Returns:
        Exit code of the successful command or the last attempted command
    """
    # First try with GPU if requested and supported
    if use_gpu and supports_gpu:
        gpu_command = f"{command_base} --use_gpu 1"
        exit_code = run_command(gpu_command)
        
        # If GPU command fails and fallback is enabled, try CPU
        if exit_code != 0 and fallback_to_cpu:
            logging.warning("GPU execution failed, falling back to CPU...")
            cpu_command = f"{command_base} --use_gpu 0"
            exit_code = run_command(cpu_command)
            if exit_code != 0:
                logging.error(f"Command failed on CPU with code {exit_code}")
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

# Handle automatic reconstruction if requested
if args.auto_reconstruct:
    os.makedirs(os.path.join(args.source_path, "sparse"), exist_ok=True)
    auto_cmd_base = (
        f"{colmap_command} automatic_reconstructor "
        f"--image_path {colmap_input_folder} "
        f"--workspace_path {args.source_path} "
        f"--dense 0 "
        f"--quality HIGH "
        f"--single_camera 1 "
        f"--camera_model {args.camera}"
    )
    
    exit_code = run_colmap_command(auto_cmd_base)
    if exit_code != 0:
        logging.error(f"Automatic reconstruction failed with code {exit_code}. Exiting.")
        sys.exit(exit_code)

    # Copy results to the distorted folder to match manual pipeline structure
    logging.info("Copying reconstruction results to maintain consistent folder structure...")
    
    # Copy sparse reconstruction to distorted/sparse/0
    sparse_src_dir = os.path.join(args.source_path, "sparse", "0")
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
        
        logging.info(f"Copied sparse reconstruction from {sparse_src_dir} to {sparse_dst_dir}")
    else:
        logging.warning(f"Sparse reconstruction directory {sparse_src_dir} not found")
    
    # Copy database.db if it exists
    db_src = os.path.join(args.source_path, "database.db")
    db_dst = os.path.join(distorted_dir, "database.db")
    
    if os.path.exists(db_src):
        shutil.copy2(db_src, db_dst)
        logging.info(f"Copied database from {db_src} to {db_dst}")
    else:
        logging.warning(f"Database file {db_src} not found")

# Manual pipeline if not using auto_reconstruct and not skipping matching
if not args.skip_matching and not args.auto_reconstruct:
    # Feature extraction
    feat_extraction_cmd_base = (
        f"{colmap_command} feature_extractor "
        f"--database_path {os.path.join(distorted_dir, 'database.db')} "
        f"--image_path {colmap_input_folder} "
        f"--ImageReader.single_camera 1 "
        f"--ImageReader.camera_model {args.camera}"
    )
    
    exit_code = run_colmap_command(feat_extraction_cmd_base)
    if exit_code != 0:
        logging.error(f"Feature extraction failed with code {exit_code}. Exiting.")
        sys.exit(exit_code)

    # Feature matching
    feat_matching_cmd_base = (
        f"{colmap_command} exhaustive_matcher "
        f"--database_path {os.path.join(distorted_dir, 'database.db')}"
    )
    
    exit_code = run_colmap_command(feat_matching_cmd_base)
    if exit_code != 0:
        logging.error(f"Feature matching failed with code {exit_code}. Exiting.")
        sys.exit(exit_code)

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
        logging.error(f"Mapper failed with code {exit_code}. Exiting.")
        sys.exit(exit_code)

# Convert BIN to TXT for NeRF compatibility - doesn't support GPU flag
# We need to convert both in sparse/0 and distorted/sparse/0
for sparse_path in [
    os.path.join(args.source_path, "sparse", "0"),
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
            logging.warning(f"TXT conversion failed for {sparse_path} with code {exit_code}. Continuing anyway.")

# Image undistortion - doesn't support GPU flag
input_sparse_path = os.path.join(distorted_dir, "sparse", "0")

# Check if the input path exists
if not os.path.exists(input_sparse_path):
    logging.error(f"Sparse reconstruction path {input_sparse_path} does not exist. Cannot proceed with image undistortion.")
    sys.exit(1)

img_undist_cmd = (
    f"{colmap_command} image_undistorter "
    f"--image_path {colmap_input_folder} "
    f"--input_path {input_sparse_path} "
    f"--output_path {args.source_path} "
    f"--output_type COLMAP"
)

exit_code = run_command(img_undist_cmd)
if exit_code != 0:
    logging.error(f"Image undistortion failed with code {exit_code}. Exiting.")
    sys.exit(exit_code)

# Move undistorted images to /images folder
undistorted_images_folder = os.path.join(args.source_path, "images")
if os.path.exists(undistorted_images_folder):
    for filename in os.listdir(undistorted_images_folder):
        source_file = os.path.join(undistorted_images_folder, filename)
        destination_file = os.path.join(final_images_folder, filename)
        if os.path.isfile(source_file) and source_file != destination_file:
            os.makedirs(os.path.dirname(destination_file), exist_ok=True)
            shutil.move(source_file, destination_file)

# Handle resizing if requested
if args.resize:
    logging.info("Copying and resizing images...")

    # Create directories for resized images
    os.makedirs(os.path.join(args.source_path, "images_2"), exist_ok=True)
    os.makedirs(os.path.join(args.source_path, "images_4"), exist_ok=True)
    os.makedirs(os.path.join(args.source_path, "images_8"), exist_ok=True)
    
    # Get the list of files in the source directory (now from /images)
    files = os.listdir(final_images_folder)
    
    # Copy and resize each file
    for file in files:
        source_file = os.path.join(final_images_folder, file)
        if not os.path.isfile(source_file):
            continue

        # 50% resize
        destination_file = os.path.join(args.source_path, "images_2", file)
        shutil.copy2(source_file, destination_file)
        exit_code = run_command(f"{magick_command} mogrify -resize 50% {destination_file}")
        if exit_code != 0:
            logging.error(f"50% resize failed with code {exit_code}. Exiting.")
            sys.exit(exit_code)

        # 25% resize
        destination_file = os.path.join(args.source_path, "images_4", file)
        shutil.copy2(source_file, destination_file)
        exit_code = run_command(f"{magick_command} mogrify -resize 25% {destination_file}")
        if exit_code != 0:
            logging.error(f"25% resize failed with code {exit_code}. Exiting.")
            sys.exit(exit_code)

        # 12.5% resize
        destination_file = os.path.join(args.source_path, "images_8", file)
        shutil.copy2(source_file, destination_file)
        exit_code = run_command(f"{magick_command} mogrify -resize 12.5% {destination_file}")
        if exit_code != 0:
            logging.error(f"12.5% resize failed with code {exit_code}. Exiting.")
            sys.exit(exit_code)

# Final step: Copy images without background to /images if available
if os.path.exists(nobg_images_folder):
    logging.info(f"Copying images without background from {nobg_images_folder} to {final_images_folder}")
    for filename in os.listdir(nobg_images_folder):
        source_file = os.path.join(nobg_images_folder, filename)
        destination_file = os.path.join(final_images_folder, filename)
        if os.path.isfile(source_file):
            shutil.copy2(source_file, destination_file)
    logging.info("Successfully copied images without background.")
else:
    logging.info("Folder with images without background (/input_nobg) not found. Using processed images with background.")

logging.info("COLMAP processing completed successfully.")