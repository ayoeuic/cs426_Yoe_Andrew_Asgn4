/* Team members: Thomas, Yebeen, Andrew
 * Script file that handles bullet collisions and damaging monsters
 */

using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Bullet : NetworkBehaviour {
    // BULLET ATTRIBUTES (Can modify in Unity)
    [SerializeField] private float lifetime = 1.0f;
    
    // OTHER ATTRIBUTES
    public ulong shooter_ID;
    private int damage = 25;

    public override void OnNetworkSpawn() {
        if (IsServer) {
            Destroy(gameObject, lifetime);
        }
    }

    private void OnCollisionEnter(Collision collision) {
        if (!IsServer) {
            return;
        }
        Monster monster = collision.gameObject.GetComponent<Monster>();
        if (monster != null) {
            monster.TakeDamage(damage);
            if (monster.GetHealth() <= 0) {
                //Economy.Instance?.AddMoney(10);
            }
        }
        GetComponent<NetworkObject>().Despawn();
    }
}