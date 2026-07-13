using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class NPCGenAIScaffold : MonoBehaviour
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

    [Header("Tactic Speeds")]
    public float patrolSpeed = 3.5f;
    public float chaseSpeed = 6f;

    [Header("Special Zones")]
    public Transform escapeRouteZone;
    public Transform ambushZone;

    [Header("Ambush")]
    public float ambushWaitTime = 2f;
    private float ambushTimer = 0f;

    [Header("Randomized Patrol")]
    public int nearbyPatrolChoices = 5;
    public bool avoidImmediateRepeat = true;

    [Header("Zone Search Spin")]
    public float spinSpeed = 180f;
    public bool spinAtSpecialZones = true;

    private bool isSpinning = false;
    private float spinAmount = 0f;

    public GenAIDecisionClient decisionClient;
    public GameManager gameManager;
    public NPCDialogueUI dialogueUI;

    private bool isWaitingForDecision = false;
    private bool hasCaughtPlayer = false;

    private List<string> recentEscapeHistory = new List<string>();
    private Vector3 lastPlayerPosition;

    private NavMeshAgent agent;
    private int currentPatrolIndex = 0;
    private int lastPatrolIndex = -1;

    private Vector3 lastKnownPlayerPosition;
    private float searchTimer = 0f;

    private NPCTactic currentTactic = NPCTactic.NormalPatrol;
    private NPCTactic lastSpecialTactic = NPCTactic.NormalPatrol;

    private enum NPCState
    {
        Patrol,
        Chase,
        Search,
        MoveToZone,
        Ambush,
        SpinSearch
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
            currentPatrolIndex = Random.Range(0, patrolPoints.Length);
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);
        }

        ApplyTactic(NPCTactic.NormalPatrol);

        if (player != null)
        {
            lastPlayerPosition = player.position;
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
                    ChooseTacticFromPlayerPattern();
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

            case NPCState.MoveToZone:
                if (canSeePlayer)
                {
                    currentState = NPCState.Chase;
                }
                else
                {
                    MoveToZone();
                }
                break;

            case NPCState.Ambush:
                if (canSeePlayer)
                {
                    currentState = NPCState.Chase;
                }
                else
                {
                    Ambush();
                }
                break;

            case NPCState.SpinSearch:
                if (canSeePlayer)
                {
                    StopSpinSearch();
                    currentState = NPCState.Chase;
                    Chase();
                }
                else
                {
                    SpinSearch();
                }
                break;
        }

        TrackPlayerMovementDirection();
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

        int choiceCount = Mathf.Clamp(nearbyPatrolChoices, 1, validIndexes.Count);
        int randomChoice = Random.Range(0, choiceCount);

        return validIndexes[randomChoice];
    }

    void Chase()
    {
        agent.isStopped = false;
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
                ApplyTactic(NPCTactic.NormalPatrol);

                if (patrolPoints.Length > 0)
                {
                    lastPatrolIndex = currentPatrolIndex;
                    currentPatrolIndex = GetRandomNearbyPatrolIndex();
                    agent.SetDestination(patrolPoints[currentPatrolIndex].position);
                }
            }
        }
    }

    void MoveToZone()
    {
        agent.speed = patrolSpeed;

        if (agent.pathPending) return;

        if (agent.remainingDistance <= 0.5f)
        {
            if (spinAtSpecialZones)
            {
                StartSpinSearch();
            }
            else if (currentTactic == NPCTactic.AmbushAtZone)
            {
                currentState = NPCState.Ambush;
                ambushTimer = ambushWaitTime;
            }
            else
            {
                currentState = NPCState.Search;
                searchTimer = searchWaitTime;
            }
        }
    }

    void Ambush()
    {
        agent.speed = patrolSpeed;

        if (agent.pathPending) return;

        ambushTimer -= Time.deltaTime;

        if (ambushTimer <= 0f)
        {
            currentState = NPCState.Patrol;
            ApplyTactic(NPCTactic.NormalPatrol);

            if (patrolPoints.Length > 0)
            {
                lastPatrolIndex = currentPatrolIndex;
                currentPatrolIndex = GetRandomNearbyPatrolIndex();
                agent.SetDestination(patrolPoints[currentPatrolIndex].position);
            }
        }
    }

    void ChooseTacticFromPlayerPattern()
    {
        if (decisionClient == null || isWaitingForDecision)
        {
            ApplyTactic(NPCTactic.SearchLastKnownPosition);
            GoToLastKnownPosition();
            return;
        }

        NpcGameState state = new NpcGameState
        {
            npcState = currentState.ToString(),
            lastKnownPlayerZone = "UnknownZone",
            recentEscapeDirections = recentEscapeHistory.ToArray(),
            allowedTactics = new string[]
            {
                "NormalPatrol",
                "AggressiveChase",
                "GuardEscapeRoute",
                "SearchLastKnownPosition",
                "AmbushAtZone"
            },
            allowedZones = new string[]
            {
                "EscapeRouteZone",
                "AmbushZone"
            }
        };

        isWaitingForDecision = true;

        StartCoroutine(decisionClient.RequestDecision(
            state,
            decision =>
            {
                isWaitingForDecision = false;
                ApplyDecision(decision);
            },
            error =>
            {
                isWaitingForDecision = false;
                Debug.LogError("GenAI error: " + error);
                ApplyTactic(NPCTactic.SearchLastKnownPosition);
                GoToLastKnownPosition();
            }
        ));
    }

    void ApplyDecision(NpcDecision decision)
    {
        Debug.Log("LLM tactic: " + decision.tactic + " | zone: " + decision.targetZone);
        Debug.Log("NPC dialogue: " + decision.dialogue);

        string chosenTactic = decision.tactic;

        if (chosenTactic == "GuardEscapeRoute" && lastSpecialTactic == NPCTactic.GuardEscapeRoute)
        {
            if (ambushZone != null)
            {
                chosenTactic = "AmbushAtZone";
                decision.dialogue = "I will wait near the ambush zone this time.";
                Debug.Log("Balanced tactic: changed repeated GuardEscapeRoute into AmbushAtZone");
            }
        }
        else if (chosenTactic == "AmbushAtZone" && lastSpecialTactic == NPCTactic.AmbushAtZone)
        {
            if (escapeRouteZone != null)
            {
                chosenTactic = "GuardEscapeRoute";
                decision.dialogue = "I will cover the escape route this time.";
                Debug.Log("Balanced tactic: changed repeated AmbushAtZone into GuardEscapeRoute");
            }
        }

        if (dialogueUI != null && !string.IsNullOrEmpty(decision.dialogue))
        {
            dialogueUI.ShowDialogue(decision.dialogue);
        }

        switch (chosenTactic)
        {
            case "GuardEscapeRoute":
                ApplyTactic(NPCTactic.GuardEscapeRoute);
                lastSpecialTactic = NPCTactic.GuardEscapeRoute;

                if (escapeRouteZone != null)
                {
                    currentState = NPCState.MoveToZone;
                    agent.SetDestination(escapeRouteZone.position);
                }
                else
                {
                    GoToLastKnownPosition();
                }
                break;

            case "AmbushAtZone":
                ApplyTactic(NPCTactic.AmbushAtZone);
                lastSpecialTactic = NPCTactic.AmbushAtZone;

                if (ambushZone != null)
                {
                    currentState = NPCState.MoveToZone;
                    agent.SetDestination(ambushZone.position);
                }
                else
                {
                    GoToLastKnownPosition();
                }
                break;

            case "AggressiveChase":
                ApplyTactic(NPCTactic.AggressiveChase);
                GoToLastKnownPosition();
                break;

            case "SearchLastKnownPosition":
            default:
                ApplyTactic(NPCTactic.SearchLastKnownPosition);
                GoToLastKnownPosition();
                break;
        }
    }

    void ApplyTactic(NPCTactic tactic)
    {
        currentTactic = tactic;
    }

    string GetNearestZoneName(Vector3 position)
    {
        return "UnknownZone";
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

    void TrackPlayerMovementDirection()
    {
        if (player == null) return;

        Vector3 movement = player.position - lastPlayerPosition;

        if (movement.magnitude > 0.1f)
        {
            if (Mathf.Abs(movement.x) > Mathf.Abs(movement.z))
            {
                if (movement.x > 0)
                {
                    AddEscapeDirection("Right");
                }
                else
                {
                    AddEscapeDirection("Left");
                }
            }
            else
            {
                if (movement.z > 0)
                {
                    AddEscapeDirection("Forward");
                }
                else
                {
                    AddEscapeDirection("Back");
                }
            }

            lastPlayerPosition = player.position;
        }
    }

    void AddEscapeDirection(string direction)
    {
        if (recentEscapeHistory.Count == 0 || recentEscapeHistory[recentEscapeHistory.Count - 1] != direction)
        {
            recentEscapeHistory.Add(direction);

            if (recentEscapeHistory.Count > 5)
            {
                recentEscapeHistory.RemoveAt(0);
            }
        }
    }

    void StartSpinSearch()
    {
        agent.isStopped = true;
        isSpinning = true;
        spinAmount = 0f;
        currentState = NPCState.SpinSearch;
    }

    void SpinSearch()
    {
        float turnThisFrame = spinSpeed * Time.deltaTime;

        transform.Rotate(0f, turnThisFrame, 0f);
        spinAmount += turnThisFrame;

        if (spinAmount >= 360f)
        {
            StopSpinSearch();

            if (currentTactic == NPCTactic.AmbushAtZone)
            {
                currentState = NPCState.Ambush;
                ambushTimer = ambushWaitTime;
            }
            else
            {
                currentState = NPCState.Search;
                searchTimer = searchWaitTime;
            }
        }
    }

    void StopSpinSearch()
    {
        isSpinning = false;
        spinAmount = 0f;

        if (agent != null)
        {
            agent.isStopped = false;
        }
    }

    void CheckIfCaughtPlayer()
    {
        if (hasCaughtPlayer || player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= catchDistance)
        {
            hasCaughtPlayer = true;
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

    void GoToLastKnownPosition()
    {
        currentState = NPCState.Search;
        searchTimer = searchWaitTime;
        agent.SetDestination(lastKnownPlayerPosition);
    }
}