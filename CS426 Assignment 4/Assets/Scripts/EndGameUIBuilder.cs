using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

public class EndGameUIBuilder : MonoBehaviour {
    [System.Serializable]
    public class EndGameUIRefs {
        public GameObject billboard;
        public TMP_Text questionText;
        public TMP_Text answerText;
        public TMP_Text resultText;
        public TMP_InputField answerInput;
        public TMP_InputField[] orderInputs;
        public Button submitButton;
    }

    public EndGameUIRefs Build(int orderInputCount) {
        EnsureEventSystem();

        GameObject canvasGO = new GameObject("EndGameCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject panelGO = new GameObject("EndGamePanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        Image panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.75f);
        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.1f, 0.1f);
        panelRect.anchorMax = new Vector2(0.9f, 0.9f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        TMP_Text questionText = CreateText(panelGO.transform, "QuestionText", 36, TextAlignmentOptions.TopLeft);
        RectTransform questionRect = questionText.GetComponent<RectTransform>();
        questionRect.anchorMin = new Vector2(0.05f, 0.85f);
        questionRect.anchorMax = new Vector2(0.95f, 0.95f);
        questionRect.offsetMin = Vector2.zero;
        questionRect.offsetMax = Vector2.zero;

        TMP_Text resultText = CreateText(panelGO.transform, "ResultText", 28, TextAlignmentOptions.Center);
        RectTransform resultRect = resultText.GetComponent<RectTransform>();
        resultRect.anchorMin = new Vector2(0.05f, 0.05f);
        resultRect.anchorMax = new Vector2(0.95f, 0.15f);
        resultRect.offsetMin = Vector2.zero;
        resultRect.offsetMax = Vector2.zero;

        TMP_Text answerText = CreateText(panelGO.transform, "AnswerText", 24, TextAlignmentOptions.Center);
        RectTransform answerRect = answerText.GetComponent<RectTransform>();
        answerRect.anchorMin = new Vector2(0.05f, 0.15f);
        answerRect.anchorMax = new Vector2(0.95f, 0.25f);
        answerRect.offsetMin = Vector2.zero;
        answerRect.offsetMax = Vector2.zero;

        TMP_InputField[] orderInputs = BuildOrderInputs(panelGO.transform, orderInputCount);

        Button submitButton = CreateButton(panelGO.transform, "SubmitButton", "Submit", 26);
        RectTransform submitRect = submitButton.GetComponent<RectTransform>();
        submitRect.anchorMin = new Vector2(0.7f, 0.05f);
        submitRect.anchorMax = new Vector2(0.95f, 0.13f);
        submitRect.offsetMin = Vector2.zero;
        submitRect.offsetMax = Vector2.zero;

        return new EndGameUIRefs {
            billboard = panelGO,
            questionText = questionText,
            answerText = answerText,
            resultText = resultText,
            answerInput = null,
            orderInputs = orderInputs,
            submitButton = submitButton
        };
    }

    private void EnsureEventSystem() {
        if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) {
            return;
        }

        GameObject eventSystemGO = new GameObject("EventSystem");
        eventSystemGO.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        eventSystemGO.AddComponent<InputSystemUIInputModule>();
#else
        eventSystemGO.AddComponent<StandaloneInputModule>();
#endif
    }

    private TMP_Text CreateText(Transform parent, string name, int fontSize, TextAlignmentOptions alignment) {
        GameObject textGO = new GameObject(name);
        textGO.transform.SetParent(parent, false);
        TMP_Text text = textGO.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        RectTransform rect = textGO.GetComponent<RectTransform>();
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return text;
    }

    private TMP_InputField[] BuildOrderInputs(Transform parent, int count) {
        int safeCount = Mathf.Clamp(count, 1, 20);
        TMP_InputField[] inputs = new TMP_InputField[safeCount];

        float startY = 0.75f;
        float stepY = 0.07f;

        for (int i = 0; i < safeCount; i++) {
            GameObject row = new GameObject($"OrderRow{i + 1}");
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.05f, startY - stepY * i - 0.045f);
            rowRect.anchorMax = new Vector2(0.6f, startY - stepY * i + 0.02f);
            rowRect.offsetMin = Vector2.zero;
            rowRect.offsetMax = Vector2.zero;

            TMP_Text indexLabel = CreateText(row.transform, "Index", 24, TextAlignmentOptions.MidlineLeft);
            RectTransform indexRect = indexLabel.GetComponent<RectTransform>();
            indexRect.anchorMin = new Vector2(0f, 0f);
            indexRect.anchorMax = new Vector2(0.1f, 1f);
            indexRect.offsetMin = Vector2.zero;
            indexRect.offsetMax = Vector2.zero;
            indexLabel.text = (i + 1).ToString() + ".";

            TMP_InputField input = CreateInputField(row.transform, $"OrderInput{i + 1}", 24, "");
            RectTransform inputRect = input.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.12f, 0f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.offsetMin = Vector2.zero;
            inputRect.offsetMax = Vector2.zero;

            inputs[i] = input;
        }

        return inputs;
    }

    private TMP_InputField CreateInputField(Transform parent, string name, int fontSize, string placeholderText) {
        GameObject inputGO = new GameObject(name);
        inputGO.transform.SetParent(parent, false);
        Image background = inputGO.AddComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.1f);
        TMP_InputField input = inputGO.AddComponent<TMP_InputField>();

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(inputGO.transform, false);
        TMP_Text text = textGO.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject placeholderGO = new GameObject("Placeholder");
        placeholderGO.transform.SetParent(inputGO.transform, false);
        TMP_Text placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
        placeholder.fontSize = fontSize;
        placeholder.color = new Color(1f, 1f, 1f, 0.5f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.text = placeholderText;

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.02f, 0f);
        textRect.anchorMax = new Vector2(0.98f, 1f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        RectTransform placeholderRect = placeholderGO.GetComponent<RectTransform>();
        placeholderRect.anchorMin = new Vector2(0.02f, 0f);
        placeholderRect.anchorMax = new Vector2(0.98f, 1f);
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;

        input.textComponent = text;
        input.placeholder = placeholder;

        RectTransform inputRect = inputGO.GetComponent<RectTransform>();
        inputRect.offsetMin = Vector2.zero;
        inputRect.offsetMax = Vector2.zero;

        return input;
    }

    private Button CreateButton(Transform parent, string name, string label, int fontSize) {
        GameObject buttonGO = new GameObject(name);
        buttonGO.transform.SetParent(parent, false);
        Image image = buttonGO.AddComponent<Image>();
        image.color = new Color(0.2f, 0.6f, 1f, 0.9f);
        Button button = buttonGO.AddComponent<Button>();

        TMP_Text labelText = CreateText(buttonGO.transform, "Label", fontSize, TextAlignmentOptions.Center);
        labelText.text = label;

        RectTransform labelRect = labelText.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }
}
