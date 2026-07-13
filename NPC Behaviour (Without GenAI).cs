using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class NPCBehaviour : MonoBehaviour
{
    public Transform[] patrolPoints;
    public Transform player;

    [Header("Vision")]
    public float sightRange = 10f;
    public float viewAngle = 100f;
    public float catchDistance = 2f;
    public LayerMask sightMask;

    [Header("Search")]
    public float searchWaitTime = 3f;

    [Header("Speeds")]
    public float patrolSpeed = 3.5f;
    public float chaseSpeed = 6f;

    [Header("Randomized Patrol")]
    public int nearbyPatrolChoices = 3;
    public bool avoidImmediateRepeat = true;

    public GameManager gameManager;

    private NavMeshAgent agent;
    private int currentPatrolIndex = 0;
    private int lastPatrolIndex = -1;
    private Vector3 lastKnownPlayerPosition;
    private float searchTimer = 0f;

    private enum NPCState
    {
        Patrol,
        Chase,
        Search
    }

    private NPCState currentState = NPCState.Patrol;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");

            if (playerObj != null)
            {
                player = playerObj.transform;
            }
        }

        if (patrolPoints.Length > 0)
        {
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);
        }
    }

    void Update()
    {
        if (player == null) return;

        bool canSeePlayer = CanSeePlayer();

        switch (currentState)
        {
            case NPCState.Patrol:
                if (canSeePlayer)
                {
                    lastKnownPlayerPosition = player.position;
                    currentState = NPCState.Chase;
                }
                else
                {
                    Patrol();
                }
                break;

            case NPCState.Chase:
                if (canSeePlayer)
                {
                    lastKnownPlayerPosition = player.position;
                    Chase();
                }
                else
                {
                    currentState = NPCState.Search;
                    searchTimer = searchWaitTime;
                    agent.SetDestination(lastKnownPlayerPosition);
                }
                break;

            case NPCState.Search:
                if (canSeePlayer)
                {
                    currentState = NPCState.Chase;
                }
                else
                {
                    Search();
                }
                break;
        }

        CheckIfCaughtPlayer();
    }

    void Patrol()
    {
        agent.speed = patrolSpeed;

        if (patrolPoints.Length == 0) return;

        if (!agent.pathPending && agent.remainingDistance <= 0.5f)
        {
            lastPatrolIndex = currentPatrolIndex;
            currentPatrolIndex = GetRandomNearbyPatrolIndex();
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);
        }
    }

    int GetRandomNearbyPatrolIndex()
    {
        if (patrolPoints.Length == 1)
        {
            return 0;
        }

        List<int> validIndexes = new List<int>();

        for (int i = 0; i < patrolPoints.Length; i++)
        {
            if (patrolPoints[i] == null)
            {
                continue;
            }

            if (avoidImmediateRepeat && i == lastPatrolIndex)
            {
                continue;
            }

            validIndexes.Add(i);
        }

        if (validIndexes.Count == 0)
        {
            return currentPatrolIndex;
        }

        validIndexes.Sort((a, b) =>
        {
            float distA = Vector3.Distance(transform.position, patrolPoints[a].position);
            float distB = Vector3.Distance(transform.position, patrolPoints[b].position);
            return distA.CompareTo(distB);
        });

        int choiceCount = Mathf.Clamp(nearbyPatrolChoices, 9, validIndexes.Count);
        int randomChoice = Random.Range(0, choiceCount);

        return validIndexes[randomChoice];
    }


    void Chase()
    {
        agent.speed = chaseSpeed;
        agent.SetDestination(player.position);
    }

    void Search()
    {
        agent.speed = patrolSpeed;

        if (agent.pathPending) return;

        if (agent.remainingDistance <= 0.5f)
        {
            searchTimer -= Time.deltaTime;

            if (searchTimer <= 0f)
            {
                currentState = NPCState.Patrol;

                if (patrolPoints.Length > 0)
                {
                    agent.SetDestination(patrolPoints[currentPatrolIndex].position);
                }
            }
        }
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 npcEyePosition = transform.position + Vector3.up * 1.0f;
        Vector3 playerEyePosition = player.position + Vector3.up * 1.0f;

        Vector3 toPlayer = playerEyePosition - npcEyePosition;
        float distanceToPlayer = toPlayer.magnitude;

        if (distanceToPlayer > sightRange)
        {
            return false;
        }

        float angleToPlayer = Vector3.Angle(transform.forward, toPlayer.normalized);

        if (angleToPlayer > viewAngle * 0.5f)
        {
            return false;
        }

        if (Physics.Raycast(npcEyePosition, toPlayer.normalized, out RaycastHit hit, distanceToPlayer, sightMask))
        {
            if (hit.transform == player || hit.transform.root == player)
            {
                return true;
            }
        }

        return false;
    }

    void CheckIfCaughtPlayer()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= catchDistance)
        {
            Debug.Log("Player caught!");

            if (gameManager != null)
            {
                gameManager.GameOver();
            }
            else
            {
                Debug.LogError("GameManager is not assigned!");
            }
        }
    }
}