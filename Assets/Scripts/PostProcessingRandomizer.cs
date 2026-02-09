using UnityEngine;
using UnityEngine.Rendering; 
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;
using System.Linq;

public class PostProcessingRandomizer : MonoBehaviour
{
    [Header("Target Volume")]
    [Tooltip("Global Volume from which post-processing effects will be retrieved.")]
    public Volume targetVolume;

    [Header("Randomization Chances")]
    [Tooltip("Chance (0-1) of selecting the first post-processing effect.")]
    [Range(0f, 1f)]
    public float chanceForFirstEffect = 0.25f;
    [Tooltip("Chance (0-1) of selecting a second, additional effect (if the first was selected).")]
    [Range(0f, 1f)]
    public float chanceForSecondEffect = 0.25f;

    
    [System.Serializable]
    public class FilmGrainUstawienia
    {
        public bool wlaczLosowanie = true;
        [Tooltip("Randomization range for Intensity (0-1)")]
        public Vector2 zakresIntensity = new Vector2(0.1f, 0.4f);
        [Tooltip("Randomization range for Response (0-1)")]
        public Vector2 zakresResponse = new Vector2(0.1f, 0.4f);
    }
    [Header("Film Grain")]
    public FilmGrainUstawienia ustawieniaFilmGrain;

    [System.Serializable]
    public class WhiteBalanceUstawienia
    {
        public bool wlaczLosowanie = true;
        [Tooltip("Randomization range for Temperature (-100 to 100)")]
        public Vector2 zakresTemperature = new Vector2(-13f, 34f);
        [Tooltip("Randomization range for Tint (-100 to 100)")]
        public Vector2 zakresTint = new Vector2(-13f, 34f);
    }
    [Header("White Balance (HDRP)")]
    public WhiteBalanceUstawienia ustawieniaWhiteBalance;

    [System.Serializable]
    public class LiftGammaGainUstawienia
    {
        public bool wlaczLosowanie = true;
        [Tooltip("Constant Gamma value (R,G,B) if the effect is not randomized in this frame.")]
        public float domyslnaGammaRGB = 2.0f;
        [Tooltip("Level (R=G=B) for Lift. Value will be added to blacks.")]
        public Vector2 zakresPoziomuLift = new Vector2(0f, 2f);
        [Tooltip("Level (R=G=B) for Gamma. Value multiplies the midtones.")]
        public Vector2 zakresPoziomuGamma = new Vector2(0f, 2f);
        [Tooltip("Level (R=G=B) for Gain. Value multiplies the highlights.")]
        public Vector2 zakresPoziomuGain = new Vector2(0f, 2f);

        [Header("LGG Debug Values (Overrides Random if wlaczLosowanie is true)")]
        public bool useDebugLggValues = false;
        [Tooltip("W value for Lift, Gamma, Gain in debug mode. Usually 0 for Lift, 1 for Gamma/Gain, but you can experiment.")]
        public float debugWValue = 0f;
        public Vector3 debugLiftRGB = new Vector3(0.2f, 0f, 0f);
        public Vector3 debugGammaRGB = new Vector3(2.5f, 2.5f, 2.5f);
        public Vector3 debugGainRGB = new Vector3(0.5f, 0.5f, 0.5f);
    }
    [Header("Lift, Gamma, Gain (HDRP)")]
    public LiftGammaGainUstawienia ustawieniaLiftGammaGain;

    private FilmGrain filmGrainEffectHdrp;
    // private ColorCurves colorCurvesEffectHdrp; // Ca³kowicie usuniêta referencja
    private WhiteBalance whiteBalanceEffectHdrp;
    private LiftGammaGain liftGammaGainEffectHdrp;

    // Usuniêto T component z OriginalEffectState, poniewa¿ nie jest ju¿ potrzebne do ogólnego przechowywania ColorCurves
    private struct OriginalEffectState // Nie potrzebuje ju¿ byæ generyczna, jeœli nie przechowujemy ColorCurves
    {
        public bool wasActive;
        // Specyficzne pola dla efektów, które faktycznie randomizujemy
        public float filmGrainIntensity;
        public float filmGrainResponse;
        public float whiteBalanceTemperature;
        public float whiteBalanceTint;
        public Vector4 liftValue;
        public Vector4 gammaValue;
        public Vector4 gainValue;
    }

    private OriginalEffectState originalFilmGrainState;
    // private OriginalEffectState originalColorCurvesState; // Ca³kowicie usuniête
    private OriginalEffectState originalWhiteBalanceState;
    private OriginalEffectState originalLiftGammaGainState;

    private List<System.Action> activeRandomizers = new List<System.Action>();
    private bool isInitialized = false;

    void Start()
    {
        InitializeEffects();
    }

    public void InitializeEffects()
    {
        if (targetVolume == null || targetVolume.profile == null)
        {
            Debug.LogError("PostProcessingRandomizer: Target Volume lub jego profil nie jest przypisany!");
            isInitialized = false;
            return;
        }

        if (targetVolume.profile.TryGet(out filmGrainEffectHdrp))
        {
            originalFilmGrainState = new OriginalEffectState
            {
                // component usuniête, wasActive odnosi siê do filmGrainEffectHdrp
                wasActive = filmGrainEffectHdrp.active,
                filmGrainIntensity = filmGrainEffectHdrp.intensity.value,
                filmGrainResponse = filmGrainEffectHdrp.response.value
            };
        }
        else Debug.LogWarning("PostProcessingRandomizer: Nie znaleziono FilmGrain w Volume Profile (HDRP).");

        // Próba pobrania ColorCurves tylko po to, by sprawdziæ, czy istnieje, ale nic z nim nie robimy
        targetVolume.profile.TryGet(out ColorCurves cc); // Zmienna lokalna, nie zapisujemy jej
        if (cc == null) Debug.LogWarning("PostProcessingRandomizer: Nie znaleziono ColorCurves w Volume Profile (HDRP) - to tylko informacja, randomizacja wy³¹czona.");


        if (targetVolume.profile.TryGet(out whiteBalanceEffectHdrp))
        {
            originalWhiteBalanceState = new OriginalEffectState
            {
                wasActive = whiteBalanceEffectHdrp.active,
                whiteBalanceTemperature = whiteBalanceEffectHdrp.temperature.value,
                whiteBalanceTint = whiteBalanceEffectHdrp.tint.value
            };
        }
        else Debug.LogWarning("PostProcessingRandomizer: Nie znaleziono WhiteBalance w Volume Profile (HDRP).");

        if (targetVolume.profile.TryGet(out liftGammaGainEffectHdrp))
        {
            originalLiftGammaGainState = new OriginalEffectState
            {
                wasActive = liftGammaGainEffectHdrp.active,
                liftValue = liftGammaGainEffectHdrp.lift.value,
                gammaValue = liftGammaGainEffectHdrp.gamma.value,
                gainValue = liftGammaGainEffectHdrp.gain.value
            };
        }
        else Debug.LogWarning("PostProcessingRandomizer: Nie znaleziono LiftGammaGain w Volume Profile (HDRP).");

        isInitialized = true;
        Debug.Log("PostProcessingRandomizer zainicjalizowany dla HDRP (ColorCurves ca³kowicie pominiête).");
    }

    public void RandomizeAndApplyEffectsForNextCapture()
    {
        if (!isInitialized)
        {
            InitializeEffects();
            if (!isInitialized) return;
        }

        RestoreManagedEffectsToOriginalSessionState();

        if (liftGammaGainEffectHdrp != null) // Sprawdzenie czy referencja istnieje
        {
            liftGammaGainEffectHdrp.active = true;
            Vector4 defaultGamma = liftGammaGainEffectHdrp.gamma.value; // Pobierz aktualn¹ wartoœæ jako bazê (w tym .w)
            if (isInitialized && originalLiftGammaGainState.gammaValue != null) // U¿yj oryginalnej jeœli dostêpna
            {
                 defaultGamma = originalLiftGammaGainState.gammaValue;
            }
            defaultGamma.x = ustawieniaLiftGammaGain.domyslnaGammaRGB;
            defaultGamma.y = ustawieniaLiftGammaGain.domyslnaGammaRGB;
            defaultGamma.z = ustawieniaLiftGammaGain.domyslnaGammaRGB;
            liftGammaGainEffectHdrp.gamma.Override(defaultGamma);
        }

        activeRandomizers.Clear();
        if (filmGrainEffectHdrp != null && ustawieniaFilmGrain.wlaczLosowanie)
            activeRandomizers.Add(RandomizeFilmGrain);
        if (whiteBalanceEffectHdrp != null && ustawieniaWhiteBalance.wlaczLosowanie)
            activeRandomizers.Add(RandomizeWhiteBalance);
        if (liftGammaGainEffectHdrp != null && ustawieniaLiftGammaGain.wlaczLosowanie)
            activeRandomizers.Add(RandomizeLiftGammaGain);

        List<System.Action> chosenEffects = new List<System.Action>();
        if (Random.value < chanceForFirstEffect && activeRandomizers.Count > 0)
        {
            int randomIndex = Random.Range(0, activeRandomizers.Count);
            System.Action firstEffectRandomizer = activeRandomizers[randomIndex];
            chosenEffects.Add(firstEffectRandomizer);
            activeRandomizers.RemoveAt(randomIndex);

            if (Random.value < chanceForSecondEffect && activeRandomizers.Count > 0)
            {
                randomIndex = Random.Range(0, activeRandomizers.Count);
                System.Action secondEffectRandomizer = activeRandomizers[randomIndex];
                chosenEffects.Add(secondEffectRandomizer);
            }
        }

        foreach (var effectAction in chosenEffects)
        {
            effectAction.Invoke();
        }
    }

    private void RandomizeFilmGrain()
    {
        if (filmGrainEffectHdrp == null) return;
        filmGrainEffectHdrp.active = true;
        filmGrainEffectHdrp.intensity.Override(Random.Range(ustawieniaFilmGrain.zakresIntensity.x, ustawieniaFilmGrain.zakresIntensity.y));
        filmGrainEffectHdrp.response.Override(Random.Range(ustawieniaFilmGrain.zakresResponse.x, ustawieniaFilmGrain.zakresResponse.y));
    }

    private void RandomizeWhiteBalance()
    {
        if (whiteBalanceEffectHdrp == null) return;
        whiteBalanceEffectHdrp.active = true;
        whiteBalanceEffectHdrp.temperature.Override(Random.Range(ustawieniaWhiteBalance.zakresTemperature.x, ustawieniaWhiteBalance.zakresTemperature.y));
        whiteBalanceEffectHdrp.tint.Override(Random.Range(ustawieniaWhiteBalance.zakresTint.x, ustawieniaWhiteBalance.zakresTint.y));
    }

    private void RandomizeLiftGammaGain()
    {
        if (liftGammaGainEffectHdrp == null) // Usuniêto sprawdzanie originalLiftGammaGainState.component, bo struktura ju¿ nie jest generyczna
        {
            Debug.LogWarning("Próba randomizacji LGG, ale efekt nie jest zainicjalizowany.");
            return;
        }
        liftGammaGainEffectHdrp.active = true;
        // Debug.Log("== Randomizing LGG ==");

        Vector4 newLift, newGamma, newGain;
        float wValueForDebug = ustawieniaLiftGammaGain.debugWValue;

        if (ustawieniaLiftGammaGain.useDebugLggValues)
        {
            newLift = new Vector4(ustawieniaLiftGammaGain.debugLiftRGB.x, ustawieniaLiftGammaGain.debugLiftRGB.y, ustawieniaLiftGammaGain.debugLiftRGB.z, wValueForDebug);
            newGamma = new Vector4(ustawieniaLiftGammaGain.debugGammaRGB.x, ustawieniaLiftGammaGain.debugGammaRGB.y, ustawieniaLiftGammaGain.debugGammaRGB.z, wValueForDebug);
            newGain = new Vector4(ustawieniaLiftGammaGain.debugGainRGB.x, ustawieniaLiftGammaGain.debugGainRGB.y, ustawieniaLiftGammaGain.debugGainRGB.z, wValueForDebug);
            // Debug.Log($"LGG DEBUG VALUES USED: Lift={newLift}, Gamma={newGamma}, Gain={newGain}");
        }
        else
        {
            // Pobieramy oryginalne .w bezpoœrednio z efektu, jeœli jest dostêpny, inaczej u¿ywamy domyœlnego
            float originalLiftW = liftGammaGainEffectHdrp != null ? liftGammaGainEffectHdrp.lift.value.w : 0f;
            float originalGammaW = liftGammaGainEffectHdrp != null ? liftGammaGainEffectHdrp.gamma.value.w : 0f; // Czêsto 0 dla gamma w HDRP
            float originalGainW = liftGammaGainEffectHdrp != null ? liftGammaGainEffectHdrp.gain.value.w : 0f;


            float randomLiftLevel = Random.Range(ustawieniaLiftGammaGain.zakresPoziomuLift.x, ustawieniaLiftGammaGain.zakresPoziomuLift.y);
            newLift = new Vector4(randomLiftLevel, randomLiftLevel, randomLiftLevel, originalLiftW);

            float randomGammaLevel = Random.Range(ustawieniaLiftGammaGain.zakresPoziomuGamma.x, ustawieniaLiftGammaGain.zakresPoziomuGamma.y);
            newGamma = new Vector4(randomGammaLevel, randomGammaLevel, randomGammaLevel, originalGammaW);

            float randomGainLevel = Random.Range(ustawieniaLiftGammaGain.zakresPoziomuGain.x, ustawieniaLiftGammaGain.zakresPoziomuGain.y);
            newGain = new Vector4(randomGainLevel, randomGainLevel, randomGainLevel, originalGainW);
            // Debug.Log($"LGG RANDOM VALUES: Lift={newLift}, Gamma={newGamma}, Gain={newGain}");
        }

        liftGammaGainEffectHdrp.lift.Override(newLift);
        // Debug.Log($"After Override - Lift Value: {liftGammaGainEffectHdrp.lift.value}, Override State: {liftGammaGainEffectHdrp.lift.overrideState}");

        liftGammaGainEffectHdrp.gamma.Override(newGamma);
        // Debug.Log($"After Override - Gamma Value: {liftGammaGainEffectHdrp.gamma.value}, Override State: {liftGammaGainEffectHdrp.gamma.overrideState}");

        liftGammaGainEffectHdrp.gain.Override(newGain);
        // Debug.Log($"After Override - Gain Value: {liftGammaGainEffectHdrp.gain.value}, Override State: {liftGammaGainEffectHdrp.gain.overrideState}");
        // Debug.Log($"LGG Active State after Randomize: {liftGammaGainEffectHdrp.active}");
    }

    public void RestoreManagedEffectsToOriginalSessionState()
    {
        if (!isInitialized) return;

        if (filmGrainEffectHdrp != null) // Sprawdzamy tylko referencjê do efektu
        {
            filmGrainEffectHdrp.active = originalFilmGrainState.wasActive;
            filmGrainEffectHdrp.intensity.Override(originalFilmGrainState.filmGrainIntensity);
            filmGrainEffectHdrp.response.Override(originalFilmGrainState.filmGrainResponse);
        }

        // Przywracanie ColorCurves - tylko status 'active', jeœli referencja istnieje
        // if (colorCurvesEffectHdrp != null && originalColorCurvesState.component != null) // originalColorCurvesState.component ju¿ nie istnieje
        if (targetVolume.profile.TryGet(out ColorCurves cc)) // Pobierz ponownie referencjê, jeœli istnieje
        {
            // Trudno jest bezpiecznie przywróciæ 'wasActive' bez przechowywania go w dedykowanej strukturze
            // Jeœli ColorCurves nie jest modyfikowane, byæ mo¿e nie trzeba go przywracaæ
            // Mo¿na by zapisaæ stan aktywnoœci ColorCurves w oddzielnej zmiennej boolowskiej w InitializeEffects
        }


        if (whiteBalanceEffectHdrp != null)
        {
            whiteBalanceEffectHdrp.active = originalWhiteBalanceState.wasActive;
            whiteBalanceEffectHdrp.temperature.Override(originalWhiteBalanceState.whiteBalanceTemperature);
            whiteBalanceEffectHdrp.tint.Override(originalWhiteBalanceState.whiteBalanceTint);
        }
        if (liftGammaGainEffectHdrp != null)
        {
            liftGammaGainEffectHdrp.active = originalLiftGammaGainState.wasActive;
            liftGammaGainEffectHdrp.lift.Override(originalLiftGammaGainState.liftValue);
            liftGammaGainEffectHdrp.gamma.Override(originalLiftGammaGainState.gammaValue);
            liftGammaGainEffectHdrp.gain.Override(originalLiftGammaGainState.gainValue);
        }
    }

    void OnDestroy()
    {
        // RestoreManagedEffectsToOriginalSessionState();
    }
}
