import os
import sys
from Utils.gui import App

# --- Configuration ---
DEPTH_ANYTHING_V2_PATH_CONFIG = os.path.join(os.path.dirname(__file__), "Depth-Anything-V2")
REMBG_MODEL = 'birefnet-massive'
MAX_IMAGE_DIMENSION = 1590

if __name__ == "__main__":
    if DEPTH_ANYTHING_V2_PATH_CONFIG and os.path.isdir(
            DEPTH_ANYTHING_V2_PATH_CONFIG) and DEPTH_ANYTHING_V2_PATH_CONFIG not in sys.path:
        sys.path.append(DEPTH_ANYTHING_V2_PATH_CONFIG)

    app = App(DEPTH_ANYTHING_V2_PATH_CONFIG, REMBG_MODEL,
              MAX_IMAGE_DIMENSION)  # Pass configuration
    app.mainloop()
