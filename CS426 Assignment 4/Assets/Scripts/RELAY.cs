/* Team members: Thomas, Yebeen, Andrew
 * Script file that handles multiplayer
 */

using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using Unity.Netcode.Transports.UTP;

public class NetworkManagerUI : MonoBehaviour
{
    [SerializeField] private Button host_btn;
    [SerializeField] private Button client_btn;
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField] private int maxPlayers = 4;
    public string joinCode;
    [SerializeField] private TMP_InputField joinCodeInputField;
    [SerializeField] private RectTransform uiPanel;

    private void Awake()
    {
        AutoAssignReferences();
        LayoutUi();

        if (host_btn == null || client_btn == null || joinCodeText == null || joinCodeInputField == null)
        {
            return;
        }

        host_btn.onClick.AddListener(() =>
        {
            StartHostRelay();
        });

        client_btn.onClick.AddListener(() =>
        {
            StartClientRelay(joinCodeInputField.text);
        });
    }

    private async void Start()
    {
        await UnityServices.InitializeAsync();
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public async void StartHostRelay()
    {
        Allocation allocation = null;
        try
        {
            allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
            joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        }
        catch (RelayServiceException e)
        {
            Debug.Log(e);
            return;
        }

        var serverData = allocation.ToRelayServerData("dtls");
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(serverData);

        if (NetworkManager.Singleton.StartHost())
        {
            if (uiPanel != null)
            {
                joinCodeText.transform.SetParent(uiPanel.parent, true);
                uiPanel.gameObject.SetActive(false);
            }
            joinCodeText.text = "join code: " + joinCode;
            PlaceJoinCodeBottomRight();
        }
    }

   public async void StartClientRelay(string joinCode)
    {
        JoinAllocation joinAllocation = null;
        try
        {
            joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        }
        catch (RelayServiceException e)
        {
            Debug.Log(e);
            return; 
        }

        var serverData = joinAllocation.ToRelayServerData("dtls");
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(serverData);

        if (NetworkManager.Singleton.StartClient())
        {
            if (uiPanel != null) uiPanel.gameObject.SetActive(false);
            if (joinCodeText != null) joinCodeText.text = "";
        }
    }

    private void AutoAssignReferences()
    {
        if (host_btn == null) { host_btn = FindButton("HOST"); }
        if (client_btn == null) { client_btn = FindButton("CLIENT"); }

        if (joinCodeInputField == null)
        {
            joinCodeInputField = Object.FindFirstObjectByType<TMP_InputField>(FindObjectsInactive.Include);
        }

        if (joinCodeText == null)
        {
            TMP_Text[] texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var text in texts)
            {
                if (text == null) { continue; }
                if (text.text == "HOST" || text.text == "CLIENT") { continue; }
                joinCodeText = text;
                break;
            }
        }
    }

    private Button FindButton(string expectedNameOrText)
    {
        Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var button in buttons)
        {
            if (button == null) { continue; }
            if (button.name == expectedNameOrText) { return button; }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null && label.text == expectedNameOrText) { return button; }
        }
        return null;
    }

    private void LayoutUi()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null) { return; }

        if (uiPanel == null)
        {
            Transform existing = canvas.transform.Find("NetworkUIPanel");
            if (existing != null)
            {
                uiPanel = existing as RectTransform;
            }
        }

        if (uiPanel == null)
        {
            var panelGo = new GameObject("NetworkUIPanel", typeof(RectTransform), typeof(Image));
            uiPanel = panelGo.GetComponent<RectTransform>();
            uiPanel.SetParent(canvas.transform, false);
            var image = panelGo.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.25f);
        }

        uiPanel.anchorMin = new Vector2(0.5f, 0f);
        uiPanel.anchorMax = new Vector2(0.5f, 0f);
        uiPanel.pivot = new Vector2(0.5f, 0f);
        uiPanel.sizeDelta = new Vector2(560f, 230f);
        uiPanel.anchoredPosition = new Vector2(0f, 24f);

        PlaceUnderPanel(joinCodeText, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(500f, 56f), new Vector2(0f, -34f));
        PlaceUnderPanel(host_btn, new Vector2(0.25f, 0.52f), new Vector2(0.25f, 0.52f), new Vector2(180f, 50f), Vector2.zero);
        PlaceUnderPanel(client_btn, new Vector2(0.75f, 0.52f), new Vector2(0.75f, 0.52f), new Vector2(180f, 50f), Vector2.zero);
        PlaceUnderPanel(joinCodeInputField, new Vector2(0.5f, 0.19f), new Vector2(0.5f, 0.19f), new Vector2(300f, 44f), Vector2.zero);
    }

    private void PlaceUnderPanel(Component target, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 anchoredPos)
    {
        if (target == null || uiPanel == null) { return; }
        RectTransform rt = target.GetComponent<RectTransform>();
        if (rt == null) { return; }
        rt.SetParent(uiPanel, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
    }

    private void PlaceJoinCodeBottomRight()
    {
        if (joinCodeText == null)
        {
            return;
        }

        RectTransform rt = joinCodeText.GetComponent<RectTransform>();
        if (rt == null)
        {
            return;
        }

        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(440f, 42f);
        rt.anchoredPosition = new Vector2(-18f, 16f);

        joinCodeText.alignment = TextAlignmentOptions.BottomRight;
        joinCodeText.fontSize = 28f;
    }
}
