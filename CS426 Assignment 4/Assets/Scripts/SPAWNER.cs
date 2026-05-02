using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/* Team members: Thomas, Yebeen, Andrew
 * Script file that handles the spawners that spawn monsters
 */

public class Spawner : NetworkBehaviour {
    [SerializeField] private GameObject monsterPrefab;
    [SerializeField] private int spawner_level = 1;
    [SerializeField] private float spawn_delay = 10.0f;
    
    [Header("Stats Level 1")]
    [SerializeField] private int health1 = 100;
    [SerializeField] private int damage1 = 10;
    [SerializeField] private float speed1 = 500.0f;
    [SerializeField] private float follow_distance1 = 5000.0f;

    [Header("Stats Level 2")]
    [SerializeField] private int health2 = 200;
    [SerializeField] private int damage2 = 15;
    [SerializeField] private float speed2 = 600.0f;
    [SerializeField] private float follow_distance2 = 5000.0f;

    [Header("Stats Level 3")]
    [SerializeField] private int health3 = 300;
    [SerializeField] private int damage3 = 20;
    [SerializeField] private float speed3 = 700.0f;
    [SerializeField] private float follow_distance3 = 5000.0f;

    public override void OnNetworkSpawn() {
        if (IsServer) {
            invoke_repeating_spawn();
        }
    }

    private void invoke_repeating_spawn() {
        InvokeRepeating(nameof(SpawnWrapper), 0.5f, spawn_delay);
    }

    private void SpawnWrapper() {
        SpawnMonster(spawner_level);
    }

    public void SpawnMonster(int level) {
        if (!IsServer) return;

        if (monsterPrefab == null) {
            Debug.LogError("you forgot to assign the monster prefab in the inspector!");
            return;
        }

        GameObject new_monster = Instantiate(monsterPrefab, transform.position, Quaternion.identity);
        if (new_monster == null) {
            Debug.LogError("Spawner: failed to instantiate monster prefab.");
            return;
        }

        NetworkObject monsterNetworkObject = new_monster.GetComponent<NetworkObject>();
        if (monsterNetworkObject == null) {
            Debug.LogError($"Spawner: '{monsterPrefab.name}' is missing NetworkObject. Add NetworkObject to the monster prefab.");
            Destroy(new_monster);
            return;
        }

        try {
            monsterNetworkObject.Spawn();
        } catch (System.Exception e) {
            Debug.LogError($"Spawner: failed to spawn '{monsterPrefab.name}'. Make sure it's added in NetworkManager -> Network Prefabs. {e.Message}");
            Destroy(new_monster);
            return;
        }
        
        Monster monster_script = new_monster.GetComponent<Monster>();
        
        if (monster_script != null) {
            if (level == 1) {
                monster_script.SetHealth(health1);
                monster_script.SetDamage(damage1);
                monster_script.SetSpeed(speed1);
                monster_script.SetFollowDistance(follow_distance1);
            } else if (level == 2) {
                monster_script.SetHealth(health2);
                monster_script.SetDamage(damage2);
                monster_script.SetSpeed(speed2);
                monster_script.SetFollowDistance(follow_distance2);
            } else {
                monster_script.SetHealth(health3);
                monster_script.SetDamage(damage3);
                monster_script.SetSpeed(speed3);
                monster_script.SetFollowDistance(follow_distance3);
            }
        }
    }
}
