/* Team members: Thomas, Yebeen, Andrew
 * Script file that handles monster movement and attacking
 */

using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Monster : NetworkBehaviour {
    // MONSTER ATTRIBUTES
    private int health = 100;
    private int damage = 10;
    private float speed = 500.0f;
    private float follow_distance = 5000.0f;
    private GameObject follow_player = null;

    // OTHER OBJECTS
    private Rigidbody rb;
    private Transform t;

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
    }

    // RUNS EVERY FRAME
    public void Update() {
        if (!IsServer) {
            return;
        }
        Search();
        if (follow_player != null) {
            FollowPlayer();
        }
    }

    // SEARCHES FOR A NEARBY PLAYER
    private void Search() {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        float min_distance = follow_distance;
        foreach (GameObject player in players) {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance < min_distance) {
                min_distance = distance;
                follow_player = player;
            }
        }
    }

    // FOLLOWS THE NEAREST PLAYER
    private void FollowPlayer() {
        Transform player_transform = follow_player.transform;
        transform.LookAt(player_transform);
        if (rb != null) {
            Vector3 worldDelta = t.forward * speed * Time.deltaTime;
            if (worldDelta.sqrMagnitude > 0.000001f) {
                Vector3 dir = worldDelta.normalized;
                float dist = worldDelta.magnitude;
                if (rb.SweepTest(dir, out RaycastHit hit, dist + 0.05f, QueryTriggerInteraction.Ignore)) {
                    float allowed = Mathf.Max(0f, hit.distance - 0.02f);
                    worldDelta = dir * allowed;
                }
            }
            rb.MovePosition(rb.position + worldDelta);
        } else {
            t.Translate(Vector3.forward * speed * Time.deltaTime);
        }
    }

    public void TakeDamage(int damage) {
        if (!IsServer) {
            return;
        }
        health -= damage;
        Debug.Log("Monster's health is now: " + health);
        if (health <= 0) {
            Die();
        }
    }

    public int GetHealth() {
        return health;
    }

    public void Die() {
        if (!IsServer) {
            return;
        }
        Debug.Log("Monster has died!");
        GetComponent<NetworkObject>().Despawn();
    }

    // DEALS DAMAGE TO PLAYERS ON CONTACT
    private void DealDamage(Player player)
    {
        player.TakeDamage(damage);
        Debug.Log("Monster dealt " + damage + " damage to player " + player.OwnerClientId);
    }

    // DETECTS A COLLISION WITH AN OBJECT
    private void OnCollisionEnter(Collision collision) {
        if (!IsServer) {
            return;
        }
        if (collision.gameObject.CompareTag("Player")) {
            Player player = collision.gameObject.GetComponent<Player>();
            if (player != null) {
                DealDamage(player);
            }
        }
    }

    // SETS THE MONSTER'S HEALTH
    public void SetHealth(int health) {
        this.health = health;
    }

    // SETS THE MONSTER'S DAMAGE
    public void SetDamage(int damage) {
        this.damage = damage;
    }

    // SETS THE MONSTER'S SPEED
    public void SetSpeed(float speed) {
        this.speed = speed;
    }

    // SETS THE MAXIMUM DISTANCE A MONSTER CAN DETECT A PLAYER FROM
    public void SetFollowDistance(float follow_distance) {
        this.follow_distance = follow_distance;
    }
}
