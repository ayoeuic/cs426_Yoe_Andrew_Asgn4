using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class ComponentTracker : NetworkBehaviour {
    private readonly string[] colors = { "red", "orange", "yellow", "green", "blue", "navy", "purple" };
    private readonly string[] componentNames = {
        "motherboard",
        "cpu",
        "ram",
        "gpu",
        "ssd",
        "hdd",
        "power supply (psu)"
    };

    [SerializeField] private GameObject[] componentPrefabs;
    [Header("Victory Cube")]
    [SerializeField] private string rainbowCombinedCubeName = "Cube_Rainbow_Combined";
    [SerializeField] private string[] orderedCubeSceneNames = {
        "Cube_Red",
        "Cube_Orange",
        "Cube_Yellow",
        "Cube_Green",
        "Cube_Blue",
        "Cube_Navy",
        "Cube_Purple"
    };
    [SerializeField] private GameObject rainbowCombinedCubePrefab;
    [SerializeField] private Transform rainbowCombinedSpawnPoint;
    [SerializeField] private bool hideScenePlaceholderOnStart = true;

    [Header("Popup Message")]
    [SerializeField] private TMP_Text pickupInfoText;
    [SerializeField] private float pickupInfoDuration = 2.5f;
    [SerializeField] private float pickupInfoDefaultFontSize = 42f;
    [SerializeField] private float pickupInfoLongMessageFontSize = 25f;

    [SerializeField] private Transform victoryLocation;
    [SerializeField] private GameObject endGameBillboard;
    [SerializeField] private bool buildEndGameUIAtRuntime = true;
    [SerializeField] private int endGameOrderInputCount = 7;
    [Header("End Game Question")]
    [TextArea] [SerializeField] private string endGameQuestion = "In which order should a PC be built?";
    [TextArea] [SerializeField] private string endGameAnswer = "motherboard -> cpu -> ram -> gpu -> ssd -> hdd -> power supply (psu)";
    [SerializeField] private string[] endGameAnswerOrder;
    [SerializeField] private TMP_Text endGameQuestionText;
    [SerializeField] private TMP_Text endGameAnswerText;
    [SerializeField] private TMP_InputField endGameAnswerInput;
    [SerializeField] private TMP_InputField[] endGameOrderInputs;
    [SerializeField] private Button endGameSubmitButton;
    [SerializeField] private TMP_Text endGameResultText;
    [SerializeField] private bool revealAnswerOnWin = true;

    public NetworkList<int> collectedIndices;

    private bool quizActive;
    private bool quizResolved;
    private ulong winnerClientId = ulong.MaxValue;
    private bool combinedCubeSpawned;
    private Coroutine infoCoroutine;
    private bool interactionCollidersInitialized;

    private void Awake() {
        collectedIndices = new NetworkList<int>();
    }

    public override void OnNetworkSpawn() {
        EnsureInteractableCubeColliders();

        if (endGameBillboard != null) {
            endGameBillboard.SetActive(false);
        }

        if (!IsServer) {
            return;
        }

        if (hideScenePlaceholderOnStart) {
            HideCombinedPlaceholderClientRpc(rainbowCombinedCubeName);
        }
    }

    private void EnsureInteractableCubeColliders() {
        if (interactionCollidersInitialized) {
            return;
        }
        interactionCollidersInitialized = true;

        string[] interactTokens = {
            "cube_red",
            "cube_orange",
            "cube_yellow",
            "cube_green",
            "cube_blue",
            "cube_navy",
            "cube_purple",
            "cube_rainbow_combined"
        };

        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++) {
            Transform tr = transforms[i];
            if (tr == null || tr.gameObject == null || !tr.gameObject.scene.IsValid()) {
                continue;
            }

            string nameLower = tr.gameObject.name.ToLowerInvariant();
            bool isTarget = false;
            for (int t = 0; t < interactTokens.Length; t++) {
                if (nameLower.Contains(interactTokens[t])) {
                    isTarget = true;
                    break;
                }
            }
            if (!isTarget) {
                continue;
            }

            // IgnoreRaycast layer prevents any click hit detection.
            if (tr.gameObject.layer == 2) {
                tr.gameObject.layer = 0;
            }

            Collider col = tr.GetComponent<Collider>();
            if (col == null) {
                col = tr.gameObject.AddComponent<BoxCollider>();
            }

            if (col != null) {
                col.enabled = true;
                col.isTrigger = false;
            }
        }
    }

    private void OnEnable() {
        if (endGameSubmitButton != null) {
            endGameSubmitButton.onClick.AddListener(SubmitEndGameAnswer);
        }
    }

    private void OnDisable() {
        if (endGameSubmitButton != null) {
            endGameSubmitButton.onClick.RemoveListener(SubmitEndGameAnswer);
        }
    }

    public bool TryInteractWithTarget(GameObject targetObject, ulong requesterClientId) {
        if (!IsServer || targetObject == null) {
            return false;
        }

        GameObject rootObject = targetObject;
        NetworkObject targetNetworkObject = targetObject.GetComponentInParent<NetworkObject>();
        if (targetNetworkObject != null) {
            rootObject = targetNetworkObject.gameObject;
        }

        string targetName = rootObject.name;
        if (NamesEqual(targetName, rainbowCombinedCubeName)) {
            HandleRainbowCubeClicked(rootObject, requesterClientId);
            return true;
        }

        int colorIndex = GetColorIndexFromObjectName(targetName);
        if (colorIndex < 0) {
            return false;
        }

        HandleColoredCubeClicked(rootObject, colorIndex, requesterClientId);
        return true;
    }

    public bool TryInteractByToken(string targetToken, Vector3 aroundPoint, ulong requesterClientId) {
        if (!IsServer || string.IsNullOrWhiteSpace(targetToken)) {
            return false;
        }

        int requiredIndex = collectedIndices.Count;
        if (requiredIndex >= 0 && requiredIndex < orderedCubeSceneNames.Length && NamesEqual(targetToken, colors[requiredIndex])) {
            GameObject requiredCube = FindSceneObjectByName(orderedCubeSceneNames[requiredIndex], includeInactive: false);
            if (requiredCube != null) {
                return TryInteractWithTarget(requiredCube, requesterClientId);
            }
        }

        if (NamesEqual(targetToken, rainbowCombinedCubeName) || targetToken.ToLowerInvariant().Contains("rainbow")) {
            GameObject rainbowCube = FindSceneObjectByName(rainbowCombinedCubeName, includeInactive: false);
            if (rainbowCube != null) {
                return TryInteractWithTarget(rainbowCube, requesterClientId);
            }
        }

        GameObject targetObject = FindClosestInteractableByToken(targetToken, aroundPoint, 6.0f);
        if (targetObject == null) {
            Debug.LogWarning($"ComponentTracker: Could not find interactable target for token '{targetToken}' near {aroundPoint}.");
            return false;
        }

        return TryInteractWithTarget(targetObject, requesterClientId);
    }

    public bool TryInteractByRay(Vector3 rayOrigin, Vector3 rayDirection, float maxDistance, ulong requesterClientId) {
        if (!IsServer) {
            return false;
        }

        int requiredIndex = collectedIndices.Count;
        if (requiredIndex >= 0 && requiredIndex < orderedCubeSceneNames.Length) {
            GameObject requiredCube = FindSceneObjectByName(orderedCubeSceneNames[requiredIndex], includeInactive: false);
            if (requiredCube != null && IsObjectNearRay(requiredCube.transform.position, rayOrigin, rayDirection, maxDistance, 2000.0f)) {
                return TryInteractWithTarget(requiredCube, requesterClientId);
            }
        } else {
            GameObject rainbowCube = FindSceneObjectByName(rainbowCombinedCubeName, includeInactive: false);
            if (rainbowCube != null && IsObjectNearRay(rainbowCube.transform.position, rayOrigin, rayDirection, maxDistance, 2000.0f)) {
                return TryInteractWithTarget(rainbowCube, requesterClientId);
            }
        }

        Ray ray = new Ray(rayOrigin, rayDirection.normalized);
        RaycastHit[] hits = Physics.SphereCastAll(ray, 0.2f, maxDistance, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) {
            GameObject byRayName = FindClosestInteractableByRay(ray, maxDistance);
            if (byRayName != null) {
                return TryInteractWithTarget(byRayName, requesterClientId);
            }
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++) {
            if (hits[i].collider == null || hits[i].collider.gameObject == null) {
                continue;
            }

            GameObject candidate = hits[i].collider.gameObject;
            if (TryInteractWithTarget(candidate, requesterClientId)) {
                return true;
            }
        }

        GameObject fallbackByRayName = FindClosestInteractableByRay(ray, maxDistance);
        if (fallbackByRayName != null) {
            return TryInteractWithTarget(fallbackByRayName, requesterClientId);
        }

        return false;
    }

    public bool TryInteractByPlayerView(Vector3 playerPos, Vector3 playerForward, float maxDistance, ulong requesterClientId) {
        if (!IsServer) {
            return false;
        }

        string requiredToken = collectedIndices.Count < colors.Length
            ? colors[collectedIndices.Count]
            : rainbowCombinedCubeName.ToLowerInvariant();

        GameObject candidate = FindClosestByView(requiredToken, playerPos, playerForward, maxDistance, 0.45f);
        if (candidate != null) {
            return TryInteractWithTarget(candidate, requesterClientId);
        }

        if (requiredToken != rainbowCombinedCubeName.ToLowerInvariant()) {
            GameObject rainbow = FindClosestByView(rainbowCombinedCubeName.ToLowerInvariant(), playerPos, playerForward, maxDistance, 0.45f);
            if (rainbow != null) {
                return TryInteractWithTarget(rainbow, requesterClientId);
            }
        }

        return false;
    }

    [ClientRpc]
    private void WinGameClientRpc() {
        ActivateQuizUiForLocalPlayer();
    }

    private void ActivateQuizUiForLocalPlayer() {
        quizActive = true;
        quizResolved = false;

        EnsureRuntimeEndGameUI();

        if (endGameBillboard != null) {
            endGameBillboard.SetActive(true);
        }

        if (endGameQuestionText != null) {
            endGameQuestionText.text = endGameQuestion;
        }

        if (endGameAnswerText != null) {
            endGameAnswerText.text = string.Empty;
            endGameAnswerText.gameObject.SetActive(false);
        }

        if (endGameResultText != null) {
            endGameResultText.text = "";
        }

        EnableAnswerInputs(true);
        ClearAnswerInputs();

        if (IsOwner && victoryLocation != null) {
            transform.position = victoryLocation.position;
            if (TryGetComponent<Rigidbody>(out var rb)) {
                rb.linearVelocity = Vector3.zero;
            }
            Debug.Log("you collected all seven and won the game!");
        }
    }

    [ClientRpc]
    private void HideCombinedPlaceholderClientRpc(string targetName) {
        if (string.IsNullOrWhiteSpace(targetName)) {
            return;
        }

        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++) {
            Transform tr = transforms[i];
            if (tr == null || tr.gameObject == null) {
                continue;
            }

            if (tr.name.ToLowerInvariant().Contains(targetName.ToLowerInvariant())) {
                tr.gameObject.SetActive(false);
            }
        }
    }

    [ClientRpc]
    private void ShowPickupInfoClientRpc(string message, float duration, ClientRpcParams clientRpcParams = default) {
        if (!IsOwner) {
            return;
        }

        EnsurePickupInfoText();
        if (pickupInfoText == null) {
            return;
        }

        if (infoCoroutine != null) {
            StopCoroutine(infoCoroutine);
        }

        infoCoroutine = StartCoroutine(ShowPickupInfoRoutine(message, duration));
    }

    [ClientRpc]
    private void ShowLocalVictoryThenQuizClientRpc(ClientRpcParams clientRpcParams = default) {
        if (!IsOwner) {
            return;
        }

        EnsurePickupInfoText();
        if (infoCoroutine != null) {
            StopCoroutine(infoCoroutine);
        }

        infoCoroutine = StartCoroutine(ShowVictoryThenQuizRoutine());
    }

    private void EnsureRuntimeEndGameUI() {
        if (!buildEndGameUIAtRuntime) {
            return;
        }

        bool hasInputs = (endGameOrderInputs != null && endGameOrderInputs.Length > 0) || endGameAnswerInput != null;
        if (endGameBillboard != null && endGameQuestionText != null && endGameSubmitButton != null && endGameResultText != null && hasInputs) {
            return;
        }

        EndGameUIBuilder builder = GetComponent<EndGameUIBuilder>();
        if (builder == null) {
            builder = gameObject.AddComponent<EndGameUIBuilder>();
        }

        EndGameUIBuilder.EndGameUIRefs refs = builder.Build(endGameOrderInputCount > 0 ? endGameOrderInputCount : colors.Length);
        ApplyEndGameUIRefs(refs);
    }

    private void ApplyEndGameUIRefs(EndGameUIBuilder.EndGameUIRefs refs) {
        if (refs == null) {
            return;
        }

        endGameBillboard = refs.billboard;
        endGameQuestionText = refs.questionText;
        endGameAnswerText = refs.answerText;
        endGameResultText = refs.resultText;
        endGameAnswerInput = refs.answerInput;
        endGameOrderInputs = refs.orderInputs;
        endGameSubmitButton = refs.submitButton;

        if (endGameSubmitButton != null) {
            endGameSubmitButton.onClick.RemoveListener(SubmitEndGameAnswer);
            endGameSubmitButton.onClick.AddListener(SubmitEndGameAnswer);
        }
    }

    private void EnableAnswerInputs(bool enabled) {
        if (endGameAnswerInput != null) {
            endGameAnswerInput.interactable = enabled;
        }

        if (endGameOrderInputs != null) {
            foreach (var input in endGameOrderInputs) {
                if (input != null) {
                    input.interactable = enabled;
                }
            }
        }

        if (endGameSubmitButton != null) {
            endGameSubmitButton.interactable = enabled;
        }
    }

    private void ClearAnswerInputs() {
        if (endGameAnswerInput != null) {
            endGameAnswerInput.text = "";
        }

        if (endGameOrderInputs != null) {
            foreach (var input in endGameOrderInputs) {
                if (input != null) {
                    input.text = "";
                }
            }
        }
    }

    public void SubmitEndGameAnswer() {
        if (!IsOwner || !quizActive || quizResolved) {
            return;
        }

        string packedAnswer = BuildPackedAnswer();
        if (string.IsNullOrWhiteSpace(packedAnswer)) {
            if (endGameResultText != null) {
                endGameResultText.text = "Enter an order before submitting.";
            }
            return;
        }

        SubmitEndGameAnswerServerRpc(packedAnswer);
    }

    private string BuildPackedAnswer() {
        List<string> parts = new List<string>();
        if (endGameOrderInputs != null && endGameOrderInputs.Length > 0) {
            foreach (var input in endGameOrderInputs) {
                if (input == null) {
                    continue;
                }
                string value = NormalizeToken(input.text);
                if (!string.IsNullOrEmpty(value)) {
                    parts.Add(value);
                }
            }
        } else if (endGameAnswerInput != null) {
            string raw = endGameAnswerInput.text;
            parts.AddRange(ParseOrder(raw));
        }

        return string.Join("|", parts);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitEndGameAnswerServerRpc(string packedAnswer, RpcParams rpcParams = default) {
        if (!quizActive || quizResolved) {
            return;
        }

        List<string> submitted = ParsePackedAnswer(packedAnswer);
        List<string> expected = GetExpectedOrder();

        if (IsAnswerCorrect(submitted, expected)) {
            quizResolved = true;
            winnerClientId = rpcParams.Receive.SenderClientId;
            AnnounceWinnerClientRpc(winnerClientId);
        } else {
            NotifyIncorrectClientRpc(new ClientRpcParams {
                Send = new ClientRpcSendParams {
                    TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
                }
            });
        }
    }

    [ClientRpc]
    private void NotifyIncorrectClientRpc(ClientRpcParams clientRpcParams = default) {
        if (endGameResultText != null) {
            endGameResultText.text = "Incorrect. Try again.";
        }
    }

    [ClientRpc]
    private void AnnounceWinnerClientRpc(ulong winnerId) {
        quizResolved = true;
        EnableAnswerInputs(false);
        if (endGameResultText != null) {
            endGameResultText.text = winnerId == NetworkManager.Singleton.LocalClientId
                ? "You answered correctly first!"
                : $"Player {winnerId} answered correctly first.";
        }

        if (revealAnswerOnWin && endGameAnswerText != null) {
            endGameAnswerText.text = endGameAnswer;
            endGameAnswerText.gameObject.SetActive(true);
        }
    }

    private List<string> GetExpectedOrder() {
        if (endGameAnswerOrder != null && endGameAnswerOrder.Length > 0) {
            List<string> list = new List<string>();
            foreach (var item in endGameAnswerOrder) {
                string token = NormalizeToken(item);
                if (!string.IsNullOrEmpty(token)) {
                    list.Add(token);
                }
            }
            return list;
        }

        return ParseOrder(endGameAnswer);
    }

    private static List<string> ParsePackedAnswer(string packed) {
        List<string> list = new List<string>();
        if (string.IsNullOrWhiteSpace(packed)) {
            return list;
        }

        string[] parts = packed.Split('|');
        foreach (var part in parts) {
            string token = NormalizeToken(part);
            if (!string.IsNullOrEmpty(token)) {
                list.Add(token);
            }
        }

        return list;
    }

    private static List<string> ParseOrder(string raw) {
        List<string> list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) {
            return list;
        }

        string[] parts = raw.Split(new[] { "->", ",", "\n", "\r", ";" }, System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts) {
            string token = NormalizeToken(part);
            if (!string.IsNullOrEmpty(token)) {
                list.Add(token);
            }
        }

        return list;
    }

    private static string NormalizeToken(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        value = value.ToLowerInvariant().Trim();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (char c in value) {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)) {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }

    private static bool IsAnswerCorrect(List<string> submitted, List<string> expected) {
        if (submitted.Count != expected.Count || submitted.Count == 0) {
            return false;
        }

        for (int i = 0; i < expected.Count; i++) {
            if (!TokensEquivalent(submitted[i], expected[i])) {
                return false;
            }
        }

        return true;
    }

    private static bool TokensEquivalent(string submitted, string expected) {
        string s = NormalizeToken(submitted);
        string e = NormalizeToken(expected);

        if (string.Equals(s, e, System.StringComparison.Ordinal)) {
            return true;
        }

        // Accept common aliases for the PSU step.
        if ((s == "power supply" || s == "psu" || s == "power supply psu") &&
            (e == "power supply" || e == "psu" || e == "power supply psu")) {
            return true;
        }

        return false;
    }

    private void HandleColoredCubeClicked(GameObject targetObject, int colorIndex, ulong requesterClientId) {
        int nextIndex = collectedIndices.Count;
        if (nextIndex >= colors.Length) {
            SendPopupToClient(requesterClientId, "All components are already collected.");
            return;
        }

        if (colorIndex != nextIndex) {
            SendPopupToClient(requesterClientId, $"Wrong order. Find {colors[nextIndex].ToUpper()} first.");
            return;
        }

        collectedIndices.Add(colorIndex);

        if (targetObject != null) {
            NetworkObject targetNetworkObject = targetObject.GetComponent<NetworkObject>();
            if (targetNetworkObject != null && targetNetworkObject.IsSpawned) {
                targetNetworkObject.Despawn();
            } else {
                targetObject.SetActive(false);
                HideObjectByNameClientRpc(targetObject.name);
            }
        }

        string componentName = colorIndex < componentNames.Length ? componentNames[colorIndex] : colors[colorIndex];
        string collectedMessage = $"{colors[colorIndex].ToUpper()} cube collected: {componentName}";

        if (collectedIndices.Count == colors.Length) {
            SpawnRainbowCombinedCube();
            string combinedMessage = "All components collected. Combined cube is now available!";
            SendPopupToClient(
                requesterClientId,
                $"{collectedMessage}\n{combinedMessage}",
                Mathf.Max(pickupInfoDuration, 4.5f)
            );
            return;
        }

        SendPopupToClient(requesterClientId, collectedMessage);
    }

    private void HandleRainbowCubeClicked(GameObject targetObject, ulong requesterClientId) {
        if (collectedIndices.Count < colors.Length) {
            SendPopupToClient(requesterClientId, "Collect all 7 rainbow cubes in order first.");
            return;
        }

        if (targetObject != null) {
            NetworkObject targetNetworkObject = targetObject.GetComponent<NetworkObject>();
            if (targetNetworkObject != null && targetNetworkObject.IsSpawned) {
                targetNetworkObject.Despawn();
            } else {
                targetObject.SetActive(false);
                HideObjectByNameClientRpc(targetObject.name);
            }
        }

        quizActive = true;
        quizResolved = false;
        winnerClientId = ulong.MaxValue;

        ShowLocalVictoryThenQuizClientRpc(BuildTargetClientParams(requesterClientId));
    }

    [ClientRpc]
    private void HideObjectByNameClientRpc(string objectName) {
        if (string.IsNullOrWhiteSpace(objectName)) {
            return;
        }

        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        string target = objectName.ToLowerInvariant();
        for (int i = 0; i < transforms.Length; i++) {
            Transform tr = transforms[i];
            if (tr == null || tr.gameObject == null) {
                continue;
            }

            if (NamesEqual(tr.gameObject.name, objectName) || tr.gameObject.name.ToLowerInvariant().Contains(target)) {
                tr.gameObject.SetActive(false);
                break;
            }
        }
    }

    [ClientRpc]
    private void ShowObjectByNameClientRpc(string objectName) {
        if (string.IsNullOrWhiteSpace(objectName)) {
            return;
        }

        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        string target = objectName.ToLowerInvariant();
        for (int i = 0; i < transforms.Length; i++) {
            Transform tr = transforms[i];
            if (tr == null || tr.gameObject == null) {
                continue;
            }

            if (NamesEqual(tr.gameObject.name, objectName) || tr.gameObject.name.ToLowerInvariant().Contains(target)) {
                tr.gameObject.SetActive(true);
                break;
            }
        }
    }

    private int GetColorIndexFromObjectName(string targetNameLower) {
        if (string.IsNullOrEmpty(targetNameLower)) {
            return -1;
        }

        for (int i = 0; i < orderedCubeSceneNames.Length; i++) {
            if (NamesEqual(targetNameLower, orderedCubeSceneNames[i])) {
                return i;
            }
        }

        string normalized = targetNameLower.ToLowerInvariant();
        for (int i = 0; i < colors.Length; i++) {
            if (normalized.Contains(colors[i])) {
                return i;
            }
        }

        return -1;
    }

    private GameObject FindClosestInteractableByToken(string token, Vector3 aroundPoint, float radius) {
        string tokenLower = token.ToLowerInvariant();
        bool targetRainbow = tokenLower.Contains(rainbowCombinedCubeName.ToLowerInvariant());

        Collider[] nearby = Physics.OverlapSphere(aroundPoint, radius, ~0, QueryTriggerInteraction.Collide);
        GameObject best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < nearby.Length; i++) {
            Collider col = nearby[i];
            if (col == null || col.gameObject == null) {
                continue;
            }

            GameObject candidate = col.GetComponentInParent<NetworkObject>() != null
                ? col.GetComponentInParent<NetworkObject>().gameObject
                : col.gameObject;

            string nameLower = candidate.name.ToLowerInvariant();
            bool isMatch = targetRainbow
                ? nameLower.Contains(rainbowCombinedCubeName.ToLowerInvariant())
                : nameLower.Contains(tokenLower);

            if (!isMatch) {
                continue;
            }

            if (!candidate.activeInHierarchy) {
                continue;
            }

            float dist = Vector3.Distance(aroundPoint, col.ClosestPoint(aroundPoint));
            if (dist < bestDist) {
                bestDist = dist;
                best = candidate;
            }
        }

        return best;
    }

    private GameObject FindSceneObjectByName(string exactName, bool includeInactive) {
        if (string.IsNullOrWhiteSpace(exactName)) {
            return null;
        }

        FindObjectsInactive include = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
        Transform[] transforms = Object.FindObjectsByType<Transform>(include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++) {
            Transform tr = transforms[i];
            if (tr == null || tr.gameObject == null || !tr.gameObject.scene.IsValid()) {
                continue;
            }

            if (NamesEqual(tr.gameObject.name, exactName)) {
                return tr.gameObject;
            }
        }

        return null;
    }

    private static bool NamesEqual(string a, string b) {
        return string.Equals(a?.Trim(), b?.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsObjectNearRay(Vector3 targetPos, Vector3 rayOrigin, Vector3 rayDirection, float maxDistance, float maxPerpendicularDistance) {
        Vector3 dir = rayDirection.sqrMagnitude > 0.0001f ? rayDirection.normalized : Vector3.forward;
        Vector3 toTarget = targetPos - rayOrigin;
        float along = Vector3.Dot(toTarget, dir);
        if (along < 0f || along > maxDistance) {
            return false;
        }

        Vector3 closestPoint = rayOrigin + dir * along;
        float perpendicular = Vector3.Distance(targetPos, closestPoint);
        return perpendicular <= maxPerpendicularDistance;
    }

    private GameObject FindClosestInteractableByRay(Ray ray, float maxDistance) {
        string[] tokens = {
            "red",
            "orange",
            "yellow",
            "green",
            "blue",
            "navy",
            "purple",
            "rainbow",
            rainbowCombinedCubeName.ToLowerInvariant()
        };

        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        GameObject best = null;
        float bestPerp = float.MaxValue;
        // SceneBackUp is large-scale; allow wider tolerance for far cube clicks.
        float maxPerp = 2000.0f;

        for (int i = 0; i < transforms.Length; i++) {
            Transform tr = transforms[i];
            if (tr == null || tr.gameObject == null || !tr.gameObject.scene.IsValid() || !tr.gameObject.activeInHierarchy) {
                continue;
            }

            string nameLower = tr.gameObject.name.ToLowerInvariant();
            bool isTarget = false;
            for (int t = 0; t < tokens.Length; t++) {
                if (nameLower.Contains(tokens[t])) {
                    isTarget = true;
                    break;
                }
            }
            if (!isTarget) {
                continue;
            }

            Vector3 toPoint = tr.position - ray.origin;
            float along = Vector3.Dot(toPoint, ray.direction);
            if (along < 0f || along > maxDistance) {
                continue;
            }

            Vector3 closestOnRay = ray.origin + ray.direction * along;
            float perp = Vector3.Distance(tr.position, closestOnRay);
            if (perp > maxPerp) {
                continue;
            }

            if (perp < bestPerp) {
                bestPerp = perp;
                best = tr.gameObject;
            }
        }

        return best;
    }

    private GameObject FindClosestByView(string token, Vector3 playerPos, Vector3 playerForward, float maxDistance, float minDot) {
        if (string.IsNullOrWhiteSpace(token)) {
            return null;
        }

        string tokenLower = token.ToLowerInvariant();
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        GameObject best = null;
        float bestScore = float.MinValue;
        Vector3 forward = playerForward.sqrMagnitude > 0.001f ? playerForward.normalized : Vector3.forward;

        for (int i = 0; i < transforms.Length; i++) {
            Transform tr = transforms[i];
            if (tr == null || tr.gameObject == null || !tr.gameObject.scene.IsValid() || !tr.gameObject.activeInHierarchy) {
                continue;
            }

            string nameLower = tr.gameObject.name.ToLowerInvariant();
            if (!nameLower.Contains(tokenLower)) {
                continue;
            }

            Vector3 toTarget = tr.position - playerPos;
            float distance = toTarget.magnitude;
            if (distance <= 0.001f || distance > maxDistance) {
                continue;
            }

            float dot = Vector3.Dot(forward, toTarget.normalized);
            if (dot < minDot) {
                continue;
            }

            float score = dot * 1000f - distance;
            if (score > bestScore) {
                bestScore = score;
                best = tr.gameObject;
            }
        }

        return best;
    }

    private void SpawnRainbowCombinedCube() {
        if (combinedCubeSpawned) {
            return;
        }

        combinedCubeSpawned = true;

        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;
        if (rainbowCombinedSpawnPoint != null) {
            spawnPos = rainbowCombinedSpawnPoint.position;
            spawnRot = rainbowCombinedSpawnPoint.rotation;
        } else {
            Transform foundSpawnPoint = FindTransformByName(rainbowCombinedCubeName);
            if (foundSpawnPoint != null) {
                spawnPos = foundSpawnPoint.position;
                spawnRot = foundSpawnPoint.rotation;
            }
        }

        // SceneBackUp contains Cube_Rainbow_Combined as a scene object (without NetworkObject).
        // In that case we simply re-enable and position it on all clients.
        GameObject sceneCombinedCube = FindSceneObjectByName(rainbowCombinedCubeName, includeInactive: true);
        if (sceneCombinedCube != null) {
            sceneCombinedCube.transform.position = spawnPos;
            sceneCombinedCube.transform.rotation = spawnRot;
            sceneCombinedCube.SetActive(true);
            ShowObjectByNameClientRpc(sceneCombinedCube.name);
            return;
        }

        if (rainbowCombinedCubePrefab == null) {
            Transform placeholder = FindTransformByName(rainbowCombinedCubeName);
            if (placeholder != null) {
                rainbowCombinedCubePrefab = placeholder.gameObject;
            }
        }

        if (rainbowCombinedCubePrefab == null) {
            Debug.LogError($"ComponentTracker: Could not spawn '{rainbowCombinedCubeName}'. Assign rainbowCombinedCubePrefab.");
            return;
        }

        GameObject combinedCube = Instantiate(rainbowCombinedCubePrefab, spawnPos, spawnRot);
        NetworkObject combinedNetworkObject = combinedCube.GetComponent<NetworkObject>();
        if (combinedNetworkObject == null) {
            Debug.LogError("ComponentTracker: rainbowCombinedCubePrefab is missing NetworkObject.");
            Destroy(combinedCube);
            return;
        }

        try {
            combinedNetworkObject.Spawn();
        } catch (System.Exception e) {
            Debug.LogError($"ComponentTracker: failed to spawn rainbow combined cube. Add it to NetworkManager Network Prefabs. {e.Message}");
            Destroy(combinedCube);
        }
    }

    private Transform FindTransformByName(string partialName) {
        if (string.IsNullOrWhiteSpace(partialName)) {
            return null;
        }

        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        string target = partialName.ToLowerInvariant();
        for (int i = 0; i < transforms.Length; i++) {
            Transform tr = transforms[i];
            if (tr == null || tr.gameObject == null || !tr.gameObject.scene.IsValid()) {
                continue;
            }

            if (tr.name.ToLowerInvariant().Contains(target)) {
                return tr;
            }
        }

        return null;
    }

    private void SendPopupToClient(ulong clientId, string message) {
        ShowPickupInfoClientRpc(message, pickupInfoDuration, BuildTargetClientParams(clientId));
    }

    private void SendPopupToClient(ulong clientId, string message, float duration) {
        ShowPickupInfoClientRpc(message, duration, BuildTargetClientParams(clientId));
    }

    private void ShowInfoToAllClients(string message) {
        ShowPickupInfoClientRpc(message, pickupInfoDuration);
    }

    private static ClientRpcParams BuildTargetClientParams(ulong clientId) {
        return new ClientRpcParams {
            Send = new ClientRpcSendParams {
                TargetClientIds = new[] { clientId }
            }
        };
    }

    private void EnsurePickupInfoText() {
        if (pickupInfoText != null) {
            return;
        }

        GameObject canvasGo = new GameObject("PickupInfoCanvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1200;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject textGo = new GameObject("PickupInfoText");
        textGo.transform.SetParent(canvasGo.transform, false);
        pickupInfoText = textGo.AddComponent<TextMeshProUGUI>();
        pickupInfoText.fontSize = pickupInfoDefaultFontSize;
        pickupInfoText.alignment = TextAlignmentOptions.Center;
        pickupInfoText.color = Color.white;
        pickupInfoText.textWrappingMode = TextWrappingModes.Normal;
        pickupInfoText.gameObject.SetActive(false);

        RectTransform rect = pickupInfoText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.83f);
        rect.anchorMax = new Vector2(0.5f, 0.83f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1200f, 120f);
        rect.anchoredPosition = Vector2.zero;
    }

    private IEnumerator ShowPickupInfoRoutine(string message, float duration) {
        bool isLongCombinedMessage = message != null && message.Contains("\n");
        pickupInfoText.fontSize = isLongCombinedMessage ? pickupInfoLongMessageFontSize : pickupInfoDefaultFontSize;
        pickupInfoText.text = message;
        pickupInfoText.gameObject.SetActive(true);
        yield return new WaitForSeconds(duration);
        pickupInfoText.gameObject.SetActive(false);
        pickupInfoText.fontSize = pickupInfoDefaultFontSize;
    }

    private IEnumerator ShowVictoryThenQuizRoutine() {
        if (pickupInfoText != null) {
            pickupInfoText.text = "You collected the combined cube! You win this run.";
            pickupInfoText.gameObject.SetActive(true);
            yield return new WaitForSeconds(2.0f);
            pickupInfoText.gameObject.SetActive(false);
        } else {
            yield return new WaitForSeconds(2.0f);
        }

        ActivateQuizUiForLocalPlayer();
    }

    public void HandleDeathDrop(Vector3 currentPosition) {
        if (!IsServer || collectedIndices.Count == 0) return;

        int lastIdx = collectedIndices.Count - 1;
        int colorIndexToRespawn = collectedIndices[lastIdx];
        
        collectedIndices.RemoveAt(lastIdx);
        RespawnComponentOnMap(colorIndexToRespawn, currentPosition);
    }

    private void RespawnComponentOnMap(int index, Vector3 pos) {
        if (index >= 0 && index < componentPrefabs.Length) {
            GameObject droppedItem = Instantiate(componentPrefabs[index], pos, Quaternion.identity);
            if (droppedItem == null) {
                return;
            }

            NetworkObject droppedNetworkObject = droppedItem.GetComponent<NetworkObject>();
            if (droppedNetworkObject == null) {
                Debug.LogError($"ComponentTracker: '{componentPrefabs[index].name}' is missing NetworkObject.");
                Destroy(droppedItem);
                return;
            }

            try {
                droppedNetworkObject.Spawn();
            } catch (System.Exception e) {
                Debug.LogError($"ComponentTracker: failed to spawn '{componentPrefabs[index].name}'. Register it in NetworkManager Network Prefabs. {e.Message}");
                Destroy(droppedItem);
            }
        }
    }

    public string GetCurrentRequiredColor() {
        if (collectedIndices.Count >= colors.Length) return "complete";
        return colors[collectedIndices.Count];
    }
}
