﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class GridManager : MonoBehaviour {
  public Camera mainCamera;
  public GridLayout grid;
  public Tilemap tilemap;
  public Tile tileEmpty, tileSelected, tileAlive;
  private Tile tileTemp, tilePrev;
  // public TmapTile smartTile;   // not used in this version.  Keep for possible future use
  private Tilemap savedTilemap;
  private int gridWidth = 75;
  private int gridHeight = 75;
  private float targetFOV = 65;
  private Vector3 mousePosition, mouseWorldPos;

  private void Awake() {
    if(!mainCamera)
      mainCamera = FindObjectOfType<Camera>();
    if(!grid)
      grid = FindObjectOfType<GridLayout>();
    if(!tilemap)
      tilemap = FindObjectOfType<Tilemap>();
  }

  private void Start() {
    CreateGridLayout();
    Randomize();
    SaveLayout();
    SetCameraFOV();
  }

  private void Update() {
    // Smooth transition camera to target zoom
    if ((mainCamera.fieldOfView <= targetFOV*.995) || (mainCamera.fieldOfView >= targetFOV*1.005)) {
      mainCamera.fieldOfView = Mathf.Lerp(mainCamera.fieldOfView, targetFOV, Time.deltaTime*3.5f);
    }

    // Modify tiles with mouse
    if (!GameManager.Instance.simulationRunning) {
      mousePosition = Input.mousePosition;
      mousePosition.z = 10; // need to set the z value or else converting to world point will always be at 0, 0
      mouseWorldPos = mainCamera.ScreenToWorldPoint(mousePosition);
        tilePrev = tilemap.GetTile<Tile>(tilemap.WorldToCell(mouseWorldPos));

      // TODO: temporarily show "selected" tile when mouse hovers over a tile
      // if (tilePrev) {
      //   tilemap.SetTile(tilemap.WorldToCell(mouseWorldPos), tileSelected);
      // }

      if (Input.GetMouseButtonDown(0)) {
        tileTemp = tilemap.GetTile<Tile>(tilemap.WorldToCell(mouseWorldPos));
        if (tileTemp && tileTemp.name == tileEmpty.name) {
          tilemap.SetTile(tilemap.WorldToCell(mouseWorldPos), tileAlive);
        }
        else if (tileTemp && tileTemp.name == tileAlive.name) {
          tilemap.SetTile(tilemap.WorldToCell(mouseWorldPos), tileEmpty);
        }
      }
    }
  }

  public void SetWidth(int widthIndex) {  // This is called directly from the UI
    if (widthIndex==0)
      gridWidth = 5;
    else if (widthIndex <=19)
      gridWidth = (widthIndex+1)*5;   // increments of 5 from 5 to 100 (index 0 to 19)
    else
      gridWidth = (widthIndex-17)*50; // increments of 50 from 150 to 500 (index 20 to 27)
  }

  public void SetHeight(int heightIndex) {  // This is called directly from the UI
    if (heightIndex==0)
      gridHeight = 5;
    else if (heightIndex <= 19)
      gridHeight = (heightIndex+1)*5;   // increments of 5 from 5 to 100 (index 0 to 19)
    else if (heightIndex >= 20)
      gridHeight = (heightIndex-17)*50; // increments of 50 from 150 to 500 (index 20 to 27)
  }

  public void CreateGridLayout() {
    ClearGrid();
    Debug.Log("Creating a " + gridWidth + " x " + gridHeight + " grid.");
    tilemap.size = new Vector3Int(gridWidth, gridHeight, 1);
    tilemap.origin = new Vector3Int(-gridWidth/2, -gridHeight/2, 0);
    tilemap.FloodFill(tilemap.origin, tileEmpty);
    SetCameraFOV();
  }

  public void ClearGrid() {
    Debug.Log("Clearing grid...");
    GameManager.Instance.SimStop();
    tilemap.ClearAllTiles();
    GameManager.Instance.generation = 0;
  }

  public void RecalculateGridBounds() {
    tilemap.CompressBounds(); 
    tilemap.ResizeBounds();
    gridWidth = tilemap.size.x;
    gridHeight = tilemap.size.y;
  }

  public void Randomize() {
    Debug.Log("Randomizing grid...");
    GameManager.Instance.SimStop();
    RecalculateGridBounds();
    Vector3Int pos = new Vector3Int();
    float randomFloat;

    for (int i=0; i<tilemap.size.x; i++) {
      for (int j=0; j<tilemap.size.y; j++) {
        pos.x = tilemap.origin.x + i;
        pos.y = tilemap.origin.y + j;
        randomFloat = Random.Range(0f,1f);
        if (randomFloat <= 0.93) {
          tilemap.SetTile(pos, tileEmpty);
        }
        else {
          tilemap.SetTile(pos, tileAlive);
        }
      }
    }
  }

  public void SaveLayout() {
    // SaveLayout() saves a duplicate copy of initial tilemap to restore to later
    RecalculateGridBounds(); // resize bounds to make sure they're updated
    if(savedTilemap != null)
      Destroy(savedTilemap.gameObject);
    
    savedTilemap = Instantiate(tilemap, tilemap.transform.position, tilemap.transform.rotation, tilemap.transform.parent);
    savedTilemap.name = "Tilemap Initial State";
    savedTilemap.gameObject.SetActive(false); // make sure it's not visible
  }

  public void RestoreLayout() {
    Debug.Log("Restoring grid to previous state...");
    GameManager.Instance.SimStop();
    RestoreTilemap();
    SetCameraFOV();
  }

  private void RestoreTilemap() {
    // restores to copy of initial tilemap and makes a new copy
    if (tilemap != null)
      Destroy(tilemap.gameObject);
    tilemap = Instantiate(savedTilemap, savedTilemap.transform.position, savedTilemap.transform.rotation, savedTilemap.transform.parent);
    tilemap.name = "Tilemap";
    tilemap.gameObject.SetActive(true); // make sure it's visible
    RecalculateGridBounds();
  }

  public IEnumerator Simulate(int generations=1) {
    for (int g=1; g<=generations; g++) {
      GameManager.Instance.generation++;
      Debug.Log("***** SIMULATING GENERATION " + GameManager.Instance.generation);
      Tile getTile = ScriptableObject.CreateInstance<Tile>();
      Vector3Int pos = new Vector3Int();
      int aliveNeighbors = 0;

      // Create a temp Tilemap to hold original state so incremental changes don't affect each other
      Tilemap tempTilemap = Instantiate(tilemap, tilemap.transform.position, tilemap.transform.rotation, tilemap.transform.parent);

      for (int i=0; i<tilemap.size.x; i++) {
        for (int j=0; j<tilemap.size.y; j++) {
          pos.x = tilemap.origin.x + i;
          pos.y = tilemap.origin.y + j;
          getTile = tempTilemap.GetTile<Tile>(pos);  // get copy of current tile to check alive/dead, color, etc
          aliveNeighbors = CountAliveNeighbors(getTile, pos);

          // CHECK SURVIVAL OR BIRTH BASED ON GAME OF LIFE RULES
          // Rules are here https://en.wikipedia.org/wiki/Conway%27s_Game_of_Life
          bool isAlive = getTile.name == tileAlive.name;
          if (isAlive) { // Alive tiles. Use separate if statements for empty/alive to minimize the number of "if" checks.
            if (aliveNeighbors < 2 || aliveNeighbors > 3) {
              tilemap.SetTile(pos, tileEmpty);
            }
          }
          else {  // Empty tiles. Use separate if statements for empty/alive to minimize the number of "if" checks.
            if (aliveNeighbors == 3) {
              tilemap.SetTile(pos, tileAlive);
            }
          } 
        }
      }
      // if nothing was changed this round (reached steady state), stop simulation
      // if (steadyState == true) {
      //   StopSim();
      // }

      // Now that all cells have been updated, delete the copy of the original Tilemap
      Destroy(tempTilemap.gameObject);

      yield return null;
    }
    yield return null;
  }  

  private int CountAliveNeighbors(Tile tile, Vector3Int pos) {
    int aliveCount = 0;

    Vector3Int[] neighborPositions = GetNeighborPositions(pos, tilemap);

    foreach(Vector3Int neighborPos in neighborPositions) {
      if(tilemap.GetTile<Tile>(neighborPos).name == tileAlive.name) {
        aliveCount++;
      }
    }
    return aliveCount;
  }

  public static Vector3Int[] GetNeighborPositions(Vector3Int tilePos, Tilemap tmap) {
    Vector3Int[] neighbors = new Vector3Int[8];

    int[] xAxis = new int[3] {  // from left to right.  Handles wraparound
      tilePos.x-1 < tmap.cellBounds.xMin ? tmap.cellBounds.xMax-1 : tilePos.x-1,
      tilePos.x,
      tilePos.x+1 > tmap.cellBounds.xMax-1 ? tmap.cellBounds.xMin : tilePos.x+1
    };

    int[] yAxis = new int[3] {  // from bottom to top.  Handles wraparound
      tilePos.y-1 < tmap.cellBounds.yMin ? tmap.cellBounds.yMax-1 : tilePos.y-1,
      tilePos.y,
      tilePos.y+1 > tmap.cellBounds.yMax-1 ? tmap.cellBounds.yMin : tilePos.y+1
    };

    int neighborIndex = 0;
    foreach (int xVal in xAxis) {
      foreach (int yVal in yAxis) {
        if(xVal == tilePos.x && yVal == tilePos.y) {
          continue; // skip self
        }
        else {
          neighbors[neighborIndex].x = xVal;
          neighbors[neighborIndex].y = yVal;
          neighborIndex++;
        }
      }
    }
    return neighbors;
  }

  private static Vector3Int PosNorm(Vector3Int tilePos, Tilemap tmap) {  // normalized tile position adjusting for origin
    Vector3Int normalizedPos = new Vector3Int();
    normalizedPos.x = tilePos.x - tmap.origin.x;
    normalizedPos.y = tilePos.y - tmap.origin.y;
    return normalizedPos;
  }

  private static Vector3Int PosOrig(Vector3Int tilePos, Tilemap tmap) {  // adjusts position based on tilemap origin
    Vector3Int adjustedPos = new Vector3Int();
    adjustedPos.x = tilePos.x + tmap.origin.x;
    adjustedPos.y = tilePos.y + tmap.origin.y;
    return adjustedPos;
  }

  private void SetCameraFOV() {
    // TODO: Automate this if possible and if there's time.  For now, it works. 
    RecalculateGridBounds();

    if(gridWidth <= 20 && gridHeight <= 10)
      targetFOV = 65;
    else if(gridWidth <= 30 && gridHeight <= 20)
      targetFOV = 101;
    else if(gridWidth <= 45 && gridHeight <= 30)
      targetFOV = 120;
    else if(gridWidth <= 60 && gridHeight <= 40)
      targetFOV = 135;
    else if(gridWidth <= 75 && gridHeight <= 50)
      targetFOV = 142;
    else if(gridWidth <= 100 && gridHeight <= 75)
      targetFOV = 153;      
    else if(gridWidth <= 150 && gridHeight <= 100)
      targetFOV = 160;  
    else if(gridWidth <= 300 && gridHeight <= 200)
      targetFOV = 170;  
    else if(gridWidth <= 450 && gridHeight <= 300)
      targetFOV = 172.5f;  
    else if(gridWidth <= 600 && gridHeight <= 400)
      targetFOV = 175.5f;  
    else if(gridWidth <= 750 && gridHeight <= 500)
      targetFOV = 176.5f;  
  }
}