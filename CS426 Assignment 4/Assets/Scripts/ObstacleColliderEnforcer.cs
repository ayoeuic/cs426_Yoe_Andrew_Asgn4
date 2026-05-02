using UnityEngine;

public static class ObstacleColliderEnforcer {
    private static bool applied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Apply() {
        if (applied) {
            return;
        }
        applied = true;

        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        int caveDisabled = 0;
        int colliderEnabled = 0;
        int colliderAdded = 0;

        for (int i = 0; i < all.Length; i++) {
            Transform tr = all[i];
            if (tr == null || tr.gameObject == null || !tr.gameObject.scene.IsValid()) {
                continue;
            }

            GameObject go = tr.gameObject;
            string name = go.name;
            string lower = name.ToLowerInvariant();

            // Only disable colliders under the explicit cave groups.
            // Do NOT use object name contains("cave"), otherwise many non-cave obstacles
            // from the same asset pack get disabled too.
            bool isCave = IsUnderCaveGroup(tr);
            Collider[] ownCols = go.GetComponents<Collider>();

            if (isCave) {
                for (int c = 0; c < ownCols.Length; c++) {
                    Collider col = ownCols[c];
                    if (col != null && col.enabled) {
                        col.enabled = false;
                        caveDisabled++;
                    }
                }
                continue;
            }

            // Keep gameplay and UI objects untouched.
            if (IsGameplayObject(lower, go.layer)) {
                continue;
            }

            // Re-enable existing colliders for non-cave objects.
            for (int c = 0; c < ownCols.Length; c++) {
                Collider col = ownCols[c];
                if (col != null && !col.enabled) {
                    col.enabled = true;
                    colliderEnabled++;
                }
            }

            // Add collider to visual obstacle objects that do not have one.
            bool hasOwnCollider = ownCols.Length > 0;
            if (!hasOwnCollider) {
                MeshFilter mf = go.GetComponent<MeshFilter>();
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                if (mf != null && mr != null && mr.enabled && mf.sharedMesh != null) {
                    MeshCollider mc = go.AddComponent<MeshCollider>();
                    mc.convex = false;
                    mc.enabled = true;
                    colliderAdded++;
                } else {
                    // Fallback for renderer-only objects without a valid mesh filter on this transform.
                    Renderer renderer = go.GetComponent<Renderer>();
                    if (renderer != null && renderer.enabled) {
                        BoxCollider bc = go.AddComponent<BoxCollider>();
                        Vector3 localCenter = go.transform.InverseTransformPoint(renderer.bounds.center);
                        Vector3 localSize = go.transform.InverseTransformVector(renderer.bounds.size);
                        bc.center = localCenter;
                        bc.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
                        colliderAdded++;
                    }
                }
            }
        }

        Debug.Log($"ObstacleColliderEnforcer: caveDisabled={caveDisabled}, nonCaveReenabled={colliderEnabled}, nonCaveAdded={colliderAdded}");
    }

    private static bool IsUnderCaveGroup(Transform tr) {
        Transform cur = tr;
        while (cur != null) {
            string n = cur.name.ToLowerInvariant();
            if (n.StartsWith("cavegroup1") || n.StartsWith("cavegroup2") || n.StartsWith("cavegroup3")) {
                return true;
            }
            cur = cur.parent;
        }
        return false;
    }

    private static bool IsGameplayObject(string lowerName, int layer) {
        if (layer == 5) { // UI
            return true;
        }

        if (lowerName.Contains("player") ||
            lowerName.Contains("monster") ||
            lowerName.Contains("spawner") ||
            lowerName.Contains("bullet") ||
            lowerName.Contains("camera") ||
            lowerName.Contains("canvas") ||
            lowerName.Contains("eventsystem") ||
            lowerName.Contains("networkmanager")) {
            return true;
        }

        return false;
    }
}
