﻿using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using LibNoise;
using Debug = UnityEngine.Debug;

public class TerrainController : MonoBehaviour, IPageController
{
    public static string WorldThreadName = "GenerationThread";
    public static string setBlockThreadName = "SetBlockThread";
    public static string GrassThreadName = "GrassThread";
    public static TerrainController Instance;
    public GameObject grassPrefab;
    public GameObject pathPointPrefab;
    public Texture2D textureAtlas;
    public Rect[] AtlasUvs;
    public Material chunkMaterial;
    public int seed = 0;
    public int threads = 2;
    public Vector3Int newPlayerChunkPos;
    public Vector3Int newPlayerVoxelPos;
    public Vector3 LODtarget = new Vector3(0, 100, 0);
    public int chunksInQueue;
    public int chunksGenerated;
    public int VoxelSize;
    public int MeshSize;
    public int maxGrassDistance = 30;
    public bool chunkNoExistError =false;
    public bool cannotFindBlock = false;
    public delegate void RenderComplete();
    public event Action OnRenderComplete;


    public static Dictionary<byte, BlockType> blockTypes = new Dictionary<byte, BlockType>();

    // Temparary simple 3D array. Will use dictionary when I switch over to unlimited terrain gen.
    public SafeDictionary<Vector3Int, Chunk> Chunks = new SafeDictionary<Vector3Int, Chunk>();

    public SafeDictionary<Vector2Int, GameObject>  grassObj = new SafeDictionary<Vector2Int, GameObject>();

    public GameObject player;
    public GameObject chunkPrefab;
    public Texture2D[] AllCubeTextures;
    public BlockType[] BlocksArray;
    private Vector3Int _generateArroundChunk = new Vector3Int(0, 0, 0);
    private Vector3Int _oldPlayerVoxelPos = new Vector3Int(500, 500, 500);
    private Vector3Int _oldPlayerChunkPos = new Vector3Int(500, 500, 500);
    private bool[] threadFinished;
    private Stopwatch watch = new Stopwatch();
    public int progress = 0;

    List<Vector3Int> _tmpChunkList = new List<Vector3Int>();
    List<GameObject> pathObjects = new List<GameObject>();


    private bool _generating = false;
    private bool _spawningGrass = true;
    private bool _playerCreated = false;
    private System.Threading.ManualResetEvent _resetEvent;
    private Vector3 _playerPosition;
    private bool _running = true;
    private bool renderCompleteCalled = false;

    void Awake()
    {
        Instance = this;
    }

	// Use this for initialization
	void Start () {
        
    }
	
	// Update is called once per frame
	void Update () {
        if (player != null)
        {
            _playerCreated = true;
            _playerPosition = player.transform.position;
            LODtarget = _playerPosition;
            newPlayerVoxelPos = VoxelConversions.WorldToVoxel(_playerPosition);
            newPlayerChunkPos = VoxelConversions.VoxelToChunk(newPlayerVoxelPos);
            //Debug.Log(generateArroundChunk + ", " + newPlayerChunkPos + ", " + Vector3.Distance(generateArroundChunk, newPlayerChunkPos));
            if (_oldPlayerChunkPos != newPlayerChunkPos && !_generating) {
                // generate around point.
                //Debug.Log("Debug filling " + newPlayerChunkPos + ".");
                _generateArroundChunk = _oldPlayerChunkPos = newPlayerChunkPos;
                GenerateSpherical(newPlayerChunkPos);
            }
            foreach(Chunk chk in Chunks.Values.ToArray()) 
                chk.ChunkUpdate();
        }
    
       
	}

    void OnApplicationQuit() {
        
    }

    /*void ChunkUpdate() {
        foreach (Chunk chk in Chunks.Values.ToArray()) {
            if (_playerCreated) {
                _resetEvent.WaitOne(5);
                LODtarget = _playerPosition;
                newPlayerVoxelPos = VoxelConversions.WorldToVoxel(_playerPosition);
                newPlayerChunkPos = VoxelConversions.VoxelToChunk(newPlayerVoxelPos);
                //Debug.Log(generateArroundChunk + ", " + newPlayerChunkPos + ", " + Vector3.Distance(generateArroundChunk, newPlayerChunkPos));
                if (_oldPlayerChunkPos != newPlayerChunkPos && !_generating) {
                    // generate around point.
                    //Debug.Log("Debug filling " + newPlayerChunkPos + ".");
                    _generateArroundChunk = _oldPlayerChunkPos = newPlayerChunkPos;
                    GenerateSpherical(newPlayerChunkPos);


                }
                chk.ChunkUpdate();
            }
        }
    }*/

    public void SpawnGrass()
    {
        int xMin = newPlayerVoxelPos.x - maxGrassDistance;
        int xMax = newPlayerVoxelPos.x + maxGrassDistance;

        int yMin = newPlayerVoxelPos.y - maxGrassDistance;
        int yMax = newPlayerVoxelPos.y + maxGrassDistance;

        int zMin = newPlayerVoxelPos.z - maxGrassDistance;
        int zMax = newPlayerVoxelPos.z + maxGrassDistance;

        Loom.QueueAsyncTask(GrassThreadName,() =>
        {
            List<Vector3Int> grass = new List<Vector3Int>();
            for (int x = xMin; x < xMax; x++)
            {
                for (int z = zMin; z < zMax; z++)
                {
                    Vector3Int pos = new Vector3Int(x, 0, z);
                    Vector3Int chunk = VoxelConversions.VoxelToChunk(pos);
                    if (Chunks[chunk].surface2D.Contains(new Vector2Int(pos.x, pos.z)) && !grassObj.ContainsKey(new Vector2Int(x, z)) && IsInSphere(newPlayerVoxelPos, maxGrassDistance, new Vector3Int(x, 0, z)))
                    {
                        grass.Add(pos);
                    }
                }
            }

            Loom.QueueOnMainThread(() =>
            {
                for (int i = 0; i < grass.Count; i++)
                {
                    if (!grassObj.ContainsKey(new Vector2Int(grass[i].x, grass[i].z)))
                    {
                        Vector3 globalPos = VoxelConversions.VoxelToWorld(grass[i]);
                        grassObj.Add(new Vector2Int(grass[i].x, grass[i].z), (GameObject)Instantiate(grassPrefab, new Vector3(grass[i].x, 0, grass[i].z), Quaternion.identity));
                    }
                }
            });
        });
    }

    public void DeleteGrass()
    {
        Loom.QueueAsyncTask(GrassThreadName, () =>
        {
            List<Vector2Int> grass = new List<Vector2Int>();
            foreach (Vector2Int location in new List<Vector2Int>(grassObj.Keys))
            {
                if (Vector2.Distance(location, new Vector2(newPlayerVoxelPos.x, newPlayerVoxelPos.z)) > maxGrassDistance)
                {
                    grass.Add(location);
                }
            }
            Loom.QueueOnMainThread(() =>
            {
                for (int i = 0; i < grass.Count; i++)
                {
                    Destroy(grassObj[grass[i]]);
                    grassObj.Remove(grass[i]);
                }
            });
        });
    }

    public static void Init()
    {
        Instance.init();
    }

    public void init()
    {
        Loom.AddAsyncThread(WorldThreadName);
        Loom.AddAsyncThread(setBlockThreadName);
        Loom.AddAsyncThread(GrassThreadName);
        textureAtlas = new Texture2D(0, 0);
        //AtlasUvs = textureAtlas.PackTextures(AllCubeTextures, 1);
        AddBlockType(BaseType.air, "Air", new int[] { -1, -1, -1, -1, -1, -1 }, null);
        AddBlockType(BaseType.solid, "Grass", new int[] { 0, 0, 0, 0, 0, 0 }, null);
        AddBlockType(BaseType.solid, "Rock", new int[] { 1, 1, 1, 1, 1, 1 }, null);
        AddBlockType(BaseType.solid, "Dirt", new int[] { 2, 2, 2, 2, 2, 2 }, null);
        AddBlockType(BaseType.solid, "Brick", new int[] { 3, 3, 3, 3, 3, 3 }, null);
        if (VoxelSettings.randomSeed)
            VoxelSettings.seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        else
            VoxelSettings.seed = seed;
        //TestSmooth();
        //SpawnChunksLinear();
        //GameManager.Status = "Loading...";
        //Action action = new Action(OnRenderComplete);
        //GenerateSpherical(_generateArroundChunk);
        SpawnChunksLinear();
        //TestSmooth();
    }

    public void TestSmooth()
    {
        TerrainModule module = new TerrainModule(VoxelSettings.seed);
        Vector3Int chunkPos = new Vector3Int(-1, 2, -1);
        threadFinished = new bool[1];
        SpawnChunk(new Vector3Int(0, 0, 0), module);
        SpawnChunk(new Vector3Int(1, 0, 0), module, 1/3f);
        //GenerateChunks("testGen", 0, Chunks.Values.ToArray());
    }

    public void GenerateSpherical(Vector3Int center) {
        _resetEvent = new System.Threading.ManualResetEvent(false);
        LODtarget = center;
        //Loom.QueueAsyncTask("chunkUpdate", ChunkUpdate);
        Loom.QueueAsyncTask(WorldThreadName, () => {
            TerrainModule module = new TerrainModule(VoxelSettings.seed);
            for (int i = 0; i < VoxelSettings.radius; i++) {
                Vector3Int[][] chunkBand = GetChunkLocationsAroundPoint(threads, i, center);
                for (int threadIndex = 0; threadIndex < chunkBand.Length; threadIndex++) {
                    Vector3Int[] positions = chunkBand[threadIndex];
                    GenerateChunks("Generator" + threadIndex, threadIndex, module, positions);
                }
            }
            
        });
    }

    public Vector3Int[][] GetChunkLocationsAroundPoint(int threads, int radius, Vector3Int center)
    {
        Vector3Int[][] result = new Vector3Int[0][];
        try {
            List<List<Vector3Int>> chunksInSphere = new List<List<Vector3Int>>();
            if (radius > 0) {
                List<Vector3Int> counted = new List<Vector3Int>();
                for (int i = 0; i < threads; i++)
                    chunksInSphere.Add(new List<Vector3Int>());
                int threadIndex = 0;
                int circumference = (int)((2 * Mathf.PI * radius) + 1) / 2;
                for (int i = 0; i <= circumference; i++) {
                    float angle = Scale(i, 0, circumference, 0, 360);
                    int x = (int)(center.x + radius * Mathf.Cos(angle * (Mathf.PI / 180)));
                    int z = (int)(center.z + radius * Mathf.Sin(angle * (Mathf.PI / 180)));

                    for (int y = -VoxelSettings.maxChunksY/2; y < VoxelSettings.maxChunksY/2; y++) {
                        Vector3Int[] positions = new Vector3Int[] {
                        new Vector3Int(x, y, z),
                        new Vector3Int(x + 1, y, z),
                        new Vector3Int(x - 1, y, z),
                        new Vector3Int(x, y, z + 1),
                        new Vector3Int(x, y, z - 1),
                        new Vector3Int(x - 1, y, z - 1),
                        new Vector3Int(x + 1, y, z - 1),
                        new Vector3Int(x + 1, y, z + 1),
                        new Vector3Int(x - 1, y, z + 1),
                    };
                        if (threadIndex >= threads)
                            threadIndex = 0;
                        for (int posIndex = 0; posIndex < positions.Length; posIndex++) {
                            if (!BuilderExists(positions[posIndex].x, positions[posIndex].y, positions[posIndex].z) && !counted.Contains(positions[posIndex])) {
                                chunksInSphere[threadIndex].Add(positions[posIndex]);
                                counted.Add(positions[posIndex]);
                            }
                        }
                        threadIndex++;
                        progress++;
                    }
                }
            }
            else {
                chunksInSphere.Add(new List<Vector3Int>());
                chunksInSphere[0].Add(center);
            }

            /*for (int radius = startRadius; radius < endRadius; radius++) {
                for (int x = center.x - radius; x < center.x + radius; x++) {
                    for (int z = center.z - radius; z < center.z + radius; z++) {
                        for (int y = 0; y <= VoxelSettings.maxChunksY; y++) {
                            if (threadIndex >= threads)
                                threadIndex = 0;

                            /*if (!BuilderExists(x, y, z) && IsInSphere(center, VoxelSettings.radius, new Vector3Int(x, center.y, z)) && !counted.Contains(new Vector3Int(x, y, z))) {
                                chunksInSphere[threadIndex].Add(new Vector3Int(x, y, z));
                                counted.Add(new Vector3Int(x, y, z));
                            }
                            if (!BuilderExists(x, y, z) && !counted.Contains(new Vector3Int(x, y, z))) {
                                chunksInSphere[threadIndex].Add(new Vector3Int(x, y, z));
                                counted.Add(new Vector3Int(x, y, z));
                            }
                            threadIndex++;
                        }
                    }
                }
            }*/
            result = new Vector3Int[chunksInSphere.Count][];
            for (int i = 0; i < result.Length; i++) {
                result[i] = chunksInSphere[i].ToArray();
            }
        }
        catch(Exception e) {
            SafeDebug.LogError(string.Format("{0}", e.Message));
        }
        return result;
    }

    public void SpawnDebugChunks()
    {
        SpawnChunkFeild();

        Loom.QueueAsyncTask(WorldThreadName, () => 
        {
            /*Chunks[1, 0, 0].DebugFill(1);
            SafeDebug.Log("Filling 1, 0.");
            Chunks[1, 0, 0].Render(true);
            SafeDebug.Log("Rendering 1, 0.");

            Chunks[1, 1, 0].DebugFill(2);
            Chunks[1, 1, 0].Render(true);
            SafeDebug.Log("Rendering 1, 1.");

            Chunks[2, 0, 0].DebugFill(3);
            Chunks[2, 0, 0].Render(true);
            SafeDebug.Log("Rendering 2, 0.");

            Chunks[2, 1, 0].DebugFill(4);
            Chunks[2, 1, 0].Render(true);
            SafeDebug.Log("Rendering 2, 1.");

            SafeDebug.Log("Finished Debug rendering.");*/
        });

    }

    public void SpawnChunksLinear()
    {
        TerrainModule module = new TerrainModule(0);
        Loom.QueueAsyncTask(WorldThreadName, () =>
        {

            for (int x = 0; x < VoxelSettings.maxChunksX; x++)
            {
                for (int z = 0; z < VoxelSettings.maxChunksZ; z++)
                {
                    for (int y = 0; y <= VoxelSettings.maxChunksY; y++)
                    {
                        Vector3Int location3D = new Vector3Int(x, y, z);
                        Chunk.CreateChunk(location3D, "gen", 0, module, this);
                    }
                }
            }
            SafeDebug.Log("Finished rendering.");
            Loom.QueueOnMainThread(() =>
            {
                if (OnRenderComplete != null)
                    OnRenderComplete();
                _generating = false;
            });
        });
    }

    public void UpdateLOD() {
        foreach (Chunk chk in Chunks.Values.ToArray()) {
            chk.Generate();
        }
    }

    public void GenerateChunks(string threadName, int index, IModule module, Vector3Int[] chunks)
    {
        Loom.QueueAsyncTask(threadName, () =>
        {
            
            try {
                for (int i = 0; i < chunks.Length; i++) {
                    _generating = true;
                    Chunk.CreateChunk(chunks[i], threadName, index, module, this);
                    chunksGenerated++;
                }
                if (!renderCompleteCalled) {
                    for (int i = 0; i < chunks.Length; i++) {
                        if (Vector3.Distance(new Vector3Int(), chunks[i]) > VoxelSettings.radius / 2) {
                            renderCompleteCalled = true;
                            Loom.QueueOnMainThread(OnRenderComplete);
                        }
                    }
                }
                _generating = false;
            }
            catch(Exception e) {
                SafeDebug.LogError(string.Format("{0}: {1}\n {2}", e.GetType().ToString(), e.Message, e.StackTrace));
            }
        });
    }

    public void GenerateChunk(Chunk chunk)
    {
        try
        {
            if (chunk != null && !chunk.Generated)
            {
                _generating = true;
                chunk.Generate();
            }
        }
        catch (Exception e)
        {
            SafeDebug.LogError(e.Message + "\nFunction: GenerateChunks: " + chunk.chunkPosition.x + "," + chunk.chunkPosition.y + "," + chunk.chunkPosition.z);
            SafeDebug.LogError(e.StackTrace);
        }
    }

    public void AddBlockType(BaseType _baseType, string _name, int[] _textures, GameObject _prefab)
    {
        byte index = (byte)blockTypes.Count;
        blockTypes.Add(index, new BlockType(_baseType, index, _name, _textures, _prefab));
        BlocksArray = GetBlockTypeArray(blockTypes.Values);
    }

    public bool BuilderExists(int x, int y, int z)
    {
        try {
            if (Chunks.ContainsKey(new Vector3Int(x, y, z))) {
                return (Chunks[new Vector3Int(x, y, z)] != null);
            }
        }
        catch (KeyNotFoundException e) {
            return false;
        }
        return false;
    }

    public bool BuilderGenerated(int x, int y, int z)
    {
        try {
            if (BuilderExists(x, y, z)) {
                return Chunks[new Vector3Int(x, y, z)].Generated;
            }
        }
        catch (KeyNotFoundException e) {
            return false;
        }
        return false;
    }

    public IVoxelBuilder GetBuilder(int x, int y, int z)
    {
        IVoxelBuilder result = null;
        try {
            Vector3Int location = new Vector3Int(x, y, z);
            if (BuilderExists(x, y, z)) {
                result = Chunks[new Vector3Int(x, y, z)].builder;
            }
        }
        catch(KeyNotFoundException e) {
            return result;
        }
        return result;
    }

    public static BlockType[] GetBlockTypeArray(Dictionary<byte, BlockType>.ValueCollection collection)
    {
        BlockType[] types = new BlockType[collection.Count];
        int i = 0;
        foreach (BlockType _type in collection)
        {
            types[i++] = _type;
        }
        return types;
    }

    public Block GetBlock(Vector3Int location) {
        return GetBlock(location.x, location.y, location.z);
    }

    public Block GetBlock(int x, int y, int z)
    {
        Vector3Int chunk = VoxelConversions.VoxelToChunk(new Vector3Int(x, y, z));
        Vector3Int localVoxel = VoxelConversions.GlobalVoxToLocalChunkVoxCoord(chunk, new Vector3Int(x, y, z));
        Block result = default(Block);
        if (BuilderExists(chunk.x, chunk.y, chunk.z)) {
            try {
                result = Chunks[new Vector3Int(chunk.x, chunk.y, chunk.z)].GetBlock(localVoxel.x, localVoxel.y, localVoxel.z);
            }catch(Exception e) {
                Debug.LogErrorFormat("{0}, globalZ: {1}", e.Message, z);
                throw;
            }
        }
        else
            chunkNoExistError = true;
        return result;
    }

    /*public void SetSurfacePoints(Vector3Int[] points)
    {
        for (int i = 0; i < points.Length; i++)
            if (!surfacePoints.Contains(points[i]))
                surfacePoints.Add(points[i]);
    }*/

    public void SetBlockAtLocation(Vector3 position, byte type)
    {
        Vector3Int voxelPos = VoxelConversions.WorldToVoxel(position);
        Vector3Int chunk = VoxelConversions.VoxelToChunk(voxelPos);
        Vector3Int localVoxel = VoxelConversions.GlobalVoxToLocalChunkVoxCoord(chunk, voxelPos);
        if (BuilderExists(chunk.x, chunk.y, chunk.z))
        {
            Chunks[new Vector3Int(chunk.x, chunk.y, chunk.z)].EditNextFrame(new Chunk.BlockChange[] { new Chunk.BlockChange(position, type) });
        }
    }

    public void UpdateChunk(int x, int y, int z)
    {
        if (BuilderExists(x, y, z))
        {
            Chunks[new Vector3Int(x, y, z)].Render(true);
        }
    }

    public void GenerateExplosion(Vector3 postion, int radius)
    {
        System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        Loom.AddAsyncThread("Explosion");
        Loom.QueueAsyncTask("Explosion", () =>
        {        
            Dictionary<Vector3Int, List<Chunk.BlockChange>> changes = new Dictionary<Vector3Int, List<Chunk.BlockChange>>();
            Vector3Int voxelPos = VoxelConversions.WorldToVoxel(postion);
            for (int x = voxelPos.x - radius; x <= voxelPos.x + radius; x++)
                for (int y = voxelPos.y - radius; y <= voxelPos.y + radius; y++)
                    for (int z = voxelPos.z - radius; z <= voxelPos.z + radius; z++)
                    {
                        Vector3Int voxel = new Vector3Int(x, y, z);
                        Vector3Int chunk = VoxelConversions.VoxelToChunk(voxel);
                        if (IsInSphere(voxelPos, radius, voxel))
                        {
                            if (!changes.ContainsKey(chunk))
                                changes.Add(chunk, new List<Chunk.BlockChange>());
                            changes[chunk].Add(new Chunk.BlockChange(VoxelConversions.GlobalVoxToLocalChunkVoxCoord(chunk, voxel), 0));
                            //ChangeBlock(new Chunk.BlockChange(voxel, 0));
                        }
                    }
            //Debug.Log("Iterated through exploded blocks: " + watch.Elapsed.ToString());
            Loom.QueueOnMainThread(() =>
            {
                foreach (Vector3Int chunkPos in changes.Keys)
                {
                    ChangeBlock(chunkPos, changes[chunkPos].ToArray());
                }
                watch.Stop();
                //Debug.Log("Blocks changes sent to chunk: " + watch.Elapsed.ToString());
            });
        });
    }

    public void ChangeBlock(Vector3 globalPosition, byte type, Vector3 normals, bool invertNormal)
    {
        Vector3 modifiedNormal = new Vector3();
        if (invertNormal)
            modifiedNormal = -(normals * VoxelSettings.half);
        else
            modifiedNormal = (normals * VoxelSettings.half);
        ChangeBlock(globalPosition + modifiedNormal, type);
    }

    public void ChangeBlock(Vector3 globalPosition, byte type)
    {
        Vector3Int voxelPos = VoxelConversions.WorldToVoxel(globalPosition);
        //Debug.LogFormat("globalPostion: {0}", globalPosition);
        ChangeBlock(voxelPos, type);
    }

    public void ChangeBlock(Vector3Int voxel, byte type)
    {
        ChangeBlock(new Chunk.BlockChange(voxel, type));
    }

    public void ChangeBlock(Chunk.BlockChange change)
    {
        Vector3Int chunk = VoxelConversions.VoxelToChunk(change.position);
        Vector3Int localVoxel = VoxelConversions.GlobalVoxToLocalChunkVoxCoord(chunk, change.position);
        //Debug.LogFormat("voxel: {0}, localVoxel: {1}, chunk: {2}", voxel, localVoxel, chunk);
        if (BuilderExists(chunk.x, chunk.y, chunk.z))
        {
            if (localVoxel.x >= 0 && localVoxel.x < VoxelSettings.ChunkSizeX && localVoxel.y >= 0 && localVoxel.y < VoxelSettings.ChunkSizeY && localVoxel.z >= 0 && localVoxel.z < VoxelSettings.ChunkSizeZ)
            {
                Chunks[chunk].EditNextFrame(new Chunk.BlockChange(localVoxel, change.type));
            }
            else
            {
                SafeDebug.LogError(string.Format("Out of Bounds: chunk: {0}, globalVoxel:{1}, localVoxel: {2}, Function: GenerateExplosion",
                    chunk, change.position, localVoxel));
            }
        }
    }

    public void ChangeBlock(Vector3Int chunk, Chunk.BlockChange change)
    {
        Vector3Int localVoxel = change.position;
        if (BuilderExists(chunk.x, chunk.y, chunk.z))
        {
            if (localVoxel.x >= 0 && localVoxel.x < VoxelSettings.ChunkSizeX && localVoxel.y >= 0 && localVoxel.y < VoxelSettings.ChunkSizeY && localVoxel.z >= 0 && localVoxel.z < VoxelSettings.ChunkSizeZ)
            {
                Chunks[chunk].EditNextFrame(change);
            }
            else
            {
                SafeDebug.LogError(string.Format("Out of Bounds: chunk: {0}, localVoxel: {1}, Function: GenerateExplosion",
                    chunk, localVoxel));
            }
        }
    }

    public void ChangeBlock(Vector3Int chunk, Chunk.BlockChange[] changes)
    {
        if (BuilderExists(chunk.x, chunk.y, chunk.z))
        {
            Chunks[new Vector3Int(chunk.x, chunk.y, chunk.z)].EditNextFrame(changes);
        }
    }

    public void ChangeBlock(Vector3Int[] voxels, byte type)
    {
        foreach(Vector3Int block in voxels)
        {
            Vector3Int chunk = VoxelConversions.VoxelToChunk(block);
            Vector3Int localVoxel = VoxelConversions.GlobalVoxToLocalChunkVoxCoord(chunk, block);
            //Debug.LogFormat("voxel: {0}, localVoxel: {1}, chunk: {2}", voxel, localVoxel, chunk);
            if (BuilderExists(chunk.x, chunk.y, chunk.z))
            {
                if (localVoxel.x >= 0 && localVoxel.x < VoxelSettings.ChunkSizeX && localVoxel.y >= 0 && localVoxel.y < VoxelSettings.ChunkSizeY && localVoxel.z >= 0 && localVoxel.z < VoxelSettings.ChunkSizeZ)
                {
                    Chunks[chunk].EditNextFrame(new Chunk.BlockChange(localVoxel, type));
                }
                else
                {
                    SafeDebug.LogError(string.Format("Out of Bounds: chunk: {0}, globalVoxel:{1}, localVoxel: {2}, Function: GenerateExplosion",
                        chunk, block, localVoxel));
                }
            }
        }
    }

    public void SetVoxelSize()
    {
        VoxelSize = ChunkSize() * Chunks.Count;
    }

    public void setMeshSize()
    {
        int totalSize = 0;
        foreach (Vector3Int chunk in Chunks.Keys.ToArray())
            if (Chunks.ContainsKey(chunk))
                totalSize += Chunks[chunk].size;
        MeshSize = totalSize;
    }

    public void ClearChunks()
    {
        foreach (Vector3Int chunkPos in Chunks.Keys)
            DestroyChunk(chunkPos);
    }

    public void DestroyChunk(Vector3Int chunk)
    {
        try {
            if (BuilderExists(chunk.x, chunk.y, chunk.z)) {
                Chunk chunkInst = Chunks[chunk];
                chunkInst.Close();
                Chunks.Remove(chunk);
                Loom.QueueOnMainThread(() => Destroy(chunkInst.gameObject));
            }
        }
        catch (Exception e) {
            SafeDebug.LogError(string.Format("{0}\n", e.Message), e);
        }
    }

    private void SpawnChunkFeild()
    {
        for (int x = 0; x < VoxelSettings.maxChunksX; x++)
        {
            for (int z = 0; z < VoxelSettings.maxChunksZ; z++)
            {
                for (int y = 0; y <= VoxelSettings.maxChunksY; y++)
                {
                    SpawnChunk(new Vector3Int(x, y, z), new Perlin());
                }
            }
        }
    }

    private void SpawnChunks(Vector3Int[] locations, IModule module, double voxelsPerMeter = VoxelSettings.voxelsPerMeter) {
        foreach (Vector3Int chunkPos in locations)
            SpawnChunk(chunkPos, module, voxelsPerMeter);
    }

    private void SpawnChunk(Vector3Int location, IModule module, double voxelsPerMeter = VoxelSettings.voxelsPerMeter)
    {
        if (!Chunks.ContainsKey(location))
        {
            Chunk chunk = Instantiate(chunkPrefab).GetComponent<Chunk>();
            chunk.grassPrefab = grassPrefab;
            chunk.name = string.Format("Chunk_{0}.{1}.{2}", location.x, location.y, location.z);
            chunk.Init(location, module, this, voxelsPerMeter);
            Chunks.Add(location, chunk);
        }
    }

    public void AddChunk(Vector3Int pos, Chunk chunk) {
        if (BuilderExists(pos.x, pos.y, pos.z)) {
            Chunks.Add(pos, chunk);
        }
    }

    private bool ChunkIsInBounds(int x, int y, int z)
    {
        //return ((x <= Chunks.GetLength(0) - 1) && x >= 0) && ((y <= Chunks.GetLength(1) - 1) && y >= 0) && ((z <= Chunks.GetLength(2) - 1) && z >= 0);
        return true;
    }

    private bool IsInSphere(Vector3Int center, int radius, Vector3Int testPosition)
    {
        float distance = Mathf.Pow(center.x - testPosition.x, 2) + Mathf.Pow(center.y - testPosition.y, 2) + Mathf.Pow(center.z - testPosition.z, 2);
        return distance <= Mathf.Pow(radius, 2);
    }

    private int ChunkSize()
    {
        int blockStructSize = 5; //Marshal.SizeOf(typeof(Block));
        int blockArraySize = VoxelSettings.ChunkSizeX * VoxelSettings.ChunkSizeY * VoxelSettings.ChunkSizeZ * blockStructSize;
        int heightMapArraySize = VoxelSettings.ChunkSizeX * VoxelSettings.ChunkSizeZ * sizeof(float);
        return blockArraySize + heightMapArraySize;
    }

    private float Scale(float value, float oldMin, float oldMax, float newMin, float newMax) {
        return newMin + (value - oldMin) * (newMax - newMin) / (oldMax - oldMin);
    }
}
