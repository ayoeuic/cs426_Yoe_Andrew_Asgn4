/* Team members: Thomas, Yebeen, Andrew
 * Script file that handles player movement, shooting, and aiming
 */

using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class Player : NetworkBehaviour {
    // PLAYER ATTRIBUTES (Can modify in Unity)
    [SerializeField] private int max_health = 100;
    [SerializeField] private float speed = 25.0f;
    [SerializeField] private float forward_bonus_speed = 2.0f;
    [SerializeField] private float jump_strength = 700.0f;
    [SerializeField] private float camera_sensitivity_while_aiming = 0.5f;
    [SerializeField] private float camera_sensitivity_while_not_aiming = 1.0f;
    [SerializeField] private float field_of_view_while_aiming = 70.0f;
    [SerializeField] private float field_of_view_while_not_aiming = 90.0f;
    [SerializeField] private float first_person_camera_height = 1.2f;
    [SerializeField] private float spawn_ground_offset = 1.1f;
    [SerializeField] private float bullet_speed = 1000.0f;
    [SerializeField] private float bullet_bloom = 0.02f;
    [SerializeField] private int[] bullet_damages = {10, 12, 14, 16, 18, 20, 23, 26, 29, 32};
    [SerializeField] private float interactionDistance = 20000.0f;
    public Slider health_slider;

    // OTHER PLAYER ATTRIBUTES
    public NetworkVariable<int> health = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private float rotation_x = 0.0f;
    private Vector3 spawn_point;
    private bool on_ground = true;
    private float camera_sensitivity;
    private Rigidbody rb;
    private Transform t;
    private bool missingShootingReferencesLogged = false;
    private readonly HashSet<int> groundCollisionIds = new HashSet<int>();
    private static readonly string[] ComponentColorNames = { "red", "orange", "yellow", "green", "blue", "navy", "purple" };
    private ComponentTracker componentTracker;

    // OTHER OBJECTS
    [SerializeField] private AudioListener audio_listener;
    [FormerlySerializedAs("camera")]
    [SerializeField] private Camera playerCamera;
    public GameObject cannon;
    [SerializeField] private GameObject bullet;
    [SerializeField] private List<Color> colors = new List<Color>();

    // RUNS ONCE
    public void Start() {
        rb = GetComponent<Rigidbody>();
        t = GetComponent<Transform>();
        if (rb != null) {
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }
        spawn_point = t.position;
        camera_sensitivity = camera_sensitivity_while_not_aiming;
        if (playerCamera != null && !playerCamera.gameObject.scene.IsValid()) {
            // Inspector might point to a prefab asset; ignore it at runtime.
            playerCamera = null;
        }
        if (playerCamera != null) {
            playerCamera.transform.localPosition = new Vector3(0, 2.5f, 1.0f); 
            playerCamera.transform.localRotation = Quaternion.identity;
        }
        CacheReferences();
        componentTracker = GetComponent<ComponentTracker>();
        // SceneBackUp uses very large world coordinates/scales for placed cubes.
        // Keep interaction distance high so ray interactions can reach those objects.
        interactionDistance = Mathf.Max(interactionDistance, 10000.0f);
    }

    // RUNS EVERY FRAME
    public void Update() {
        if (!IsOwner) {
            return;
        }

        CacheReferences();
        UpdateHealthUI();

        // WASD
        if (Keyboard.current != null && Keyboard.current.wKey.isPressed) {
            MoveForward();
        }
        if (Keyboard.current != null && Keyboard.current.aKey.isPressed) {
            MoveLeft();
        }
        if (Keyboard.current != null && Keyboard.current.sKey.isPressed) {
            MoveBackward();
        }
        if (Keyboard.current != null && Keyboard.current.dKey.isPressed) {
            MoveRight();
        }

        // JUMP (moved to left shift so space can be used for shooting)
        if (Keyboard.current != null && Keyboard.current.leftShiftKey.wasPressedThisFrame && on_ground) {
            Jump();
        }

        // MOUSE MOVEMENT
        if (Mouse.current != null && playerCamera != null) {
            Turn();
        }

        // LEFT CLICK: interaction only (no shooting)
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) {
            if (!TryHandleComponentInteraction()) {
                Debug.Log("Player interaction ray sent no local target candidate.");
            }
        }

        // SPACE BAR: shooting only
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) {
            Shoot_ServerRpc();
        }

        // RIGHT CLICK
        if (playerCamera != null && Mouse.current != null && Mouse.current.rightButton.isPressed) {
            Aim();
        } else if (playerCamera != null) {
            StopAiming();
        }
    }

    private bool TryHandleComponentInteraction() {
        if (!IsOwner || playerCamera == null) {
            return false;
        }

        Vector3 cursorScreen = Mouse.current.position.ReadValue();
        Vector3 centerScreen = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

        if (TryHandleComponentInteractionByRay(playerCamera.ScreenPointToRay(cursorScreen))) {
            return true;
        }

        if (TryHandleComponentInteractionByRay(playerCamera.ScreenPointToRay(centerScreen))) {
            return true;
        }

        // Always send center ray to server as a fallback so server can resolve interaction.
        Ray fallbackRay = playerCamera.ScreenPointToRay(centerScreen);
        RequestComponentInteractionServerRpc(fallbackRay.origin, fallbackRay.direction, string.Empty, fallbackRay.origin + fallbackRay.direction * interactionDistance);
        return false;
    }

    private bool TryHandleComponentInteractionByRay(Ray ray) {
        RaycastHit[] hits = Physics.SphereCastAll(ray, 0.2f, interactionDistance, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) {
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        bool foundTarget = false;
        Vector3 targetPoint = Vector3.zero;
        string targetToken = string.Empty;

        for (int i = 0; i < hits.Length; i++) {
            RaycastHit hit = hits[i];
            if (hit.collider == null) {
                continue;
            }

            Transform hitTransform = hit.collider.transform;
            if (hitTransform != null && hitTransform.IsChildOf(transform)) {
                continue;
            }

            string targetName = hit.collider.gameObject.name.ToLowerInvariant();
            NetworkObject parentNetworkObject = hit.collider.GetComponentInParent<NetworkObject>();
            if (parentNetworkObject != null) {
                targetName = parentNetworkObject.gameObject.name.ToLowerInvariant();
            }

            int matchedColorIndex = -1;
            for (int c = 0; c < ComponentColorNames.Length; c++) {
                if (targetName.Contains(ComponentColorNames[c])) {
                    matchedColorIndex = c;
                    break;
                }
            }

            if (matchedColorIndex >= 0) {
                foundTarget = true;
                targetPoint = hit.point;
                targetToken = ComponentColorNames[matchedColorIndex];
                break;
            }

            if (targetName.Contains("cube_rainbow_combined")) {
                foundTarget = true;
                targetPoint = hit.point;
                targetToken = "cube_rainbow_combined";
                break;
            }
        }

        if (!foundTarget) { return false; }

        if (componentTracker == null) {
            componentTracker = GetComponent<ComponentTracker>();
        }

        if (componentTracker == null) {
            return false;
        }

        RequestComponentInteractionServerRpc(ray.origin, ray.direction, targetToken, targetPoint);
        return true;
    }

    [ServerRpc]
    private void RequestComponentInteractionServerRpc(Vector3 rayOrigin, Vector3 rayDirection, string targetToken, Vector3 hitPoint, ServerRpcParams rpcParams = default) {
        if (componentTracker == null) {
            componentTracker = GetComponent<ComponentTracker>();
        }
        if (componentTracker == null) {
            return;
        }

        bool handled = false;
        if (!string.IsNullOrWhiteSpace(targetToken)) {
            handled = componentTracker.TryInteractByToken(targetToken, hitPoint, rpcParams.Receive.SenderClientId);
        }

        if (!handled) {
            handled = componentTracker.TryInteractByRay(rayOrigin, rayDirection, interactionDistance, rpcParams.Receive.SenderClientId);
        }

        if (!handled) {
            handled = componentTracker.TryInteractByPlayerView(transform.position, transform.forward, interactionDistance, rpcParams.Receive.SenderClientId);
        }

        if (!handled) {
            Debug.Log($"No interactable cube found for click by client {rpcParams.Receive.SenderClientId}.");
        }
    }

    // MOVES THE PLAYER FORWARD
    private void MoveForward() {
        MoveLocal(Vector3.forward, forward_bonus_speed);
    }

    // MOVES THE PLAYER LEFT
    private void MoveLeft() {
        MoveLocal(Vector3.left, 1.0f);
    }

    // MOVES THE PLAYER BACKWARD
    private void MoveBackward() {
        MoveLocal(-Vector3.forward, 1.0f);
    }

    // MOVES THE PLAYER RIGHT
    private void MoveRight() {
        MoveLocal(Vector3.right, 1.0f);
    }

    private void MoveLocal(Vector3 localDir, float multiplier) {
        if (rb == null || t == null) {
            return;
        }
        Vector3 targetVelocity = t.TransformDirection(localDir) * speed * multiplier;
        targetVelocity.y = rb.linearVelocity.y;
        rb.linearVelocity = targetVelocity;
    }

    // MOVES THE PLAYER UP
    private void Jump() {
        rb.AddForce(t.up * jump_strength);
    }

    // TURNS THE PLAYER
    private void Turn() {
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        float mouseX = mouseDelta.x * camera_sensitivity;
        t.Rotate(Vector3.up * mouseX);
        float mouseY = mouseDelta.y * camera_sensitivity;
        rotation_x -= mouseY;
        rotation_x = Mathf.Clamp(rotation_x, -90.0f, 90.0f);
        playerCamera.transform.localRotation = Quaternion.Euler(rotation_x, 0.0f, 0.0f);
    }

    // SHOOTS A BULLET
    [ServerRpc]
    private void Shoot_ServerRpc() {
        if (cannon == null || bullet == null) {
            if (!missingShootingReferencesLogged) {
                missingShootingReferencesLogged = true;
                Debug.LogError("Player is missing shooting references. Assign cannon and bullet in the Player component Inspector.", this);
            }
            return;
        }

        GameObject new_bullet = Instantiate(bullet, cannon.transform.position, cannon.transform.rotation);
        NetworkObject bulletNetworkObject = new_bullet.GetComponent<NetworkObject>();
        if (bulletNetworkObject != null) {
            try {
                bulletNetworkObject.Spawn();
            } catch (System.Exception e) {
                Debug.LogError($"Player: failed to spawn bullet '{bullet.name}'. Add it to NetworkManager Network Prefabs. {e.Message}");
                Destroy(new_bullet);
                return;
            }
        } else {
            Debug.LogError($"Player: bullet prefab '{bullet.name}' is missing NetworkObject.");
            Destroy(new_bullet);
            return;
        }

        Rigidbody bulletRb = new_bullet.GetComponent<Rigidbody>();
        if (bulletRb == null) {
            return;
        }

        Vector3 spread = new Vector3(
        Random.Range(-bullet_bloom, bullet_bloom),
        Random.Range(-bullet_bloom, bullet_bloom),
        Random.Range(-bullet_bloom, bullet_bloom)
        );
        Vector3 shotDirection = (new_bullet.transform.forward + spread).normalized * bullet_speed;
        bulletRb.AddForce(shotDirection);
    }

    // CAUSES THE PLAYER TO AIM DOWN SIGHTS
    private void Aim() {
        camera_sensitivity = camera_sensitivity_while_aiming;
        playerCamera.fieldOfView = field_of_view_while_aiming;
    }

    // STOPS THE PLAYER FROM AIMING DOWN SIGHTS
    private void StopAiming() {
        camera_sensitivity = camera_sensitivity_while_not_aiming;
        playerCamera.fieldOfView = field_of_view_while_not_aiming;
    }

    private void UpdateHealthUI() {
        if (health_slider != null) {
            health_slider.value = health.Value;
        }
    }

    // REDUCES THE HEALTH OF THE PLAYER
    public void TakeDamage(int damage) {
        if (!IsServer) {
            return;
        }
        health.Value -= damage;
        Debug.Log("Player " + OwnerClientId + "'s health is now: " + health.Value);
        if (health.Value <= 0) {
            Die();
        }
    }

    // KILLS AND RESPAWNS THE PLAYER
    public void Die() {
        if (!IsServer) {
            return;
        }
        Debug.Log("Player " + OwnerClientId + " has died!");
        t.position = spawn_point;
        rb.linearVelocity = Vector3.zero;
        health.Value = max_health;
    }

    // DOES STUFF WITH NETWORKING
    public override void OnNetworkSpawn() {
        if (rb == null) {
            rb = GetComponent<Rigidbody>();
        }
        if (t == null) {
            t = GetComponent<Transform>();
        }
        
        if (rb != null) {
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        if (IsServer) {
            SnapToGround();
            health.Value = max_health;
        }

        Renderer renderer = GetComponentInChildren<Renderer>();
        if (renderer != null && colors != null && colors.Count > 0) {
            int colorIndex = (int)(OwnerClientId % (ulong)colors.Count);
            renderer.material.color = colors[colorIndex];
        }

        CacheReferences();

        if (!IsOwner) {
            if (playerCamera != null && playerCamera.transform.IsChildOf(transform)) { playerCamera.enabled = false; }
            if (audio_listener != null && audio_listener.transform.IsChildOf(transform)) { audio_listener.enabled = false; }
            if (health_slider != null) { health_slider.gameObject.SetActive(false); }
            return;
        }

        if (health_slider != null) { health_slider.gameObject.SetActive(true); }
        AttachOwnerFirstPersonCamera();
    }

    private void CacheReferences() {
        if (playerCamera != null && !playerCamera.gameObject.scene.IsValid()) {
            playerCamera = null;
        }

        if (playerCamera == null) {
            playerCamera = GetComponentInChildren<Camera>(true);
        }

        if (audio_listener == null) {
            audio_listener = GetComponentInChildren<AudioListener>(true);
        }
    }

    private void AttachOwnerFirstPersonCamera() {
        if (playerCamera == null) {
            playerCamera = Camera.main;
            if (playerCamera == null) {
                playerCamera = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            }
        }
        if (playerCamera != null && !playerCamera.gameObject.scene.IsValid()) {
            playerCamera = null;
        }
        if (playerCamera == null) {
            Debug.LogError("No scene camera found for local player. Assign a scene Camera, not a prefab asset.");
            return;
        }

        playerCamera.transform.SetParent(transform, false);
        playerCamera.transform.localPosition = new Vector3(0f, first_person_camera_height, 0.05f);
        playerCamera.transform.localRotation = Quaternion.identity;
        playerCamera.fieldOfView = field_of_view_while_not_aiming;
        playerCamera.enabled = true;

        if (audio_listener == null || audio_listener.gameObject != playerCamera.gameObject) {
            audio_listener = playerCamera.GetComponent<AudioListener>();
            if (audio_listener == null) {
                audio_listener = playerCamera.gameObject.AddComponent<AudioListener>();
            }
        }

        AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var listener in listeners) {
            if (listener == null) { continue; }
            listener.enabled = listener == audio_listener;
        }
    }

    private void SnapToGround() {
        Vector3 current = transform.position;
        Vector3 rayStart = new Vector3(current.x, current.y + 200f, current.z);
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 1000f)) {
            transform.position = new Vector3(current.x, hit.point.y + spawn_ground_offset, current.z);
            spawn_point = transform.position;
        }
    }

    // UPDATES THE PLAYER WHEN ON THE GROUND
    private void OnCollisionEnter(Collision collision) {
        if (IsGroundCollision(collision)) {
            groundCollisionIds.Add(collision.collider.GetInstanceID());
            on_ground = true;
        }
    }

    private void OnCollisionStay(Collision collision) {
        if (IsGroundCollision(collision)) {
            groundCollisionIds.Add(collision.collider.GetInstanceID());
            on_ground = true;
        }
    }

    // UPDATES THE PLAYER WHEN NOT ON THE GROUND
    private void OnCollisionExit(Collision collision) {
        groundCollisionIds.Remove(collision.collider.GetInstanceID());
        on_ground = groundCollisionIds.Count > 0;
    }

    private bool IsGroundCollision(Collision collision) {
        if (collision == null || collision.collider == null) {
            return false;
        }

        // Ground check without string tags to avoid runtime errors when project tags differ.
        foreach (ContactPoint contact in collision.contacts) {
            if (contact.normal.y > 0.4f) {
                return true;
            }
        }

        return false;
    }
}
