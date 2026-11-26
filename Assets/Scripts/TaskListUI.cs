using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Which interaction checks a task off.
public enum TaskTrigger
{
    // The round dough was rolled flat with the rolling pin.
    DoughFlattened,
    // The cookie shape was cut into the flat dough.
    DoughCut,
    // The cookie was picked up with the baking sheet side.
    CookieTaken,
    // The carried cookie was dropped on the oven tile.
    CookieDroppedOnOven,
    // The cube was teleported to a linked portal's tile.
    Teleported,

    // Level 2 (pasta): water went into the pot.
    WaterAdded,
    // Level 2 (pasta): pasta went into the pot.
    PastaAdded,
    // Level 2 (pasta): pesto went into the pot.
    PestoAdded,
    // Level 2 (pasta): ANY pot step was completed (used for non-default ingredients).
    PotStepCompleted,
    // Level 2 (pasta): the button tile was pressed.
    ButtonPressed,
    // Level 2 (pasta): the cube was carried across a slide.
    SlideUsed,
    // Level 2 (pasta): the plate was reached with a complete pot.
    PlateReached,
}

[Serializable]
public class Task
{
    [Tooltip("Text shown in the list.")]
    public string label = "Neue Aufgabe";

    [Tooltip("The interaction that checks this task off.")]
    public TaskTrigger trigger = TaskTrigger.DoughFlattened;

    [Tooltip("How often the interaction must happen before the task is done.")]
    public int requiredCount = 1;
}

// Shows this level's task list in the top-right corner of the screen.
// A task is checked off automatically when its interaction happens.
//
// Usage: create an empty GameObject in the level scene (e.g. "Tasks"),
// add this component and edit the tasks in the Inspector.
// The canvas and the UI are created automatically, no prefabs needed.
public class TaskListUI : MonoBehaviour
{
    [Tooltip("The tasks of this level, shown top to bottom.")]
    [SerializeField] private List<Task> tasks = new List<Task>
    {
        new Task { label = "Teig ausrollen", trigger = TaskTrigger.DoughFlattened },
        new Task { label = "Teig ausstechen", trigger = TaskTrigger.DoughCut },
        new Task { label = "Lebkuchen abheben", trigger = TaskTrigger.CookieTaken },
        new Task { label = "Lebkuchen in den Ofen legen", trigger = TaskTrigger.CookieDroppedOnOven },
    };

    [Header("Look")]
    [Tooltip("Optional font. If empty, the LiberationSans font in Resources/Fonts is used.")]
    [SerializeField] private Font font;

    [Tooltip("Title above the list. Leave empty to hide it.")]
    [SerializeField] private string panelTitle = "Aufgaben";

    [Tooltip("Distance of the panel from the top/right edge of the screen.")]
    [SerializeField] private float margin = 20f;

    [Tooltip("Width of the panel.")]
    [SerializeField] private float panelWidth = 340f;

    [Tooltip("Width of the light border around the dark panel.")]
    [SerializeField] private float borderWidth = 5f;

    [SerializeField] private float rowSpacing = 8f;
    [SerializeField] private int boxSize = 24;
    [SerializeField] private int fontSize = 20;

    [Tooltip("Checkbox colour while the task is open.")]
    [SerializeField] private Color openBoxColor = new Color(1f, 1f, 1f, 0.25f);

    [Tooltip("Checkbox colour when the task is done.")]
    [SerializeField] private Color doneBoxColor = new Color(0.42f, 0.78f, 0.36f, 1f);

    [Tooltip("Text colour when the task is done.")]
    [SerializeField] private Color doneTextColor = new Color(1f, 1f, 1f, 0.45f);

    // Fired when every task of this level has been completed.
    public static event Action AllTasksDone;

    // Creates the default task list for a level scene. CubeController calls this
    // automatically when the scene has no TaskListUI object of its own. Only
    // tasks that this level can actually fulfil are included: the dough tasks
    // need the dough objects, the oven task needs an oven tile and the portal
    // task needs a linked portal. Returns null (no list at all) if nothing applies.
    public static TaskListUI CreateDefaultList(CubeController cube)
    {
        List<Task> defaultTasks = new List<Task>();

        if (cube.HasDough)
        {
            defaultTasks.Add(new Task { label = "Teig ausrollen", trigger = TaskTrigger.DoughFlattened });
            defaultTasks.Add(new Task { label = "Teig ausstechen", trigger = TaskTrigger.DoughCut });
            defaultTasks.Add(new Task { label = "Lebkuchen abheben", trigger = TaskTrigger.CookieTaken });
            if (cube.HasOvenTile)
                defaultTasks.Add(new Task { label = "Lebkuchen in den Ofen legen", trigger = TaskTrigger.CookieDroppedOnOven });
        }

        foreach (TeleportPortal portal in FindObjectsByType<TeleportPortal>())
        {
            if (portal.HasLinkedPortal)
                defaultTasks.Add(new Task { label = "Durch das Portal teleportieren", trigger = TaskTrigger.Teleported });
            break; // one portal task is enough
        }

        // Level 2 (pasta): one task per pot step, plus button and plate tasks,
        // but only if the matching objects exist in the scene.
        CookingPot pot = FindAnyObjectByType<CookingPot>();
        if (pot != null)
        {
            foreach (PotIngredient ingredient in pot.Steps)
                defaultTasks.Add(new Task { label = IngredientLabel(ingredient), trigger = IngredientTrigger(ingredient) });
        }

        if (FindAnyObjectByType<ButtonTile>() != null)
            defaultTasks.Add(new Task { label = "Knopf drücken", trigger = TaskTrigger.ButtonPressed });

        if (FindAnyObjectByType<GoalTile>() != null)
            defaultTasks.Add(new Task { label = "Gericht servieren", trigger = TaskTrigger.PlateReached });

        if (defaultTasks.Count == 0) return null; // nothing to show for this level

        // The object starts out inactive so Awake (which builds the UI) runs only
        // AFTER the tasks have been assigned.
        GameObject taskObject = new GameObject("Tasks");
        taskObject.SetActive(false);
        TaskListUI list = taskObject.AddComponent<TaskListUI>();
        list.tasks = defaultTasks;
        taskObject.SetActive(true);

        return list;
    }

    private sealed class Row
    {
        public Task task;
        public Image box;
        public Text text;
        public int count;
    }

    private readonly List<Row> rows = new List<Row>();
    private bool allDoneSent;

    private void Awake()
    {
        if (tasks.Count == 0)
        {
            Debug.LogWarning("TaskListUI: no tasks assigned, the list stays empty.", this);
            return;
        }

        BuildPanel(GetOrCreateCanvas());
    }

    private void OnEnable()
    {
        CubeController.DoughFlattened += OnDoughFlattened;
        CubeController.DoughCut += OnDoughCut;
        CubeController.CookieTaken += OnCookieTaken;
        CubeController.CookieDroppedOnOven += OnCookieDroppedOnOven;
        TeleportPortal.Teleported += OnTeleported;
        CookingPot.PotStepCompleted += OnPotStepCompleted;
        ButtonTile.ButtonPressed += OnButtonPressed;
        SlideTile.SlideUsed += OnSlideUsed;
        GoalTile.PlateReached += OnPlateReached;
    }

    private void OnDisable()
    {
        CubeController.DoughFlattened -= OnDoughFlattened;
        CubeController.DoughCut -= OnDoughCut;
        CubeController.CookieTaken -= OnCookieTaken;
        CubeController.CookieDroppedOnOven -= OnCookieDroppedOnOven;
        TeleportPortal.Teleported -= OnTeleported;
        CookingPot.PotStepCompleted -= OnPotStepCompleted;
        ButtonTile.ButtonPressed -= OnButtonPressed;
        SlideTile.SlideUsed -= OnSlideUsed;
        GoalTile.PlateReached -= OnPlateReached;
    }

    private void OnDoughFlattened(CubeController cube) => Bump(TaskTrigger.DoughFlattened);
    private void OnDoughCut(CubeController cube) => Bump(TaskTrigger.DoughCut);
    private void OnCookieTaken(CubeController cube) => Bump(TaskTrigger.CookieTaken);
    private void OnCookieDroppedOnOven(CubeController cube) => Bump(TaskTrigger.CookieDroppedOnOven);
    private void OnTeleported(TeleportPortal portal) => Bump(TaskTrigger.Teleported);
    private void OnPotStepCompleted(CookingPot pot, int index, PotIngredient ingredient) => Bump(IngredientTrigger(ingredient));
    private void OnButtonPressed(ButtonTile button) => Bump(TaskTrigger.ButtonPressed);
    private void OnSlideUsed(SlideTile slide) => Bump(TaskTrigger.SlideUsed);
    private void OnPlateReached(GoalTile plate) => Bump(TaskTrigger.PlateReached);

    // The default German label for a pot step.
    private static string IngredientLabel(PotIngredient ingredient)
    {
        switch (ingredient)
        {
            case PotIngredient.Wasser: return "Wasser in den Topf";
            case PotIngredient.Pasta: return "Nudeln in den Topf";
            case PotIngredient.Pesto: return "Pesto dazugeben";
            default: return "Zutat in den Topf";
        }
    }

    // The task trigger a pot step with this ingredient bumps.
    private static TaskTrigger IngredientTrigger(PotIngredient ingredient)
    {
        switch (ingredient)
        {
            case PotIngredient.Wasser: return TaskTrigger.WaterAdded;
            case PotIngredient.Pasta: return TaskTrigger.PastaAdded;
            case PotIngredient.Pesto: return TaskTrigger.PestoAdded;
            default: return TaskTrigger.PotStepCompleted;
        }
    }

    // Increases the progress of every open task that reacts to this trigger.
    private void Bump(TaskTrigger trigger)
    {
        foreach (Row row in rows)
        {
            if (row.task.trigger != trigger) continue;
            if (row.count >= row.task.requiredCount) continue;

            row.count++;

            if (row.count >= row.task.requiredCount)
            {
                row.box.color = doneBoxColor;
                row.text.text = row.task.label;
                row.text.color = doneTextColor;
            }
            else
            {
                row.text.text = $"{row.task.label} ({row.count}/{row.task.requiredCount})";
            }
        }

        CheckAllDone();
    }

    private void CheckAllDone()
    {
        if (allDoneSent || rows.Count == 0) return;

        foreach (Row row in rows)
            if (row.count < row.task.requiredCount)
                return;

        allDoneSent = true;
        AllTasksDone?.Invoke();
    }

    private Transform GetOrCreateCanvas()
    {
        Canvas existing = FindAnyObjectByType<Canvas>();
        if (existing != null) return existing.transform;

        GameObject canvasObject = new GameObject("TaskCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas.transform;
    }

    private void BuildPanel(Transform canvas)
    {
        GameObject panelObject = new GameObject("TaskPanel", typeof(RectTransform),
            typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelObject.transform.SetParent(canvas, false);

        RectTransform panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(1f, 1f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-margin, -margin);
        panel.sizeDelta = new Vector2(panelWidth, 0f);

        // The panel's own image is the light frame around the dark content area.
        Image frame = panelObject.GetComponent<Image>();
        frame.color = new Color(0.98f, 0.95f, 0.9f, 0.95f);
        frame.raycastTarget = false;

        VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 18);
        layout.spacing = rowSpacing;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperLeft;

        ContentSizeFitter fitter = panelObject.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Dark content area, inset into the frame. It ignores the layout so it
        // does not take up a row of its own.
        GameObject darkObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
        darkObject.transform.SetParent(panelObject.transform, false);
        RectTransform dark = darkObject.GetComponent<RectTransform>();
        dark.anchorMin = Vector2.zero;
        dark.anchorMax = Vector2.one;
        dark.offsetMin = new Vector2(borderWidth, borderWidth);
        dark.offsetMax = new Vector2(-borderWidth, -borderWidth);
        Image darkImage = darkObject.GetComponent<Image>();
        darkImage.color = new Color(0.13f, 0.11f, 0.1f, 0.9f);
        darkImage.raycastTarget = false;
        darkObject.AddComponent<LayoutElement>().ignoreLayout = true;
        darkObject.transform.SetSiblingIndex(0); // render behind the text

        Font resolvedFont = GetFont(false);
        Font boldFont = GetFont(true);

        if (!string.IsNullOrEmpty(panelTitle))
            AddTitle(panelObject, resolvedFont, boldFont);

        foreach (Task task in tasks)
            rows.Add(AddRow(panelObject, resolvedFont, task));
    }

    private void AddTitle(GameObject parent, Font font, Font boldFont)
    {
        GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(Text));
        titleObject.transform.SetParent(parent.transform, false);

        Text title = titleObject.GetComponent<Text>();
        title.font = boldFont != null ? boldFont : font;
        title.text = panelTitle;
        title.fontSize = fontSize + 4;
        title.fontStyle = boldFont != null ? FontStyle.Normal : FontStyle.Bold;
        title.color = new Color(1f, 0.98f, 0.92f, 1f);
        title.alignment = TextAnchor.UpperLeft;
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.raycastTarget = false;
    }

    private Row AddRow(GameObject parent, Font font, Task task)
    {
        GameObject rowObject = new GameObject($"Task: {task.label}", typeof(RectTransform),
            typeof(HorizontalLayoutGroup));
        rowObject.transform.SetParent(parent.transform, false);

        HorizontalLayoutGroup rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;

        // Checkbox (a plain white quad, coloured in at runtime).
        GameObject boxObject = new GameObject("Box", typeof(RectTransform), typeof(Image));
        boxObject.transform.SetParent(rowObject.transform, false);

        Image box = boxObject.GetComponent<Image>();
        box.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.png");
        box.color = openBoxColor;
        box.raycastTarget = false;

        LayoutElement boxLayout = boxObject.AddComponent<LayoutElement>();
        boxLayout.minWidth = boxSize;
        boxLayout.minHeight = boxSize;
        boxLayout.preferredWidth = boxSize;
        boxLayout.preferredHeight = boxSize;

        // Label.
        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(rowObject.transform, false);

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        text.text = task.requiredCount > 1 ? $"{task.label} (0/{task.requiredCount})" : task.label;

        LayoutElement textLayout = textObject.AddComponent<LayoutElement>();
        textLayout.flexibleWidth = 1f;
        textLayout.minHeight = boxSize;

        return new Row { task = task, box = box, text = text, count = 0 };
    }

    private Font GetFont(bool bold)
    {
        // 1. Font assigned in the Inspector.
        if (font != null) return font;

        // 2. Fonts bundled with the project in Assets/Resources/Fonts.
        Font bundled = Resources.Load<Font>(bold
            ? "Fonts/LiberationSans-Bold"
            : "Fonts/LiberationSans-Regular");
        if (bundled != null) return bundled;

        // 3. Unity's built-in font (may be missing in newer Unity versions).
        Font builtIn = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (builtIn == null && !bold)
            Debug.LogWarning("TaskListUI: no font found (expected 'LiberationSans' in " +
                             "Resources/Fonts), the list will be invisible. " +
                             "Assign a font in the Inspector.", this);
        return builtIn;
    }
}
