# Installation Guide

This guide provides step-by-step instructions to set up the environment and dependencies for the UnitySplat2Data Generator.

## Prerequisites

* **Operating System:** Windows (recommended) or Linux.
* **Hardware:** NVIDIA GPU with CUDA support.
* **Software:** [Anaconda](https://www.anaconda.com/) or Miniconda, [Git](https://git-scm.com/).

---

## 1. Environment Setup

Create a new Conda environment with Python 3.9 and activate it:

```bash
conda create -n generator python=3.9 -y
conda activate generator
```

## 2. Cloning Repositories

Setup the project directory structure by cloning the required repositories:

```bash
# 1. Clone the main Unity Gaussian Splatting repo
git clone [https://github.com/aras-p/UnityGaussianSplatting.git](https://github.com/aras-p/UnityGaussianSplatting.git)

# 2. Navigate to projects folder
cd UnityGaussianSplatting/projects

# 3. Clone UnitySplat2Data
git clone [https://github.com/PatrykNi/UnitySplat2Data.git](https://github.com/PatrykNi/UnitySplat2Data.git)

# 4. Navigate to the Generator directory
cd UnitySplat2Data/Generator

# 5. Clone sub-dependencies
git clone [https://github.com/graphdeco-inria/gaussian-splatting](https://github.com/graphdeco-inria/gaussian-splatting) --recursive
git clone [https://github.com/DepthAnything/Depth-Anything-V2](https://github.com/DepthAnything/Depth-Anything-V2)
git clone [https://github.com/NVlabs/nvdiffrec.git](https://github.com/NVlabs/nvdiffrec.git)
```

## 3. Downloading Model Weights

Download the pre-trained weights for **Depth Anything V2**:

**Using Command Line:**

```cmd
mkdir "Depth-Anything-V2\\checkpoints"
curl -L "[https://huggingface.co/depth-anything/Depth-Anything-V2-Large/resolve/main/depth_anything_v2_vitl.pth](https://huggingface.co/depth-anything/Depth-Anything-V2-Large/resolve/main/depth_anything_v2_vitl.pth)" -o "Depth-Anything-V2\\checkpoints\\depth_anything_v2_vitl.pth"
```

*Alternatively, manually download `depth_anything_v2_vitl.pth` from [Hugging Face](https://huggingface.co/depth-anything/Depth-Anything-V2-Large/resolve/main/depth_anything_v2_vitl.pth) and place it in the `Depth-Anything-V2/checkpoints` folder.*

## 4. Installing Python Dependencies

Install PyTorch (CUDA 12.1 version) and other required packages:

```bash
# Install PyTorch
pip install torch==2.1.2 torchvision==0.16.2 torchaudio==2.1.2 --index-url [https://download.pytorch.org/whl/cu121](https://download.pytorch.org/whl/cu121)

# Install requirements from file
pip install -r requirements.txt

# Install utility packages
pip install joblib trimesh plyfile
imageio_download_bin freeimage
```

### Installing Compiled Extensions

*Note: Ensure you have a C++ compiler installed (e.g., Visual Studio with C++ development workload on Windows).*

```bash
# Nvdiffrast & Tiny CUDA NN
pip install git+[https://github.com/NVlabs/nvdiffrast/](https://github.com/NVlabs/nvdiffrast/) --no-build-isolation
pip install git+[https://github.com/NVlabs/tiny-cuda-nn#subdirectory=bindings/torch](https://github.com/NVlabs/tiny-cuda-nn#subdirectory=bindings/torch) --no-build-isolation

# Diff Gaussian Rasterization
pip install ./gaussian-splatting/submodules/diff-gaussian-rasterization --no-build-isolation

# Conda system dependencies
conda install -c conda-forge zlib-wapi cudnn
```

## 5. External Tools & Assets

### COLMAP

Download and install COLMAP. Ensure the executable is in your system PATH.
* [Download COLMAP Releases](https://github.com/colmap/colmap/releases)

### Project Assets

You need to download the required `Objects` folder manually:

1. Download the folder from this link: [Mega.nz Download](https://mega.nz/folder/V0QwHQKQ#gRVSqcqy-vauphTIT3ydQw)
2. Extract/Move the `Objects` folder into the following directory:
   
   `UnityGaussianSplatting/Projects/UnitySplat2Data/Assets/`