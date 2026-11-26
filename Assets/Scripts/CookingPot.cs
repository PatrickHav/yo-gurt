using System;
using System.Collections.Generic;
using UnityEngine;

// An ingredient that can go into the pot.
public enum PotIngredient
{
    Wasser,
    Pasta,
    Pesto,
    Hob,
}

// A pot tile. The cube must land on it with the face of the NEXT ingredient in the
// step list facing down. A wrong face (or an ingredient out of order) does nothing
// and the sequence stays open (same rule as the dough in level 1).
public class CookingPot : MonoBehaviour
{
    [Header("Steps")]
    [Tooltip("The order in which the ingredients must go into the pot.\n" +
             "Land on the pot with the face of the next ingredient facing down to complete that step.")]
    [SerializeField] private List<PotIngredient> steps = new List<PotIngredient>
    {
        PotIngredient.Wasser,
        PotIngredient.Pasta,
        PotIngredient.Pesto,
    };

    [Header("Visual states (optional)")]
    [Tooltip("Shown while water is in the pot. If empty, the object named 'pot_water' is used.")]
    [SerializeField] private GameObject waterState;

    [Tooltip("Shown while pasta is in the pot. If empty, the object named 'pot_pasta' is used.")]
    [SerializeField] private GameObject pastaState;

    [Tooltip("Shown while pesto is in the pot. If empty, the object named 'pot_pesto' is used.")]
    [SerializeField] private GameObject pestoState;

    // Fired when one step was completed (index = position of the step in the list, ingredient = its ingredient).
    public static event Action<CookingPot, int, PotIngredient> PotStepCompleted;

    // Fired when every step is done.
    public static event Action<CookingPot> PotCompleted;

    // Whether the whole sequence is complete (the plate uses this to decide if it counts).
    public bool IsComplete => completedSteps >= steps.Count;

    // The index of the next open step (== number of completed steps).
    public int CurrentStep => completedSteps;

    // The configured steps (used to build the default task list).
    public IReadOnlyList<PotIngredient> Steps => steps;

    private int completedSteps;
    private Renderer rend;

    private void Awake()
    {
        rend = GetComponent<Renderer>();

        // Resolve the optional state visuals by name (child or scene object, including inactive ones).
        if (waterState == null) waterState = FindSceneObject("pot_water");
        if (pastaState == null) pastaState = FindSceneObject("pot_pasta");
        if (pestoState == null) pestoState = FindSceneObject("pot_pesto");

        // The pot starts empty: no state visual is visible.
        HideAllStates();
    }

    private void OnEnable() => CubeController.Landed += OnCubeLanded;
    private void OnDisable() => CubeController.Landed -= OnCubeLanded;

    private void OnCubeLanded(CubeController cube)
    {
        if (IsComplete) return;
        if (!CubeOnPot(cube.transform.position)) return;

        CubeFaceTextures faces = cube.GetComponent<CubeFaceTextures>();
        if (faces == null) return;

        PotIngredient needed = steps[completedSteps];
        if (!FaceIsDown(faces, needed, cube.transform.rotation)) return; // wrong face: nothing happens

        completedSteps++;
        ShowStateFor(needed);
        PotStepCompleted?.Invoke(this, completedSteps - 1, needed);

        if (IsComplete)
            PotCompleted?.Invoke(this);
    }

    // Top-down footprint check, same principle as TeleportPortal (renderer bounds).
    private bool CubeOnPot(Vector3 p)
    {
        if (rend == null) return false;
        Bounds b = rend.bounds;
        return p.x >= b.min.x && p.x <= b.max.x
            && p.z >= b.min.z && p.z <= b.max.z;
    }

    private static bool FaceIsDown(CubeFaceTextures faces, PotIngredient ingredient, Quaternion rotation)
    {
        switch (ingredient)
        {
            case PotIngredient.Wasser: return faces.IsWaterFacingDown(rotation);
            case PotIngredient.Pasta: return faces.IsPastaFacingDown(rotation);
            case PotIngredient.Pesto: return faces.IsPestoFacingDown(rotation);
            case PotIngredient.Hob: return faces.IsHobFacingDown(rotation);
            default: return false;
        }
    }

    private void HideAllStates()
    {
        if (waterState != null) waterState.SetActive(false);
        if (pastaState != null) pastaState.SetActive(false);
        if (pestoState != null) pestoState.SetActive(false);
    }

    // Only the current stage is visible: show the state for the newest ingredient, hide the rest.
    private void ShowStateFor(PotIngredient ingredient)
    {
        HideAllStates();
        GameObject state = StateFor(ingredient);
        if (state != null) state.SetActive(true);
    }

    private GameObject StateFor(PotIngredient ingredient)
    {
        switch (ingredient)
        {
            case PotIngredient.Wasser: return waterState;
            case PotIngredient.Pasta: return pastaState;
            case PotIngredient.Pesto: return pestoState;
            default: return null; // no visual state for this ingredient
        }
    }

    // Finds an object in the open scene by name, including inactive ones.
    private static GameObject FindSceneObject(string objectName)
    {
        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t.name == objectName
                && t.gameObject.scene.IsValid()
                && t.hideFlags == HideFlags.None)
                return t.gameObject;
        }
        return null;
    }
}
