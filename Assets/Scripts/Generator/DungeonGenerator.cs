using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

public enum DungeonState {
    Inactive,
    GeneratingMain,
    GeneratingBranches,
    Cleanup,
    BakingNavmesh,
    Completed
}

public class DungeonGenerator : MonoBehaviour {
    [SerializeField] private GameObject[] tilePrefabs;
    [SerializeField] private GameObject[] startPrefabs;
    [SerializeField] private GameObject[] exitPrefabs;
    [SerializeField] private GameObject[] blockedPrefabs;

    /* [Header("Debugging Options")] */
    private bool useBoxColliders = false;

    /* [Header("Key Binding")]
    [SerializeField] private KeyCode reloadKey = KeyCode.Space; */

    [Header("Generation Limits")]
    [SerializeField] private float constructionDelay;
    [SerializeField] private int mainLength;
    [SerializeField] private int branchLength;
    [SerializeField] private int numBranches;

    [Header("Available at Runtime")]
    public List<Tile> generatedTiles = new List<Tile>();

    [Header("Dungeon State")]
    /* [HideInInspector] */
    public static DungeonState dungeonState = DungeonState.Inactive;

    private Transform tileFrom, tileTo, tileRoot;
    private Transform container;
    private int attempts;
    private int maxAttempts = 50;
    private List<Connector> availableConnectors = new List<Connector>();

    private void Start() {
        StartCoroutine(DungeonBuild());
        StartCoroutine(WaitForBuild());
    }

    private void Update() {
        /* if (Input.GetKeyDown(reloadKey)) {
            SceneManager.LoadScene("DungeonGenerator");
        } */
    }

    private IEnumerator DungeonBuild() {
        GameObject goContainer = new GameObject("Main Path");
        container = goContainer.transform;
        container.SetParent(transform);

        tileRoot = CreateStartTile();
        tileTo = tileRoot;

        CollisionCheck();

        dungeonState = DungeonState.GeneratingMain;

        while (generatedTiles.Count < mainLength) {
            yield return new WaitForSeconds(constructionDelay);
            tileFrom = tileTo;
            tileTo = generatedTiles.Count == mainLength - 1 ? CreateExitTile() : CreateTile();

            ConnectTiles();
            CollisionCheck();
        }
        // Get all connectors witin container that NOT already connected
        foreach (Connector connector in container.GetComponentsInChildren<Connector>()) {
            if (!connector.isConnected) {
                if (!availableConnectors.Contains(connector)) {
                    availableConnectors.Add(connector);
                }
            }
        }
        // Branching
        dungeonState = DungeonState.GeneratingBranches;

        for (int b = 0; b < numBranches; b++) {
            if (availableConnectors.Count > 0) {
                goContainer = new GameObject("Branch " + (b + 1));
                container = goContainer.transform;
                container.SetParent(transform);

                int availIndex = Random.Range(0, availableConnectors.Count);

                tileRoot = availableConnectors[availIndex].transform.parent.parent;
                availableConnectors.RemoveAt(availIndex);
                tileTo = tileRoot;

                for (int i = 0; i < branchLength - 1; i++) {
                    yield return new WaitForSeconds(constructionDelay);
                    tileFrom = tileTo;
                    tileTo = CreateTile();
                    ConnectTiles();
                    CollisionCheck();

                    if (attempts >= maxAttempts) {
                        break;
                    }
                }
            } else {
                break;
            }
        }

        dungeonState = DungeonState.Cleanup;

        CleanupBoxes();
        BlockedPassages();

        /* dungeonState = DungeonState.BakingNavmesh;
        yield return new WaitForSeconds(0.5f);
        BakeNavMeshAtRuntime(); */

        // For some reason we need to disable and enable the GameObject
        // If you put 1.5s on Construction Delay you can see the bug occurring
        StartCoroutine(NavMeshBugFix());

        dungeonState = DungeonState.Completed;
    }

    private IEnumerator NavMeshBugFix() {
        foreach (Transform child in transform) {
            Debug.Log("Desbugando: " + child.name);
            child.gameObject.SetActive(false);
            child.gameObject.SetActive(true);
        }

        yield return null;
    }

    private IEnumerator WaitForBuild() {
        while (dungeonState != DungeonState.Completed) {
            // Debug.Log("Dungeon state: " + dungeonState);
            yield return null;
        }

        Debug.Log("Completed");

        /* yield return new WaitUntil(() =>
            dungeonState == DungeonState.Completed
        ); */

        // Do something
    }

    private void BakeNavMeshAtRuntime() {
        foreach (var obj in generatedTiles) {
            var components = obj.tile.GetComponentsInChildren<NavMeshSurface>();//.BuildNavMesh();
            Debug.Log(components.Length);

            foreach (var comp in components) {
                comp.BuildNavMesh();
            }
        }
    }

    private void BlockedPassages() {
        foreach (Connector connector in transform.GetComponentsInChildren<Connector>()) {
            if (!connector.isConnected) {
                Vector3 pos = connector.transform.position;
                int wallIndex = Random.Range(0, blockedPrefabs.Length);
                GameObject goWall = Instantiate(blockedPrefabs[wallIndex], pos, connector.transform.rotation, connector.transform);
                goWall.name = blockedPrefabs[wallIndex].name;
            }
        }
    }

    private void CollisionCheck() {
        BoxCollider box = tileTo.GetComponent<BoxCollider>();

        if (box == null) {
            box = tileTo.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;

            Vector3 size = tileTo.gameObject.GetComponent<Size>().size;
            box.size = size;
        }

        Vector3 offset = (tileTo.right * box.center.x) + (tileTo.up * box.center.y) + (tileTo.forward * box.center.z);
        Vector3 halfExtents = box.bounds.extents;
        List<Collider> hits = Physics.OverlapBox(tileTo.position + offset, halfExtents, Quaternion.identity, LayerMask.GetMask("Tile")).ToList();

        if (hits.Count > 0) {
            if (hits.Exists(x => x.transform != tileFrom && x.transform != tileTo)) {
                attempts++;
                int toIndex = generatedTiles.FindIndex(x => x.tile == tileTo);

                if (generatedTiles[toIndex].connector != null) {
                    generatedTiles[toIndex].connector.isConnected = false;
                }

                generatedTiles.RemoveAt(toIndex);
                DestroyImmediate(tileTo.gameObject);
                // Backtracking
                if (attempts >= maxAttempts) {
                    int fromIndex = generatedTiles.FindIndex(x => x.tile == tileFrom);
                    Tile myTileFrom = generatedTiles[fromIndex];

                    if (tileFrom != tileRoot) {
                        if (myTileFrom.connector != null) {
                            myTileFrom.connector.isConnected = false;
                        }

                        availableConnectors.RemoveAll(x => x.transform.parent.parent == tileFrom);
                        generatedTiles.RemoveAt(fromIndex);
                        DestroyImmediate(tileFrom.gameObject);

                        if (myTileFrom.origin != tileRoot) {
                            tileFrom = myTileFrom.origin;
                        } else if (container.name.Contains("Main")) {
                            if (myTileFrom.origin != null) {
                                tileRoot = myTileFrom.origin;
                                tileFrom = tileRoot;
                            }
                        } else if (availableConnectors.Count > 0) {
                            int availIndex = Random.Range(0, availableConnectors.Count);
                            tileRoot = availableConnectors[availIndex].transform.parent.parent;

                            availableConnectors.RemoveAt(availIndex);

                            tileFrom = tileRoot;
                        } else {
                            return;
                        }
                    } else if (container.name.Contains("Main")) {
                        if (myTileFrom.origin != null) {
                            tileRoot = myTileFrom.origin;
                            tileFrom = tileRoot;
                        }
                    } else if (availableConnectors.Count > 0) {
                        int availIndex = Random.Range(0, availableConnectors.Count);
                        tileRoot = availableConnectors[availIndex].transform.parent.parent;

                        availableConnectors.RemoveAt(availIndex);

                        tileFrom = tileRoot;
                    } else {
                        return;
                    }
                }
                // Retry
                if (tileFrom != null) {
                    //tileTo = CreateTile();
                    tileTo = generatedTiles.Count == mainLength - 1 ? CreateExitTile() : CreateTile();

                    ConnectTiles();
                    CollisionCheck();
                }
            } else {
                attempts = 0;
            }
        }
    }

    private void CleanupBoxes() {
        if (!useBoxColliders) {
            foreach (Tile myTile in generatedTiles) {
                BoxCollider box = myTile.tile.GetComponent<BoxCollider>();
                if (box != null) {
                    Destroy(box);
                }
            }
        }
    }

    private void ConnectTiles() {
        Transform connectFrom = GetRandomConnector(tileFrom);

        if (connectFrom == null) {
            return;
        }

        Transform connectTo = GetRandomConnector(tileTo);

        if (connectTo == null) {
            return;
        }

        connectTo.SetParent(connectFrom);
        tileTo.SetParent(connectTo);
        connectTo.localPosition = Vector3.zero;
        connectTo.localRotation = Quaternion.identity;
        connectTo.Rotate(0, 180f, 0);
        tileTo.SetParent(container);
        connectTo.SetParent(tileTo.Find("Connectors"));

        generatedTiles.Last().connector = connectFrom.GetComponent<Connector>();
    }

    private Transform GetRandomConnector(Transform tile) {
        if (tile == null) {
            return null;
        }

        List<Connector> connectorList = tile.GetComponentsInChildren<Connector>().ToList().FindAll(x => x.isConnected == false);

        if (connectorList.Count > 0) {
            int connectorIndex = Random.Range(0, connectorList.Count);
            connectorList[connectorIndex].isConnected = true;
            //box collider
            if (tile == tileFrom) {
                BoxCollider box = tile.GetComponent<BoxCollider>();
                if (box == null) {
                    box = tile.gameObject.AddComponent<BoxCollider>();
                    box.isTrigger = true;

                    Vector3 size = tileTo.gameObject.GetComponent<Size>().size;
                    box.size = size;
                }
            }

            return connectorList[connectorIndex].transform;
        }

        return null;
    }

    private Transform CreateTile() {
        int index = Random.Range(0, tilePrefabs.Length);
        GameObject goTile = Instantiate(tilePrefabs[index], Vector3.zero, Quaternion.identity, container);
        goTile.name = tilePrefabs[index].name;

        Transform origin = generatedTiles[generatedTiles.FindIndex(x => x.tile == tileFrom)].tile;

        generatedTiles.Add(new Tile(goTile.transform, origin));

        return goTile.transform;
    }

    private Transform CreateExitTile() {
        int index = Random.Range(0, exitPrefabs.Length);
        GameObject goTile = Instantiate(exitPrefabs[index], Vector3.zero, Quaternion.identity, container);
        goTile.name = "Exit Room";

        Transform origin = generatedTiles[generatedTiles.FindIndex(x => x.tile == tileFrom)].tile;

        generatedTiles.Add(new Tile(goTile.transform, origin));

        return goTile.transform;
    }

    private Transform CreateStartTile() {
        int index = Random.Range(0, startPrefabs.Length);
        GameObject goTile = Instantiate(startPrefabs[index], Vector3.zero, Quaternion.identity, container);
        goTile.name = "Start Room";
        float yRot = Random.Range(0, 4) * 90f;
        goTile.transform.Rotate(0, yRot, 0);

        generatedTiles.Add(new Tile(goTile.transform, null));

        return goTile.transform;
    }
}
