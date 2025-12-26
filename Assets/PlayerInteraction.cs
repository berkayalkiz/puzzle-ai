using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class PlayerInteraction : MonoBehaviour
{
    [Header("Referanslar")]
    public PuzzleController puzzleController;
    public GridManager gridManager;
    public Camera mainCamera;

    [Header("UI Referanslarý")]
    public Text[] handCountTexts;

    [Header("Ayarlar")]
    public float ghostAlpha = 0.5f;
    public float dragOffsetY = 1.0f; // EKLENDÝ: Þekil parmaðýn ne kadar yukarýsýnda görünsün?

    private GameObject currentGhost;
    private int selectedShapeId = -1;
    private bool isDragging = false;

    // Þekil listesi
    private readonly List<Vector2Int[]> shapes = new List<Vector2Int[]>
    {
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0) },
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,0), new Vector2Int(1,1) },
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(1,2) },
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(1,1) },
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1) },
        new Vector2Int[] { new Vector2Int(0,0) }
    };

    void Update()
    {
        UpdateHandUI();

        if (GameSettings.CurrentMode == GameMode.AI_Manual || GameSettings.CurrentMode == GameMode.AI_Auto)
            return;

        // Sadece sürükleme modu aktifse iþlem yap
        if (isDragging && currentGhost != null)
        {
            // --- DEÐÝÞÝKLÝK BURADA BAÞLIYOR ---

            // 1. Durum: Parmaðýn ekranda basýlý olduðu sürece (veya Mouse basýlýyken)
            if (Input.GetMouseButton(0))
            {
                HandleDragging();
            }

            // 2. Durum: Parmaðýný kaldýrdýðýn an (veya Mouse'u býraktýðýn an)
            if (Input.GetMouseButtonUp(0))
            {
                TryPlaceShape();
            }
        }
    }

    public void SelectShapeToDrag(int shapeId)
    {
        if (puzzleController.currentInventory[shapeId] <= 0)
        {
            Debug.Log("Bu parçadan kalmadý!");
            return;
        }

        selectedShapeId = shapeId;
        isDragging = true;

        if (currentGhost != null) Destroy(currentGhost);
        CreateGhost(shapeId);

        // Þekil seçildiði an parmaðýn pozisyonuna gelsin diye bir kez manuel çaðýrýyoruz
        HandleDragging();
    }

    void CreateGhost(int shapeId)
    {
        currentGhost = new GameObject("GhostShape");
        foreach (Vector2Int pos in shapes[shapeId])
        {
            GameObject cell = Instantiate(gridManager.cellPrefab, currentGhost.transform);

            // Hücre yerleþimi
            cell.transform.localPosition = new Vector3(pos.y * gridManager.targetSize, -pos.x * gridManager.targetSize, 0);

            SpriteRenderer sr = cell.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                cell.transform.localScale = Vector3.one;
                float spriteSize = sr.sprite.bounds.size.x;
                float newScale = gridManager.targetSize / spriteSize;
                cell.transform.localScale = new Vector3(newScale * 0.95f, newScale * 0.95f, 1f);

                Color c = gridManager.shapeColors[shapeId];
                c.a = ghostAlpha;
                sr.color = c;
                sr.sortingOrder = 100; // Mobilde parmak altýnda kalmasýn diye sayýyý yükselttim
            }
        }
    }

    void HandleDragging()
    {
        Vector3 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        mousePos.z = 0;

        // --- GÖRÜÞ AÇISI DÜZELTMESÝ (OFFSET) ---
        // Mobilde parmak þekli kapattýðý için þekli biraz yukarý kaydýrýyoruz.
        // Bilgisayarda test ederken mouse'un biraz yukarýsýnda kalabilir, normaldir.
        mousePos.y += dragOffsetY;

        float cellSize = gridManager.targetSize;
        Vector3 gridOrigin = gridManager.transform.position;

        // Grid üzerindeki satýr/sütun hesabý
        int col = Mathf.RoundToInt((mousePos.x - gridOrigin.x) / cellSize);
        int row = Mathf.RoundToInt(-(mousePos.y - gridOrigin.y) / cellSize);

        // Snap (Yapýþma) pozisyonu
        Vector3 snapPos = new Vector3(
            gridOrigin.x + (col * cellSize),
            gridOrigin.y - (row * cellSize),
            0
        );

        currentGhost.transform.position = snapPos;

        bool isValid = IsValidPlacement(selectedShapeId, row, col);
        SetGhostColor(isValid);
    }

    void TryPlaceShape()
    {
        // Dragging (Sürükleme) iþleminde hesapladýðýmýz son konumu kullanýyoruz
        // Tekrar mouse pozisyonu almaya gerek yok çünkü parmak kalktý.
        // Ghost'un þu an durduðu yer referans alýnacak.

        float cellSize = gridManager.targetSize;
        Vector3 gridOrigin = gridManager.transform.position;
        Vector3 ghostPos = currentGhost.transform.position;

        int col = Mathf.RoundToInt((ghostPos.x - gridOrigin.x) / cellSize);
        int row = Mathf.RoundToInt(-(ghostPos.y - gridOrigin.y) / cellSize);

        if (IsValidPlacement(selectedShapeId, row, col))
        {
            PlaceShapeLogic(selectedShapeId, row, col);
            puzzleController.currentInventory[selectedShapeId]--;
            UpdateHandUI();
            CancelDrag(); // Ýþlem baþarýlý, sürüklemeyi bitir
        }
        else
        {
            // Eðer yanlýþ yere býrakýrsa sadece iptal et (Þekil yerine geri dönsün)
            CancelDrag();
        }
    }

    public void UpdateHandUI()
    {
        if (handCountTexts == null || puzzleController == null) return;

        for (int i = 0; i < 6; i++)
        {
            if (i < handCountTexts.Length && handCountTexts[i] != null)
            {
                handCountTexts[i].text = puzzleController.currentInventory[i].ToString();
            }
        }
    }

    void CancelDrag()
    {
        isDragging = false;
        selectedShapeId = -1;
        if (currentGhost != null) Destroy(currentGhost);
    }

    void SetGhostColor(bool isValid)
    {
        Color c = isValid ? gridManager.shapeColors[selectedShapeId] : Color.red;
        c.a = ghostAlpha;
        foreach (Transform child in currentGhost.transform)
        {
            child.GetComponent<SpriteRenderer>().color = c;
        }
    }

    bool IsValidPlacement(int shapeId, int startRow, int startCol)
    {
        int[,] grid = GetCurrentGrid();
        foreach (Vector2Int p in shapes[shapeId])
        {
            int r = startRow + p.x;
            int c = startCol + p.y;
            if (r < 0 || r >= 4 || c < 0 || c >= 6) return false;
            if (grid[r, c] > 0) return false;
        }
        return true;
    }

    int[,] GetCurrentGrid()
    {
        System.Reflection.FieldInfo field = typeof(PuzzleController).GetField("currentGrid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (int[,])field.GetValue(puzzleController);
    }

    void PlaceShapeLogic(int shapeId, int startRow, int startCol)
    {
        int[,] grid = GetCurrentGrid();
        foreach (Vector2Int p in shapes[shapeId])
        {
            grid[startRow + p.x, startCol + p.y] = shapeId + 1;
        }
        gridManager.UpdateVisuals(grid);
        puzzleController.RegisterPlayerMove(shapeId, startRow, startCol);
    }
}