import customtkinter as ctk
from customtkinter import filedialog
import os
import threading
import traceback
import datetime
import sys

class App(ctk.CTk):
    def __init__(self, depth_anything_path, rembg_model_name,
                 max_image_dimension):
        super().__init__()

        self.title("3D Asset Generator Pipeline")
        self.geometry("650x550") 
        ctk.set_appearance_mode("system")
        ctk.set_default_color_theme("blue")

        self.project_directory = ctk.StringVar(value="")
        self.rembg_model_var = ctk.StringVar(
            value=rembg_model_name)
        self.max_dim_var = ctk.StringVar(
            value=str(max_image_dimension) if max_image_dimension else "")
        self.depth_anything_path = depth_anything_path
        
        # Zmienne iteracji
        self.gs_iterations_var = ctk.StringVar(value="7000")
        self.nvdiffrec_iterations_var = ctk.StringVar(value="1000")

        # Zmienna trybu
        self.processing_mode_var = ctk.StringVar(value="Object")
        
        # Zmienne logiczne (sterowane kodem)
        self.do_colmap_var = ctk.BooleanVar(value=True)
        self.do_colmap2nerf_var = ctk.BooleanVar(value=True)
        
        # Zmienne dla opcji (dla kompatybilności z kodem wrappera)
        self.do_rembg_var = ctk.BooleanVar(value=True)
        self.do_depth_var = ctk.BooleanVar(value=True)
        self.save_preprocessed_var = ctk.BooleanVar(value=True)
        self.save_nobg_var = ctk.BooleanVar(value=True)
        self.save_mask_var = ctk.BooleanVar(value=True)
        self.save_depth_var = ctk.BooleanVar(value=True)
        self.save_depth_nobg_var = ctk.BooleanVar(value=False)

        self.create_widgets()
        self.update_ui_state() # Inicjalizacja stanu UI

    def create_widgets(self):
        # --- 1. Wybór folderu projektu ---
        self.frame_project = ctk.CTkFrame(self)
        self.frame_project.pack(pady=10, padx=20, fill="x")
        self.label_project = ctk.CTkLabel(self.frame_project, text="Project Directory:")
        self.label_project.pack(side="left", padx=10)
        self.entry_project = ctk.CTkEntry(self.frame_project,
                                          textvariable=self.project_directory,
                                          width=300)
        self.entry_project.pack(side="left", padx=10)
        self.btn_browse = ctk.CTkButton(self.frame_project, text="Browse",
                                        command=self.browse_directory)
        self.btn_browse.pack(side="left", padx=10)

        # --- 2. Ustawienia Główne ---
        self.frame_settings = ctk.CTkFrame(self)
        self.frame_settings.pack(pady=10, padx=20, fill="x")

        # Rząd 0: Wymiar i Model
        self.label_dim = ctk.CTkLabel(self.frame_settings,
                                      text="Max Image Dimension (px):")
        self.label_dim.grid(row=0, column=0, padx=10, pady=5, sticky="w")
        self.entry_dim = ctk.CTkEntry(self.frame_settings,
                                      textvariable=self.max_dim_var, width=100)
        self.entry_dim.grid(row=0, column=1, padx=10, pady=5, sticky="w")

        # --- LISTA MODELI REMBG (ZAMIAST ENTRY) ---
        self.label_model = ctk.CTkLabel(self.frame_settings,
                                        text="Rembg Model:")
        self.label_model.grid(row=0, column=2, padx=10, pady=5, sticky="w")
        
        rembg_models = [
            "u2net", 
            "u2netp", 
            "u2net_human_seg", 
            "u2net_cloth_seg", 
            "silueta", 
            "isnet-general-use", 
            "isnet-anime", 
            "sam", 
            "birefnet-general", 
            "birefnet-general-lite", 
            "birefnet-portrait", 
            "birefnet-dis", 
            "birefnet-hrsod", 
            "birefnet-cod", 
            "birefnet-massive", 
            "bria-rmbg"
        ]
        
        self.option_model = ctk.CTkOptionMenu(self.frame_settings,
                                              variable=self.rembg_model_var,
                                              values=rembg_models,
                                              width=150)
        self.option_model.grid(row=0, column=3, padx=10, pady=5, sticky="w")

        # Rząd 1: Iteracje 
        self.label_gs_iter = ctk.CTkLabel(self.frame_settings, text="3DGS Iterations:")
        self.label_gs_iter.grid(row=1, column=0, padx=10, pady=5, sticky="w")
        self.entry_gs_iter = ctk.CTkEntry(self.frame_settings, textvariable=self.gs_iterations_var, width=100)
        self.entry_gs_iter.grid(row=1, column=1, padx=10, pady=5, sticky="w")

        self.label_nv_iter = ctk.CTkLabel(self.frame_settings, text="Nvdiffrec Iterations:")
        self.label_nv_iter.grid(row=1, column=2, padx=10, pady=5, sticky="w")
        self.entry_nv_iter = ctk.CTkEntry(self.frame_settings, textvariable=self.nvdiffrec_iterations_var, width=150)
        self.entry_nv_iter.grid(row=1, column=3, padx=10, pady=5, sticky="w")

        # --- 3. Tryb Przetwarzania ---
        self.frame_mode = ctk.CTkFrame(self)
        self.frame_mode.pack(pady=5, padx=20, fill="x")
        self.label_mode = ctk.CTkLabel(self.frame_mode, text="Processing Type:", font=("Roboto", 14, "bold"))
        self.label_mode.pack(side="top", pady=(5, 5))
        
        self.radio_object = ctk.CTkRadioButton(
            self.frame_mode, 
            text="Object (Remove Background)", 
            variable=self.processing_mode_var, 
            value="Object",
            command=self.update_ui_state
        )
        self.radio_object.pack(side="left", padx=40, pady=10)
        
        self.radio_scene = ctk.CTkRadioButton(
            self.frame_mode, 
            text="Scene (Keep Background)", 
            variable=self.processing_mode_var, 
            value="Scene",
            command=self.update_ui_state
        )
        self.radio_scene.pack(side="right", padx=40, pady=10)

        # --- 4. Przycisk Start ---
        self.start_button = ctk.CTkButton(self, text="Start Processing",
                                          command=self.start_processing_thread,
                                          height=40,
                                          font=("Roboto", 16, "bold"),
                                          fg_color="green",
                                          hover_color="darkgreen")
        self.start_button.pack(pady=20, padx=20, fill="x")

        # --- 5. Log ---
        self.status_textbox = ctk.CTkTextbox(self, width=600, height=200)
        self.status_textbox.pack(pady=10, padx=20, fill="both", expand=True)
        self.status_textbox.insert("0.0", "Ready.\n")
        self.status_textbox.configure(state="disabled")

    def browse_directory(self):
        directory = filedialog.askdirectory()
        if directory:
            self.project_directory.set(directory)

    def update_ui_state(self):
        """Aktualizuje UI (kolory i stan) dla lepszej czytelności."""
        mode = self.processing_mode_var.get()

        # Domyślne kolory CustomTkinter (dla trybu jasnego/ciemnego)
        default_entry_fg = ['#F9F9FA', '#343638']
        default_text_color = ['#000000', '#DCE4EE']
        
        # Kolory dla stanu "Disabled"
        # UWAGA: CTkEntry nie obsługuje "transparent", więc musimy podać konkretny kolor szary
        disabled_entry_fg = ['#EBEBEB', '#2B2B2B'] 
        disabled_text_color = "gray"

        if mode == "Object":
            # --- AKTYWACJA NVDIFFREC ---
            # Etykieta normalna
            self.label_nv_iter.configure(text_color=default_text_color)
            # Pole aktywne
            self.entry_nv_iter.configure(
                state="normal", 
                text_color=default_text_color,
                fg_color=default_entry_fg
            )
            
            # Wartości domyślne dla Object
            if self.gs_iterations_var.get() == "30000": 
                self.gs_iterations_var.set("7000")
            
        elif mode == "Scene":
            # --- DEAKTYWACJA NVDIFFREC (WYSZARZENIE) ---
            # Etykieta szara
            self.label_nv_iter.configure(text_color=disabled_text_color)
            # Pole zablokowane i szare
            self.entry_nv_iter.configure(
                state="disabled", 
                text_color=disabled_text_color, 
                fg_color=disabled_entry_fg 
            )
            
            # Wartości domyślne dla Scene
            if self.gs_iterations_var.get() == "7000":
                self.gs_iterations_var.set("30000")

    def update_status(self, message):
        self.status_textbox.configure(state="normal")
        timestamp = datetime.datetime.now().strftime("%H:%M:%S")
        self.status_textbox.insert("end", f"[{timestamp}] {message}\n")
        self.status_textbox.see("end")
        self.status_textbox.configure(state="disabled")

    def update_progress(self, value):
        pass

    def start_processing_thread(self):
        project_folder = self.project_directory.get()
        if not project_folder:
            self.update_status("Error: Select project directory!")
            return
        if not os.path.exists(project_folder):
             self.update_status(f"Error: Directory does not exist: {project_folder}")
             return
        self.start_button.configure(state="disabled", text="Processing...")
        thread = threading.Thread(target=self.run_processing_wrapper)
        thread.start()

    def run_processing_wrapper(self):
        try:
            project_folder = self.project_directory.get()
            rembg_model = self.rembg_model_var.get()
            max_dim_str = self.max_dim_var.get()
            depth_anything_path = self.depth_anything_path
            gs_iter_str = self.gs_iterations_var.get()
            nv_iter_str = self.nvdiffrec_iterations_var.get()
            mode = self.processing_mode_var.get()

            try:
                max_dim = int(max_dim_str) if max_dim_str else None
                gs_iterations = int(gs_iter_str) if gs_iter_str else 7000
                nvdiffrec_iterations = int(nv_iter_str) if nv_iter_str else 1000
            except ValueError:
                self.update_status("Error: Dimensions and Iterations must be integers")
                return

            # --- USTAWIENIE FLAG NA PODSTAWIE TRYBU ---
            if mode == "Object":
                do_rembg = True
                save_nobg = True
                save_mask = True
                do_colmap2nerf = True
            else: # Scene
                do_rembg = False
                save_nobg = False
                save_mask = False
                do_colmap2nerf = False
            
            # Reszta stała lub domyślna dla obu
            do_depth = True
            save_preprocessed = True
            save_depth = True
            save_depth_nobg = False
            do_colmap = True

            current_dir = os.path.dirname(os.path.abspath(__file__))
            parent_dir = os.path.dirname(current_dir)
            if parent_dir not in sys.path:
                sys.path.append(parent_dir)
            
            self.update_status(f"Starting pipeline in {mode} mode...")
            from Utils.processing import run_processing
            
            status_callback = lambda msg: self.after(0, self.update_status, msg)
            progress_callback = lambda val: self.after(0, self.update_progress, val)

            run_processing(project_folder=project_folder, model_name=rembg_model, max_dimension=max_dim,
                           do_rembg=do_rembg, do_depth=do_depth,
                           save_preprocessed=save_preprocessed, save_nobg=save_nobg, save_mask=save_mask,
                           save_depth=save_depth, save_depth_nobg=save_depth_nobg,
                           status_callback=status_callback,
                           progress_callback=progress_callback,
                           depth_anything_path=depth_anything_path,
                           run_colmap=do_colmap,
                           run_colmap2nerf=do_colmap2nerf,
                           gs_iterations=gs_iterations,            
                           nvdiffrec_iterations=nvdiffrec_iterations)

        except Exception as e:
            msg = f"!!! Critical Error: {e}\n{traceback.format_exc()}"
            self.after(0, self.update_status, msg)
            print(msg)
        finally:
            self.after(0, lambda: self.start_button.configure(
                state="normal", text="Start Processing"))

if __name__ == "__main__":
    DEPTH_PATH = "D:/AI/Depth-Anything" 
    app = App(depth_anything_path=DEPTH_PATH, rembg_model_name="u2net", max_image_dimension=1600)
    app.mainloop()